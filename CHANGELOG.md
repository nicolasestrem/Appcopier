# Changelog

Notable changes to Appcopier are documented in this file.

## [Unreleased]

### Changed
- **Migrated the app from .NET Framework 4.8 to .NET 8** (`net8.0-windows`). The project file is now SDK-style, so the build is `dotnet build src\Appcopier.sln` instead of `nuget restore` + `msbuild` from a Visual Studio Developer environment. Build output also moves to `bin\<Configuration>\net8.0-windows\`. Releases now ship **self-contained**, so the runtime is bundled into the executable and users still download a single `.exe` and run it with nothing to install — the download grows from roughly 1 MB to roughly 69 MB in exchange for keeping that no-install experience.
- Newtonsoft.Json is now referenced as a `<PackageReference>`; `packages.config` and the checked-in `HintPath` to a `lib\net45` assembly are gone. `App.config` was removed — it only declared a `<supportedRuntime>` for .NET Framework, which is meaningless on .NET 8.
- Backup paths are now composed with `Path.Combine` rather than string concatenation. On .NET 5+ `Application.StartupPath` gained a trailing separator, which would otherwise have produced doubled (and in one case tripled) separators in every `regedit` command line, in `backup_log.txt`, and in the on-screen log. The on-disk layout is unchanged: backups still go to `<exe dir>\app\<yyyy-MM-dd - HH.mm>\`.
- The update checker's version parsing moved out of `CheckForUpdates` into `Data.ParseLatestVersion(string)`. `CheckForUpdates` performs network I/O and shows message boxes, so the parse could not be tested in place; the extracted method is byte-for-byte the original logic, quirks included, and is now covered by tests. No behavior changed.
- The in-app version is now read from `[assembly: AssemblyFileVersion]` by reflection instead of `Application.ProductVersion`. This is the exact attribute the deployed update checker parses out of `AssemblyInfo.cs`, so the local and remote sides of the update comparison can no longer drift apart — previously the correct value depended on no one ever adding an `AssemblyInformationalVersion` attribute, which would have appended a `+<git-sha>` suffix and broken the update check for every installed copy.

### Fixed
- **Opening any web link no longer crashes the app on .NET 8.** `Process.Start` no longer launches URLs through the shell by default, so all five link-opening call sites now request it explicitly. Without this, clicking through the QR-code prompt on the main window terminated the process outright (it ran on a timer thread with no error handling), every link on the About page raised an unhandled-exception dialog, and the "download update" link failed with a misleading "Checking for App updates failed" message *after* the user had already agreed to download.

### Added
- An xUnit test project at `src/Appcopier.Tests`, runnable with `dotnet test src\Appcopier.sln` — the project's first automated tests. 16 tests currently pin down the update-checker version handling on both sides of its comparison: parsing `AssemblyFileVersion` out of a downloaded `AssemblyInfo.cs`, and the local version the parsed value is compared against. Coverage is deliberately limited to pure logic — nothing touches the registry, the file system, or a process, since the backup modules shell out to `regedit.exe` and need elevation.
- The tests run against the **real** `src/Appcopier/Properties/AssemblyInfo.cs`, which the build copies into the test output, rather than against a hand-copied literal that could silently drift from the file the deployed update checker actually downloads.
- Claude Code project automation under `.claude/`, now tracked in the repository: hooks that block edits to generated `bin/`/`obj/` artifacts and run a `dotnet build` compile check after every C# edit, a `windows-safety-reviewer` subagent for auditing destructive Windows operations (registry imports, process kills, restore overwrites), and two skills — `new-backup-module` (scaffolds a `Conf/` module and registers it) and `/release` (guided version-bump/publish/tag/release flow). Only `.claude/settings.local.json` stays ignored, since it holds per-user paths.
- `CLAUDE.md` with build instructions and an architecture overview; this `CHANGELOG.md`.
- `docs/superpowers/specs/2026-07-20-net8-migration-design.md`, the design record for the .NET 8 migration and the phased roadmap that follows it.
- `src/NuGet.config` declaring nuget.org as a package source, so restore works on machines whose user-level NuGet configuration has no sources.

### Removed
- `src/Appcopier/bin/` and `src/Appcopier/obj/` are no longer tracked in git; a root `.gitignore` now covers build outputs, `src/packages/`, and Visual Studio user files. Build artifacts previously produced noise in every diff.

## [0.30.0]

Latest released version at the time this changelog was introduced; see [GitHub releases](https://github.com/builtbybel/Appcopier/releases) for prior history.
