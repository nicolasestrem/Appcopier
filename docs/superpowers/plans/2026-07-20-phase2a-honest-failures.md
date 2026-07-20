# Phase 2a — Honest Failure Reporting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Appcopier capable of reporting that a backup or restore failed, by threading a `ModuleResult` (`Succeeded` / `Skipped` / `Failed` + reason) through `BackupBase` → 23 `Conf/` modules → `Utils` → the views.

**Architecture:** Two immutable value types (`StepResult` for one sub-operation, `ModuleResult` for one module's verdict) folded by a single `ModuleResult.Aggregate` entry point. `Utils` primitives stop swallowing exceptions and start returning `StepResult`. `ConfPageView` aggregates module results into a four-state run summary that replaces the unconditional "Back up done." Registry and process launches sit behind narrow interfaces so module logic is testable without elevation.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WinForms, xUnit 2.9.3. No new NuGet dependencies.

**Design spec:** `docs/superpowers/specs/2026-07-20-phase2a-honest-failures-design.md` (committed at `77d3498`). Read it before starting — it records *why* each rule is shaped the way it is, and the measured `regedit`/`netsh` facts the verification rules rest on.

## Global Constraints

Every task's requirements implicitly include this section.

- **Branch:** `feat/phase2-honest-failures`. Never commit to `main`. Never force-push.
- **Build:** `dotnet build src\Appcopier.sln`. **Test:** `dotnet test src\Appcopier.sln`.
- **Project settings are fixed:** `Nullable` is `disable`, `ImplicitUsings` is `disable`. Do not add nullable annotations or rely on implicit usings — every file declares its own `using` directives.
- **Never touch `src/Appcopier/Properties/AssemblyInfo.cs`** version lines, and never set `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` in any csproj. The deployed v0.30.0 update checker string-parses that file.
- **Namespaces are flat and do not follow folders:** `Appcopier` (core, helpers, `Utils`, the new result types), `Conf` (all backup modules), `Views`, `DataHelper`, `ViewHelper`. Match the namespace already in the file you are editing.
- `[assembly: InternalsVisibleTo("Appcopier.Tests")]` already exists (`AssemblyInfo.cs:52`), so `internal` types are reachable from tests.
- **No writer's filename derivation may change.** Backups written by v0.30.0 must stay restorable. `{Title}.reg` and `{Title}_{GetSafeFileName(key)}.reg` stay exactly as they are.
- **Restore-side reason strings say "applied", never "verified" or "restored".** `regedit /s` returns 0 on partially-applied files.
- **`Reason` is mandatory and non-empty for `Skipped` and `Failed`**, enforced in the factory.
- **All `ModuleResult` construction goes through `ModuleResult.Aggregate`.** There are no public `ModuleResult.Succeeded/Skipped/Failed` factories.
- **Update `CHANGELOG.md`** under `[Unreleased]` as part of the final task.
- The `.claude/` PostToolUse hook runs `dotnet build` after every `.cs` edit. Expect it to report errors mid-task; the build must be green before any commit.

---

### Task 1: The result types

**Files:**
- Create: `src/Appcopier/Results/ResultState.cs`
- Create: `src/Appcopier/Results/StepResult.cs`
- Create: `src/Appcopier/Results/ModuleResult.cs`
- Test: `src/Appcopier.Tests/ModuleResultTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Appcopier.ResultState { Succeeded, Skipped, Failed }`; `Appcopier.StepResult` with `string Target`, `ResultState State`, `string Reason` and static factories `Succeeded(string target, string reason)`, `Skipped(string target, string reason)`, `Failed(string target, string reason)`, `Applied(string target, string reason)`; `Appcopier.ModuleResult` with `ResultState State`, `string Reason`, `IReadOnlyList<StepResult> Steps` and the single static factory `Aggregate(IReadOnlyList<StepResult> steps)`.

The `Results/` folder is new. The SDK project globs `**/*.cs`, so no csproj change is needed.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/ModuleResultTests.cs`:

```csharp
using Appcopier;
using System;
using System.Collections.Generic;
using Xunit;

namespace Appcopier.Tests
{
    public class ModuleResultTests
    {
        private static StepResult Ok(string t = "key") => StepResult.Succeeded(t, "exported 1 key");
        private static StepResult Skip(string t = "key") => StepResult.Skipped(t, "not present on this system");
        private static StepResult Bad(string t = "key") => StepResult.Failed(t, "access denied");

        // --- Aggregation rule 1: no steps ---

        [Fact]
        public void Aggregate_NoSteps_IsSkipped()
        {
            ModuleResult r = ModuleResult.Aggregate(new StepResult[0]);
            Assert.Equal(ResultState.Skipped, r.State);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }

        // --- Rule 2: any failure dominates ---

        [Fact]
        public void Aggregate_AnyFailed_IsFailed()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Bad("b"), Skip("c") });
            Assert.Equal(ResultState.Failed, r.State);
        }

        [Fact]
        public void Aggregate_Failed_ReasonNamesCountAndFirstFailure()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Bad("b"), Bad("c") });
            Assert.Contains("2 of 3", r.Reason);
            Assert.Contains("access denied", r.Reason);
        }

        // --- Rule 3: all skipped stays skipped (the rule the inventory forced) ---

        [Fact]
        public void Aggregate_AllSkipped_IsSkippedNotSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Skip("a"), Skip("b") });
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Rule 4: a mix of success and legitimate absence is success ---

        [Fact]
        public void Aggregate_SucceededPlusSkipped_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonNamesTheSkippedTarget()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.Contains("Accent", r.Reason);
        }

        // Rule 4 must not read as a bare ratio - "1 of 2" under a "Done" heading reads as
        // partial failure, which is the ambiguity that justified dropping a Partial state.
        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonIsNotABareRatio()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.DoesNotContain("1 of 2", r.Reason);
        }

        // --- Rule 5: all succeeded ---

        [Fact]
        public void Aggregate_AllSucceeded_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Ok("b") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_PreservesSteps()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Skip("b") });
            Assert.Equal(2, r.Steps.Count);
        }

        [Fact]
        public void Aggregate_NullSteps_IsSkippedNotCrash()
        {
            ModuleResult r = ModuleResult.Aggregate(null);
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Factory invariants ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_SkippedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Skipped("t", reason));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_FailedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Failed("t", reason));

        [Fact]
        public void StepResult_SucceededWithoutReason_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded("t", ""));

        [Fact]
        public void StepResult_NullTarget_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded(null, "fine"));

        // --- The restore-side wording rule ---

        [Fact]
        public void StepResult_Applied_IsSucceeded()
            => Assert.Equal(ResultState.Succeeded, StepResult.Applied("t", "1 key").State);

        [Fact]
        public void StepResult_Applied_ReasonSaysAppliedAndNeverVerified()
        {
            StepResult s = StepResult.Applied("Mouse.reg", "1 key");
            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~ModuleResultTests`
Expected: FAIL — build errors, `The type or namespace name 'StepResult' could not be found`.

- [ ] **Step 3: Create `ResultState`**

Create `src/Appcopier/Results/ResultState.cs`:

```csharp
namespace Appcopier
{
    /// <summary>
    /// The outcome of one sub-operation or one module.
    /// </summary>
    /// <remarks>
    /// Three states answer three different user questions: did I get my data (Succeeded), was there
    /// nothing to get (Skipped), did something break (Failed). There is deliberately no Partial:
    /// it carries nothing that Succeeded/Failed plus the step list does not already carry, and it
    /// forces every consumer to answer a question with no stable answer - is Partial good news?
    /// </remarks>
    public enum ResultState
    {
        Succeeded,
        Skipped,
        Failed
    }
}
```

- [ ] **Step 4: Create `StepResult`**

Create `src/Appcopier/Results/StepResult.cs`:

```csharp
using System;

namespace Appcopier
{
    /// <summary>
    /// The outcome of a single sub-operation: one registry key, one folder copy, one shell command.
    /// </summary>
    public sealed class StepResult
    {
        /// <summary>Human-readable label: a registry key path, a folder path, "winget export".</summary>
        public string Target { get; }

        public ResultState State { get; }

        /// <summary>Never null, never empty. States what happened, not merely that it happened.</summary>
        public string Reason { get; }

        private StepResult(string target, ResultState state, string reason)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("A step must name its target.", nameof(target));

            // Enforced here rather than by convention: an empty reason on a Skipped or Failed step
            // produces a summary dialog that says something went wrong without saying what, which
            // is the failure mode this whole phase exists to remove.
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A step must carry a reason.", nameof(reason));

            Target = target;
            State = state;
            Reason = reason;
        }

        public static StepResult Succeeded(string target, string reason)
            => new StepResult(target, ResultState.Succeeded, reason);

        public static StepResult Skipped(string target, string reason)
            => new StepResult(target, ResultState.Skipped, reason);

        public static StepResult Failed(string target, string reason)
            => new StepResult(target, ResultState.Failed, reason);

        /// <summary>
        /// A successful restore-side operation.
        /// </summary>
        /// <remarks>
        /// A separate factory so the wording cannot drift across the 16 modules that restore.
        /// "applied" is not a synonym for "verified" here: regedit /s returns exit code 0 on a file
        /// it only partially applied, so having run it successfully is the strongest claim available
        /// without reading the keys back. Read-back verification is Phase 2b.
        /// </remarks>
        public static StepResult Applied(string target, string what)
            => new StepResult(target, ResultState.Succeeded, "applied " + what);
    }
}
```

- [ ] **Step 5: Create `ModuleResult`**

Create `src/Appcopier/Results/ModuleResult.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Appcopier
{
    /// <summary>
    /// One module's verdict for one Backup or Restore call.
    /// </summary>
    /// <remarks>
    /// Immutable and returned by value, which is required rather than stylistic: five modules run
    /// their work on the UI thread (they override the async pair directly) and the rest run on a
    /// thread-pool thread via BackupBase's Task.Run wrapper, so no shared mutable accumulator is safe.
    /// </remarks>
    public sealed class ModuleResult
    {
        public ResultState State { get; }

        /// <summary>One line, shown to the user.</summary>
        public string Reason { get; }

        public IReadOnlyList<StepResult> Steps { get; }

        private ModuleResult(ResultState state, string reason, IReadOnlyList<StepResult> steps)
        {
            State = state;
            Reason = reason;
            Steps = steps;
        }

        /// <summary>
        /// The single construction path. Modules never fold by hand, and there are deliberately no
        /// public Succeeded/Skipped/Failed factories - one of them would be used to bypass these
        /// rules within a week, and the rules are the whole point.
        /// </summary>
        public static ModuleResult Aggregate(IReadOnlyList<StepResult> steps)
        {
            StepResult[] all = steps == null ? new StepResult[0] : steps.Where(s => s != null).ToArray();

            // Rule 1. A module that produced no steps did not decide anything. Reporting that as
            // success would be the original bug in miniature.
            if (all.Length == 0)
                return new ModuleResult(ResultState.Skipped, "nothing to do", all);

            StepResult[] failed = all.Where(s => s.State == ResultState.Failed).ToArray();
            StepResult[] skipped = all.Where(s => s.State == ResultState.Skipped).ToArray();
            StepResult[] ok = all.Where(s => s.State == ResultState.Succeeded).ToArray();

            // Rule 2. Any failure dominates. A backup missing one of its keys will restore wrong,
            // and calling that "partial" invites the user to treat it as good enough.
            if (failed.Length > 0)
            {
                string reason = string.Format(
                    "{0} of {1} operations failed: {2}",
                    failed.Length, all.Length, failed[0].Reason);

                return new ModuleResult(ResultState.Failed, reason, all);
            }

            // Rule 3. Everything was legitimately absent. This is Skipped, not Succeeded: folding
            // it up to success would claim a module was backed up having written zero bytes, which
            // is exactly what GGaming and WTelemetry do on a stock consumer machine.
            if (ok.Length == 0)
            {
                string reason = "nothing to back up: " +
                    string.Join("; ", skipped.Select(s => s.Reason).Distinct());

                return new ModuleResult(ResultState.Skipped, reason, all);
            }

            // Rule 4. Some captured, some legitimately absent. This must read as success with a
            // note - WPersonalization and WUpdates hit it on a large share of healthy machines, and
            // rendering it as a warning is the cry-wolf failure this phase exists to remove.
            //
            // Worded by what was OBTAINED, never as a bare ratio: "1 of 2 captured" under a heading
            // of "Done" reads as partial failure, reintroducing the ambiguity that justified having
            // no Partial state.
            if (skipped.Length > 0)
            {
                string reason = string.Format(
                    "captured {0}; {1} not present on this system ({2})",
                    Describe(ok), skipped.Length,
                    string.Join(", ", skipped.Select(s => s.Target)));

                return new ModuleResult(ResultState.Succeeded, reason, all);
            }

            // Rule 5.
            return new ModuleResult(ResultState.Succeeded, "captured " + Describe(ok), all);
        }

        private static string Describe(StepResult[] ok)
            => ok.Length == 1 ? ok[0].Target : ok.Length + " items";
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~ModuleResultTests`
Expected: PASS, 19 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Appcopier/Results src/Appcopier.Tests/ModuleResultTests.cs
git commit -m "Add the result types that let a module report what happened"
```

---

### Task 2: Fix the LogHelper format-string hazard

**Files:**
- Modify: `src/Appcopier/Helpers/LogHelper.cs`
- Test: `src/Appcopier.Tests/LogHelperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LogHelper.Instance.LogMessage(string message)` — logs a pre-formatted message with no `string.Format` pass. `LogHelper.Log(string format, params object[] args)` keeps its existing signature and behaviour.

`ModuleResult.Reason` will contain registry paths (`HKEY_CURRENT_USER\...`) and exception text. `AppendLog` runs `string.Format` on its input (`LogHelper.cs:48`), so a single `{` sends the line to `LogError` → `Console.WriteLine`, which in a WinForms app goes nowhere. The reason string vanishes silently. `Utils.LogQuietly` already dodges this by passing the message as an *argument*; this task makes that a first-class method.

- [ ] **Step 1: Write the failing test**

Create `src/Appcopier.Tests/LogHelperTests.cs`:

```csharp
using Appcopier;
using System.Windows.Forms;
using Xunit;

namespace Appcopier.Tests
{
    public class LogHelperTests
    {
        // A RichTextBox with a forced handle so InvokeRequired answers honestly and AppendText works.
        private static RichTextBox NewTarget()
        {
            RichTextBox box = new RichTextBox();
            System.IntPtr unused = box.Handle;   // force handle creation
            return box;
        }

        [Fact]
        public void LogMessage_TextContainingBraces_ReachesTheTarget()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            // A real reason string: a registry path plus exception text with braces in it.
            const string reason = @"could not export HKEY_CURRENT_USER\Software\{4D36E96B}: access denied";
            LogHelper.Instance.LogMessage(reason);

            Assert.Contains("4D36E96B", box.Text);
            Assert.Contains("access denied", box.Text);
        }

        [Fact]
        public void LogMessage_UnmatchedBrace_DoesNotThrowAndStillLogs()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            LogHelper.Instance.LogMessage("failed on {0 unbalanced");

            Assert.Contains("unbalanced", box.Text);
        }

        [Fact]
        public void Log_WithFormatArguments_StillFormats()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            LogHelper.Instance.Log("exported {0} keys", 3);

            Assert.Contains("exported 3 keys", box.Text);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~LogHelperTests`
Expected: FAIL — `'LogHelper' does not contain a definition for 'LogMessage'`.

- [ ] **Step 3: Add `LogMessage`**

In `src/Appcopier/Helpers/LogHelper.cs`, add this method immediately after the existing `Log` method:

```csharp
        /// <summary>
        /// Logs an already-composed message, with no <see cref="string.Format"/> pass over it.
        /// </summary>
        /// <remarks>
        /// Use this for anything whose text is data rather than a template - result reason strings,
        /// registry paths, exception messages. Log(string, params object[]) treats its first
        /// argument as a format string, so a single brace in the text throws FormatException inside
        /// AppendLog, which routes the line to Console.WriteLine - invisible in a WinForms app.
        /// The message is not lost loudly; it is lost silently, which is worse.
        /// </remarks>
        public void LogMessage(string message)
        {
            // "{0}" as the template and the caller's text as an ARGUMENT: string.Format then has
            // nothing to parse in the untrusted half.
            Log("{0}", message ?? string.Empty);
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~LogHelperTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Appcopier/Helpers/LogHelper.cs src/Appcopier.Tests/LogHelperTests.cs
git commit -m "Add LogHelper.LogMessage so reason strings with braces are not silently dropped"
```

---

### Task 3: `.reg` file validation

**Files:**
- Create: `src/Appcopier/Results/RegFile.cs`
- Test: `src/Appcopier.Tests/RegFileTests.cs`

**Interfaces:**
- Consumes: `StepResult` (Task 1).
- Produces: `Appcopier.RegFile` with `internal static RegFileCheck Validate(string path)` returning `RegFileCheck { Valid, Missing, Empty, BadHeader }`, and `internal const string Header = "Windows Registry Editor Version 5.00"`.

Measured 2026-07-20: a real `regedit /e` export is **UTF-16LE with a BOM** (`FF FE 57 00 ...`), and `File.ReadAllText` strips the BOM so `StartsWith(Header)` is true. A byte-wise ASCII compare would *not* match. The implementation is pinned to `File.ReadAllText`.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/RegFileTests.cs`:

```csharp
using Appcopier;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Appcopier.Tests
{
    public class RegFileTests : IDisposable
    {
        private readonly string _dir;

        public RegFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acreg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Write(string name, string content, Encoding encoding)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, content, encoding);
            return p;
        }

        // This is the shape regedit /e actually produces (measured 2026-07-20): UTF-16LE with BOM.
        [Fact]
        public void Validate_RealShapedExport_Utf16WithBom_IsValid()
        {
            string p = Write("ok.reg",
                "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Control Panel\\Mouse]\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.Valid, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_Utf8Export_IsAlsoValid()
        {
            string p = Write("utf8.reg",
                "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UTF8Encoding(false));

            Assert.Equal(RegFileCheck.Valid, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_MissingFile_IsMissing()
            => Assert.Equal(RegFileCheck.Missing, RegFile.Validate(Path.Combine(_dir, "nope.reg")));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_NoPath_IsMissing(string path)
            => Assert.Equal(RegFileCheck.Missing, RegFile.Validate(path));

        [Fact]
        public void Validate_EmptyFile_IsEmpty()
        {
            string p = Path.Combine(_dir, "empty.reg");
            File.WriteAllBytes(p, new byte[0]);
            Assert.Equal(RegFileCheck.Empty, RegFile.Validate(p));
        }

        // A BOM and nothing else: 2 bytes on disk, so a naive Length > 0 check passes.
        [Fact]
        public void Validate_BomOnly_IsEmpty()
        {
            string p = Path.Combine(_dir, "bomonly.reg");
            File.WriteAllBytes(p, new byte[] { 0xFF, 0xFE });
            Assert.Equal(RegFileCheck.Empty, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_WrongHeader_IsBadHeader()
        {
            string p = Write("wrong.reg", "REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_TruncatedHeader_IsBadHeader()
        {
            string p = Write("trunc.reg", "Windows Registry Ed",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_HeaderWithLeadingWhitespace_IsBadHeader()
        {
            string p = Write("lead.reg", "   Windows Registry Editor Version 5.00\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~RegFileTests`
Expected: FAIL — `The type or namespace name 'RegFile' could not be found`.

- [ ] **Step 3: Create `RegFile`**

Create `src/Appcopier/Results/RegFile.cs`:

```csharp
using System;
using System.IO;

namespace Appcopier
{
    internal enum RegFileCheck
    {
        Valid,
        Missing,
        Empty,
        BadHeader
    }

    /// <summary>
    /// Checks that a .reg file is what it claims to be.
    /// </summary>
    /// <remarks>
    /// This exists because regedit lies. Measured on Windows 11, 2026-07-20: "regedit /e" against a
    /// key that does not exist returns exit code 0 and writes no file at all. An exit code is
    /// therefore necessary but nowhere near sufficient, and the artifact itself has to be checked.
    /// </remarks>
    internal static class RegFile
    {
        internal const string Header = "Windows Registry Editor Version 5.00";

        internal static RegFileCheck Validate(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return RegFileCheck.Missing;

            string text;

            try
            {
                // File.ReadAllText detects and strips the byte order mark. A real export is UTF-16LE
                // with a BOM (measured: FF FE 57 00 ...), so a byte-wise ASCII comparison against the
                // header would NOT match. Pinned to this call deliberately.
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                // Unreadable is not the same as absent, but for the caller's purposes both mean
                // "cannot be used", and the caller reports the path either way.
                return RegFileCheck.BadHeader;
            }

            if (string.IsNullOrWhiteSpace(text))
                return RegFileCheck.Empty;

            return text.StartsWith(Header, StringComparison.OrdinalIgnoreCase)
                ? RegFileCheck.Valid
                : RegFileCheck.BadHeader;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~RegFileTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Appcopier/Results/RegFile.cs src/Appcopier.Tests/RegFileTests.cs
git commit -m "Verify .reg artifacts, because regedit exits 0 having written nothing"
```

---

### Task 4: Tri-state registry probe

**Files:**
- Modify: `src/Appcopier/Helpers/WindowsHelper.cs:106-119` (`KeyExists`, `KeyExistsInRegistry`)
- Test: `src/Appcopier.Tests/ProbeKeyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Appcopier.KeyProbe { Present, Absent, Indeterminate }`; `public static KeyProbe Utils.ProbeKey(string key)`; `public static bool Utils.KeyExists(string key)` retained as a shim returning `ProbeKey(key) == KeyProbe.Present`.

`KeyExists` becomes the Skipped-vs-Failed discriminator for the whole design, and a `bool` cannot express "I could not tell". The existing implementation also has no `try`/`catch`, so a `SecurityException` on a restricted key propagates out — tolerable when called once at tree-build time, not once per key on the backup path.

**Both mappings must exist and they differ by caller.** The backup path maps `Indeterminate → Failed`. `SelectInstalled` (`ConfPageView.cs:244-258`) maps `Indeterminate → false` via the `KeyExists` shim, because auto-checking a module you could not probe would manufacture a `Failed` row in the very dialog this phase exists to make trustworthy.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/ProbeKeyTests.cs`:

```csharp
using Appcopier;
using Xunit;

namespace Appcopier.Tests
{
    // These run unelevated against real HKCU/HKLM keys that exist on every Windows 11 install.
    public class ProbeKeyTests
    {
        [Fact]
        public void ProbeKey_CoreHkcuKey_IsPresent()
            => Assert.Equal(KeyProbe.Present, Utils.ProbeKey(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void ProbeKey_CoreHklmKey_IsPresent()
            => Assert.Equal(KeyProbe.Present,
                   Utils.ProbeKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion"));

        [Fact]
        public void ProbeKey_NonexistentKey_IsAbsent()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProbeKey_NoKey_IsAbsent(string key)
            => Assert.Equal(KeyProbe.Absent, Utils.ProbeKey(key));

        // The HKCU-probed-under-HKLM bug: the old prefix strip only removed the MATCHING base name,
        // so an HKCU path was additionally probed under HKLM with its full prefix still attached.
        [Fact]
        public void ProbeKey_HkcuPath_IsNotMatchedUnderHklm()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NoSuchSubkey"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnPresent()
            => Assert.True(Utils.KeyExists(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnAbsent()
            => Assert.False(Utils.KeyExists(@"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll"));

        // The shim must never throw - SelectInstalled calls it for every module at tree-build time.
        [Fact]
        public void KeyExists_MalformedKey_ReturnsFalseInsteadOfThrowing()
            => Assert.False(Utils.KeyExists(@"NOT_A_HIVE\whatever"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~ProbeKeyTests`
Expected: FAIL — `The type or namespace name 'KeyProbe' could not be found`.

- [ ] **Step 3: Replace `KeyExists` and `KeyExistsInRegistry`**

In `src/Appcopier/Helpers/WindowsHelper.cs`, replace the whole block from `// Reg operations` through the closing brace of `KeyExistsInRegistry` (currently lines 105-119) with:

```csharp
        // Reg operations

        /// <summary>
        /// Whether a registry key is present, absent, or could not be determined.
        /// </summary>
        /// <remarks>
        /// The third state is the point. This method is the Skipped-vs-Failed discriminator for the
        /// whole backup path, and "I could not tell" is a failure of the tool, not an absence of the
        /// data - reporting a permission-denied probe as Absent would silently downgrade a real
        /// failure into a reassuring "not present on this system".
        /// </remarks>
        public static KeyProbe ProbeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return KeyProbe.Absent;

            KeyProbe hkcu = ProbeUnder(key, Registry.CurrentUser);
            if (hkcu != KeyProbe.Absent)
                return hkcu;

            return ProbeUnder(key, Registry.LocalMachine);
        }

        /// <summary>
        /// Convenience wrapper for callers that only need a yes/no and must never throw.
        /// </summary>
        /// <remarks>
        /// Indeterminate deliberately maps to FALSE here, the opposite of the backup path's mapping.
        /// The only caller is the IsInstalled() tree-build (ConfPageView.SelectInstalled), and
        /// auto-checking a module whose keys could not be probed would manufacture a Failed row in
        /// the very summary this phase exists to make trustworthy.
        /// </remarks>
        public static bool KeyExists(string key)
            => ProbeKey(key) == KeyProbe.Present;

        private static KeyProbe ProbeUnder(string key, RegistryKey baseKey)
        {
            string prefix = baseKey.Name + "\\";

            // Only probe under this hive if the path actually names it. The previous implementation
            // stripped only the matching prefix and then probed the remainder under BOTH hives, so
            // an HKCU path was also looked up under HKLM with "HKEY_CURRENT_USER\" still attached -
            // always null, so the HKLM half of the check was dead for every HKCU input.
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return KeyProbe.Absent;

            string subKey = key.Substring(prefix.Length);

            try
            {
                using (RegistryKey opened = baseKey.OpenSubKey(subKey))
                {
                    return opened != null ? KeyProbe.Present : KeyProbe.Absent;
                }
            }
            catch (System.Security.SecurityException)
            {
                return KeyProbe.Indeterminate;
            }
            catch (UnauthorizedAccessException)
            {
                return KeyProbe.Indeterminate;
            }
            catch (Exception)
            {
                // A malformed path or an unexpected provider error. Not knowing is the honest answer.
                return KeyProbe.Indeterminate;
            }
        }
```

- [ ] **Step 4: Add the `KeyProbe` enum**

Create `src/Appcopier/Results/KeyProbe.cs`:

```csharp
namespace Appcopier
{
    /// <summary>
    /// The outcome of looking for a registry key. See <see cref="Utils.ProbeKey"/> for why the
    /// third state matters.
    /// </summary>
    public enum KeyProbe
    {
        Present,
        Absent,
        Indeterminate
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~ProbeKeyTests`
Expected: PASS, 10 tests.

- [ ] **Step 6: Verify the whole suite still passes**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS. `KeyExists` kept its signature, so the 16 `IsInstalled()` call sites compile unchanged.

- [ ] **Step 7: Commit**

```bash
git add src/Appcopier/Helpers/WindowsHelper.cs src/Appcopier/Results/KeyProbe.cs src/Appcopier.Tests/ProbeKeyTests.cs
git commit -m "Make the registry probe tri-state so denied is not reported as absent"
```

---

### Task 5: Split registry export and import behind a seam

**Files:**
- Create: `src/Appcopier/Helpers/IRegistryTool.cs`
- Modify: `src/Appcopier/Helpers/WindowsHelper.cs:78-103` (replace `ExportImportRegistryKey`)
- Test: `src/Appcopier.Tests/RegistryStepTests.cs`

**Interfaces:**
- Consumes: `StepResult`, `RegFile`, `RegFileCheck`, `KeyProbe`, `Utils.ProbeKey` (Tasks 1, 3, 4).
- Produces:
  - `internal interface IRegistryTool { ProcessOutcome Export(string filePath, string registryPath); ProcessOutcome Import(string filePath); }`
  - `internal sealed class ProcessOutcome { bool Started; bool TimedOut; int ExitCode; string Error; }` with factories `Ran(int exitCode)`, `Timeout()`, `Failed(string error)`.
  - `internal static StepResult Utils.ExportRegistryKey(string filePath, string registryPath, bool absenceIsNormal, IRegistryTool tool = null)`
  - `internal static StepResult Utils.ImportRegistryKey(string filePath, string registryPath, IRegistryTool tool = null)`
  - When `tool` is null both use the real `RegeditTool`, so production call sites pass two or three arguments and tests inject a fake.

The `bool import` flag forced one swallowing implementation to serve two genuinely different contracts: export is verifiable, import is not. Splitting also drops the undocumented extra registry-key argument the import branch appended to `regedit /s` (`WindowsHelper.cs:92`).

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/RegistryStepTests.cs`:

```csharp
using Appcopier;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Appcopier.Tests
{
    public class RegistryStepTests : IDisposable
    {
        private readonly string _dir;

        public RegistryStepTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acstep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private const string PresentKey = @"HKEY_CURRENT_USER\Control Panel\Mouse";
        private const string AbsentKey = @"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll";

        private string Valid(string name)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UnicodeEncoding(false, true));
            return p;
        }

        // A tool that reports whatever the test wants and records what it was asked to do.
        private sealed class FakeTool : IRegistryTool
        {
            private readonly ProcessOutcome _outcome;
            private readonly Action<string> _onExport;

            public bool ImportCalled;
            public bool ExportCalled;

            public FakeTool(ProcessOutcome outcome, Action<string> onExport = null)
            {
                _outcome = outcome;
                _onExport = onExport;
            }

            public ProcessOutcome Export(string filePath, string registryPath)
            {
                ExportCalled = true;
                if (_onExport != null) _onExport(filePath);
                return _outcome;
            }

            public ProcessOutcome Import(string filePath)
            {
                ImportCalled = true;
                return _outcome;
            }
        }

        // --- Export ---

        [Fact]
        public void Export_AbsentKey_AbsenceNormal_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, true, tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ExportCalled);
        }

        [Fact]
        public void Export_AbsentKey_AbsenceNotNormal_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        // The measured case: regedit exits 0 and writes nothing at all.
        [Fact]
        public void Export_ExitZeroButNoFile_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "never.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("no file", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroAndValidFile_IsSucceeded()
        {
            string path = Path.Combine(_dir, "good.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0), p =>
                File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                    new UnicodeEncoding(false, true)));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        [Fact]
        public void Export_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        [Fact]
        public void Export_Timeout_IsFailedAndSaysSo()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Timeout()));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("did not exit", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // --- Import ---

        [Fact]
        public void Import_MissingFile_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(Path.Combine(_dir, "gone.reg"), "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ImportCalled);
        }

        // The registry must not be touched by a file we already know is malformed.
        [Fact]
        public void Import_MalformedFile_IsFailedBeforeTouchingTheRegistry()
        {
            string bad = Path.Combine(_dir, "bad.reg");
            File.WriteAllText(bad, "REGEDIT4\r\n", new UnicodeEncoding(false, true));

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(bad, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_EmptyFile_IsFailedBeforeTouchingTheRegistry()
        {
            string empty = Path.Combine(_dir, "empty.reg");
            File.WriteAllBytes(empty, new byte[0]);

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(empty, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_ValidFileAndZeroExit_IsSucceeded()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        // The wording rule: regedit /s returns 0 on partially-applied files, so we can only claim
        // to have applied it.
        [Fact]
        public void Import_Success_SaysAppliedAndNeverVerified()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in2.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Import_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in3.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~RegistryStepTests`
Expected: FAIL — `'Utils' does not contain a definition for 'ExportRegistryKey'`.

- [ ] **Step 3: Create the seam**

Create `src/Appcopier/Helpers/IRegistryTool.cs`:

```csharp
using System;
using System.Diagnostics;

namespace Appcopier
{
    /// <summary>
    /// What happened when we tried to run an external tool.
    /// </summary>
    internal sealed class ProcessOutcome
    {
        public bool Started { get; private set; }
        public bool TimedOut { get; private set; }
        public int ExitCode { get; private set; }
        public string Error { get; private set; }

        public static ProcessOutcome Ran(int exitCode)
            => new ProcessOutcome { Started = true, ExitCode = exitCode };

        public static ProcessOutcome Timeout()
            => new ProcessOutcome { Started = true, TimedOut = true };

        public static ProcessOutcome Failed(string error)
            => new ProcessOutcome { Started = false, Error = error };
    }

    /// <summary>
    /// The registry export/import launch, behind an interface purely so module logic can be tested.
    /// </summary>
    /// <remarks>
    /// Nothing in the test suite can assert what regedit.exe returns for a denied key or a
    /// partially-applied file - those need elevation and a real hive. This seam does not fix that;
    /// it confines it, so everything ABOVE the launch is covered and the uncovered surface is one
    /// small class.
    /// </remarks>
    internal interface IRegistryTool
    {
        ProcessOutcome Export(string filePath, string registryPath);

        ProcessOutcome Import(string filePath);
    }

    internal sealed class RegeditTool : IRegistryTool
    {
        // regedit blocking on a modal error dialog used to hang the backup thread forever, because
        // the old WaitForExit() had no timeout and nothing read the exit code afterwards.
        private const int TimeoutMs = 60000;

        public ProcessOutcome Export(string filePath, string registryPath)
            => Run(string.Format("/e \"{0}\" \"{1}\"", filePath, registryPath));

        // Note: no registry path argument. The old code appended one to /s, which documented regedit
        // syntax does not define.
        public ProcessOutcome Import(string filePath)
            => Run(string.Format("/s \"{0}\"", filePath));

        private static ProcessOutcome Run(string arguments)
        {
            try
            {
                using (Process proc = new Process())
                {
                    proc.StartInfo.FileName = "regedit.exe";
                    proc.StartInfo.Arguments = arguments;
                    proc.StartInfo.UseShellExecute = false;

                    // Deliberately no StartInfo.Verb = "runas": Verb is ignored while
                    // UseShellExecute is false, so the old line granted nothing and merely implied
                    // an elevation request that was not happening. Elevation comes from app.manifest.

                    proc.Start();

                    if (!proc.WaitForExit(TimeoutMs))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        return ProcessOutcome.Timeout();
                    }

                    return ProcessOutcome.Ran(proc.ExitCode);
                }
            }
            catch (Exception ex)
            {
                return ProcessOutcome.Failed(ex.Message);
            }
        }
    }
}
```

- [ ] **Step 4: Replace `ExportImportRegistryKey` in `Utils`**

In `src/Appcopier/Helpers/WindowsHelper.cs`, delete the entire `ExportImportRegistryKey` method (currently lines 78-103) and put this in its place:

```csharp
        private static readonly IRegistryTool DefaultRegistryTool = new RegeditTool();

        /// <summary>
        /// Exports one registry key and verifies the artifact it was supposed to produce.
        /// </summary>
        /// <remarks>
        /// The verification is not belt-and-braces. Measured on Windows 11, 2026-07-20: regedit /e
        /// against a key that does not exist returns exit code 0 and writes no file. Checking the
        /// exit code alone would report success for a backup containing nothing.
        /// </remarks>
        internal static StepResult ExportRegistryKey(string filePath, string registryPath,
                                                     bool absenceIsNormal, IRegistryTool tool = null)
        {
            tool = tool ?? DefaultRegistryTool;

            KeyProbe probe = ProbeKey(registryPath);

            if (probe == KeyProbe.Indeterminate)
                return StepResult.Failed(registryPath, "could not read " + registryPath + " to check whether it exists");

            if (probe == KeyProbe.Absent)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(registryPath, "not present on this system")
                    : StepResult.Failed(registryPath, "expected " + registryPath + " is missing");
            }

            ProcessOutcome outcome = tool.Export(filePath, registryPath);

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.ExitCode != 0)
                return StepResult.Failed(registryPath, "regedit exited with code " + outcome.ExitCode);

            switch (RegFile.Validate(filePath))
            {
                case RegFileCheck.Missing:
                    return StepResult.Failed(registryPath, "regedit reported success but wrote no file");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "regedit wrote an empty file");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the exported file is not a valid .reg file");
                default:
                    return StepResult.Succeeded(registryPath, "exported " + registryPath);
            }
        }

        /// <summary>
        /// Imports one .reg file, having first checked it is worth importing.
        /// </summary>
        /// <remarks>
        /// The pre-flight matters more than the exit code here. regedit /s returns 0 on a file it
        /// only partially applied, so a successful run is reported as "applied", never "verified" -
        /// reading the keys back to prove an import took is Phase 2b. Refusing a malformed file
        /// BEFORE launching regedit is the one strong guarantee available on this path.
        /// </remarks>
        internal static StepResult ImportRegistryKey(string filePath, string registryPath,
                                                     IRegistryTool tool = null)
        {
            tool = tool ?? DefaultRegistryTool;

            RegFileCheck check = RegFile.Validate(filePath);

            switch (check)
            {
                case RegFileCheck.Missing:
                    return StepResult.Skipped(registryPath, "nothing was backed up for this item");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "the backed-up file is empty - not importing it");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the backed-up file is not a valid .reg file - not importing it");
            }

            ProcessOutcome outcome = tool.Import(filePath);

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.ExitCode != 0)
                return StepResult.Failed(registryPath, "regedit exited with code " + outcome.ExitCode);

            return StepResult.Applied(registryPath, registryPath);
        }
```

- [ ] **Step 5: Build and expect the 16 module call sites to break**

Run: `dotnet build src\Appcopier.sln`
Expected: FAIL with `CS0117: 'Utils' does not contain a definition for 'ExportImportRegistryKey'`, roughly 32 errors across `Conf/`. This is expected — Task 8 migrates them. Do **not** fix them here.

- [ ] **Step 6: Temporarily verify the new code in isolation**

The solution will not build until Task 8, so the tests cannot run yet. Confirm the new files themselves are syntactically sound by checking that every error in the Step 5 output is a `CS0117` in `src/Appcopier/Conf/` and none is in `Helpers/` or `Results/`:

Run: `dotnet build src\Appcopier.sln 2>&1 | grep -E "error" | grep -v "Conf" | head -20`
Expected: no output.

- [ ] **Step 7: Commit the work-in-progress**

This commit does not build on its own. That is deliberate and it is the only such commit in the plan: `Backup(string)` cannot return `ModuleResult` and `void` simultaneously, so the contract change and its 23 call sites are atomic. Task 8 completes it.

```bash
git add src/Appcopier/Helpers/IRegistryTool.cs src/Appcopier/Helpers/WindowsHelper.cs src/Appcopier.Tests/RegistryStepTests.cs
git commit -m "Split registry export from import and verify their artifacts

Does not build on its own: the 23 Conf modules still call the old
ExportImportRegistryKey. The next commit migrates them. Kept separate
because the split and its rationale are reviewable on their own, and
squashing them would bury a 25-file mechanical change in a design change."
```

---

### Task 6: Folder copy reports a tally

**Files:**
- Modify: `src/Appcopier/Helpers/WindowsHelper.cs:15-64` (`CopyFolder`), delete `CopyFile` (lines 66-76)
- Create: `src/Appcopier/Results/CopyResult.cs`
- Test: `src/Appcopier.Tests/CopyFolderTests.cs`

**Interfaces:**
- Consumes: `StepResult` (Task 1).
- Produces: `internal sealed class CopyResult { bool SourceMissing; int FilesCopied; int FilesFailed; long BytesCopied; string FirstError; StepResult ToStep(string target, bool absenceIsNormal); }`; `internal static Task<CopyResult> Utils.CopyFolder(string source, string destination)`.

Three distinct failures currently produce an identical normally-completing `Task`: source missing (`:22-26`), per-file exception (`:48-51`), outer exception (`:60-63`).

Per the spec's Decision 2, **any** file failure makes the module `Failed`. No threshold, no cache allowlist.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/CopyFolderTests.cs`:

```csharp
using Appcopier;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    public class CopyFolderTests : IDisposable
    {
        private readonly string _root;

        public CopyFolderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "accopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Dir(string name)
        {
            string p = Path.Combine(_root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        [Fact]
        public async Task CopyFolder_MissingSource_ReportsSourceMissing()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst1"));

            Assert.True(r.SourceMissing);
            Assert.Equal(0, r.FilesCopied);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToSkippedWhenAbsenceIsNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst2"));

            Assert.Equal(ResultState.Skipped, r.ToStep("Chrome", true).State);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToFailedWhenAbsenceIsNotNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst3"));

            Assert.Equal(ResultState.Failed, r.ToStep("Themes", false).State);
        }

        [Fact]
        public async Task CopyFolder_EmptySource_CopiesNothingAndIsSkipped()
        {
            CopyResult r = await Utils.CopyFolder(Dir("emptysrc"), Dir("dst4"));

            Assert.Equal(0, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(ResultState.Skipped, r.ToStep("Empty", true).State);
        }

        [Fact]
        public async Task CopyFolder_NestedTree_CopiesEveryFile()
        {
            string src = Dir("src5");
            Directory.CreateDirectory(Path.Combine(src, "a", "b"));
            File.WriteAllText(Path.Combine(src, "top.txt"), "1");
            File.WriteAllText(Path.Combine(src, "a", "mid.txt"), "22");
            File.WriteAllText(Path.Combine(src, "a", "b", "deep.txt"), "333");

            string dst = Path.Combine(_root, "dst5");
            CopyResult r = await Utils.CopyFolder(src, dst);

            Assert.Equal(3, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(6, r.BytesCopied);
            Assert.True(File.Exists(Path.Combine(dst, "a", "b", "deep.txt")));
            Assert.Equal(ResultState.Succeeded, r.ToStep("Tree", false).State);
        }

        // A locked file is the browser-profile case, made deterministic.
        [Fact]
        public async Task CopyFolder_LockedFile_CountsTheFailureAndKeepsGoing()
        {
            string src = Dir("src6");
            File.WriteAllText(Path.Combine(src, "fine.txt"), "ok");
            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst6"));

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
        }

        // Decision 2 of the spec: any file failure is a failed module. No threshold.
        [Fact]
        public async Task CopyFolder_OneLockedFileAmongMany_IsFailedNotPartial()
        {
            string src = Dir("src7");
            for (int i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(src, "f" + i + ".txt"), "x");

            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst7"));
                StepResult s = r.ToStep("Chrome", true);

                Assert.Equal(ResultState.Failed, s.State);
                Assert.Contains("1", s.Reason);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build src\Appcopier.sln`
Expected: FAIL — `CopyResult` not found (plus the expected Task 5 `Conf/` errors).

- [ ] **Step 3: Create `CopyResult`**

Create `src/Appcopier/Results/CopyResult.cs`:

```csharp
namespace Appcopier
{
    /// <summary>
    /// The tally from one folder copy.
    /// </summary>
    /// <remarks>
    /// CopyFolder previously returned a plain Task, so a missing source, a per-file exception and
    /// an outer exception were indistinguishable from a clean copy: all three completed normally.
    /// No module could be honest until this carried counts.
    /// </remarks>
    internal sealed class CopyResult
    {
        public bool SourceMissing { get; set; }
        public int FilesCopied { get; set; }
        public int FilesFailed { get; set; }
        public long BytesCopied { get; set; }
        public string FirstError { get; set; }

        /// <summary>
        /// Maps the tally onto a step outcome.
        /// </summary>
        /// <remarks>
        /// Any failed file fails the whole step - deliberately, per the Phase 2a design. There is no
        /// tolerated-subtree allowlist and no ratio threshold: a browser profile missing Login Data
        /// and History is not a usable backup regardless of how few files that is. The browser
        /// modules will therefore read Failed whenever the browser was running, which is the
        /// intended signal, not a regression.
        /// </remarks>
        public StepResult ToStep(string target, bool absenceIsNormal)
        {
            if (SourceMissing)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(target, "not present on this system")
                    : StepResult.Failed(target, "expected folder for " + target + " is missing");
            }

            if (FilesFailed > 0)
            {
                string reason = string.Format(
                    "{0} of {1} files could not be copied: {2}",
                    FilesFailed, FilesFailed + FilesCopied, FirstError);

                return StepResult.Failed(target, reason);
            }

            if (FilesCopied == 0)
                return StepResult.Skipped(target, "there was nothing to copy");

            return StepResult.Succeeded(target,
                string.Format("copied {0} file(s)", FilesCopied));
        }
    }
}
```

- [ ] **Step 4: Rewrite `CopyFolder` and delete `CopyFile`**

In `src/Appcopier/Helpers/WindowsHelper.cs`, replace `CopyFolder` (lines 15-64) and delete `CopyFile` entirely (lines 66-76 — verified zero callers in `src`):

```csharp
        internal static async Task<CopyResult> CopyFolder(string source, string destination)
        {
            CopyResult result = new CopyResult();
            await CopyFolderInto(source, destination, result).ConfigureAwait(false);
            return result;
        }

        private static async Task CopyFolderInto(string source, string destination, CopyResult result)
        {
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(source);

                if (!sourceDir.Exists)
                {
                    // Only the top-level call can legitimately find nothing; a missing subdirectory
                    // mid-recursion would already have been enumerated, so it cannot happen here.
                    result.SourceMissing = true;
                    logger.LogMessage("Source directory does not exist: " + source);
                    return;
                }

                DirectoryInfo destinationDir = new DirectoryInfo(destination);

                if (!destinationDir.Exists)
                    destinationDir.Create();

                foreach (FileInfo file in sourceDir.GetFiles())
                {
                    string destinationFilePath = Path.Combine(destinationDir.FullName, file.Name);

                    try
                    {
                        using (FileStream sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                        using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            // ConfigureAwait(false) so an aggregating caller is not marshalled back
                            // to the UI thread once per file across a browser profile.
                            await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                        }

                        result.FilesCopied++;
                        result.BytesCopied += file.Length;
                    }
                    catch (Exception ex)
                    {
                        result.FilesFailed++;
                        if (result.FirstError == null)
                            result.FirstError = file.Name + ": " + ex.Message;

                        logger.LogMessage("Error copying file " + file.FullName + ": " + ex.Message);
                    }
                }

                foreach (DirectoryInfo subDirectory in sourceDir.GetDirectories())
                {
                    string newDestinationPath = Path.Combine(destinationDir.FullName, subDirectory.Name);
                    await CopyFolderInto(subDirectory.FullName, newDestinationPath, result).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Enumeration or directory creation failed. Counted as a failure rather than
                // swallowed, so the caller cannot mistake it for an empty folder.
                result.FilesFailed++;
                if (result.FirstError == null)
                    result.FirstError = ex.Message;

                logger.LogMessage("Error copying folder " + source + " to " + destination + ": " + ex.Message);
            }
        }
```

- [ ] **Step 5: Verify no `Helpers/` errors remain**

Run: `dotnet build src\Appcopier.sln 2>&1 | grep -E "error" | grep -v "Conf" | head -20`
Expected: no output. All remaining errors are the `Conf/` call sites Task 8 fixes.

- [ ] **Step 6: Commit**

```bash
git add src/Appcopier/Results/CopyResult.cs src/Appcopier/Helpers/WindowsHelper.cs src/Appcopier.Tests/CopyFolderTests.cs
git commit -m "Make folder copy return a tally instead of an indistinguishable Task"
```

---

### Task 7: Guard the process helpers

**Files:**
- Modify: `src/Appcopier/Helpers/WindowsHelper.cs` — `IsProcessRunning`, `CloseProcess`, `RunWT`
- Create: `src/Appcopier/Helpers/IProcessRunner.cs`

**Interfaces:**
- Consumes: `ProcessOutcome` (Task 5).
- Produces: `public enum CloseResult { NotRunning, Exited, StillRunning, AccessDenied }`; `public static CloseResult Utils.CloseProcess(string processName)`; `internal static Task<ProcessOutcome> Utils.RunWTAsync(string args)`.

`Process.Kill()` at `WindowsHelper.cs:192` is unguarded and throws on access-denied or on a process that exited between enumeration and the call. The three browser modules reach it from an `async void` handler, so it is a live path to an unhandled UI-thread exception that aborts the whole run — that is why the **guard** is in this phase.

Per the spec, the **bounded `WaitForExit` is not**: it changes what gets copied rather than what gets reported, and belongs with the browser-module work in 2b.

`RunWT` is `async void`, which is structurally incapable of feeding a result — it returns to its caller at the first `await`, so `AStoreApps` logs success before winget has started.

- [ ] **Step 1: Replace the three methods**

In `src/Appcopier/Helpers/WindowsHelper.cs`, replace `IsProcessRunning`, `CloseProcess` and `RunWT`:

```csharp
        // Check for running processes in Confs
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);

                try
                {
                    return processes.Length > 0;
                }
                finally
                {
                    foreach (Process p in processes)
                        p.Dispose();
                }
            }
            catch (Exception)
            {
                // A false negative here only means the user is not prompted to close the app.
                return false;
            }
        }

        /// <summary>
        /// Asks every instance of a process to terminate.
        /// </summary>
        /// <remarks>
        /// The guard is the point. Kill() throws InvalidOperationException when the process exited
        /// between enumeration and the call - likely, not exotic, because Chrome is a whole tree of
        /// child processes that come and go - and Win32Exception when access is denied. The browser
        /// modules reach this from an async void click handler, so an escape here took down the
        /// entire run and every result collected with it.
        ///
        /// Deliberately does NOT wait for exit. Adding a bounded wait would change what gets copied
        /// (a still-flushing browser holds its SQLite files), which is a reliability fix rather than
        /// a reporting one, and it belongs with the browser-module work.
        /// </remarks>
        public static CloseResult CloseProcess(string processName)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return CloseResult.AccessDenied;
            }

            if (processes.Length == 0)
                return CloseResult.NotRunning;

            CloseResult worst = CloseResult.Exited;

            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = CloseResult.AccessDenied;
                }
                catch (InvalidOperationException)
                {
                    // Already gone between enumeration and Kill. Nothing to report.
                }
                catch (Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = CloseResult.StillRunning;
                }
                finally
                {
                    process.Dispose();
                }
            }

            return worst;
        }

        /// <summary>
        /// Runs Windows Terminal and waits for it, reporting how it went.
        /// </summary>
        /// <remarks>
        /// Replaces an "async void" version that returned to its caller at the first await, so
        /// AStoreApps logged "Backup successful" before winget had started. async void cannot feed a
        /// result into anything - that is not a style preference here, it is the reason the module
        /// could not report the truth.
        /// </remarks>
        internal static async Task<ProcessOutcome> RunWTAsync(string args)
        {
            if (!File.Exists(DataHelper.Data.ShellWT))
                return ProcessOutcome.Failed("Windows Terminal is not installed");

            return await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = DataHelper.Data.ShellWT,
                        Arguments = args,
                        // The old WorkingDirectory was Data.DataRootDir, which may not exist yet -
                        // Process.Start then threw Win32Exception onto the sync context.
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    using (Process proc = Process.Start(startInfo))
                    {
                        if (proc == null)
                            return ProcessOutcome.Failed("Windows Terminal did not start");

                        proc.WaitForExit();
                        return ProcessOutcome.Ran(proc.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    return ProcessOutcome.Failed(ex.Message);
                }
            }).ConfigureAwait(false);
        }
```

- [ ] **Step 2: Add the `CloseResult` enum**

Create `src/Appcopier/Results/CloseResult.cs`:

```csharp
namespace Appcopier
{
    public enum CloseResult
    {
        NotRunning,
        Exited,
        StillRunning,
        AccessDenied
    }
}
```

- [ ] **Step 3: Verify no `Helpers/` errors remain**

Run: `dotnet build src\Appcopier.sln 2>&1 | grep -E "error" | grep -v "Conf" | head -20`
Expected: no output.

- [ ] **Step 4: Commit**

```bash
git add src/Appcopier/Helpers/WindowsHelper.cs src/Appcopier/Results/CloseResult.cs
git commit -m "Guard Process.Kill and give RunWT a return value

Kill() was unguarded on a path reachable from an async void click handler,
so a child process exiting between enumeration and the call took down the
whole run. RunWT was async void, which cannot report anything to anyone."
```

---

### Task 8: The contract change

**Files:**
- Modify: `src/Appcopier/BackupBase.cs`
- Modify: all 23 files in `src/Appcopier/Conf/`
- Modify: `src/Appcopier/Views/ConfPageView.cs:140,189`
- Test: `src/Appcopier.Tests/ModuleShapeTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: `public virtual ModuleResult BackupBase.Backup(string path)`, `Restore(string path)`, `public virtual Task<ModuleResult> BackupAsync(string path)`, `RestoreAsync(string path)`.

This is the atomic task. `Backup(string)` cannot return `void` and `ModuleResult` at once, so the base class and all 23 modules move in one commit. It is mechanical — every module now composes `StepResult`s from the helpers built in Tasks 5-7 and returns `ModuleResult.Aggregate`.

**Do not retype any registry key, folder path, or `Title` string.** Keep the existing `Key` / `Keys` / `Folder` field values exactly as they are. Changing a `Title` or a filename expression would make existing v0.30.0 backups unrestorable.

- [ ] **Step 1: Change `BackupBase`**

Replace `src/Appcopier/BackupBase.cs` entirely:

```csharp
using System;
using System.Threading.Tasks;

namespace Appcopier
{
    public abstract class BackupBase
    {
        // Property to indicate whether a restart is required
        public virtual bool RequiresExplorerRestart { get; protected set; } = false;

        // Property to display Hints
        public virtual string WarningMessage { get; protected set; } = "";

        // Property to display Info
        public string Title { get; set; }

        public string Info { get; set; }
        public string Version { get; set; }

        public virtual bool IsInstalled()
        { return false; }

        /// <remarks>
        /// The default is a FAILURE, not a Skip. It is unreachable for all 23 shipped modules -
        /// ConfPageView only ever calls the async pair, and every module implements one side or the
        /// other - so it fires only for a future module whose author forgot to implement backup.
        /// That is a bug, and a bug that announces itself beats one that returns a reassuring
        /// "nothing to do" and is never noticed.
        /// </remarks>
        public virtual ModuleResult Backup(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement backup")
            });

        public virtual ModuleResult Restore(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement restore")
            });

        public virtual async Task<ModuleResult> BackupAsync(string path)
        {
            return await Task.Run(() => Backup(path)).ConfigureAwait(true);
        }

        public virtual async Task<ModuleResult> RestoreAsync(string path)
        {
            return await Task.Run(() => Restore(path)).ConfigureAwait(true);
        }
    }
}
```

- [ ] **Step 2: Migrate the 10 shape-S1 modules**

Every S1 module has exactly one registry key and writes `{Title}.reg`. Here is `WAccessibility` in full — the reference implementation:

```csharp
using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class WAccessibility : BackupBase
    {
        public string Key = @"HKEY_CURRENT_USER\Control Panel\Accessibility";

        // Whether this key can legitimately be missing on a healthy Windows 11 install. Core
        // per-profile Control Panel key, so its absence means something is wrong.
        private const bool AbsenceIsNormal = false;

        public WAccessibility()
        {
            Title = "Accessibility";
            Info = "This will back up Windows Accessibility settings.";
        }

        public override bool IsInstalled()
        {
            return Utils.KeyExists(Key);
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>
            {
                Utils.ExportRegistryKey(FileFor(path), Key, AbsenceIsNormal)
            };

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>
            {
                Utils.ImportRegistryKey(FileFor(path), Key)
            };

            return ModuleResult.Aggregate(steps);
        }

        // Path.Combine rather than concatenation. Produces byte-identical paths today because
        // Data.DataRootDir and RestPageView both hand us a trailing separator, but that is a field
        // contract to honour, not a coincidence to depend on.
        private string FileFor(string path) => Path.Combine(path, Title + ".reg");
    }
}
```

Apply that exact structure to the other nine, changing only the class name and the `AbsenceIsNormal`
value. **Keep each module's existing `Key` value, `Title` and `Info` verbatim** — read them from the
file, do not retype them.

| Module | `AbsenceIsNormal` | Why |
| --- | --- | --- |
| `WAccessibility` | `false` | core per-profile Control Panel key |
| `DMouse` | `false` | core per-profile Control Panel key |
| `DKeyboard` | `false` | core per-profile Control Panel key |
| `WTaskbar` | `false` | core shell key; absence means a broken profile |
| `WAPrivacy` | `false` | CapabilityAccessManager ConsentStore, present on Windows 11 |
| `WOther` | `false` | HKLM policies key holding the UAC values |
| `WPrivacy` | `true` | **unverified judgement** — expected on mainstream Win11, plausibly absent on LTSC or debloated images |
| `WVisualEffects` | `true` | **unverified judgement** — the borderline call in this set |
| `DUSB` | `true` | narrow shell-notification key |
| `DTouchpad` | `true` | absent by design on every desktop PC |

While in `DMouse.cs:12` and `DKeyboard.cs:12`, fix the `Info` typo "This will the backup ... settigs" → "This will back up ... settings".

- [ ] **Step 3: Migrate the 5 shape-S2 modules**

These loop over a `List<string> Keys` and write `{Title}_{GetSafeFileName(key)}.reg`. One `StepResult` per key. Here is `WPersonalization` in full:

```csharp
        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string outputFileName = Path.Combine(path, $"{Title}_{GetSafeFileName(k)}.reg");
                steps.Add(Utils.ExportRegistryKey(outputFileName, k, AbsenceIsNormal(k)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string inputFileName = Path.Combine(path, $"{Title}_{GetSafeFileName(k)}.reg");
                steps.Add(Utils.ImportRegistryKey(inputFileName, k));
            }

            return ModuleResult.Aggregate(steps);
        }

        // Per-key, and it CANNOT be inferred from IsInstalled(): that returns true as soon as any
        // one key exists, so "installed" says nothing about the others. Explorer\Accent is the
        // canonical legitimately-absent key - treating it as a failure would mark this module red
        // on a large share of perfectly healthy machines.
        private static bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\Accent", System.StringComparison.OrdinalIgnoreCase);
```

For the other four, the same shape with these per-key rules:

| Module | Key ending | `AbsenceIsNormal` |
| --- | --- | --- |
| `WTelemetry` | `...\DataCollection` (policy) | `true` — absent on clean Home/Pro |
| `WTelemetry` | `...\DiagTrack` (service) | `true` — routinely removed by debloat scripts |
| `WUpdates` | `...\CurrentVersion\WindowsUpdate` | `false` — core servicing key |
| `WUpdates` | `...\WindowsUpdate\AU` (policy) | `true` — WSUS/Group Policy only |
| `DPrinters` | `HKEY_CURRENT_USER\Printers` | `true` — per-user, lazily populated |
| `DPrinters` | `HKEY_LOCAL_MACHINE\...\Print\Printers` | `false` — created by the spooler on every install |
| `GGaming` | both (GameBar, GameDVR) | `true` — commonly removed or disabled by policy |

`WTelemetry` and `GGaming` are the modules that will hit aggregation rule 3 (all-Skipped → `Skipped`)
on a stock consumer machine. That is correct behaviour, not a bug to work around.

- [ ] **Step 4: Migrate `APinnedApps` (S3) and `WThemes` (S5)**

Both override only the async pair. `APinnedApps`:

```csharp
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true) });
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true) });
        }
```

`WThemes` performs three heterogeneous sub-operations — two folder copies and one registry export —
so it is the real exercise of `Aggregate`. Collect all three `StepResult`s into one list and fold
once. Its backup sources use `AbsenceIsNormal = false` (both ship with Windows or are created at
first logon); its restore folders use `true`. Remove the now-unreachable `catch` at `WThemes.cs:73`
— `CopyFolder` no longer throws, it returns counts.

- [ ] **Step 5: Migrate the 3 shape-S4 browser modules**

All three are the same code with the folder and process name swapped. `BGoogleChrome`:

```csharp
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            if (Utils.IsProcessRunning("chrome"))
            {
                DialogResult answer = MessageBox.Show(
                    "The Chrome process is currently running. Do you want to close it before backup?",
                    "Process Running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    // Previously a bare "return", reported to the user as "Back up done." This is
                    // the canonical Skipped case in the codebase: a deliberate user choice, not an
                    // error, and not a success either.
                    steps.Add(StepResult.Skipped(Title, "you chose not to close Chrome, so it was not backed up"));
                    return ModuleResult.Aggregate(steps);
                }

                CloseResult closed = Utils.CloseProcess("chrome");

                if (closed == CloseResult.AccessDenied || closed == CloseResult.StillRunning)
                {
                    steps.Add(StepResult.Failed(Title, "Chrome could not be closed, so its files are still locked"));
                    return ModuleResult.Aggregate(steps);
                }
            }

            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));
            steps.Add(copy.ToStep(Title, true));

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true) });
        }
```

`BMozillaFirefox` uses process name `firefox` and its existing `Folder`; `BMicrosoftEdge` uses
`msedge`. For Edge, word the absent-folder reason as *"no Edge profile data found"* rather than
*"Edge is not installed"* — absence there usually means the browser was never launched.

- [ ] **Step 6: Migrate `WNetworkConf` and `AStoreApps`**

`WNetworkConf` already consumes its exit code correctly (`:24`, `:27`) — keep that logic and wrap the
outcome in `StepResult`s. Backup succeeds only when `exitCode == 0 && File.Exists(filePath) && new
FileInfo(filePath).Length > 0`. Restore maps a missing file to `Skipped` and reports success as
*applied*.

**Leave the `StreamWriter(outputFilePath)` defect at `WNetworkConf.cs:83` in place.** It throws
`ArgumentNullException` on every restore because `:48` passes `null`, and it is already caught and
logged as a failure — so it is broken, not dishonest, and it belongs to Phase 2c. It will now report
`Failed` loudly, which is accurate.

`AStoreApps` backup uses `await Utils.RunWTAsync(...)` and then verifies the artifact: the `.json`
exists, is non-empty, and parses with a `Sources[0].Packages` array (the shape `RestAppsForm.cs:81-83`
reads). Its restore returns:

```csharp
            return ModuleResult.Aggregate(new[]
            {
                StepResult.Skipped(Title, "handled interactively in the app restore dialog")
            });
```

`AStoreApps` restores nothing itself — it opens `RestAppsForm`, whose installs happen later. Claiming
a result it does not have would be a new lie in a phase built to remove them.

- [ ] **Step 7: Update the two call sites in `ConfPageView`**

At `ConfPageView.cs:140`, capture the result. At `:189`, the same. Both loops get a per-module
`try`/`catch` implementing aggregation rule 6. Task 9 consumes the collected results; for now store
them in a local `List<ModuleResult>` so the file compiles:

```csharp
                    ModuleResult outcome;

                    try
                    {
                        outcome = await a.BackupAsync(CurrentBackupPath);
                    }
                    catch (Exception ex)
                    {
                        // Rule 6. Mandatory, not defensive style: this loop is driven by an
                        // async void click handler, so an escaping exception is unhandled and
                        // takes the process down along with every result gathered so far.
                        outcome = ModuleResult.Aggregate(new[]
                        {
                            StepResult.Failed(a.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                        });
                    }

                    results.Add(outcome);
```

Do the same at `:189` inside `PerformRestoration`, and have it return `List<ModuleResult>`. **That
loop matters more than the backup one:** it is awaited by `HandleRestorationAfterSelection`, which is
awaited from a different file's `async void` (`RestPageView.cs:49,62`), and it contains
`AStoreApps.Restore` opening a dialog from a thread-pool thread with no message pump.

- [ ] **Step 8: Build until green**

Run: `dotnet build src\Appcopier.sln`
Expected: `0 Error(s)`. Pre-existing warnings only (2 × `SYSLIB0014`, 1 × `WFAC010`).

- [ ] **Step 9: Write the module shape tests**

Create `src/Appcopier.Tests/ModuleShapeTests.cs`:

```csharp
using Appcopier;
using Conf;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    // These exercise the module shapes against the real registry, unelevated, using keys whose
    // presence or absence is knowable. They cover the SHAPE of a module's decision, not regedit.
    public class ModuleShapeTests
    {
        [Fact]
        public void EveryRegisteredModule_HasATitle()
        {
            foreach (BackupBase m in new BackupBase[]
            {
                new WAccessibility(), new DMouse(), new DKeyboard(), new WTaskbar(),
                new WAPrivacy(), new WOther(), new WPrivacy(), new WVisualEffects(),
                new DUSB(), new DTouchpad(), new WPersonalization(), new WTelemetry(),
                new WUpdates(), new DPrinters(), new GGaming(), new APinnedApps(),
                new BMozillaFirefox(), new BMicrosoftEdge(), new BGoogleChrome(),
                new WThemes(), new WNetworkConf(), new CWiFiConf(), new AppStoreApps()
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Title));
            }
        }

        // The base default must be a failure, not a reassuring skip.
        private sealed class ForgetfulModule : BackupBase
        {
            public ForgetfulModule() { Title = "Forgetful"; }
        }

        [Fact]
        public void BackupBase_UnimplementedBackup_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Backup("C:\\nowhere").State);

        [Fact]
        public void BackupBase_UnimplementedRestore_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Restore("C:\\nowhere").State);

        [Fact]
        public async Task BackupBase_AsyncWrapper_CarriesTheResultThrough()
        {
            ModuleResult r = await new ForgetfulModule().BackupAsync("C:\\nowhere");
            Assert.Equal(ResultState.Failed, r.State);
        }

        // Restoring from a folder containing no .reg file must be Skipped, not a false success.
        [Fact]
        public void S1Module_RestoreWithNoBackedUpFile_IsSkipped()
        {
            ModuleResult r = new DMouse().Restore(System.IO.Path.GetTempPath());
            Assert.Equal(ResultState.Skipped, r.State);
        }
    }
}
```

- [ ] **Step 10: Run the full suite**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS, all tests including the 48 pre-existing ones.

- [ ] **Step 11: Commit**

```bash
git add src/Appcopier/BackupBase.cs src/Appcopier/Conf src/Appcopier/Views/ConfPageView.cs src/Appcopier.Tests/ModuleShapeTests.cs
git commit -m "Return a result from every backup and restore module

