# Phase 4 — UI revamp: task-first redesign on WinForms (Path D)

Design record for the Phase 4 UI track. Written 2026-07-21, before any implementation, from a
four-agent brainstorm that produced four complete and independently grounded directions. The user chose
one of them and borrowed a milestone from a second; both choices are recorded below as user decisions
rather than as conclusions the design arrived at on its own.

This document covers the UI track only. The two orthogonal Phase 4 items already in `docs/ROADMAP.md` —
replacing `WebClient` with `HttpClient`, and rewriting the update checker against the GitHub Releases
API — interleave as separate PRs and are unaffected by anything here.

## Context — why the phase opened somewhere else

Phase 4 was scoped in the original roadmap as modernization odds-and-ends: HttpClient, the update
checker, per-monitor DPI, dark mode. The user opened it with a full UI/UX revamp instead, and the
reasoning is worth recording because it reframes the phase rather than widening it.

Phases 2a through 3c rebuilt the engine's honesty and safety. `ModuleResult` made failure
representable, `RestorePlan` made consent informed, `SnapshotGate` made restore undoable, and
`RestoreScope`, `RestoreDispatch` and `ExplorerRestartPrompt` moved decisions out of a `UserControl` the
test suite cannot instantiate. The presentation layer never got that treatment, and it now actively
hides the work: a module's `WarningMessage` is delivered as a modal MessageBox while the user is
*browsing* the tree — the moment they are least able to act on it — the same `RichTextBox` serves as
both help text and activity log, so selecting a node wipes a mid-run failure line, and the results
MessageBox flattens 29 per-module outcomes into one dismissible paragraph. The Explorer-restart
affordance is a hot-pink banner button. A 24-succeeded/1-failed run reads, on screen, as roughly green.

That is the same failure class Phase 2a existed to remove, arriving through the UI instead of through
the engine. Modernization items do not fix it; a layout does.

## The decision: Path D, on WinForms, with Path C's Core extraction

**User decision, 2026-07-21: Path D — "jobs, not modules".** The app is reframed around the user's three
actual jobs — *stay backed up*, *recover*, *trust the result* — instead of a 29-module checkbox
inventory. Path B's Windows 11 Settings visual language is absorbed into it.

**User decision, 2026-07-21: stay on WinForms / .NET 8.** Every tested seam (`RunSummary`,
`RestorePlan`, `SnapshotGate`, `RestoreScope`, `BackupLog`/`RestoreLog`, `ExplorerRestartPrompt`) is a
pure class the UI merely renders, so nothing in this redesign needs a different framework. A port would
orphan zero engine code and one hundred percent of the Designer code either way, while adding packaging
and elevation unknowns to a self-contained artifact whose release contract is one ~69 MB exe.

**User decision, 2026-07-21: adopt Path C's Core-extraction milestone as an early PR**, even though the
framework migration around it was rejected. The extraction is valuable under any path — it is what lets
the test suite own the engine while the UI churns — and taking it early means the largest UI PRs land
against a project boundary rather than through one.

The engine and safety architecture are untouched in behavior. There is exactly one engine addition, the
backup manifest described below, and it uses Newtonsoft.Json, already a dependency. **No new NuGet
packages.**

## Alternatives considered and rejected

**Path A — refined WinForms in place.** Keep every screen recognizable; replace absolute positioning
with layout containers, split the info pane from the log, delete three MessageBoxes, add a `Theme`
class. It is the cheapest path and the least risky, and its diagnosis is correct: the problem is not
WinForms, it is that the UI was hand-placed in 2023 and never structured. Rejected because the user
asked for a complete revamp and this is not one. Path A's own honest assessment of its ceiling is the
reason: stock controls cap out at "clean, well-spaced Windows 10 utility", the TreeView stays
load-bearing so richer per-module information (last-backed-up timestamps, portability indicators) stays
cramped in a control with no cell model, and the module inventory remains the app's mental model.

