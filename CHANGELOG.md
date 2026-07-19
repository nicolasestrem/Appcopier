# Changelog

Notable changes to Appcopier are documented in this file.

## [Unreleased]

### Added
- Claude Code project automation under `.claude/`: hooks that block edits to tracked `bin/`/`obj/` build artifacts and run an MSBuild compile check after C# edits, a `windows-safety-reviewer` subagent for auditing destructive Windows operations (registry imports, process kills, restore overwrites), and two skills — `new-backup-module` (scaffolds a `Conf/` module with all three registration points) and `/release` (guided version-bump/tag/release flow).
- `CLAUDE.md` with build instructions and architecture overview; this `CHANGELOG.md`.
- `src/NuGet.config` declaring nuget.org as a package source, so `packages.config` restore works on machines whose user-level NuGet configuration has no sources.
- Root `.gitignore` covering `src/packages/`, build outputs, and Visual Studio user files (already-tracked `bin`/`obj` files remain tracked until deliberately untracked).

## [0.30.0]

Latest released version at the time this changelog was introduced; see [GitHub releases](https://github.com/builtbybel/Appcopier/releases) for prior history.