Completes the contract change begun in the previous two commits: Backup and
Restore now return ModuleResult, so all 23 modules move together. Mechanical
for 18 of them; the browser modules gain the Skipped case for a declined
close prompt, which was previously a bare return reported as success."
```

---

### Task 9: The honest run summary

**Files:**
- Modify: `src/Appcopier/Views/ConfPageView.cs`
- Create: `src/Appcopier/Results/RunSummary.cs`
- Test: `src/Appcopier.Tests/RunSummaryTests.cs`

**Interfaces:**
- Consumes: `ModuleResult` (Task 1).
- Produces: `internal enum RunState { Problems, Done, NothingDone, DidNotRun }`; `internal sealed class RunSummary` with `RunState State`, `string Headline`, `string Detail`, `MessageBoxIcon Icon`, and `internal static RunSummary For(IReadOnlyList<ModuleResult> results, bool ran, string verb)`.

Four states replace the two the app has. **Skipped counts are never summed into the failure count.**

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/RunSummaryTests.cs`:

```csharp
using Appcopier;
using System.Collections.Generic;
using Xunit;

namespace Appcopier.Tests
{
    public class RunSummaryTests
    {
        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skip() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Bad() => ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") });

        [Fact]
        public void AnyFailure_IsProblems()
            => Assert.Equal(RunState.Problems,
                   RunSummary.For(new List<ModuleResult> { Ok(), Bad() }, true, "Back up").State);

        [Fact]
        public void AllSucceeded_IsDone()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleResult> { Ok(), Ok() }, true, "Back up").State);

        [Fact]
        public void SucceededPlusSkipped_IsDoneNotProblems()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleResult> { Ok(), Skip() }, true, "Back up").State);

        // The whole point: absences must never be counted as failures.
        [Fact]
        public void SucceededPlusSkipped_HeadlineDoesNotClaimAProblem()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Ok(), Skip() }, true, "Back up");

            Assert.DoesNotContain("problem", s.Headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fail", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AllSkipped_IsNothingDone()
            => Assert.Equal(RunState.NothingDone,
                   RunSummary.For(new List<ModuleResult> { Skip(), Skip() }, true, "Back up").State);

        // The old code said "Back up done." here. It must not.
        [Fact]
        public void AllSkipped_NeverSaysDone()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Skip(), Skip() }, true, "Back up");
            Assert.DoesNotContain("Back up done", s.Headline);
        }

        // The silent no-op at ConfPageView.cs:185.
        [Fact]
        public void NotRun_IsDidNotRun()
            => Assert.Equal(RunState.DidNotRun,
                   RunSummary.For(new List<ModuleResult>(), false, "Restore").State);

        [Fact]
        public void NotRun_SaysItDidNotRun()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult>(), false, "Restore");
            Assert.Contains("did not run", s.Detail, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Problems_DetailNamesEveryFailedModule()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Bad(), Bad(), Ok() }, true, "Back up");
            Assert.Contains("access denied", s.Detail);
            Assert.Contains("2", s.Headline);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~RunSummaryTests`
