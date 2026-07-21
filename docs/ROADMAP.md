# Appcopier Roadmap

The plan for bringing Appcopier back into maintenance, and the reasoning behind it. Written 2026-07-20,
after the app had gone unmaintained since January 2024 (v0.30.0).

Each phase is a separate spec, branch, and PR. Phase specs live in `docs/superpowers/specs/`.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | .NET 8 migration, test harness, repo/tooling cleanup | **Done** — [spec](superpowers/specs/2026-07-20-net8-migration-design.md) |
| 2a | Make failure representable and reported | **Done** — [spec](superpowers/specs/2026-07-20-phase2a-honest-failures-design.md) |
| 2b | Restore safety: snapshot, rollback, confirmation | **Done** — [spec](superpowers/specs/2026-07-20-phase2b-restore-safety-design.md) |
| 2c | Known module bugs | **Done** — [spec](superpowers/specs/2026-07-20-phase2c-module-bugs-design.md) |
| 3a | Module bases: refactor & retire | **In review** — [spec](superpowers/specs/2026-07-21-phase3a-module-bases-design.md) |
| 3b | Module coverage: developer tooling | Not started |
| 3c | Module coverage: power-user settings | Not started |
| 4 | Modernization: HttpClient, update checker, DPI, dark mode | Not started |

Phase 2 was originally written as one phase. It is four independent workstreams, and splitting it was
the first decision of the 2a design. 2a is the foundation: until failure can be *expressed*, none of the
others can be verified. Modernization moved out of Phase 2 entirely — dark mode and
rollback-a-bad-registry-import share no code, and bundling them means the reviewer scrutinising
destructive operations is also diffing ARGB values.

## Direction

Three priorities, in order: **safety and correctness**, **modernization**, **better coverage**. Sequencing is
migrate-first — the platform move landed before the behavior work, so the safety changes are written once,
against the runtime they will live on, instead of being written twice.

Deliberately **not** pursued for now: scheduled/automatic backups, backup compression, backup diffing,
cloud targets. They are real ideas, but they add surface area to a tool whose core operations are not yet
trustworthy.

## Phase 1 — .NET 8 migration (done)

Retargeted .NET Framework 4.8 → `net8.0-windows`, SDK-style csproj, `PackageReference`, xUnit harness,
`bin`/`obj` untracked, tooling updated. Behavior-preserving by design.

Three runtime breaks were fixed — all of which compile cleanly and fail only when run:

- `Process.Start` no longer shells out by default, so URL links threw. The QR-code prompt would have
  terminated the process outright (timer thread, no `try`/`catch`).
- `Application.StartupPath` gained a trailing separator, doubling it throughout backup paths.
- `Application.ProductVersion` prefers `AssemblyInformationalVersion` on .NET, whose `+<sha>` suffix would
  have made `new Version(...)` throw.

Releases ship **self-contained single-file** (~69 MB, one `.exe`, no runtime install). See the spec for the
measured size comparison and the flags that matter.

## Phase 2 — safety and correctness

The highest-value work remaining. Appcopier runs elevated and performs destructive operations, and until
2a it could not tell you when they failed.

**A backup tool that misreports success is worse than no backup tool** — the user finds out at restore time,
which is exactly when they have no fallback.

### Phase 2a — honest reporting (done)

Full design: [`superpowers/specs/2026-07-20-phase2a-honest-failures-design.md`](superpowers/specs/2026-07-20-phase2a-honest-failures-design.md).

The root defect is structural, not a collection of missing checks: `BackupBase.Backup(string)` returns
`void`, `Utils` swallows every exception, so the call chain is *incapable* of expressing failure. 2a
threads a `ModuleResult` (`Succeeded` / `Skipped` / `Failed` + reason) through `BackupBase` → 23 modules
→ `Utils` → the views, and everything below depends on it landing first.

All of the following landed. Kept as a record of what the phase actually addressed:

- ~~`Views/ConfPageView.cs` shows "Back up done." and "Restore done." unconditionally~~ — both now
  reflect real per-module outcomes, via a four-state summary that also distinguishes "nothing was
  present to back up" from "the run never happened".
- Replace the silent-catch pattern throughout `Conf/` and `Helpers/WindowsHelper.cs`: failures currently
  write a log line and return as if successful.
- `Utils.ExportImportRegistryKey` never checks an exit code and never verifies the `.reg` file exists, so a
  missing or corrupt file imports silently. Measured 2026-07-20: `regedit /e` on a nonexistent key exits
  **0 and writes no file**, so the file check is mandatory, not belt-and-braces.
