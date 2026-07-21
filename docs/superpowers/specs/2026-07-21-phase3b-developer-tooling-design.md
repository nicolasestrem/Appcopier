# Phase 3b — Module coverage: developer tooling

Design record for the second sub-phase of Phase 3. Written 2026-07-21, alongside the implementation on
`feat/phase3b-developer-tooling`. The plan behind it came from a multi-agent exploration and design
pass over the 3a bases; the two open scope questions it raised were decided by the user and are
recorded below rather than left implicit in the code.

## Goal

Add the developer-facing state a power user would actually miss, on top of the module bases 3a
extracted. Five new modules in a new **Developer** tree category (filename prefix `E`), plus the two
pieces of machinery they need: a `FileModule` base for copying *named files*, and a `RestoreTarget.File`
kind so the confirmation dialog can say "file:" where it previously only knew about folders.

## What landed

### `RestoreTarget.File`

A fourth kind beside `RegistryKey`, `Folder` and `Command`. It exists because the two are different
promises to the user: `folder: C:\Users\me\.ssh` reads as "everything under here is replaced", and
these modules replace *named files* inside directories they otherwise leave alone. Overstating restore
scope in the text the user consents against is the same defect as understating it.

`RestorePlan.Render` is the only switch on the kind in the codebase; it gained one arm.
`RestorePlanTests.ConfirmationText_LabelsEachTargetKind` asserts a distinct label per kind, so a future
kind added without an arm falls through to the bare path and fails a test rather than shipping as an
unlabelled line.

### `Utils.CopyFile` and `CopyResult.ToFileStep`

There was no single-file copy primitive — only `CopyFolder`. `CopyFile` returns the same `CopyResult`
tally so both directions fold through one Skipped-vs-Failed ladder, and it does not throw: a failure
comes back as `FilesFailed = 1` with `FirstError` set.

Two decisions worth recording:

- **A source it could not *examine* is a failure, not an absence.** The `FileInfo` construction is
  wrapped separately from the copy, and that catch deliberately does **not** set `SourceMissing`.
  Absence maps to `Skipped` for four of the five modules, so folding a failed probe into absence is the
  "I could not tell" → "nothing was there" slide the Phase 2a rules exist to prevent.
- **It creates the destination directory.** Load-bearing rather than convenient: a machine being
  restored onto may never have run ssh, so `%USERPROFILE%\.ssh` does not exist, and failing there would
  report "could not be copied" for a restore that is simply the first one.

`ToFileStep` is a separate ladder from `ToStep` only because of its nouns — `ToStep`'s absent-and-not-
normal wording is "expected **folder** for X is missing", and telling a user their hosts *folder* is
missing sends them to look for something that was never sought. The wording lives next to the mapping
rather than being patched at five call sites.

### `FileModule`

The file-level sibling of `FolderModule`. Shape borrowed from `MultiKeyRegistryModule` (N items,
per-item names, fields read at access time); decisions borrowed from `FolderModule` (sealed async pair,
restore-side absence always `NothingBackedUp`).

- **It is a whitelist by construction.** It copies what `Files` lists and *never enumerates a
  directory*. That is how the private-key exclusion below is expressed — as a structural property
  rather than as an exclusion filter someone has to keep correct.
- **`AbsenceIsNormal(string file)` is abstract**, following `MultiKeyRegistryModule` rather than
  `FolderModule`'s virtual-default-`true`. The consumers genuinely disagree: a Terminal Preview settings
  file is absent on most machines, an absent `hosts` means a broken install. A default would be right
  for one and silently wrong for the other, on the flag whose two failure directions are cry-wolf and
  hidden-problem.
- **`BackupFileNameFor` defaults to the file's BASE NAME, deliberately not the full path.** The paths in
  `Files` are composed from `Data.*` roots at runtime, so a path-derived name would carry the backing-up
  account's user name into the artifact name and stop resolving under any other account — the WThemes
  wallpaper-pointer class of defect, except this one would break the restore rather than the result.
  A module whose files share a base name must override; `ETerminal` does.