Expected: FAIL — `The type or namespace name 'RunSummary' could not be found`.

- [ ] **Step 3: Create `RunSummary`**

Create `src/Appcopier/Results/RunSummary.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Appcopier
{
    internal enum RunState
    {
        Problems,
        Done,
        NothingDone,
        DidNotRun
    }

    /// <summary>
    /// What to tell the user after a whole backup or restore run.
    /// </summary>
    /// <remarks>
    /// Four states where the app previously had one message. Kept out of the view so it can be
    /// tested: the wording IS the deliverable of this phase, and asserting on it in xUnit is the
    /// only way it stays honest as modules change.
    /// </remarks>
    internal sealed class RunSummary
    {
        public RunState State { get; private set; }
        public string Headline { get; private set; }
        public string Detail { get; private set; }

        public MessageBoxIcon Icon
            => State == RunState.Problems ? MessageBoxIcon.Warning : MessageBoxIcon.Information;

        internal static RunSummary For(IReadOnlyList<ModuleResult> results, bool ran, string verb)
        {
            if (!ran)
            {
                return new RunSummary
                {
                    State = RunState.DidNotRun,
                    Headline = verb + " did not run.",
                    Detail = verb + " did not run because the backup folder could not be found."
                };
            }

            ModuleResult[] all = (results ?? new List<ModuleResult>()).Where(r => r != null).ToArray();

            ModuleResult[] failed = all.Where(r => r.State == ResultState.Failed).ToArray();
            ModuleResult[] ok = all.Where(r => r.State == ResultState.Succeeded).ToArray();
            ModuleResult[] skipped = all.Where(r => r.State == ResultState.Skipped).ToArray();

            if (failed.Length > 0)
            {
                return new RunSummary
                {
                    State = RunState.Problems,
                    Headline = string.Format("{0} of {1} items had problems.", failed.Length, all.Length),
                    Detail = string.Join("\r\n", failed.Select(r => "  - " + r.Reason))
                };
            }

            if (ok.Length == 0)
            {
                return new RunSummary
                {
                    State = RunState.NothingDone,
                    Headline = "Nothing was backed up.",
                    Detail = "None of the selected items were present on this system."
                };
            }

            // Skipped items are reported, but never as a problem and never added to a failure
            // count. Absences are the normal state of a real machine.
            string detail = string.Join("\r\n", ok.Select(r => "  - " + r.Reason));

            if (skipped.Length > 0)
            {
                detail += string.Format("\r\n\r\n{0} item(s) had nothing to back up on this system.",
                    skipped.Length);
            }

            return new RunSummary
            {
                State = RunState.Done,
                Headline = string.Format("{0} {1} item(s).", verb, ok.Length),
                Detail = detail
            };
        }
    }
}
```

