# Appcopier Roadmap

The plan for bringing Appcopier back into maintenance, and the reasoning behind it. Written 2026-07-20,
after the app had gone unmaintained since January 2024 (v0.30.0).

Each phase is a separate spec, branch, and PR. Phase specs live in `docs/superpowers/specs/`.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | .NET 8 migration, test harness, repo/tooling cleanup | **Done** — [spec](superpowers/specs/2026-07-20-net8-migration-design.md) |
| 2a | Make failure representable and reported | **Done** — [spec](superpowers/specs/2026-07-20-phase2a-honest-failures-design.md) |
| 2b | Restore safety: snapshot, rollback, confirmation | **Done** — [spec](superpowers/specs/2026-07-20-phase2b-restore-safety-design.md) |
| 2c | Known module bugs | Not started |
| 3 | Module coverage: dev tooling and power-user settings | Not started |
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

### Phase 2c — known module bugs

Each of these becomes *visible* once 2a lands, which is why they follow rather than lead.

- `WTelemetry` hardcodes `ControlSet001` instead of `CurrentControlSet` — wrong on systems booted from a
  different control set.
- `WNetworkConf.ExecuteNetshCommand` does `new StreamWriter(outputFilePath)` on both paths, and `Restore`
  passed `null`. **Fixed in 2a after a safety audit disproved the reasoning for deferring it.** The
  defect was worse than recorded here: `process.Start()` ran *before* the throw, so netsh was already
  applying the backup's addresses, DNS servers and interface metrics when the exception fired — and
  was never waited on or killed. The user was told the restore failed while their networking was being
  reconfigured. "Broken, not dishonest" was exactly backwards.
- ~~`CWiFiConf` restore imports only `xmlFiles[0]`~~ **Fixed in 2a**, along with the filename-filter
  half of the pair — correcting only one would have left the module still restoring nothing useful.
- `AStoreApps` restore is dead code; the real `winget import` is commented out.
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
- `OSHelper` dereferences registry values with no null check.
- `WThemes` backs up `%Windir%\Web\Wallpaper` — the stock OS wallpapers, identical on every machine — but
  not the actual active wallpaper.
- `Forms/RestAppsForm` wires `Click` to the `SelectedIndexChanged` handler, and its filename casing is
  inconsistent between load and restore (works only because Windows is case-insensitive).

Modernization was originally listed here and has moved to [Phase 4](#phase-4--modernization). It shares
no code with the safety work, and mixing UI theming into a review of destructive registry operations
serves neither.

## Phase 3 — module coverage

23 modules exist today, strong on core Windows personalization/privacy and Wi-Fi/winget, and largely absent
on the state a power user would actually miss.

**Developer tooling:** Windows Terminal settings, VS Code (settings, keybindings, extension list), `.ssh`
config and keys, user environment variables, WSL distro configuration, `hosts` file.

**Power-user settings:** power plans, installed fonts, mapped network drives, scheduled tasks, file
associations, regional and input settings, display layout.

**Refactor first.** The 23 modules are near-identical copy-paste; `WNetworkConf` and `CWiFiConf` each carry
their own `netsh` helper. Extract shared `RegistryModule` / `FolderModule` / `CommandModule` bases before
adding more, or the duplication doubles.

**Browsers are deprioritized.** Chrome profile sync already solves this better than a local export, and the
current modules are blunt full-directory copies — they grab caches and GPU data, miss half the Firefox
profile, and copy live locked databases. Fix or retire them; do not extend the pattern to more browsers.

**Also worth revisiting:** `WTaskbar` does not capture pinned taskbar apps (those live in `Taskband`);
`APinnedApps` copies a build-specific Start menu database that is notoriously non-portable between machines;
`DUSB` targets a near-empty key; `WUpdates` targets WSUS-era policy keys rather than modern Windows 11
update settings.

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