**Path B — Windows 11 Settings-style shell on stock WinForms.** Left nav, card rows, status chips,
InfoBars, a real results page, six owner-drawn controls, zero new dependencies. Its control-library
survey is worth keeping: Krypton (BSD, maintained, but Office-2007/365 themes — the wrong decade, ~10 MB
of assemblies), ReaLTaiizor (MIT, active, single maintainer — supply-chain risk in an elevated process),
MaterialSkin.2 (wrong idiom), Guna/Bunifu (commercial closed-source, unacceptable in an elevated OSS
backup tool). The conclusion — owner-draw the six controls ourselves — is sound and Path D inherits it.
Rejected as a *direction* because it restyles the module-tree-first UX rather than replacing it: the
user still opens the app to an inventory. **Its visual language is absorbed into Path D wholesale**, so
what was rejected is the information architecture, not the look.

**Path C — WPF migration on .NET 10, strangling WinForms view by view.** Rejected for the UI stack;
its Core extraction is adopted. The framework analysis is recorded here because a future maintainer will
ask why not, and the answers are not obvious:

- **WinUI 3 — rejected.** Elevated (`highestAvailable`) processes have a troubled support history in the
  Windows App SDK, and unpackaged self-contained deployment works while *single-file* does not bundle
  the native WinAppSDK binaries into one exe. That breaks the release contract — one executable a user
  double-clicks with nothing installed — which is not tradeable for Mica.
- **Avalonia — rejected narrowly.** MIT, actively maintained, single-file friendly, ships a Fluent dark
  theme, runs elevated fine as plain Win32. But its custom Skia rendering means no `WindowsFormsHost`
  and therefore no strangler: it is a big-bang rewrite. For a solo maintainer, the inability to ship
  incrementally is disqualifying on its own; the weaker Windows accessibility/IME story and the
  cross-platform capability that buys this app nothing only add to it.
- **WPF — genuinely viable, and still rejected.** In-box, same manifest elevation semantics,
  `ShowDialog(owner)` gives exact consent parity, native PerMonitorV2, an in-box Fluent theme with
  `ThemeMode`, and real WinForms coexistence for a strangler. Path C's own gate is what settles it: it
  is worth 7–8 working weeks only if the five-year horizon is real, and it estimated the artifact at
  ~78–84 MB during the coexistence phase. If the ambition is Phase 4 and stop, WPF over-spends for
  what a WinForms theming slog mostly delivers.

## Target UX

Left rail in `MainForm`: **Home · Back up · Restore · History**, with About in the footer. The wallpaper
splash and the QR easter egg are removed.

**Home** answers "am I okay?" in five seconds:

```
+------+----------------------------------------------+
| Home | This PC: DESKTOP-NB01                        |
| Back | Last backup: 12 days ago  [1 FAILED]         |
| up   | 2026-07-09 - 14.22 · 24 items · 1 failed     |
| Rest |   ! Wi-Fi profiles FAILED - <reason line>    |
| ore  |   -> View details    -> Back up again        |
| Hist | ------------------------------------------   |
| ory  | Undo points: 1 pre-restore snapshot (Jul 10) |
|      | Disk: 118 GB free on C:                      |
+------+----------------------------------------------+
```

Failures render the module's own `Reason` verbatim and are pinned first — never a rollup that hides
them. "Back up again" re-selects what the last run selected and jumps to Back up.

**Back up** offers a curated default with the tree preserved:

```
| Choose what to back up                             |
| (o) Everything on this PC        24 items found    |
| ( ) Developer machine            Terminal, VSCode, |
|                                  SSH, env, hosts + |
| ( ) Minimal privacy-safe         excludes WUpdates |
|                                  id, env vars,     |
|                                  Wi-Fi keys        |
| ( ) Custom...        [Advanced: full module tree v]|
|                                                    |
| ! 2 items carry warnings (Pinned apps, WUpdates)   |
|   - shown inline, click to read                    |
|                            [ Back up now ]         |
```

Presets are named lists of module type names — pure UI, no engine concept. "Everything" is today's
*Select available*. The Advanced expander hosts the existing `treeConfigurations` verbatim, so the
`new-backup-module` skill's two registration touch-points survive unchanged.