- [ ] **Step 4: Use it in `ConfPageView`**

Replace the unconditional block at `ConfPageView.cs:148-149`:

```csharp
                RunSummary summary = RunSummary.For(results, true, "Backed up");

                logger.LogMessage(summary.Headline);
                logger.LogMessage(summary.Detail);

                MessageBox.Show(summary.Headline + "\r\n\r\n" + summary.Detail,
                    "Backup", MessageBoxButtons.OK, summary.Icon);
```

And replace `HandleRestorationAfterSelection`'s unconditional "Restore done." at `:205-206` the same
way, passing `ran: CurrentRestorePath != "" && Directory.Exists(CurrentRestorePath)` and the verb
`"Restored"`.

Gate the restart banner on a `Succeeded` restore of a module that declares `RequiresExplorerRestart`,
rather than on the declaration alone.

- [ ] **Step 5: Run the tests**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Appcopier/Results/RunSummary.cs src/Appcopier/Views/ConfPageView.cs src/Appcopier.Tests/RunSummaryTests.cs
git commit -m "Replace 'Back up done.' with a summary that reflects what happened"
```

---

### Task 10: `backup_log.txt` records outcomes

**Files:**
- Modify: `src/Appcopier/Views/ConfPageView.cs:161-180` (`LogBackedUpElements`)
- Create: `src/Appcopier/Results/BackupLog.cs`
- Test: `src/Appcopier.Tests/BackupLogTests.cs`

**Interfaces:**
- Consumes: `ModuleResult` (Task 1).
- Produces: `internal static class BackupLog` with `internal const string VersionHeader = "# Appcopier backup log v2"`, `internal static string Compose(IReadOnlyList<BackupBase> modules, IReadOnlyList<ModuleResult> results, string when)`, and `internal static bool IsLegacy(string text)`.

The file has exactly one writer (`ConfPageView.cs:170`) and one reader (`RestPageView.cs:77,83`), and
the reader is a verbatim `File.ReadAllText` dump into a textbox with no parsing — so a format change
is inert. The restore *set* comes from `btnRestore_Click` before `RestPageView` is shown.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/BackupLogTests.cs`:

