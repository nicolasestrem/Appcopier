# Phase 3a — Module bases: refactor & retire

Design record for the first sub-phase of Phase 3 (module coverage). Written 2026-07-21, alongside the
implementation on `feat/phase3a-module-bases`. The plan behind it was produced by a multi-agent design
pass whose adversarial critique confirmed twelve defects in the first draft; the corrections are folded
in below and marked where they changed the shape of the work.

## Goal

The roadmap's "refactor first" gate: extract the shared module shapes before Phase 3b/3c add ~9 new
modules, or the copy-paste duplication doubles. Behavior-preserving by construction — no new modules,
no changed backup filenames, so the existing test suite is the correctness oracle.

## What landed

### `MultiKeyRegistryModule`

Five modules (WPersonalization, WUpdates, GGaming, DPrinters, WTelemetry) carried the same template
verbatim: any-key `IsInstalled`, read-at-access-time `RestoreTargets`, export/import loops over
`RegFileNameFor`. The base writes it once; subclasses supply keys, metadata, and a per-key
`AbsenceIsNormal(key)` rule.

Contracts deliberately preserved because tests (and the promises they pin) depend on them:

- `Keys` stays a **public mutable field** — `BackupFileNamingTests` discovers multi-key modules via
  `GetField("Keys")`, and its mutation test appends a synthetic key after construction and observes the
  filename `RestoreAsync` actually computes.
- Everything reads `Keys` at access time, never captures it, so declaration and behavior cannot
  describe different key sets.
- `WThemes` stays bespoke: its folders+keys hybrid and legacy `RegFileNameFor` override are a
  compatibility promise (`Themes.reg`), not a template instance. The retargeted `WTaskbar` (3c) will be
  the second bespoke hybrid on the WThemes pattern — the critique showed forcing it onto this base
  cannot express its folder target.
- `RegistryModule` (single-key) stays separate: its files are named `{Title}.reg`, a promise this base
  must not inherit.

### `FolderModule`

The folder-copy shape (declaration, `Directory.Exists` install probe, copy → `CopyResult.ToStep` →
`Aggregate`, restore-side `NothingBackedUp` wording). Three design decisions came straight from the
critique, each against the draft:

- **`AbsenceIsNormal` defaults `true`** — every real consumer passed `true` on both directions; the
  draft's `false` default would have flipped Skipped→Failed during a "behavior-preserving" refactor.
- **No close-before-backup consent block.** After the browser retirement its only would-be consumer,
  APinnedApps, deliberately closes nothing on backup. A module that must close an app first should
  hand-roll from `BackupBase`, where the requirement is visible, rather than flip an unexercised flag.
- **`HasBackupIn` earns the real folder check only when the module closes a process.** Close-nothing
  modules keep the base default of `true` — the documented invariant that a module not taught to check
  must never be quietly skipped, swept by
  `RestoreDeclarationTests.ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem`.

The backup-side wording seam (`BackupStepFor`) is `private protected` because `CopyResult` is internal;
the restore side is not a seam at all — an absent source there is always `NothingBackedUp`, a fact no
per-module wording may restate as a claim about the live machine.

`FolderModuleTests` exercises the base decisions through fake subclasses against real directories,
mirroring how `RegistryModule`'s rules are held. Modules that inherit the base are deliberately not
retested per-module.

### `Utils.RunToolAsync` + `Utils.ValidateExportArtifact` — helpers, **not** a `CommandModule` base

The plan originally sketched a `CommandModule` base class. The critique demonstrated it fit exactly one
of its three intended consumers — CWiFiConf validates by a before/after file-set diff rather than a
single-artifact ladder and deliberately *fails* on an empty backup where WNetworkConf *skips*, and
AppStoreApps carries four comment-defended structural deviations — so the base was dropped and the real
shared seam shipped instead:

- `RunToolAsync`: both streams drained **concurrently from process start** (the draft's line-by-line
  stdout drain with redirected, undrained stderr was the fill-and-block pipe hazard WNetworkConf's own
  comment documented), bounded wait, kill-tree on timeout, optional stdout-to-file for dump-style
  exports. A failed artifact write is logged and surfaces through the artifact check as the missing
  file — the user-visible fact — rather than inventing an outcome for a process that ran fine.
- `ValidateExportArtifact`: the export-side "exit code is not evidence" ladder (missing file, empty
  file) for tools other than regedit. Content checks stay with callers; only they know what a usable
  file looks like (AppStoreApps' JSON ladder is untouched).

**winget stays on `RunWingetAsync`.** RestAppsForm shows winget's console window as its only progress
reporting — incompatible with redirected streams — and the ten-minute budget reflects measured
installs. Folding it in was considered and rejected; unifying the *netsh* duplication was the roadmap's
actual callout.

### Retirements

- **Browsers (BGoogleChrome, BMicrosoftEdge, BMozillaFirefox)** — user decision, 2026-07-21. Blunt
  full-profile copies (caches, GPU data, locked databases); sync solves it better; fixing meant
  per-browser exclusion lists plus the missed Firefox Local half, sustained maintenance the roadmap
  deprioritized. Old backups keep their folders on disk; the app no longer restores them. Disclosed in
  CHANGELOG.
- **DUSB** — near-empty shell-notification key whose Info text over-claimed. Same disclosure.
- Module count 23 → **19**; the "Browser" tree category is gone; the consented-close roster is empty
  until 3b repopulates it.

### `AllowPrompts` removed (deviation from the approved plan, recorded)

The plan said to fold the deferred `BackupAsync(path, allowPrompts)` signature change into 3a. During
implementation the browser retirement removed the flag's **only readers**, so the honest fix became
deletion: property gone, ConfPageView's set/reset plumbing gone, and a note on `BackupBase` records
what a future prompting module must do instead (permission as a call parameter, never instance state;
never on the snapshot path). Strictly less code than the planned change, same intent — the shared
mutable state is unrepresentable now.

### `AStoreApps.cs` → `AppStoreApps.cs`

Filename catches up with the class it has contained since 2c.

## Consequences for existing backups

None for filenames — that is the point of the compatibility contracts above. The retirements mean
browser folders and `USB Devices.reg` files in old backups are no longer restorable through the app;
both are disclosed in CHANGELOG with the manual alternatives.

## Deferred / out of scope

- The 3b `FileModule` base and `RestoreTarget.File` kind (designed in the Phase 3 plan, not needed
  until the dev-tooling modules land).
- WThemes/WTaskbar hybrid-base generalization — two bespoke hybrids are cheaper than an abstraction
  designed from one example plus a guess.
- The WTelemetry legacy-filename fallback (2c decision stands: the old payload names the stale hive).

## Verification

`dotnet build` + `dotnet test` green at every commit boundary (506 → 497 → 504 → 510 tests as clusters
landed); `windows-safety-reviewer` pass over the runner and module changes before the PR; manual
elevated smoke of the migrated modules is on the release checklist rather than per-commit, since no
module's registry keys, filenames, or copy targets changed.