**Restore** inverts the flow into a three-step wizard that starts from the backup:

```
Step 1: pick backup (cards: date, item count, OK/fail
        counts, "from DESKTOP-NB01 / nicol" badge)
Step 2: contents & portability
| Restore from 2026-07-09 - 14.22                    |
| [x] Personalization        OK in backup            |
| [x] Wi-Fi profiles         OK in backup            |
| [ ] Pinned apps   ! machine-specific: backup is    |
|                     from THIS machine - safe       |
| [-] Power plans   nothing in this backup           |
| ! This backup was made under user "nicol";         |
|   Themes wallpaper path will not resolve for       |
|   other accounts.                     [ Next ]     |
Step 3: consent - RestorePlan.ConfirmationText
        verbatim, per-process consent checkboxes,
        snapshot destination named, Cancel default
```

Step 2 uses the existing `RestoreScope.HasBackup` to grey out modules the folder holds nothing for. That
moves the "nothing was backed up for this item" surprise from *after* the run to *before* it, which is
where every orphaned-filename disclosure in Phases 2c and 3c wanted it. Step 3 is `RestoreConfirmForm`'s
content, with `RestorePlan` still the sole author of the words consented to.

**Results**, in both flows, is an in-page panel replacing the summary MessageBox: `RunSummary.Headline`
as the header, then one row per module with a state chip and its `Reason`, failed rows first. The
Explorer-restart action becomes a normal highlighted results row driven by the same
`ExplorerRestartPrompt.IsNeeded` seam, replacing the hot-pink button.

**History** is a timeline of every folder under `Data.DataRootDir`: backups, and `(pre-restore)`
snapshots labeled **Undo point** with what the restore changed, from `restore_log.txt`. "Undo this
restore" is a shortcut into the wizard with that snapshot preselected — Phase 2b's rollback-is-just-a-
restore design, finally visible.

### Visual language

Segoe UI Variable Text at 9.75pt body with Display headers at 16 and 12 — already the pairing
`RestoreConfirmForm` uses. Glyphs stay Segoe Fluent Icons, which is a font and therefore DPI-free. A
shared spacing constants class holds 4/8/12/24; absolute positioning is deleted, not adjusted. One
`Theme` static class carrying Light and Dark palettes replaces every inline `Color.FromArgb`, with light
keeping today's 243/243/243 and 245/241/249 family. State chips are green/amber/red pairs tuned per
theme, and **amber for Skipped, never green** — the styling has to keep the distinction the engine
fought for.

One honesty rule is a styling rule: reasons are always shown in full and are **selectable** (TextBox
rows, not Labels), so a user can copy failure text straight into an issue.

## Dark mode, accurately

`Application.SetColorMode` is **.NET 9+** and was not backported; it remains experimental
(`WFO5001`) through .NET 9/10. It is **not available on `net8.0-windows`**, so dark mode here is
hand-rolled:

- One `Theme` token class plus a control-tree walker, driven at startup from `AppsUseLightTheme` and
  live from `SystemEvents.UserPreferenceChanged` (marshaled through the main form; never touched from
  module or thread-pool code).
- `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE = 20)` for the title bar.
- Optionally `SetWindowTheme(hwnd, "DarkMode_Explorer", null)` for dark scrollbars on TreeView and
  ListView. Undocumented but stable, and used by essentially every dark WinForms app.

Both P/Invokes are wrapped in try/catch. They are cosmetic calls in an elevated process and must never
be able to throw.

**MessageBoxes and common dialogs stay light no matter what.** That is disclosed, not chased — and it
is cheap to accept precisely because Path D reduces the remaining MessageBoxes to the consent-class
prompts. Owner-drawing our way out of it is a budget this phase does not have.

.NET 8 reaches end of life in **November 2026**. A later retarget would let `SetColorMode` replace most
of the theme service, so it must be kept thin and disposable rather than grown into a framework.

## `backup_manifest.json` — the one engine addition

Home and History cannot make honest status claims by parsing prose logs. `backup_log.txt` is written for
a human; deriving "24 items, 1 failed" from it means a text parser whose failure mode is a confidently
wrong colour on the screen whose entire job is telling the user whether they are okay.