```csharp
using Appcopier;
using Conf;
using System.Collections.Generic;
using Xunit;

namespace Appcopier.Tests
{
    public class BackupLogTests
    {
        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skip() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Bad() => ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") });

        private static List<BackupBase> Modules() => new List<BackupBase> { new DMouse(), new DTouchpad() };

        [Fact]
        public void Compose_StartsWithTheVersionHeader()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");
            Assert.StartsWith(BackupLog.VersionHeader, text);
        }

        [Fact]
        public void Compose_RecordsTheOutcomeNotJustTheSelection()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Bad() }, "2026-07-20");

            Assert.Contains("Mouse", text);
            Assert.Contains("access denied", text);
        }

        [Fact]
        public void Compose_DistinguishesSkippedFromFailed()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");

            Assert.Contains("SKIPPED", text);
            Assert.DoesNotContain("FAILED", text);
        }

        [Fact]
        public void Compose_NamesEveryModule()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");

            Assert.Contains("Mouse", text);
            Assert.Contains("Touchpad", text);
        }

        [Fact]
        public void Compose_MismatchedCounts_DoesNotThrow()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok() }, "2026-07-20");
            Assert.Contains("Mouse", text);
        }

        // A v0.30.0 file is a bare list of titles with no header.
        [Fact]
        public void IsLegacy_OldFormatFile_IsDetected()
            => Assert.True(BackupLog.IsLegacy("Mouse (DMouse)\r\nKeyboard (DKeyboard)\r\n"));

        [Fact]
        public void IsLegacy_NewFormatFile_IsNot()
            => Assert.False(BackupLog.IsLegacy(BackupLog.VersionHeader + "\r\nMouse  OK\r\n"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~BackupLogTests`
