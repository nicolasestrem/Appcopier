---
name: new-backup-module
description: Scaffold a new Appcopier backup module - creates the Conf/ class and wires up the ConfPageView tree registration that is easy to miss. Use when adding support for backing up a new Windows setting, app, or device area.
---

# New Backup Module

Adding a backup module requires **two synchronized edits**. The SDK-style project globs `**/*.cs`, so the file is compiled automatically — but registration in the UI tree is not, and missing it means the module silently never appears.

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

## Step 1 — Create the class in `src/Appcopier/Conf/`

Namespace is `Conf` (flat, not folder-based). `Title` is used as the backup **filename** — no invalid filename characters, and treat it as a stable contract (changing it orphans users' existing backups).

Registry-based template (see `Conf/DMouse.cs`):

```csharp
using Appcopier;

namespace Conf
{
    public class WExample : BackupBase
    {
        public string Key = @"HKEY_CURRENT_USER\Software\...";

        public WExample()
        {
            Title = "Example";
            Info = "This will back up ...";
            // Optional, only if relevant:
            // WarningMessage = "...";        // shown as a MessageBox on select
            // RequiresExplorerRestart = true; // shows restart button after restore
        }

        public override bool IsInstalled()
        {
            return Utils.KeyExists(Key);
        }

        public override void Backup(string path)
        {
            Utils.ExportImportRegistryKey(path + Title + ".reg", Key, false);
        }

        public override void Restore(string path)
        {
            Utils.ExportImportRegistryKey(path + Title + ".reg", Key, true);
        }
    }
}
```

Folder-based modules use `await Utils.CopyFolder(source, path + Title)` (see `Conf/BMozillaFirefox.cs` for the pattern, including closing the target app first with `Utils.IsProcessRunning` / `Utils.CloseProcess`).

Conventions the base class implies:
- The incoming `path` ends with a trailing backslash; modules concatenate (`path + Title + ".reg"`), they do not `Path.Combine`.
- `Restore` must consume exactly what `Backup` produced — same filename, same key.
- `IsInstalled()` returns `false` by default; override it so "Select installed" works.

Do **not** add a `<Compile Include="Conf\WExample.cs" />` entry to `Appcopier.csproj`. Since the .NET 8 migration
the project is SDK-style and globs `**/*.cs` by default, so an explicit entry is a duplicate and fails the build
with `NETSDK1022: Duplicate 'Compile' items were included`.

## Step 2 — Register in the UI tree

In `src/Appcopier/Views/ConfPageView.cs`, method `InitializeConfigurations()`, add next to its category siblings:

```csharp
AddConfiguration(new WExample(), "Settings");
```

The second argument must exactly match an existing tree node name from the table above (a typo silently creates a new top-level category).

## Verify

Build the solution (`dotnet build src\Appcopier.sln`) — the PostToolUse hook does this automatically after edits. Then confirm manually if possible: module appears under the right tree node, backup produces the expected file in `app\<timestamp>\`, and restore consumes it.