- `backup_log.txt` records what was *selected*, not what *succeeded*. It should record outcomes.
- `CWiFiConf` restore matches `WLAN*.xml` but `netsh` writes `<interface>-<SSID>.xml` — measured 0 of 19
  files matched. Pulled into 2a because honest reporting would otherwise render total data loss as a
  tidy "Skipped".

Deferred out of 2a: full persistent file logging. `LogHelper` writes only to a `RichTextBox`, so every
error trace dies with the window. 2a fixes only the format-string hazard that would silently swallow
reason strings containing braces; the rest is its own workstream.

### Phase 2b — restore safety (done)

Full design: [`superpowers/specs/2026-07-20-phase2b-restore-safety-design.md`](superpowers/specs/2026-07-20-phase2b-restore-safety-design.md).

2a made restore *report* honestly; 2b makes it *behave* safely. All of the following landed:

- ~~Snapshot current state before any restore~~ — a restore now runs an ordinary backup of the items it
  is about to overwrite into a `(pre-restore)` folder first, so rollback is the existing restore flow.
  The decision not to build a delete-then-import rollback engine is recorded in the spec: it buys
  fidelity by adding registry *deletion* to the phase whose purpose is to make destruction safe. The
  additive-merge limitation is disclosed to the user instead of being papered over.
- ~~Real confirmation before destructive restore~~ — a dialog listing every item's registry keys,
  folders and commands, defaulting to Cancel, and carrying the per-module `WarningMessage`s that were
  previously shown only while browsing the tree.
- ~~Guard the unchecked `Process.Kill()` in `RestartExplorer`~~ — it killed every Explorer process and
  started a shell once *per kill*. It now closes once, starts at most one shell, starts none when
  Windows already restarted it, and returns a result instead of `void`.
- ~~Systematically close a target app before overwriting its profile~~ — consent is gathered once, on
  the UI thread, in the confirmation dialog, and flows into a pure per-module dispatch decision.
  Declining skips the module; a process that will not close fails it.
- ~~Write a restore-time log~~ — `restore_log.txt`, written into the snapshot folder beside the artifact
  that undoes the restore, and surfaced in `RestPageView`.
- Read-back verification of registry imports, deferred *into* 2b by the 2a spec, also landed. The
  mapping is deliberately asymmetric: a key absent after an import is a failure, a key that cannot be
  probed is not. The reasoning is in the spec, and it is the opposite of the export path's mapping.

Also pulled in: the QR-code timer below, because this phase added the app's first consequential modal
dialog and the timer's defect was a dialog-ownership defect.

### MainForm's QR-code timer

Found while hardening the link handlers. The first two items were **fixed in 2b**; the third belongs to
the persistent-logging workstream and is still open.

- ~~`MainForm`'s `System.Timers.Timer` has no `SynchronizingObject`~~, so `QRTimerElapsed` ran on a
  thread-pool thread and its `MessageBox` had no owner — it could paint *behind* the main window while
  the app stayed clickable, so a user saw nothing happen and clicked again, stacking up hidden dialogs.
- ~~That same timer is never stopped or disposed~~ — it was in neither `components` nor any teardown
  path, so an `Elapsed` still pending at close ran against a disposed control.
- `LogHelper.Log` is invoke-safe only by accident: `Control.InvokeRequired` returns false when the
  target has no created handle, so in that state it touches the `RichTextBox` from whatever thread
  called it. The catch-all hides it. This is part of the persistent-logging work above.

### Follow-ups left by Phase 2b

Raised by the safety review of the 2b branch. None was reachable in ordinary use; each was recorded
because the reason it was unreachable is a coincidence rather than a guarantee.

Two of the six were in fact **fixed inside the 2b commit itself** and this prose was never updated to
match — `CHANGELOG.md` recorded both correctly, so the record contradicted itself for the length of one
commit. Corrected below in 2c. It is worth noting how it happened: the entries were written when they
were deferred, the deferral was reversed during implementation, and nobody re-read the list. That is
the same class of drift as a stale comment, in the document whose whole job is being the accurate record.