Expected: FAIL — `The type or namespace name 'BackupLog' could not be found`.

- [ ] **Step 3: Create `BackupLog`**

Create `src/Appcopier/Results/BackupLog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace Appcopier
{
    /// <summary>
    /// Composes backup_log.txt.
    /// </summary>
    /// <remarks>
    /// v1 listed what was SELECTED, which is the same category of lie as the old success dialog: it
    /// described an intention as though it were an outcome. v2 records what happened per module.
    ///
    /// Safe to change format: the only reader (RestPageView) dumps the file verbatim into a textbox
    /// and never parses it, and the restore SET is chosen before that view is shown. The version
    /// header is cheap insurance in case anything ever does parse it.
    /// </remarks>
    internal static class BackupLog
    {
        internal const string VersionHeader = "# Appcopier backup log v2";

        internal static string Compose(IReadOnlyList<BackupBase> modules,
                                       IReadOnlyList<ModuleResult> results,
                                       string when)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(VersionHeader);
            sb.AppendLine("# " + when);
            sb.AppendLine();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                BackupBase module = modules[i];

                // Counts can diverge if a module threw before producing a result. Report that
                // rather than indexing past the end.
                ModuleResult result = (results != null && i < results.Count) ? results[i] : null;

                if (result == null)
                {
                    sb.AppendLine(string.Format("{0} ({1})  UNKNOWN  no result was recorded",
                        module.Title, module.GetType().Name));
                    continue;
                }

                sb.AppendLine(string.Format("{0} ({1})  {2}  {3}",
                    module.Title, module.GetType().Name, Label(result.State), result.Reason));
            }

            return sb.ToString();
        }

        private static string Label(ResultState state)
        {
            switch (state)
            {
                case ResultState.Succeeded: return "OK";
                case ResultState.Skipped: return "SKIPPED";
                default: return "FAILED";
            }
        }

        /// <summary>Whether this is a v0.30.0-era file, which listed selections and had no header.</summary>
        internal static bool IsLegacy(string text)
            => string.IsNullOrEmpty(text) || !text.StartsWith(VersionHeader, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Use it in `ConfPageView`**

Replace the body of `LogBackedUpElements` so it takes the results list and writes
`BackupLog.Compose(...)` via `File.WriteAllText`. Keep the filename `backup_log.txt` and the
`try`/`catch` around the write — a failure to write the log must not fail the backup, but it must be
reported through `logger.LogMessage`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Appcopier/Results/BackupLog.cs src/Appcopier/Views/ConfPageView.cs src/Appcopier.Tests/BackupLogTests.cs
git commit -m "Record outcomes in backup_log.txt instead of selections"
```