So the backup writes `backup_manifest.json` beside `backup_log.txt`, from
`ConfPageView.LogBackedUpElements`: module type names, titles, states, reasons, the run timestamp,
`Environment.MachineName`, `Environment.UserName`, the OS build, and the app version. It is tested the
way `BackupLog` is.

**The reader treats an absent or unparsable manifest as *unknown*, never as inferred green.** Every
backup taken before this PR has no manifest, and a dashboard that guesses at those is the cry-wolf
failure running in the dangerous direction. Best-effort text parsing may label something "approximate";
it may not produce a verdict.

Recorded explicitly because it comes due later: **the moment Home and History parse this file, its
schema is a versioned compatibility surface forever.** The `BackupLog` comment's "cheap insurance" stops
being cheap. A field removed or retyped in a future phase silently changes what old backups appear to
say.

## Invariants that must not regress

These converged across all four brainstorm agents independently, which is the strongest signal in the
document.

- **`RestoreConfirmForm` semantics survive exactly**: modal, `ActiveControl = btnCancel`, consent
  checkboxes unchecked by default, every word authored only by `RestorePlan` — including the Undeclared
  and null-entry markers, which are warts shown on purpose. The snapshot-override Yes/No stays modal and
  stays defaulted to **No**. Consent modality is the feature; what is being removed is modal *spam*,
  which is a different thing.
- **Nothing consent-relevant is lost by deleting the browse-time warning MessageBox.** `RestorePlan`
  re-carries every `WarningMessage` into the consent text, and inline row warnings show them *earlier*
  than a popup does. Backup is non-destructive, so inline-only is sufficient there.
- **No dialogs from module code or from thread-pool threads.** The existing rule; the wizard does not
  weaken it, because Core still cannot reach a dialog at all.
- **Layout containers land before the PerMonitorV2 DPI flip.** Absolute positions do not survive
  `WM_DPICHANGED` rescales; `TableLayoutPanel`/`Dock`/`AutoSize` do. Flipping first would produce
  breakage attributed to DPI that is actually the 2023 layout.
- **Tested seams are consumed unchanged.** `RunSummary`, `RestorePlan.Render`, `BackupLog`/
  `RestoreLog.Compose`, `ExplorerRestartPrompt.IsNeeded`, `RestoreScope.HasBackup` are read as strings
  and booleans by the new views exactly as the MessageBoxes read them. **Existing tests must pass
  unmodified**; PR 2 changes only their project reference.
- The release artifact stays one self-contained ~69 MB exe, no `PublishTrimmed`, elevation unchanged.

## PR sequence

Nine PRs, each shipping a working app.

1. **Design spec + roadmap.** This document, the Phase 4 scope update in `docs/ROADMAP.md`, and a
   CHANGELOG note. Small.
2. **Core extraction.** New `src/Appcopier.Core/` class library on `net8.0-windows`: `BackupBase`,
   `Conf/*`, `Results/*`, `Utils`/`WindowsHelper`, `DataHelper`, `OSHelper`. `LogHelper` splits into a
   Core `ILogSink` seam and a UI RichTextBox sink, keeping the `Log`/`LogMessage` format-string
   discipline verbatim. `Appcopier.Tests` retargets to Core and changes in no other way. Zero behavior
   change. ~1–1.5 wk.
3. **Backup manifest (engine, no UI).** As above. ~2–3 days.
4. **Shell + NavigationService + Home.** `MainForm` becomes rail plus content host; the static
   `ViewHelper.SwitchView` is replaced by an instance `NavigationService`, because "clear the panel, add
   a control" cannot express the push/pop the wizard needs. The old `ConfPageView` is hosted unchanged
   behind "Back up". Pre-manifest folders show "details unavailable". ~4–5 days.