- **The Explorer auto-restart probe is taken with no settle delay.** `RestartExplorer` asks
  `IsProcessRunning("explorer")` on the line after `CloseProcess` returns, but Windows relaunches the
  shell through winlogon some hundreds of milliseconds later — so the probe reads absent, Appcopier
  starts a shell, and Windows starts a second one. The guard bounds the damage to one stray window
  (versus N before 2b) and the risk is disclosed, but as written the `RestartedByWindows` branch may
  be close to dead code. **Measure N2 on the smoke matrix before changing anything** — if the
  relaunch is faster than assumed, the code is already right and a speculative delay would only slow
  the button down.
- **`AllowPrompts` is shared mutable state.** It is set on the module instance before each backup and
  restored in a `finally`, which is correct but depends on one caller remembering. `BackupAsync(path,
  allowPrompts)` — or a scoped guard — removes the class rather than the instance, and would make it
  testable at the module level instead of only through a `UserControl` the suite cannot instantiate.
  Deferred because it is a 23-signature change that 2b explicitly chose not to make.
- ~~**`results` is index-aligned against `selectedConfigs` but produced by iterating `scope`.**~~
  **Was already fixed in 2b**, not deferred: `ConfPageView` projects `restoredModules` from `scope`
  and pairs against that everywhere, so the alignment is structural. `selectedConfigs` is no longer
  used for pairing at all.
- ~~**`RestorePlan` composition sits outside any catch**, and `Render` dereferences each
  `RestoreTarget` unguarded.~~ **Fixed in 2c.** A null entry now renders its own marker — a different
  sentence from the undeclared marker, because "the module declared nothing" and "one line of the
  declaration is broken" are different facts — and the composition is wrapped in a catch that
  abandons the restore rather than half-describing it. Nothing is written when Appcopier cannot say
  what it would write.
- ~~**`SnapshotGate.Evaluate` counts an all-null outcome list as `considered == 0`**~~ **Fixed in
  2c.** A null entry is counted before the null check and folded into the existing failure branch, so
  it forces the prompt instead of vanishing. `ModuleOutcome.Pair` still never emits nulls; the point
  was to make the invariant structural rather than coincidental.
- ~~**A null entry in `ProcessesToCloseBeforeRestore` is handled inconsistently**~~ **Was already
  fixed in 2b**, not deferred: all four readers now guard identically, and the one that was missing
  carries a comment explaining the symmetry.

### Phase 2c — known module bugs

Each of these becomes *visible* once 2a lands, which is why they follow rather than lead.

- ~~`WTelemetry` hardcodes `ControlSet001` instead of `CurrentControlSet`~~ **Fixed in 2c.** The entry
  above understated it: this is not "wrong on systems booted from a different control set", it is
  *silently* wrong on them. `ControlSet001` normally still exists as a stale hive after such a boot, so
  the key probed present, the export succeeded and the row was green over configuration the running
  system was not using — the silent-wrong-data direction, not cry-wolf, which is why it survived. The
  fix also raises the stakes of a restore, from an inert write to a live service key, so the module
  gained a `WarningMessage`. **The filename is derived from the key, so this orphans the DiagTrack file
  in pre-2c backups**, which now report "nothing was backed up for this item". A restore-side fallback
  was designed and then deferred out of 2c on two grounds recorded in the spec: it would write outside
  the pre-restore snapshot while the gate still reported the restore undoable, and the old file's
  *contents* name `ControlSet001`, so applying it would re-commit the defect. An honest fallback has to
  rewrite the payload, not just find the file.
- `WNetworkConf.ExecuteNetshCommand` does `new StreamWriter(outputFilePath)` on both paths, and `Restore`
  passed `null`. **Fixed in 2a after a safety audit disproved the reasoning for deferring it.** The
  defect was worse than recorded here: `process.Start()` ran *before* the throw, so netsh was already
  applying the backup's addresses, DNS servers and interface metrics when the exception fired — and
  was never waited on or killed. The user was told the restore failed while their networking was being
  reconfigured. "Broken, not dishonest" was exactly backwards.
- ~~`CWiFiConf` restore imports only `xmlFiles[0]`~~ **Fixed in 2a**, along with the filename-filter
  half of the pair — correcting only one would have left the module still restoring nothing useful.
- ~~`AStoreApps` restore is dead code; the real `winget import` is commented out.~~ **This entry was
  false at the time 2c started, and is corrected rather than fixed.** 2a deleted the commented-out
  block; restore is a deliberate, tested delegation — `RestoreAsync` returns a completed task so
  `ShowDialog` runs on the STA UI thread rather than a thread-pool thread, and it reports `Skipped`
  because the installs happen from choices made inside the dialog. `winget import` is also the wrong
  feature: the dialog exists so the user can reinstall a *subset*, which import cannot express. Left as
  a record that a stale roadmap entry cost a planning cycle before anyone read the code.