---

### Task 11: Fix CWiFiConf's profile selection

**Files:**
- Modify: `src/Appcopier/Conf/CWiFiConf.cs`
- Create: `src/Appcopier/Results/WlanProfile.cs`
- Test: `src/Appcopier.Tests/WlanProfileTests.cs`

**Interfaces:**
- Consumes: `StepResult` (Task 1).
- Produces: `internal static class WlanProfile` with `internal static bool IsWlanProfile(string xmlPath)` and `internal static string[] FindIn(string folder)`.

Measured 2026-07-20: `netsh` writes `<interface name>-<SSID>.xml` — on the test machine `Wi-Fi 2-<SSID>.xml`. `CWiFiConf.cs:46`'s `WLAN*.xml` filter matched **0 of 19** files. The interface name is machine-specific, so a corrected wildcard is not a fix either. Selection must be by **content**: root element `WLANProfile` in namespace `http://www.microsoft.com/networking/WLAN/profile/v1`.

The second half of the pair — restore importing only `xmlFiles[0]` — is fixed here too, because with the filter corrected it would otherwise restore 1 of 19 networks.

- [ ] **Step 1: Write the failing tests**

Create `src/Appcopier.Tests/WlanProfileTests.cs`:

```csharp
using Appcopier;
using System;
using System.IO;
using Xunit;

namespace Appcopier.Tests
{
    public class WlanProfileTests : IDisposable
    {
        private readonly string _dir;

        public WlanProfileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acwlan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // The real shape netsh produces (measured 2026-07-20), trimmed to its structure.
        private const string RealProfile =
            "<?xml version=\"1.0\"?>\r\n" +
            "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\r\n" +
            "  <name>MyNetwork</name>\r\n" +
            "  <SSIDConfig><SSID><name>MyNetwork</name></SSID></SSIDConfig>\r\n" +
            "</WLANProfile>\r\n";

        private string Write(string name, string content)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, content);
            return p;
        }

        // The measured filename shape - note it does NOT start with "WLAN".
        [Fact]
        public void IsWlanProfile_RealNetshFilename_IsRecognised()
            => Assert.True(WlanProfile.IsWlanProfile(Write("Wi-Fi 2-MyNetwork.xml", RealProfile)));

        [Fact]
        public void IsWlanProfile_DifferentInterfaceName_IsStillRecognised()
            => Assert.True(WlanProfile.IsWlanProfile(Write("Wireless Network Connection-Cafe.xml", RealProfile)));

        [Fact]
        public void IsWlanProfile_UnrelatedXml_IsRejected()
            => Assert.False(WlanProfile.IsWlanProfile(Write("other.xml", "<Something><name>x</name></Something>")));

        [Fact]
        public void IsWlanProfile_MalformedXml_IsRejectedWithoutThrowing()
            => Assert.False(WlanProfile.IsWlanProfile(Write("broken.xml", "<WLANProfile>unclosed")));

        [Fact]
        public void IsWlanProfile_MissingFile_IsRejected()
            => Assert.False(WlanProfile.IsWlanProfile(Path.Combine(_dir, "nope.xml")));

        // The bug that made this task necessary: the old WLAN*.xml filter matched none of these.
        [Fact]
        public void FindIn_FindsEveryProfileRegardlessOfInterfaceName()
        {
            Write("Wi-Fi 2-Home.xml", RealProfile);
            Write("Wi-Fi 2-Cafe.xml", RealProfile);
            Write("Wi-Fi-Office.xml", RealProfile);
            Write("Network configuration.txt", "not xml");
            Write("unrelated.xml", "<Other/>");

            string[] found = WlanProfile.FindIn(_dir);

            Assert.Equal(3, found.Length);
        }

        [Fact]
        public void FindIn_MissingFolder_ReturnsEmptyWithoutThrowing()
            => Assert.Empty(WlanProfile.FindIn(Path.Combine(_dir, "nowhere")));

        [Fact]
        public void FindIn_NoProfiles_ReturnsEmpty()
        {
            Write("only.txt", "nothing here");
            Assert.Empty(WlanProfile.FindIn(_dir));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src\Appcopier.sln --filter FullyQualifiedName~WlanProfileTests`
