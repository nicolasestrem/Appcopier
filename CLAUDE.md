# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Appcopier is a Windows Forms desktop app (.NET 8, C#) that backs up and restores Windows 11 settings locally — an offline alternative to the built-in Windows Backup app. Backups are lightweight: each module exports registry keys (as `.reg` files) and/or copies folders/files into a timestamped folder.

## Build

SDK-style csproj targeting `net8.0-windows`. Use the dotnet CLI:

```
dotnet build src\Appcopier.sln
dotnet test src\Appcopier.sln
```

Output lands in `src\Appcopier\bin\<Configuration>\net8.0-windows\`. This dev build is framework-dependent, so running it needs the **.NET Desktop Runtime 8** (`Microsoft.WindowsDesktop.App` 8.0.x) installed.

Releases are different: they ship **self-contained single-file**, so end users install nothing. The `/release` skill has the exact publish command and the flags it depends on — all of them matter, and the artifact must come out as exactly one ~69 MB `Appcopier.exe`. Never ship the framework-dependent `bin\Release\` exe; on its own it cannot start. Do not add `PublishTrimmed` — WinForms resolves types by reflection and is not trim-safe.

The only runtime NuGet dependency is Newtonsoft.Json, declared as a `<PackageReference>` (`packages.config` is gone). Tests are xUnit, in `src/Appcopier.Tests`. There is no linter.

`Properties/AssemblyInfo.cs` is hand-maintained and the csproj sets `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` — this is load-bearing for the update checker (see "Data flow and paths"). Never set `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` in the csproj, and never add an `AssemblyInformationalVersion` attribute; both would create a second, silently diverging version source. The csproj carries comments explaining this and the DPI constraint — read them before editing it.

The app declares `requestedExecutionLevel level="highestAvailable"` in `app.manifest` — registry export/import shells out to `regedit.exe`, so meaningful manual testing requires an elevated Windows session. The unit tests deliberately cover only logic that runs without elevation.

Note: `src/Appcopier/bin/` and `src/Appcopier/obj/` are untracked and gitignored.

## Roadmap

`docs/ROADMAP.md` holds the phased plan for the app and the reasoning behind it. Phase 1 (.NET 8 migration)
is done; Phase 2 is the safety overhaul, Phase 3 is module coverage. Read it before proposing work that
spans more than one file — it records what was deliberately deferred and why, including a list of known
module bugs that are *not* regressions.

## Architecture

### Backup module system (the core pattern)

- `src/Appcopier/BackupBase.cs` — abstract base every backup module inherits: `Title`, `Info`, `WarningMessage`, `RequiresExplorerRestart`, `IsInstalled()`, `Backup(path)`, `Restore(path)`, plus `BackupAsync`/`RestoreAsync` wrappers (Task.Run around the sync methods). `Backup`/`Restore` return a `ModuleResult`, not `void` — see "Reporting outcomes" below.
- `src/Appcopier/Conf/*.cs` — one class per backup area. Filename prefix letter encodes the category: `A` = Apps, `C` = Credentials, `D` = Devices, `E` = Developer, `G` = Gaming, `W` = Windows settings. (There is no `B`/Browser anymore — those modules were retired in Phase 3a and the roadmap says not to add new ones.) Most modules call `Utils.ExportRegistryKey()` / `Utils.ImportRegistryKey()` (regedit `/e` and `/s`) and/or `Utils.CopyFolder()`.
- `src/Appcopier/Conf/RegistryModule.cs` — base for the ten modules that capture exactly one registry key to `{Title}.reg`. Subclasses supply data (`Key`, `AbsenceIsNormal`) and inherit the decision logic, so the skipped-vs-failed rule is written once. **Prefer inheriting this over hand-rolling `Backup`/`Restore`** when a module is a single-key export.
- `src/Appcopier/Conf/FileModule.cs` — base for modules that copy **named files** into `{Title}\`. It is a whitelist by construction: it copies what `Files` lists and **never enumerates a directory**, which is how `ESsh` excludes private keys structurally rather than through a filter that has to be kept correct. Use it, not `FolderModule`, whenever the containing folder holds anything that must not be captured. Its naming seam is `BackupFileNameFor`, defaulting to the file's *base name* — never the full path, which would carry the backing-up account's user name into the artifact name and stop resolving under any other account. A module with two same-named files (three Windows Terminal installs all call theirs `settings.json`) **must** override it, or the second copy overwrites the first while both steps report success.
- **A loop over N targets must build N distinct filenames.** Build the path with `BackupBase.RegFileNameFor(key)`; never `Title + ".reg"` inside a `foreach` over `Keys`. `WThemes` did the latter, which was harmless only while it had one key — a second export would delete the first via `TryDeleteExport` and write over it while *both* steps reported success, and the restore would import that one file once per key while the post-import probe found every key present, because the keys exist on the live machine regardless of what the file contained. Every row green, one key never captured. `BackupFileNamingTests` catches this by giving a module a synthetic extra key and observing the filename `RestoreAsync` actually computes — not by calling the seam, which a broken call site would still pass.
- **Keyless artifacts are named by a `const` on the class that writes them**, not through that seam: it derives `.reg` names from a registry key, and something like `AStoreApps`' `.json` export has no key. `AppStoreApps.ExportFileName` is the pattern. The rule is the same either way — a name kept away from its producer drifts. That one was spelled four ways at once, including in the `Info` text the user reads.
- **Changing a module's registry key changes its backup filename**, because the name is derived from the key. That orphans the file in every existing backup, which then restores as `Skipped("nothing was backed up for this item")`. Decide deliberately and disclose it; do not reach for a filename fallback without checking what the old file *contains*, since a `.reg` written for the old key applies to the old key no matter which key you pass `regedit`.

Adding a new module requires touching **two** places:
1. Create the class in `Conf/` inheriting `BackupBase` — or `RegistryModule` for a single-key export (namespace `Conf`).
2. Register it in `ConfPageView.InitializeConfigurations()` (`src/Appcopier/Views/ConfPageView.cs`) with its category node name ("Settings", "Apps", "Devices", "Gaming", "Credentials", "Developer"). There is no category enum — `FindOrCreateNode` creates the node from the first call, so consistent spelling is the whole mechanism.

…and it must **declare what its restore touches** (see "Restore safety" below). `RegistryModule` subclasses inherit that declaration from their `Key`; everything else states it explicitly. `RestoreDeclarationTests` enumerates every registered module and fails if one does not.

### Restore safety (read before writing a module that restores)

A restore snapshots what it is about to overwrite, asks the user to confirm it, and logs what it did.
None of that works unless modules declare their own restore surface, so `BackupBase` carries three
virtuals:

- `RestoreTargets` — the registry keys, folders, or commands this module's restore writes to. These
  are read out to the user in the confirmation dialog *before* anything is overwritten. The default is
  a loud `Undeclared` marker, not an empty list: a module that forgets shows a visible wart in the text
  users read before consenting, rather than quietly claiming to touch nothing.
- `ProcessesToCloseBeforeRestore` — the app that owns those files. Set `NeedsConsent` when the user must
  agree to close it (browsers); leave it false only for a process that restarts itself, which is closed
  just-in-time instead of being offered as a choice. Never overwrite a live profile without one of these.
- `RestoreMakesChanges` — leave it `true` unless the module's restore genuinely writes nothing (only
  `AStoreApps`, which opens a dialog). Setting it false exempts the module from being snapshotted, so
  getting it wrong means a restore that cannot be undone. This is the same class of judgement call as
  `absenceIsNormal`.

A declaration must contain **no null entries**. The dialog renders a per-entry marker for one, which is
loud on purpose, but it is a wart shown to a user deciding whether to consent — not a supported way to
declare a target. `RestoreDeclarationTests` fails on it.

**Anything a restore writes must be inside the pre-restore snapshot.** The snapshot is taken by running
the module's own `Backup`, so a restore path that writes somewhere `Backup` does not read is invisible to
it — and `SnapshotGate` will still report the restore as fully undoable, because it has no way to know.
That asymmetry is what pushed the `WTelemetry` legacy-filename fallback out of Phase 2c: a caveat inside
a step reason does not correct a verdict the user reads as "this can be undone".

The orchestration lives in `ConfPageView`, not in modules: consent is gathered once on the UI thread and
`RestoreDispatch.Decide` turns it into a per-module Run/Skip/Fail. **Never show a dialog from module
code on the restore path** — modules run on thread-pool threads, where a `MessageBox` has no owner and
can paint behind the main window.

### Reporting outcomes (read before writing a module)

This app's core failure mode was announcing success it had not verified. The rules below exist to keep
that from coming back; each was written after the corresponding mistake was actually made.

- **Build `StepResult`s and fold them with `ModuleResult.Aggregate`.** That is the only public
  construction path — there are deliberately no `ModuleResult.Succeeded/Skipped/Failed` factories,
  because one of them would be used to bypass the aggregation rules within a week.
- **Every sub-operation declares whether its target may legitimately be absent.** Absent + normal is
  `Skipped`; absent + not normal is `Failed`; a target that could not be *probed* is always `Failed`.
  "I could not tell" is a tool failure, not an absence. Getting this flag wrong is the cry-wolf
  failure in one direction and a hidden problem in the other.
- **Never claim more than you verified.** Registry exports are checkable (exit code *and* the file
  exists, is non-empty, and has a valid header). Imports are checked by reading the key back afterwards,
  but `regedit /s` still returns 0 on files it only partially applied and a present key does not prove
  its values match — so restore-side reasons say **applied**, never *verified* or *restored*.
- **Post-import probing is the mirror image of pre-export probing, on purpose.** Exporting, a key that
  cannot be probed is `Failed`, because the probe is the only evidence for the claim. Importing, a key
  that cannot be probed is still `Succeeded` ("could not confirm"), because exit code 0 already supports
  "applied" and failing there would cry wolf on every unelevated `HKLM` import. Only an *absent* key
  after an import is a failure.
- **An exit code is not evidence.** Measured on Windows 11: `regedit /e` on a nonexistent key exits 0
  and writes nothing; `netsh wlan export` printed "saved successfully" with exit code 0 while writing
  nothing. Always check the artifact the command was supposed to produce.
- **Log data-bearing text with `LogHelper.LogMessage`, never `LogHelper.Log`.** `Log` treats its first
  argument as a format string, so a registry path or exception message containing `{` throws inside
  the logger and the line is routed to `Console.WriteLine` — invisible in a WinForms app. The message
  is not lost loudly; it is lost silently.
- **Don't identify files by a name pattern you did not write.** `CWiFiConf` matched `WLAN*.xml` while
  `netsh` writes `<adapter name>-<SSID>.xml`, so restore found 0 of 19 profiles. Match on content when
  another tool chose the filename.

The csproj no longer needs a `<Compile Include>` entry — the SDK project globs `**/*.cs` automatically. (Older docs describing a third csproj step predate the .NET 8 migration.)

### UI navigation

`MainForm` hosts swappable `UserControl` views. `ViewHelper.SwitchView` (static, in `Helpers/ViewHelper.cs`) holds references to `MainForm` and the default nav page and swaps controls in `pnlForm`; `SetMainFormAsView()` navigates back. Views live in `Views/`: `ConfPageView` (main TreeView of modules, drives backup/restore), `RestPageView` (pick a backup folder to restore), `AboutPageView`. `Forms/RestAppsForm` is a dialog for reinstalling apps from a winget export.

### Data flow and paths

- `DataHelper.Data` (`Helpers/DataHelper.cs`) centralizes paths and URLs. Backups go to `<exe dir>\app\<yyyy-MM-dd - HH.mm>\` (`Data.DataRootDir`); each backup folder gets a `backup_log.txt` listing what was backed up, which `RestPageView` reads to describe backups.
- `LogHelper` (singleton) logs directly into a `RichTextBox` set via `SetTarget()` — UI-bound logging, invoke-safe.
- Open web links with `Utils.OpenUrl`, never `Process.Start` directly. The app runs elevated, and `ShellExecute` passes that elevated token to the browser it launches; `OpenUrl` goes through `explorer.exe` so the browser runs as the user, rejects anything that is not an `http`/`https` URL (a shell launch would otherwise execute it), and cannot throw — it is called from a timer thread where .NET 8 turns an escaping exception into process termination.
- Update check (`Data.CheckForUpdates`) downloads `AssemblyInfo.cs` from the GitHub repo raw URL and string-parses the `[assembly: AssemblyFileVersion("x.y.z")]` line out of it. `Program.GetCurrentVersionTostring()` reads that same attribute off the running assembly by reflection, so both sides of the comparison resolve to one source of truth and cannot diverge. Both sides then go through `Program.NormalizeVersion` before being compared with `==` — keep it that way, since normalizing only one side makes an up-to-date client report a phantom update on every check. Version bumps happen in `src/Appcopier/Properties/AssemblyInfo.cs`, must stay three-part (`0.31.0`, never `0.31.0.0`), and must keep that exact line format — the already-deployed v0.30.0 checker parses it with raw substring math, so a reformat silently disables update checks for existing users.

### Namespace quirk

Namespaces do not follow folder structure and are flat: `Appcopier` (core + helpers like `Utils`, `LogHelper`), `Conf` (all backup modules), `Views`, `DataHelper`, `ViewHelper`. Match the existing namespace of the folder you're working in.

## Project automation (`.claude/`)

- **Hooks** (`.claude/settings.json` + `.claude/hooks/*.ps1`): edits to `bin/`/`obj/` are blocked (generated build artifacts), and every `.cs` edit triggers a `dotnet build src\Appcopier.sln` compile check. The build check builds in place (safe now that `bin`/`obj` are gitignored) and exits 0 silently when the dotnet SDK isn't present, so it never produces false failures on a machine without the toolchain.
- **Skills**: use `new-backup-module` when adding a `Conf/` module (it covers the registration points); `/release` (user-invoked only) walks the version-bump/tag/release flow, including the AssemblyInfo format constraints the update checker depends on.
- **Subagent**: run `windows-safety-reviewer` after changing `Utils`, `Conf/` modules, or restore logic — it audits destructive operations (silent registry imports, process kills, profile overwrites) and silent-failure handling.