- ~~`Utils.RunWTAsync` waits on `wt.exe`, which is a launcher rather than the work~~ **Measured and
  fixed in 2a.** Filed here as a suspicion, then confirmed on a real backup, 2026-07-20: the app
  reported `Remember installed apps FAILED — winget reported success but wrote no file` and wrote
  `backup_log.txt` at 07:35:54.295, and winget wrote a complete, valid 113-package export to that
  same path at 07:36:23.164 — **29 seconds after the app had already declared it missing**. `wt.exe`
  forwards the command and exits, so `WaitForExit` was returning on a process that had done nothing
  but pass along an argument. A backup that worked was reported as failed. `Utils.RunWingetAsync`
  now runs `winget.exe` directly, so the wait and the exit code belong to the process doing the
  work. This is the cry-wolf direction of the phase's failure mode, and it was invisible from the
  reporting layer: no care taken there could fix being asked about a file still being written.
- ~~`Utils.RunWT` is `async void`~~ **Fixed in 2a**: it is now `RunWTAsync` returning a
  `ProcessOutcome`. `async void` returns to its caller at the first `await`, so `AStoreApps` logged
  success before winget had started — it was structurally incapable of reporting a real result, which
  made it a prerequisite for the phase rather than cleanup.
- ~~`OSHelper` dereferences registry values with no null check.~~ **Fixed in 2c**, and it was the most
  severe item on this list rather than the tidiest. It runs from `ConfPageView`'s constructor, which is
  evaluated as the *argument* to `Application.Run` and therefore outside the message pump — and there
  was no `ThreadException` or `AppDomain.UnhandledException` handler anywhere in the tree. A missing
  `UBR` value, which is real on sysprepped and container images, terminated the process via WER with no
  window, no dialog and no log line. `Program.Main` now reports and rethrows.
- ~~`WThemes` backs up `%Windir%\Web\Wallpaper` … but not the actual active wallpaper.~~ **Fixed in
  2c.** Measured 2026-07-20: that folder is 20 files / 20.0 MB, about 95% of the module's bytes, and
  was its only write to a directory shared by every account on the PC. It now captures
  `HKCU\Control Panel\Desktop`, so the *pointer* to the wallpaper survives and not just the pixels —
  which the module was already copying. Two things are disclosed rather than fixed: the key carries
  display-specific passengers (`WindowMetrics` with its `AppliedDPI`, the `Colors` subkey) that
  `regedit` cannot leave behind, and the pointer is an absolute path containing the user name, so
  under a different account name the desktop comes back black while the row still reads Succeeded.
- ~~`Forms/RestAppsForm` wires `Click` to the `SelectedIndexChanged` handler, and its filename casing is
  inconsistent~~ **Fixed in 2c**, and the first half was understated here as a wiring inconsistency: it
  was silent data loss. The handler starts by clearing the checked-list, and the combo is a
  `DropDownList`, so *opening the dropdown* discarded every app the user had ticked — in the one dialog
  whose purpose is choosing a subset. The filename had four spellings, not two; the fourth was in the
  `Info` text, the only one a user reads.
- Two further `RestAppsForm` defects this list never recorded, both fixed in 2c: `btnRestore_Click`
  re-parsed the export to build an argument the install loop ignored, and the loop was `async void`
  called un-awaited with nothing disabling the button — so a second click started a concurrent
  *elevated* install run. Its parse-error log line also went through `LogHelper.Log`, whose first
  argument is a format string, carrying a JSON parse error: the one message needed to diagnose a
  broken export was the one guaranteed to be discarded.