Expected: FAIL — `The type or namespace name 'WlanProfile' could not be found`.

- [ ] **Step 3: Create `WlanProfile`**

Create `src/Appcopier/Results/WlanProfile.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Appcopier
{
    /// <summary>
    /// Finds exported Wi-Fi profiles by what they contain rather than what they are called.
    /// </summary>
    /// <remarks>
    /// The old code globbed "WLAN*.xml". Measured on Windows 11, 2026-07-20: netsh names its exports
    /// "&lt;interface name&gt;-&lt;SSID&gt;.xml" - on the test machine "Wi-Fi 2-Home.xml" - so the
    /// filter matched 0 of 19 exported profiles and restore silently found nothing.
    ///
    /// A corrected wildcard would not fix it either: the prefix is the network interface's name,
    /// which differs per machine and is localised. Content is the only stable discriminator.
    /// </remarks>
    internal static class WlanProfile
    {
        private const string ProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";
        private const string RootElement = "WLANProfile";

        internal static bool IsWlanProfile(string xmlPath)
        {
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                return false;

            try
            {
                XDocument doc = XDocument.Load(xmlPath);

                if (doc.Root == null)
                    return false;

                // Match on the local name, and on the namespace when one is present. Hand-edited
                // profiles sometimes lose the xmlns; the root element name is the reliable part.
                if (!string.Equals(doc.Root.Name.LocalName, RootElement, StringComparison.OrdinalIgnoreCase))
                    return false;

                string ns = doc.Root.Name.NamespaceName;

                return string.IsNullOrEmpty(ns)
                    || string.Equals(ns, ProfileNamespace, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Not XML, unreadable, or truncated. Either way it is not a profile we can restore.
                return false;
            }
        }

        internal static string[] FindIn(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return new string[0];

            List<string> found = new List<string>();

            try
            {
                foreach (string path in Directory.GetFiles(folder, "*.xml"))
                {
                    if (IsWlanProfile(path))
                        found.Add(path);
                }
            }
            catch (Exception)
            {
                return found.ToArray();
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found.ToArray();
        }
    }
}
```

- [ ] **Step 4: Rewrite `CWiFiConf`**

Backup: snapshot `*.xml` in the folder **before** the export, run `netsh wlan export profile
key=clear folder="..."`, consume the exit code (`ExecuteNetshCommand` already returns it at `:89` —
stop discarding it at `:23`), then snapshot again and count only the *newly added* profiles. A bare
file count is meaningless because `ConfPageView.cs:140` passes the shared backup root and other
modules write there too. Zero new files with a zero exit code is `Failed`, not `Succeeded`.

Restore: `WlanProfile.FindIn(path)`, then import **every** profile with one `StepResult` each, and
`ModuleResult.Aggregate` them. No profiles found is `Failed` — the user selected this module for
restore and there is nothing in the backup to give them.

- [ ] **Step 5: Run the tests**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Appcopier/Results/WlanProfile.cs src/Appcopier/Conf/CWiFiConf.cs src/Appcopier.Tests/WlanProfileTests.cs
git commit -m "Find Wi-Fi profiles by content, and restore all of them

netsh writes '<interface>-<SSID>.xml', so the WLAN*.xml filter matched 0 of
19 exported profiles on the test machine and restore found nothing. The
interface name varies per machine, so a corrected wildcard is not a fix.
Restore also imported only xmlFiles[0], discarding every network but one."
```

---

### Task 12: Verify, document, and open the PR

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Full clean build**

Run: `dotnet build src\Appcopier.sln --no-incremental`
Expected: `0 Error(s)`. Exactly 3 warnings, all pre-existing: 2 × `SYSLIB0014` (`WebClient`, `DataHelper.cs`) and 1 × `WFAC010` (DPI, `app.manifest`). **An incremental build reports 0 warnings because it skips `CoreCompile` — `--no-incremental` is required for an honest count.**

- [ ] **Step 2: Full test run**

Run: `dotnet test src\Appcopier.sln`
Expected: PASS, 0 failed. Paste the raw output into the PR body — do not paraphrase it.

- [ ] **Step 3: Run the safety review**

Dispatch the `windows-safety-reviewer` subagent over the diff. `CLAUDE.md` mandates it for any change to `Utils`, `Conf/` modules, or restore logic — this phase changes all three.

- [ ] **Step 4: The manual elevated smoke matrix**

These cannot be unit tested. Run each on a real elevated session and record the result:

| # | Scenario | Must report |
| --- | --- | --- |
| a | Desktop PC with no touchpad, back up `DTouchpad` | Skipped, **not** Failed |
| b | Machine without `Explorer\Accent`, back up `WPersonalization` | Succeeded, with the absence noted |
| c | Stock Windows 11 Home, back up `WTelemetry` and `GGaming` | Skipped (rule 3) |
| d | Stock Windows 11 Home, back up `WUpdates` | Succeeded with a note (rule 4) |
| e | Chrome running, decline the close prompt | Skipped, **not** "Back up done." |
| f | Chrome running, accept the prompt | Succeeded or Failed-with-a-count, never a silent partial |
| g | Restore pointed at a deleted folder | "did not run", **not** "Restore done." |
| h | **Unelevated run**, back up `WOther`/`WUpdates`/`WTelemetry`/`DUSB`/`DPrinters` | Failed — this is the headline improvement and is observable no other way |
| i | Back up Wi-Fi, then restore it | every saved network restored, not one |

- [ ] **Step 5: Eyeball `backup_log.txt`**

Open the file from a smoke run and check it line by line against what actually landed in the folder. This is the only end-to-end check that the log tells the truth.

- [ ] **Step 6: Update `CHANGELOG.md`**

Under `[Unreleased]`, add a `### Changed` entry for the contract change and `### Fixed` entries for: the unconditional success dialogs; the silent restore no-op; the declined browser prompt reported as success; unverified registry exports; the unguarded `Process.Kill`; `RunWT`'s `async void`; and the Wi-Fi filename defect. **State plainly that the browser modules will now report failure whenever the browser was running** — it is intended behaviour (spec Decision 2) but reads as a regression to anyone who has not read the spec.

- [ ] **Step 7: Update `CLAUDE.md`**

Add to the module-authoring section: modules return `ModuleResult` built only via `ModuleResult.Aggregate`; every sub-operation declares `absenceIsNormal`; restore-side reasons say *applied*, never *verified*; and log data-bearing text with `LogHelper.LogMessage`, never as a format string.

- [ ] **Step 8: Commit and push**

```bash
git add CHANGELOG.md CLAUDE.md
git commit -m "Document Phase 2a"
git push -u origin feat/phase2-honest-failures
```

- [ ] **Step 9: Open the PR and STOP**

```bash
gh pr create --repo nicolasestrem/Appcopier --base main --head feat/phase2-honest-failures --title "Phase 2a: make failure representable and reported" --body "..."
```

`gh` in this repo resolves to upstream `builtbybel/Appcopier`, so `--repo nicolasestrem/Appcopier` is **required** on every `gh` command.

Include in the body: the verbatim build and test output, the smoke matrix results with any unrun rows marked as unrun, and the measured facts table. **Do not merge.** Merging requires explicit approval.

---

## Self-Review

**Spec coverage.** Every section of the design spec maps to a task: result types → 1; classification and aggregation → 1, 5, 6; run summary → 9; `Utils` contract table → 4, 5, 6, 7; module migration by shape → 8; the CWiFiConf fix → 11; logging → 2, 10; testing → tests in every task plus 12; the four decisions → Decision 1 in Task 8 Step 1, Decision 2 in Task 6 Step 3, Decision 3 in Task 11, Decision 4 in Tasks 2 and 10.

**Deliberate deferrals carried through.** `WNetworkConf`'s `StreamWriter(null)` stays broken (Task 8 Step 6, flagged in the roadmap as 2c). `CloseProcess` gains its guard but not its bounded wait (Task 7). `RestartExplorer`'s N-explorers bug and `FormatBytes`' integer division are untouched. Read-back verification of imports is 2b.

**Known gap, stated rather than hidden.** Tasks 5, 6 and 7 leave the solution unbuildable until Task 8 completes the contract change, because `Backup(string)` cannot return `void` and `ModuleResult` simultaneously. Task 5 Step 7 commits a non-building state deliberately, with the reasoning in the commit message. Steps 6 of Task 5 and 5 of Task 6 substitute a filtered build check for a test run in that window. If the executing agent prefers, Tasks 5-8 can be squashed into a single commit at the cost of a 30-file reviewable unit.

**Empirical dependencies.** Task 12 Step 4 rows b, c, d and h, plus the three `absenceIsNormal` judgement calls in Task 8 Step 2 (`WPrivacy`, `WVisualEffects`, `DPrinters`), cannot be settled without hardware. They are marked unverified in the spec and must be confirmed before release, not before merge.