- **Artifacts live under `{Title}\`** rather than loose at the backup root. That groups them, keeps a
  base name like `config` from colliding between two modules, and gives `HasBackupIn` one directory to
  probe.
- **`HasBackupIn` earns the real check only when the module closes a process** — the `FolderModule`
  rule, and here it is what stops the orchestrator closing Windows Terminal, and every shell running in
  it, for a restore that had nothing to copy.

### The five modules

| Module | Base | Closes | Absence |
| --- | --- | --- | --- |
| `ETerminal` | `FileModule` | WindowsTerminal (consented) | normal |
| `EVSCode` | `BackupBase` (hybrid) | Code (consented) | normal |
| `ESsh` | `FileModule` | — | normal |
| `EEnvironment` | `RegistryModule` | — | **not** normal |
| `EHosts` | `FileModule` | — | **not** normal |

**`ETerminal` covers all three installs** (Store, Preview, unpackaged) — user decision, 2026-07-21.
Covering only the Store build would hand Preview and scoop/choco users a green "Skipped" over a
settings file that was right there. All three files are called `settings.json`, which is exactly the
collision the naming seam exists for: without the override the second export would overwrite the first
while *both* steps reported success, and the restore would write one file to all three destinations.
The override matches on path rather than list position, because a positional name changes meaning the
moment a fourth location is added and would orphan artifacts in every existing backup.

**`EVSCode` is hand-rolled from `BackupBase`,** not a `FileModule`. Two of its three targets are files
and the third — `snippets` — is a directory of arbitrarily many user-named files. Teaching `FileModule`
about folders to serve this one consumer is the mistake 3a's critique caught with the dropped
`CommandModule`: a base that fits one of its consumers is a worse seam than two honest ones. `WThemes`
is the precedent for a heterogeneous module folding both kinds of sub-operation through one `Aggregate`.
Because it does not inherit the sealed pair, it repeats the restore-side absence rule explicitly, and
its own backup/restore behaviour is tested directly rather than through the base's tests.

**`ESsh` excludes private keys** — user decision, 2026-07-21. Appcopier writes backups as ordinary
unencrypted files beside the executable, which is the wrong home for key material: a copy of `id_rsa`
there is a credential in plaintext, surviving in every backup folder the user forgets to delete, with
the passphrase protection on the original bypassed. Keys are meant to be re-issued on a new machine,
not carried to it by a settings tool. `DeveloperModuleTests` pins the exact file list *and*, separately,
that no declared target names a key file — so a future edit that appends to `Files` has to defeat both,
and the test that fails states the reason.

Not restored: NTFS ACLs. Windows OpenSSH refuses to use a *private key* whose ACL is too permissive but
is tolerant about `config` and `known_hosts`, so the files this module actually carries are usable after
a plain copy. This would need saying if the exclusion above were ever reversed.

**`EHosts` is the one module here that writes outside the user's profile.** Machine-wide, read by every
program that resolves a name, so it carries a `WarningMessage` even though the mechanics are an ordinary
file copy. Writing it needs elevation; the app manifests `highestAvailable`, and an unelevated run
produces an honest `Failed` step out of the copy primitive rather than a special case in the module.
Deliberately no pre-flight elevation probe — it would report the same fact one step earlier while adding
a second place that has to agree with the first about what "can write" means.

Note that `ModuleTargetTests.Themes_WritesNothingMachineWide` is WThemes-specific on purpose and is not
a global sweep. This module would legitimately fail such a sweep, which is why that test names the
module it constrains.

**`EEnvironment` is a plain `RegistryModule`** despite shipping with the file-based set — the category
is about what the user is backing up, not which base class it needs. Two limitations are disclosed
rather than engineered around:

1. A restore is an additive **merge**, like every registry import in this app. A variable present on
   this machine but absent from the backup survives; only variables the backup names are overwritten.
   This is the Phase 2b fidelity stance, stated in `Info` because `PATH` is the value where a user is
   most likely to expect otherwise.
2. **No `WM_SETTINGCHANGE` broadcast.** Already-running shells and editors keep the variables they
   started with; new processes see the restored values. Broadcasting is deliberately not built here —
   it would be this app's first message sent to every top-level window, which is a different kind of
   operation from writing a key and belongs to its own review rather than to a coverage phase.

### Registration

There is no category enum: `ConfPageView.FindOrCreateNode` creates "Developer" from the first
`AddConfiguration` call, so consistent spelling is the whole mechanism. The block sits after
Credentials, which puts the node last in the tree.

## Snapshot coverage

The invariant from `CLAUDE.md` — *anything a restore writes must be inside the pre-restore snapshot*,
because the snapshot is taken by running the module's own `Backup`. It holds structurally for all five:
every module's restore writes exactly the paths its backup reads, with no legacy-filename fallback and
no write to a location backup does not visit. That is the asymmetry that pushed `WTelemetry`'s fallback
out of 2c, and nothing here reintroduces it.

## Deferred, with reasons

- **WSL configuration.** The state that matters lives inside distro filesystems; `%USERPROFILE%\.wslconfig`
  is only the outer shell of it. Honest coverage needs distro enumeration and per-distro export, which
  is a different shape of work from a file copy.
- **VS Code extension list and reinstall.** Reinstalling extensions is an `AStoreApps`-style dialog flow
  (the user picks a subset), not a file copy. Roadmap-deferred; unchanged here.
- **VS Code Insiders and VSCodium; per-profile settings under `User\profiles\`.** User decision,
  2026-07-21: stable, default profile only. Widening is additive and cheap; the file names are the
  compatibility surface and none of them would change.
- **`WM_SETTINGCHANGE` broadcast after an environment-variable restore.** See above.