Modernization was originally listed here and has moved to [Phase 4](#phase-4--modernization). It shares
no code with the safety work, and mixing UI theming into a review of destructive registry operations
serves neither.

## Phase 3 — module coverage

23 modules existed going in, strong on core Windows personalization/privacy and Wi-Fi/winget, and largely
absent on the state a power user would actually miss. Split into three sub-phases in the 2026-07-21
planning pass (multi-agent design plus an adversarial critique that confirmed twelve defects in the first
draft; the corrected plan is what the sub-phases below implement).

### Phase 3a — module bases: refactor & retire (in review)

Full design: [`superpowers/specs/2026-07-21-phase3a-module-bases-design.md`](superpowers/specs/2026-07-21-phase3a-module-bases-design.md).

- ~~**Refactor first.** The modules are near-identical copy-paste; `WNetworkConf` and `CWiFiConf` each
  carry their own `netsh` helper.~~ Done in 3a: `MultiKeyRegistryModule` and `FolderModule` bases, one
  shared `Utils.RunToolAsync` runner and `ValidateExportArtifact` ladder. A planned `CommandModule` base
  was **dropped by the critique** — it fit one of its three intended consumers; the runner was the real
  shared seam. winget deliberately keeps its own runner (its visible console window is the app-restore
  dialog's progress reporting).
- ~~**Browsers are deprioritized** … fix or retire them~~ **Retired**, by user decision 2026-07-21:
  sync solves it better, and fixing meant per-browser exclusion lists plus the missed Firefox Local
  half. Old backups keep their browser folders on disk; the app no longer restores them (disclosed in
  CHANGELOG).
- ~~`DUSB` targets a near-empty key~~ **Retired** in 3a — the Info text promised far more than the key held.
- The 2b-deferred `AllowPrompts` cleanup resolved itself: the retirement removed the flag's only
  readers, so the mechanism was deleted outright rather than redesigned.

### Phase 3b — developer tooling (not started)

New `FileModule` base + `RestoreTarget.File` kind, new "Developer" tree category (prefix `E`):
Windows Terminal settings, VS Code settings/keybindings/snippets, `.ssh` **config and known_hosts only**
(private keys are deliberately excluded from plaintext backups — user decision), user environment
variables (`HKCU\Environment`), `hosts` file. Terminal and VS Code declare consented closes: both rewrite
their own settings files while running, so an unclosed app can silently overwrite a restored file minutes
later. Deferred with reasons recorded: WSL config, VS Code extension list + reinstall dialog.

### Phase 3c — power-user settings (not started)

All under the existing "Settings" category (`W` prefix): power plans (`powercfg` export per scheme +
active-scheme manifest; restore defaults to re-activating the recorded scheme — importing plans creates
objects the pre-restore snapshot cannot remove, which must be argued to the safety reviewer explicitly,
not assumed under the 2b additive-merge stance), per-user fonts (HKCU fonts key + `%LOCALAPPDATA%`
fonts folder; username-absolute-path limitation disclosed like the WThemes wallpaper pointer), mapped
network drives (`HKCU\Network`), regional/input settings (International + keyboard layout keys). Plus
the two retargets still worth doing, with their orphaned-filename consequences disclosed per the 2c
WTelemetry precedent:

- `WTaskbar` does not capture pinned taskbar apps (those live in `Taskband` and the pinned shortcuts
  folder). Becomes a WThemes-style hybrid keeping the legacy `Taskbar.reg` name for its existing key,
  pinned by a literal test.
- `WUpdates` pairs the core servicing key with a WSUS-era `\AU` policy key; the parent key (which
  already contains the modern `UX\Settings`) stays to preserve its filename, `\AU` is dropped or
  demoted, DeliveryOptimization config added.

**Excluded from Phase 3 with recorded reasons:** scheduled tasks (honest restore needs SID/path
rewriting and system-task filtering; creates elevated executable entries), file associations (the
`UserChoice` hash is anti-tamper — a registry merge passes the post-import probe while Windows rejects
the association, a guaranteed dishonest green row), display layout (monitor-EDID-keyed, inherently
non-portable). `APinnedApps` copies a build-specific Start menu database that is notoriously
non-portable between machines — kept, with its warning strengthened in 3c rather than retired, because
same-machine restore is its honest use case.

## Phase 4 — modernization

Independent of the safety work and of each other; can land any time after Phase 1, in any order.

- Rewrite the update checker against the GitHub Releases API. It currently downloads `AssemblyInfo.cs` and
  string-parses it with raw index arithmetic; any reformat breaks it. Note the compatibility constraint in
  the Phase 1 spec — deployed clients still parse that file, so the format must survive the change.
- Replace obsolete `WebClient` with `HttpClient` (the two `SYSLIB0014` warnings).
- Per-monitor DPI awareness (currently System-aware; the `WFAC010` warning marks this).
- Dark mode. Colors are hardcoded light-theme ARGB values across the views.

## Cross-machine portability

Several modules are machine-specific (Start menu database, printers, USB, display), and nothing warns the
user when restoring onto different hardware. Worth addressing once Phase 2 makes failures visible — the two
problems share a mechanism.
