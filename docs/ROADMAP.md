# Appcopier Roadmap

The plan for bringing Appcopier back into maintenance, and the reasoning behind it. Written 2026-07-20,
after the app had gone unmaintained since January 2024 (v0.30.0).

Each phase is a separate spec, branch, and PR. Phase specs live in `docs/superpowers/specs/`.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | .NET 8 migration, test harness, repo/tooling cleanup | **Done** — [spec](superpowers/specs/2026-07-20-net8-migration-design.md) |
| 2 | Safety and correctness overhaul | Not started |
| 3 | Module coverage: dev tooling and power-user settings | Not started |

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

The highest-value work remaining. Appcopier runs elevated and performs destructive operations, and today it
cannot tell you when they fail.

**A backup tool that misreports success is worse than no backup tool** — the user finds out at restore time,
which is exactly when they have no fallback.

### Honest reporting

- `Views/ConfPageView.cs` shows "Back up done." and "Restore done." unconditionally, even when every module
  threw. Both must reflect real per-module outcomes.
- Replace the silent-catch pattern throughout `Conf/` and `Helpers/WindowsHelper.cs`: failures currently
  write a log line and return as if successful.
- `Utils.ExportImportRegistryKey` never checks an exit code and never verifies the `.reg` file exists, so a
  missing or corrupt file imports silently.
- `backup_log.txt` records what was *selected*, not what *succeeded*. It should record outcomes.
- Add persistent file logging. `LogHelper` writes only to a `RichTextBox`, so every error trace dies with
  the window — including the ones that would explain a failed restore.

### Restore safety

- Snapshot current state before any restore, so a bad `.reg` import can be rolled back. Restore is currently
  irreversible.
- Real confirmation before destructive restore, stating what will be overwritten.
- Guard the unchecked `Process.Kill()` in `Utils.CloseProcess` and `RestartExplorer` (which kills *every*
  Explorer process).
- Systematically close a target app before overwriting its profile; the helpers exist but the restore path
  does not use them.
- Write a restore-time log. There is currently no audit trail of what a restore changed.

### MainForm's QR-code timer

Found while hardening the link handlers; deferred here because none of it is specific to links, and
all of it predates the .NET 8 migration.

- `MainForm`'s `System.Timers.Timer` has no `SynchronizingObject`, so `QRTimerElapsed` runs on a
  thread-pool thread. Its `MessageBox` therefore has no owner and can paint *behind* the main window
  while the app stays clickable — a user sees nothing happen and clicks again, stacking up hidden
  dialogs. Setting `SynchronizingObject` marshals the whole handler to the UI thread and fixes this.
- That same timer is never stopped or disposed — it is not added to `components` and there is no
  `FormClosing` handler — so an `Elapsed` still pending when the form closes runs against a disposed
  control. Harmless today only because the log call swallows `ObjectDisposedException`.
- `LogHelper.Log` is invoke-safe only by accident: `Control.InvokeRequired` returns false when the
  target has no created handle, so in that state it touches the `RichTextBox` from whatever thread
  called it. The catch-all hides it. This is part of the persistent-logging work above.

### Known module bugs

- `WTelemetry` hardcodes `ControlSet001` instead of `CurrentControlSet` — wrong on systems booted from a
  different control set.
- `AStoreApps` restore is dead code; the real `winget import` is commented out.
- `Utils.RunWT` is `async void` with a `WorkingDirectory` that may not exist, so the `Win32Exception` is
  rethrown on the sync context and crashes the app.
- `OSHelper` dereferences registry values with no null check.
- `WThemes` backs up `%Windir%\Web\Wallpaper` — the stock OS wallpapers, identical on every machine — but
  not the actual active wallpaper.
- `Forms/RestAppsForm` wires `Click` to the `SelectedIndexChanged` handler, and its filename casing is
  inconsistent between load and restore (works only because Windows is case-insensitive).

### Modernization carried into this phase

- Rewrite the update checker against the GitHub Releases API. It currently downloads `AssemblyInfo.cs` and
  string-parses it with raw index arithmetic; any reformat breaks it. Note the compatibility constraint in
  the Phase 1 spec — deployed clients still parse that file, so the format must survive the change.
- Replace obsolete `WebClient` with `HttpClient` (the two `SYSLIB0014` warnings).
- Per-monitor DPI awareness (currently System-aware; the `WFAC010` warning marks this).
- Dark mode. Colors are hardcoded light-theme ARGB values across the views.

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

## Cross-machine portability

Several modules are machine-specific (Start menu database, printers, USB, display), and nothing warns the
user when restoring onto different hardware. Worth addressing once Phase 2 makes failures visible — the two
problems share a mechanism.