5. **Orchestrator extraction.** `RunBackup`, `RunRestore` stages 1–8, `RestoreOne`, `TakeSnapshot` and
   `ProcessesWorthClosing` move out of `ConfPageView` into a `BackupRestoreOrchestrator` behind a small
   `IRunUi` interface (progress text, UI-thread modal prompts, results sink). **The load-bearing
   comments move with the code** — stage order, `RestoreScope.For` single-evaluation, fail-closed plan
   composition. **Zero visual change, and `windows-safety-reviewer` runs on it.** ~3–4 days.
6. **Backup page.** Presets, inline warnings, in-page results, the Explorer-restart row. ~4–5 days.
7. **Restore wizard.** Three steps; `RestoreConfirmForm`'s content becomes step 3, or the form survives
   as the host. **`windows-safety-reviewer` runs on it.** ~5–6 days.
8. **History/timeline.** Supersedes `RestPageView`; its `ReadLogOrNull` and log-concatenation logic move
   intact. ~3 days.
9. **Theme + dark mode + PerMonitorV2 DPI.** `Theme` replaces every inline `Color.FromArgb`; DWM title
   bar; `SystemEvents.UserPreferenceChanged`; then flip `HighDpiMode` and the manifest and test the
   matrix. Absorbs two original Phase 4 roadmap items. ~4–5 days.

Three ordering constraints, and why each holds. **Layout containers before the DPI flip**, for the
reason in the invariants. **The orchestrator extraction (5) before the pages that rewrite around it
(6, 7)**, because it is the scariest diff in the phase and isolating it as visual-change-free is the
only way a safety reviewer can read it as a move rather than a rewrite. **Presets after the backup
page**, not with it, because presets are a curation decision and bundling them into the page's layout
diff means the reviewer judging what "Minimal privacy-safe" excludes is also diffing a TableLayoutPanel.

## Verification

Every PR: `dotnet build src\Appcopier.sln` and `dotnet test src\Appcopier.sln`, with raw output pasted,
per the repo rule. PRs 5 and 7 additionally get a `windows-safety-reviewer` pass before the PR opens.
Manual elevated smoke on the real machine per PR — a backup run, a restore run through the consent
dialog, the snapshot-override path, the Explorer-restart row — with before/after screenshots in the PR
description.

PR 9 needs a DPI matrix at 100/150/200% plus a cross-monitor drag, and a live light/dark switch through
the OS setting. **No size or startup numbers are asserted anywhere in this document, because none have
been measured.** If the artifact size becomes a question, the number comes from the `/release` skill's
publish command and nowhere else.

## Risks, deferred items, and known tensions

- **Presets create curation debt.** Every new module must be deliberately placed into the preset lists,
  or "Everything" and "Developer machine" silently drift. This is a **third registration point** beyond
  the two `CLAUDE.md` names today, and it must be added to the `new-backup-module` skill's checklist in
  the same PR that lands presets — not afterwards, because "afterwards" is how the drift starts.
- **Home invites the scheduling scope-creep the roadmap deliberately forbids.** "Last backup: 40 days
  ago" begs for reminders and scheduled runs, which `docs/ROADMAP.md` excludes on the grounds that they
  add surface area to a tool whose core operations were not yet trustworthy. Home stays a *status
  display*. The tension is permanent and will recur every time someone reads the screen.
- **The advanced tree can rot** once it stops being the primary surface. Future module UX — search,
  per-module settings, portability indicators — has to be designed twice, for the curated rows and for
  the tree, or the tree quietly stops keeping up with the app.
- **Path D deepens the WinForms investment and forecloses a cheap framework move.** A future WPF or
  WinUI port would then be porting a shell, a theme system and custom row controls, not one TreeView.
  That is the price of the visual language, and it is being paid knowingly.
- **The dashboard must never claim more than the log recorded** — manifest-only for status claims, with
  unknown rendering as unknown. This is the same rule as every `AbsenceIsNormal` judgement call, at the
  scale of a whole screen.
- **Safety-UX regression is the phase's real risk**, concentrated in PRs 5 and 7. The mitigations are
  structural rather than diligent: PR 5 has no visual change, consent text stays `RestorePlan`-authored
  and pinned by existing tests, and both PRs get the safety reviewer.
