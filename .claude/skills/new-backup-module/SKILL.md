---
name: new-backup-module
description: Scaffold a new Appcopier backup module - creates the Conf/ class, the restore-safety declarations, and the ConfPageView tree registration that is easy to miss. Use when adding support for backing up a new Windows setting, app, or device area.
---

# New Backup Module

Adding a backup module requires **two synchronized edits**, plus declarations the restore path depends on. The SDK-style project globs `**/*.cs`, so the file is compiled automatically — but registration in the UI tree is not, and missing it means the module silently never appears.

Read `CLAUDE.md`'s "Reporting outcomes" and "Restore safety" sections before writing any of this. The rules there are not style preferences; each was written after the corresponding mistake shipped.

## Inputs to determine first

1. **Module name** — prefix letter encodes the category and drives the filename:
   | Prefix | Category | Tree node name in UI |
   |--------|----------|----------------------|
   | `A` | Apps | "Apps" |
   | `B` | Browser | "Browser" |
   | `C` | Credentials | "Credentials" |
   | `D` | Devices | "Devices" |
   | `G` | Gaming | "Gaming" |
   | `W` | Windows settings | "Settings" |
2. **What gets backed up** — a registry key, a folder, or both. Look at an existing module in the same category first and follow its shape.
3. **Whether an absent target is normal.** A touchpad key on a desktop is normal; a key the module exists to capture is not. This flag is the difference between a reassuring "skipped" and a real problem being hidden, in one direction, and crying wolf in the other.

## Step 1a — Single registry key? Inherit `RegistryModule`

This is the common case (10 of the 23 shipped modules). It supplies `Backup`, `Restore`, `IsInstalled` and the restore declaration from your data, so the skipped-vs-failed decision and the `RestoreTargets` declaration are written once rather than copied.

```csharp
using Appcopier;

namespace Conf
{
    public class WExample : RegistryModule
    {
        public WExample()
        {
            Title = "Example";
            Info = "This will back up ...";
        }

        protected override string Key => @"HKEY_CURRENT_USER\Software\...";

        // True when the key is legitimately missing on some healthy machines.
        protected override bool AbsenceIsNormal => false;
    }
}
```

Check `Conf/RegistryModule.cs` for the exact member names before writing — it is the authority, this is a sketch.

## Step 1b — Anything else: inherit `BackupBase`

`Backup`/`Restore` return a `ModuleResult`, never `void`. Build `StepResult`s and fold them with `ModuleResult.Aggregate` — that is the only construction path, and there are deliberately no `ModuleResult.Succeeded/Skipped/Failed` factories.

```csharp
using Appcopier;
using System.Collections.Generic;

namespace Conf
{
    public class WExample : BackupBase
    {
        public List<string> Keys = new List<string> { /* ... */ };

        public WExample()
        {
            Title = "Example";
            Info = "This will back up ...";
            // Optional:
            // WarningMessage = "...";         // shown while browsing AND in the restore confirmation
            // RequiresExplorerRestart = true; // offers the restart button after a successful restore
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string key in Keys)
                steps.Add(Utils.ExportRegistryKey(FileFor(path, key), key, absenceIsNormal: false));

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string key in Keys)
                steps.Add(Utils.ImportRegistryKey(FileFor(path, key), key));

            return ModuleResult.Aggregate(steps);
        }

        // One file per key, with the backslashes flattened. The existing multi-key modules each
        // carry their own copy of this; follow whichever one you are sitting next to.
        private string FileFor(string path, string key)
            => Path.Combine(path, $"{Title}_{key.Replace('\\', '_')}.reg");

        // The restore path reads this out to the user before overwriting anything.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => Keys.ConvertAll(RestoreTarget.RegistryKey);
    }
}
```

Folder modules use `await Utils.CopyFolder(source, destination)`, which returns a `CopyResult`; call `.ToStep(...)` on it rather than inventing a step. See `Conf/BMozillaFirefox.cs`.

**A module that overwrites a live app's profile must also declare its process:**

```csharp
public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
    => new[] { new RestoreCloseRequirement("firefox", "Mozilla Firefox", needsConsent: true) };
```

The process name is what `Process.GetProcessesByName` takes — no `.exe`. Require consent for anything whose closing destroys work the user can see (an open browser with tabs); pass `false` only for a process Windows brings straight back on its own, where a checkbox asks permission for something the user cannot meaningfully decline and only adds to the dialog fatigue that makes the real checkboxes stop being read. **Never** write into a profile whose owner is running without one of these — the orchestrator does the closing, but only for processes that were declared.

Leave `RestoreMakesChanges` alone unless the module's restore genuinely writes nothing. Setting it false exempts the module from the pre-restore snapshot, which means a restore that cannot be undone.

Conventions the base class implies:
- The incoming `path` ends with a trailing backslash; existing modules concatenate rather than `Path.Combine`.
- `Restore` must consume exactly what `Backup` produced — same filename, same key.
- `IsInstalled()` returns `false` by default; override it so "Select installed" works.
- **Never show a dialog from module code on the restore path.** Modules run on thread-pool threads, where a `MessageBox` has no owner and can paint behind the main window. Restore consent is gathered by `ConfPageView` before dispatch.

Do **not** add a `<Compile Include="Conf\WExample.cs" />` entry to `Appcopier.csproj`. Since the .NET 8 migration the project is SDK-style and globs `**/*.cs`, so an explicit entry is a duplicate and fails the build with `NETSDK1022`.

## Step 2 — Register in the UI tree

In `src/Appcopier/Views/ConfPageView.cs`, method `InitializeConfigurations()`, add next to its category siblings:

```csharp
AddConfiguration(new WExample(), "Settings");
```

The second argument must exactly match an existing tree node name from the table above (a typo silently creates a new top-level category).

## Verify

```
dotnet build src\Appcopier.sln
dotnet test src\Appcopier.sln
```

`RestoreDeclarationTests` enumerates every registered module and fails if yours does not declare what its restore touches, so a forgotten declaration is caught here rather than in front of a user mid-restore.

Then confirm manually where possible: the module appears under the right tree node, backup produces the expected file in `app\<timestamp>\`, the restore confirmation lists your declared targets, and restore consumes what backup wrote. Meaningful manual testing needs an elevated session — registry work shells out to `regedit.exe`.
