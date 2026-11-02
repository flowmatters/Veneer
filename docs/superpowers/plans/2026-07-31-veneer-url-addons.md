# URL Addons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `.veneer` addon entry open a link — `type: "url"` — and fix the URL launching that .NET 8 broke for the HTML report items and the Veneer logo.

**Architecture:** A pure `AddonUrl` in `Addons/` owns the scheme allowlist (a literal prefix test, not `Uri` parsing) so `VeneerAddon.Validate` can reject a bad link at menu-build time and render a disabled item with a tooltip. A single `ShellLink.TryOpen` in `DomainActions/` is the only place that sets `UseShellExecute = true`, shared by the new type and the two existing call sites. `AddonLauncher.LaunchUrl` is a separate entry point from `Launch`, because a URL needs neither a project directory nor a child-process environment.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`) on `master`; .NET Framework 4.8 / C# 7.3 on `legacy_ci`. WinForms menu-bar integration. NUnit 4.1.0.

**Spec:** [`docs/superpowers/specs/2026-07-31-veneer-url-addons-design.md`](../specs/2026-07-31-veneer-url-addons-design.md)

---

## Execution status (2026-07-31)

| Task | State | Commit |
|---|---|---|
| 0 Starting state | done | baseline 101, anchors 105/262/264 confirmed |
| 1 `AddonUrl` | done | `ee97265` — 23 cases, red then green |
| 2 `url` field + validation | done | `5bde4ce` — 10 cases, suite at 134 |
| 3 `ShellLink` | done | `65e498a` |
| 4 Existing links + port | done | `0e296ad` — **the regression fix** |
| 5 `LaunchUrl` | done | `9522fa6` |
| 6 Menu dispatch | done | `6d97b20` |
| 7 Docs + sample | done | `f930b1e` — field table verified against `VeneerAddon` both directions |
| 8 Manual verification | **OUTSTANDING** | needs the Source GUI |
| 9 `legacy_ci` port | done, partially verified | `a3eb58b` on `legacy_ci` |

**One surprise during execution.** Widening the "nothing to run" message broke
`AddonLauncherIntegrationTests.cs:183`, which pinned the old wording. Neither the
plan nor its reviewer caught it — both traced only `AddonValidationTests`. Fixed
by updating the assertion to the new phrase, in the Task 2 commit.

**Task 8 is the gap.** `ShellLink` launches a browser and the rest is WinForms, so
none of Tasks 3-6 has unit coverage: **the link behaviour has not been observed,
only reasoned about.** Step 2 matters most — it confirms the regression exists
before verifying the fix, because the `UseShellExecute` diagnosis was inferred
from the runtime default and never seen to fail in Source.

**Task 9's caveat.** The port is committed but `legacy_ci` **does not build** in
this environment — pristine HEAD fails with the same `MC1000` WPF
markup-compiler error as the ported tree, so it is pre-existing and unrelated.
Verified instead: the identical code passes 134 tests on `master`, and both
`AddonUrl.cs` and `ShellLink.cs` compile clean under
`csc /langversion:7.3 /warnaserror`. The 33 new tests have **not** been run there.

Both `master` and `legacy_ci` have unpushed commits.

---

## Prerequisites

### Build and test command

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

**Baseline at commit `313f9ce`: `Passed: 101`, 0 failed** — verified immediately before writing this plan. This plan adds 33 cases, so the expected end state is **134**. Append `--filter "FullyQualifiedName~AddonUrlTests"` to scope. `MSB3277` reference-version warnings are pre-existing noise.

If the command stops working, the recovery procedure is Task 0 of `docs/superpowers/plans/2026-07-30-addon-launch-modes.md`.

### `TreatWarningsAsErrors` is true in Debug

`NoWarn` covers only `1591` and `1587`. Do not leave unused locals behind. An unused `using` is **not** a compiler warning, so it will not break the build.

### C# 7.3 only

`legacy_ci` targets .NET Framework 4.8 with C# 7.3 and Task 9 ports there. **Do not use** target-typed `new()`, switch expressions, index-from-end, or ranges. Object initialisers, `?.`, `??`, `$"…"` and `out var` are all fine.

### NUnit must stay portable

`Tests/AddonAssert.cs` documents the constraint: `legacy_ci` builds against whatever NUnit the targeted Source version bundles, **from 2.6.4 up**. So use only `Assert.That(x, Is.True/False/Null/Not.Null/EqualTo(...))` and `[TestCase]`. **Do not use** `Does.Contain`, `StringAssert`, or `Assert.Multiple`. For substring assertions on validation messages use the existing `AddonAssert.Contains(actual, expected, because)`.

### Line numbers drift

Cited line numbers are valid at `313f9ce`. Anchor edits on **method names and quoted snippets**, which are stable.

---

## File Structure

| File | Responsibility |
|---|---|
| `FlowMatters.Source.Veneer/Addons/AddonUrl.cs` | **New.** Scheme allowlist and normalisation. Pure — no RiverSystem, TIME or WinForms dependency. |
| `FlowMatters.Source.Veneer/Tests/AddonUrlTests.cs` | **New.** 23 cases over the allowlist. |
| `FlowMatters.Source.Veneer/DomainActions/ShellLink.cs` | **New.** The single `UseShellExecute = true` call, guarded, returning bool. |
| `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` | `url` field; four validation rules; widened catch-all. |
| `FlowMatters.Source.Veneer/Tests/AddonValidationTests.cs` | 10 new cases. |
| `FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs` | `LaunchUrl` entry point. |
| `FlowMatters.Source.Veneer/VeneerMenu.cs` | `case "url"`; `SourceAddonLog`; shared context builder; the two call sites; the report port. |
| `docs/veneer-file-format.md` | Whole addon field table reconciled with the schema. |
| `Samples/addons/link-menu.rsproj.veneer` | **New.** Worked example. |

`AddonUrl` goes in `Addons/`, not `DomainActions/`: `VeneerAddon.Validate` needs the scheme rule, and `DomainActions` already depends on `Addons`. Putting it in `DomainActions` would point that dependency both ways.

**Not touched:** `ExchangeObjects/VeneerStatus.cs` and `docs/api/`. `VeneerAddon` appears in seven files, none an endpoint, and nowhere in `ISourceService`/`SourceService`. **No `PROTOCOL_VERSION` bump.** `CLAUDE.md` pushes the other way — it does not apply here.

---

## Task 0: Confirm the starting state

- [ ] **Step 1: Build and test**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: `Passed!`, **101 passed**. A higher number means more fixtures landed since — note the new baseline so you can check the +33 at the end. If it fails outright, **stop and report**.

- [ ] **Step 2: Check the line anchors**

Run:
```
git -C C:\src\projects\Veneer grep -n "switch (addon.type)\|private void Launch(string p)\|int port = SourceRESTfulService" -- FlowMatters.Source.Veneer/VeneerMenu.cs
```
At `313f9ce` these are lines 105, 262 and 264. If they have moved, the quoted snippets in Tasks 5 and 6 still apply — locate by method name.

- [ ] **Step 3: Only stage named paths**

Other work is in progress in this tree. **Never `git add -A` or `git add .`** — several files (`CLAUDE.md`, `build.bat`, `specs/`, `all.txt`) are deliberately untracked.

---

## Task 1: `AddonUrl` — the scheme allowlist

**Files:**
- Create: `FlowMatters.Source.Veneer/Addons/AddonUrl.cs`
- Create: `FlowMatters.Source.Veneer/Tests/AddonUrlTests.cs`

- [ ] **Step 1: Create the class with stubs that throw**

A missing class is a *build* failure in C#, not a test failure, so stubbing first is what makes a red run observable.

```csharp
using System;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// The scheme allowlist for <c>type: "url"</c> addons. Pure: no RiverSystem,
    /// TIME or WinForms dependency, so it is testable without a loaded scenario.
    /// </summary>
    public static class AddonUrl
    {
        public static string Normalise(string url)
        {
            throw new NotImplementedException();
        }

        public static bool HasAllowedScheme(string url)
        {
            throw new NotImplementedException();
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `FlowMatters.Source.Veneer/Tests/AddonUrlTests.cs`:

```csharp
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonUrlTests
    {
        [TestCase("http://wiki.example.org/catchment")]
        [TestCase("https://wiki.example.org/catchment")]
        [TestCase("mailto:support@example.org")]
        [TestCase("HTTPS://WIKI.EXAMPLE.ORG")]        // schemes are case-insensitive
        [TestCase("MailTo:support@example.org")]
        [TestCase("  https://wiki.example.org  ")]     // incidental JSON whitespace
        [TestCase("http://localhost:%VENEER_PORT%/doc/notes.html")]
        [TestCase("https://wiki.example.org/%VENEER_SCENARIO%")]
        public void HasAllowedScheme_Accepts(string url)
        {
            Assert.That(AddonUrl.HasAllowedScheme(url), Is.True);
        }

        // file:// is deliberately excluded: it would admit file://server/share/tool.exe,
        // and without it type "url" cannot launch a local program by any spelling.
        [TestCase("file://server/share/manual.pdf")]
        // Uri.TryCreate calls this scheme "file", which is why validation is a
        // literal prefix test rather than Uri parsing.
        [TestCase(@"C:\Windows\System32\cmd.exe")]
        [TestCase(@"\\server\share\tool.exe")]
        [TestCase("ms-msdt:/id")]
        [TestCase("javascript:alert(1)")]
        [TestCase("wiki.example.org")]                 // no scheme at all
        [TestCase("https:/wiki.example.org")]          // one slash
        [TestCase("httpsx://wiki.example.org")]
        [TestCase("%HELP_URL%")]                       // scheme must be literal
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void HasAllowedScheme_Rejects(string url)
        {
            Assert.That(AddonUrl.HasAllowedScheme(url), Is.False);
        }

        [Test]
        public void Normalise_TrimsSurroundingWhitespace()
        {
            Assert.That(AddonUrl.Normalise("  https://x  "), Is.EqualTo("https://x"));
        }

        [Test]
        public void Normalise_ReturnsNullForNull()
        {
            Assert.That(AddonUrl.Normalise(null), Is.Null);
        }

        [Test]
        public void Normalise_LeavesInteriorTextAlone()
        {
            Assert.That(AddonUrl.Normalise("https://x/a b"), Is.EqualTo("https://x/a b"));
        }
    }
}
```

Four rows carry the design and are the reason this fixture exists:

- `C:\Windows\System32\cmd.exe` and `\\server\share\tool.exe` — `Uri.TryCreate` returns **true** for both with `Scheme == "file"`. Any `Uri`-based allowlist has to special-case them; a prefix test never sees them as schemes.
- `http://localhost:%VENEER_PORT%/…` — `Uri.TryCreate` returns **false**. Parsing would reject the most useful form.
- `%HELP_URL%` — records that the scheme must be written literally, because validation runs before expansion.

- [ ] **Step 3: Run to verify they fail**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~AddonUrlTests"
```
Expected: **23 failures**, all `System.NotImplementedException`. A *build* error instead means the stub or fixture has a syntax problem.

- [ ] **Step 4: Implement**

Replace both stub bodies in `Addons/AddonUrl.cs`:

```csharp
        private static readonly string[] AllowedPrefixes = { "http://", "https://", "mailto:" };

        /// <summary>
        /// The canonical form of a url value. Trimmed, because an incidental
        /// leading space in JSON would otherwise fail the prefix test for no
        /// reason the author could see. Null in, null out.
        /// </summary>
        public static string Normalise(string url)
        {
            return url == null ? null : url.Trim();
        }

        /// <summary>
        /// A literal prefix test, not Uri parsing, and deliberately so.
        /// Uri.TryCreate rejects "http://localhost:%VENEER_PORT%/x" -- the
        /// variable sits in the port position -- and reports scheme "file" for
        /// "C:\Windows\System32\cmd.exe". Parsing would therefore reject the most
        /// useful form and accept a bare executable path.
        /// </summary>
        public static bool HasAllowedScheme(string url)
        {
            var normalised = Normalise(url);
            if (string.IsNullOrEmpty(normalised)) return false;

            foreach (var prefix in AllowedPrefixes)
            {
                if (normalised.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
```

`HasAllowedScheme` guards null before comparing, so it returns false rather than throwing.

- [ ] **Step 5: Run to verify they pass**

Run the same filtered command. Expected: **23 passed, 0 failed**.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/AddonUrl.cs FlowMatters.Source.Veneer/Tests/AddonUrlTests.cs
git commit -m "feat: add AddonUrl scheme allowlist for url addons

Allows http://, https:// and mailto: by literal case-insensitive prefix
on the trimmed value. Uri parsing is unusable here: it rejects a URL
containing %VENEER_PORT% and reports scheme \"file\" for a bare Windows
path such as C:\\Windows\\System32\\cmd.exe."
```

---

## Task 2: The `url` field and its validation rules

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/AddonValidationTests.cs`

- [ ] **Step 1: Add the field**

In `VeneerAddon`, after `public string[] script { get; set; }`:

```csharp
        public string url { get; set; }
```

- [ ] **Step 2: Write the failing tests**

Append inside the `AddonValidationTests` class, before its closing brace:

```csharp
        [Test]
        public void UrlAddon_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "url", url = "https://wiki.example.org" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void UrlWithPath_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a", type = "url",
                url = "https://wiki.example.org", path = "tools/x.bat"
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlWithScript_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a", type = "url",
                url = "https://wiki.example.org", script = new[] { "echo hi" }
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlWithEmptyScriptArray_IsInvalid()
        {
            // The exclusion rule tests script != null, matching the existing
            // path/script rule -- an empty array must not mean "absent" for one
            // rule and "present" for another in the same method.
            var addon = new VeneerAddon
            {
                name = "a", type = "url",
                url = "https://wiki.example.org", script = new string[0]
            };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mutually exclusive");
        }

        [Test]
        public void UrlOnANonUrlType_IsInvalid()
        {
            // Without this rule the entry passes validation, dispatches to
            // LaunchExe, and dies in Path.Combine(dir, null).
            var addon = new VeneerAddon { name = "a", type = "exe", url = "https://wiki.example.org" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "type is not 'url'");
        }

        [Test]
        public void UrlTypeWithNoUrl_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "url" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void UrlTypeWithNoUrl_ReportsTheUrlReasonNotTheNeitherReason()
        {
            var addon = new VeneerAddon { name = "a", type = "url" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "no 'url'",
                                 "the more specific reason must win over the catch-all");
        }

        [Test]
        public void UrlWithDisallowedScheme_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "url", url = "file://server/share/x.pdf" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "mailto:");
        }

        [Test]
        public void UrlTypeIsMatchedCaseInsensitivelyByValidate()
        {
            // Validate compares case-insensitively but VeneerMenu's switch does
            // not, so "URL" validates and then renders as an unknown type. That
            // asymmetry pre-dates this feature and is recorded, not fixed.
            var addon = new VeneerAddon { name = "a", type = "URL", url = "https://wiki.example.org" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void NeitherPathScriptNorUrl_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe" };
            AddonAssert.Contains(VeneerAddon.Validate(addon), "neither");
        }
```

- [ ] **Step 3: Run to verify they fail**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~AddonValidationTests"
```
Expected: the 10 existing cases pass; several of the 10 new ones fail. `UrlAddon_IsValid` fails because the catch-all still fires; the rest fail on their missing messages.

- [ ] **Step 4: Implement the rules**

Replace the body of `VeneerAddon.Validate` entirely:

```csharp
        public static string Validate(VeneerAddon addon)
        {
            bool hasScript = addon.script != null && addon.script.Length > 0;
            bool hasUrl = !string.IsNullOrEmpty(addon.url);
            bool isUrlType = string.Equals(addon.type, "url", StringComparison.OrdinalIgnoreCase);

            // Mutual exclusion first, so an entry wrong in two ways reports the
            // structural problem rather than something downstream of it. Tests
            // script != null rather than hasScript, matching the path/script rule
            // below: an empty array must not mean "absent" for one rule and
            // "present" for another in the same method.
            if (hasUrl && (!string.IsNullOrEmpty(addon.path) || addon.script != null))
                return "specifies 'url' together with 'path' or 'script'; they are mutually exclusive";

            if (!string.IsNullOrEmpty(addon.path) && addon.script != null)
                return "specifies both 'path' and 'script'; they are mutually exclusive";

            // Without this, {"type":"exe","url":"..."} passes validation,
            // dispatches to LaunchExe, and fails inside Path.Combine(dir, null)
            // as an opaque ArgumentNullException rather than a schema error.
            if (hasUrl && !isUrlType)
                return "specifies 'url' but type is not 'url'";

            if (isUrlType && !hasUrl)
                return "is type 'url' but has no 'url'";

            if (hasUrl && !AddonUrl.HasAllowedScheme(addon.url))
                return "has a 'url' that is not http://, https:// or mailto:";

            if (string.Equals(addon.type, "script", StringComparison.OrdinalIgnoreCase) && !hasScript)
                return "is type 'script' but has no 'script' lines";

            // Widened to mention url. Last, so the specific type reasons above win.
            if (!hasScript && !hasUrl && string.IsNullOrEmpty(addon.path))
                return "has neither 'path', 'script' nor 'url'; there is nothing to launch";

            return null;
        }
```

The catch-all message keeps the word "neither", so the existing `ExeWithNoPath_IsInvalid` assertion still holds.

- [ ] **Step 5: Run the whole suite**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: **134 passed, 0 failed** (101 baseline + 23 + 10).

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs FlowMatters.Source.Veneer/Tests/AddonValidationTests.cs
git commit -m "feat: add url field and validation to VeneerAddon

Four rules, each producing a disabled menu item with a tooltip at
menu-build time rather than a failure on click: url alongside path or
script, url on a non-url type, type url with no url, and a url outside
the http/https/mailto allowlist. The catch-all is widened to mention url
and stays last so the specific reasons win."
```

---

## Task 3: `ShellLink` — the one place that opens a URL

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/ShellLink.cs`

No unit test: `TryOpen` either launches a browser or does nothing testable. It is covered by the manual checks in Task 8.

- [ ] **Step 1: Create the class**

```csharp
using System;
using System.Diagnostics;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// The one place that hands a URL to the shell. Returns false rather than
    /// throwing because every caller is a WinForms Click handler, where an
    /// escaping exception becomes an unhandled-exception dialog in Source.
    /// </summary>
    internal static class ShellLink
    {
        public static bool TryOpen(string url, out string error)
        {
            error = null;
            try
            {
                // .NET Core flipped the UseShellExecute default from true to
                // false, so Process.Start(string) tries to execute the URL as a
                // file and throws Win32Exception. Setting it explicitly restores
                // the browser/handler behaviour .NET Framework had by default,
                // and is a harmless no-op on legacy_ci where it already is.
                var startInfo = new ProcessStartInfo(url) { UseShellExecute = true };

                // Null when an already-running handler absorbs the request -- a
                // browser opening a tab starts no new process.
                var process = Process.Start(startInfo);
                if (process != null)
                    process.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: **134 passed**. Nothing calls `ShellLink` yet, so the count is unchanged from Task 2.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/ShellLink.cs
git commit -m "feat: add ShellLink.TryOpen for launching URLs

Sets UseShellExecute explicitly. .NET Core flipped its default to false,
so Process.Start(string) does not open a browser on net8.0-windows --
it tries to execute the URL as a file. Returns bool because every caller
is a WinForms Click handler where a throw becomes a dialog in Source."
```

---

## Task 4: Fix the two existing call sites and the report port

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

This is the regression fix. After it, HTML report items and the logo should work on `master` again, and report links should honour the configured port.

- [ ] **Step 1: Add the shared link opener**

Add next to `Launch`:

```csharp
        /// <summary>
        /// Click handlers must not throw -- an escaping exception becomes an
        /// unhandled-exception dialog in Source. Failures go straight to Source's
        /// log rather than through LogOnce, which de-duplicates by message and is
        /// cleared only on project change: right for menu-build spam, wrong for a
        /// click, where clicking a broken link twice should report twice.
        /// </summary>
        private void OpenLink(string url, string description)
        {
            string error;
            if (!ShellLink.TryOpen(url, out error))
            {
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer could not open {0}: {1}", description, error));
            }
        }
```

- [ ] **Step 2: Rewrite `Launch`**

Before:
```csharp
        private void Launch(string p)
        {
            int port = SourceRESTfulService.DEFAULT_PORT;
            string url = string.Format("http://localhost:{0}/doc/{1}", port, p);
            Process.Start(url);
        }
```

After:
```csharp
        private void Launch(string p)
        {
            // Was SourceRESTfulService.DEFAULT_PORT -- the compile-time constant
            // 9876 -- so report links pointed there no matter where the server
            // was actually listening.
            int port = Control != null ? Control.Port : WebServerStatusControl.DefaultPort;
            string url = string.Format("http://localhost:{0}/doc/{1}", port, p);
            OpenLink(url, string.Format("report '{0}'", p));
        }
```

- [ ] **Step 3: Route the logo through it**

In `PopulateReportMenu`'s logo block, replace:
```csharp
                veneer.Click += (eventSender, eventArgs) => Process.Start("http://www.flowmatters.com.au");
```
with:
```csharp
                veneer.Click += (eventSender, eventArgs) =>
                    OpenLink("http://www.flowmatters.com.au", "the Veneer home page");
```

- [ ] **Step 4: No `using` to add**

`ShellLink` is in `FlowMatters.Source.Veneer.DomainActions`, and `VeneerMenu.cs` already has `using FlowMatters.Source.Veneer.DomainActions;` at line 11 for `AddonLauncher`. **Do not add it again** — `CS0105` plus `TreatWarningsAsErrors` is a build failure.

- [ ] **Step 5: Build and test**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: **134 passed**.

`System.Diagnostics` may now be unused in `VeneerMenu.cs` if nothing else uses `Process`. That is not a warning, so leave it or remove it — do not chase a build error that will not appear. Likewise `SourceRESTfulService` may now be unreferenced there.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "fix: open report and logo links through ShellLink, on the right port

Process.Start(url) does not open a browser on net8.0-windows, so the HTML
report items and the logo click were throwing on their Click handlers --
an unhandled-exception dialog in Source rather than a logged failure.
Both now route through ShellLink.

Report links also used the SourceRESTfulService.DEFAULT_PORT constant
rather than the configured port, so they pointed at 9876 regardless of
where the server was listening."
```

---

## Task 5: `AddonLauncher.LaunchUrl`

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs`

- [ ] **Step 1: Add the entry point**

Add after `Launch`, before `LaunchScript`:

```csharp
        /// <summary>
        /// Separate entry point from Launch, not a branch inside it. Launch opens
        /// with a hard ProjectDirectory guard and builds a full child-process
        /// environment; a URL needs neither, and a wiki link routed through Launch
        /// would fail with "no project directory is available".
        /// </summary>
        public static void LaunchUrl(VeneerAddon addon, AddonContext context, IAddonLog log)
        {
            // Re-validated for the same reason Launch re-validates: VeneerMenu
            // disables invalid entries, but this is a public entry point and must
            // not assume its caller did. Here the concrete risk is a null url.
            var invalid = VeneerAddon.Validate(addon);
            if (invalid != null)
            {
                log.Write(string.Format("Addon '{0}' {1}", addon.name, invalid),
                          AddonLogLevel.Error);
                return;
            }

            try
            {
                var env = AddonEnvironment.BuildEffective(context, addon.env);

                // Normalised again after expansion: a variable whose value carries
                // whitespace would otherwise reintroduce the leading space the
                // validation-time trim removed.
                var url = AddonUrl.Normalise(
                    AddonEnvironment.Expand(AddonUrl.Normalise(addon.url), env));

                // No scheme re-check. None of the allowed prefixes contains a '%',
                // and Expand only replaces %NAME% spans, so expansion cannot alter
                // the scheme Validate already accepted.
                string error;
                if (!ShellLink.TryOpen(url, out error))
                {
                    log.Write(string.Format("Addon '{0}' could not open {1}: {2}",
                                            addon.name, url, error),
                              AddonLogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not be launched: {1}",
                                        addon.name, ex.Message),
                          AddonLogLevel.Error);
            }
        }
```

`AddonEnvironment.BuildEffective` already coalesces a null `ProjectDirectory` to empty, so no guard is needed here.

- [ ] **Step 2: Build and test**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: **134 passed**. Nothing calls `LaunchUrl` yet.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs
git commit -m "feat: add AddonLauncher.LaunchUrl

Expands %VAR% through the existing AddonEnvironment then opens the link
via ShellLink. A separate entry point from Launch, whose ProjectDirectory
guard and child-process environment a URL neither needs nor can satisfy."
```

---

## Task 6: Wire `type: "url"` into the menu

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

- [ ] **Step 1: Add the log sink for when the panel is closed**

`ControlAddonLog.Write` dereferences `_control`, and the URL path deliberately does not open the panel. Add next to it:

```csharp
        /// <summary>
        /// Used when the Veneer panel is closed, which the URL path allows --
        /// opening the panel to show a wiki link would be an odd side effect.
        /// The URL path emits only errors, so there is no Debug or Warning
        /// traffic to lose here.
        /// </summary>
        private sealed class SourceAddonLog : IAddonLog
        {
            public void Write(string message, AddonLogLevel level)
            {
                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
            }
        }

        private IAddonLog AddonLog()
        {
            return Control != null ? (IAddonLog)new ControlAddonLog(Control) : new SourceAddonLog();
        }
```

The `(IAddonLog)` cast is required — without it the conditional has no common type between the two sealed classes and fails to compile.

- [ ] **Step 2: Extract the shared context builder**

Add next to `LaunchAddon`:

```csharp
        private AddonContext BuildAddonContext()
        {
            return new AddonContext
            {
                ProjectDirectory = Scenario?.Project?.FileDirectory,
                ProjectFile = Scenario?.Project?.FullFilename,
                // The configured port, not a promise the server is listening.
                // Control is null on the URL path when the panel was never
                // opened, which that path deliberately does not force.
                Port = Control != null ? Control.Port : WebServerStatusControl.DefaultPort
            };
        }
```

- [ ] **Step 3: Rewrite `LaunchAddon` to use it**

Before:
```csharp
        private void LaunchAddon(VeneerAddon addon)
        {
            if (Control == null)
            {
                WebServerStatusControl.Launch();
            }

            var context = new AddonContext
            {
                ProjectDirectory = Scenario.Project.FileDirectory,
                ProjectFile = Scenario.Project.FullFilename,
                // The configured port, not a promise the server is listening --
                // Port is set independently of Running, and addons may be launched
                // with the server stopped.
                Port = Control.Port
            };

            AddonLauncher.Launch(addon, context, new ControlAddonLog(Control));
        }
```

After:
```csharp
        private void LaunchAddon(VeneerAddon addon)
        {
            // Still force-opens the panel, unlike the URL path: this path routes a
            // child process's stdout and stderr there, so having it open is the point.
            if (Control == null)
            {
                WebServerStatusControl.Launch();
            }

            AddonLauncher.Launch(addon, BuildAddonContext(), AddonLog());
        }

        private void LaunchUrlAddon(VeneerAddon addon)
        {
            AddonLauncher.LaunchUrl(addon, BuildAddonContext(), AddonLog());
        }
```

Note `BuildAddonContext` uses `Scenario?.Project?.FileDirectory` where the original used `Scenario.Project.FileDirectory`. `AddonLauncher.Launch` already guards an empty project directory with a clear message, so this turns a possible `NullReferenceException` into that guard. Intentional.

- [ ] **Step 4: Add the dispatch case**

In `PopulateReportMenu`'s `switch (addon.type)`, after the `break;` that ends the `case "script":` arm and **before the `// Previously absent…` comment** that belongs to `default:`. Inserting literally before `default:` would orphan that comment onto the new case.

```csharp
                                case "url":
                                    item.Click += (o, args) => LaunchUrlAddon(addon);
                                    break;

```

- [ ] **Step 5: Build and test**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```
Expected: **134 passed**.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "feat: dispatch type: \"url\" addons from the menu

Adds the url case, a SourceAddonLog for when the Veneer panel is closed,
and a shared context builder. The URL path takes the port from
Control?.Port falling back to DefaultPort rather than forcing the panel
open -- opening the web-server panel to show a wiki link would be an odd
side effect. The exe and script path keeps force-opening it, because that
is where child process output is routed."
```

---

## Task 7: Documentation and a sample

**Files:**
- Modify: `docs/veneer-file-format.md`
- Create: `Samples/addons/link-menu.rsproj.veneer`
- Modify: `Samples/addons/README.md`

The reference doc lists five addon fields; the schema now has ten. Documenting only `url` would leave it just as wrong, so the whole table is reconciled.

- [ ] **Step 1: Reconcile the addon field table**

Replace the table under `## Addons` in `docs/veneer-file-format.md`. Current rows for `name`, `menu` and `scenario` are correct and unchanged; `type` and `path` are corrected; five rows are added.

```markdown
| Field              | Type              | Required | Purpose |
|--------------------|-------------------|----------|---------|
| `name`             | string            | yes      | Text shown on the menu item. |
| `type`             | string            | yes      | `"exe"`, `"script"` or `"url"`. An unrecognised value renders a **disabled** menu item with a tooltip. |
| `path`             | string            | for `"exe"` | Program or batch file, relative to the directory containing the `.rsproj` unless rooted. |
| `script`           | array of strings  | for `"script"` | Command lines run in one `cmd.exe` session. |
| `url`              | string            | for `"url"` | Link to open. Must begin with `http://`, `https://` or `mailto:`. |
| `args`             | array of strings  | no       | Arguments for an `"exe"`. Veneer quotes each element — do not add your own quotes. |
| `env`              | object            | no       | Environment variables for the launched program. Overrides the injected variables below. |
| `workingDirectory` | string            | no       | Relative to the project directory, and defaults to it. |
| `menu`             | string            | no       | Where the item appears in the menu bar. Defaults to `Reporting`. See **Menu paths** below. |
| `scenario`         | string            | no       | Per-addon scenario filter. Overrides `targetScenario`. See **Scenario scoping** below. |

`path`, `script` and `url` are three ways of saying what an entry does, and an entry must use **exactly one**. Specifying two renders a disabled item with a tooltip explaining which pair conflicted.
```

- [ ] **Step 2: Add the injected variables**

The reference does not mention them at all. Add after the field table:

```markdown
### Injected variables

`%VENEER_PORT%`, `%VENEER_PROJECT_DIR%` and `%VENEER_PROJECT_FILE%` expand inside `path`, `args`, `workingDirectory`, `url`, `env` values and script lines.

An unknown `%VAR%` is left as literal text rather than blanked, so a typo is visible rather than silently producing a truncated argument — or, for a `url`, a malformed address in the browser.

`VENEER_PORT` is the *configured* port, not a promise that the server is listening.
```

- [ ] **Step 3: Add the `type: "url"` section**

Add after the **Launching exe addons** section. The outer fence below is `~~~` so the inner JSON fence renders — copy the content between the `~~~` markers, not the markers themselves.

~~~markdown
### Linking to a page — `type: "url"`

```json
{ "name": "Model wiki", "type": "url", "url": "https://wiki.example.org/catchment", "menu": "Help" }
```

The URL must begin with `http://`, `https://` or `mailto:`. Anything else renders a disabled menu item with a tooltip.

`file://` is **deliberately excluded**. It would admit `file://server/share/tool.exe`, and without it `type: "url"` cannot launch a local program by any spelling. For a document on a network share, serve it over HTTP or use an `exe` addon.

**The scheme must be written literally.** `"url": "https://%HOST%/help"` is fine — the variable may appear anywhere after the scheme — but `"url": "%HELP_URL%"` is rejected, because the entry is validated when the menu is built, before any variable is expanded.

The link opens in whatever application the machine has registered for it. A `mailto:` link on a machine with no mail client will fail and be logged.
~~~

- [ ] **Step 4: Cross-link the how-to guide**

`Samples/addons/README.md` already covers the `cmd.exe` limitations, the output and logging model, and how to diagnose a greyed-out item. Do **not** copy any of that here — two copies to keep in sync is what produced this gap. Add a pointer at the end of the Addons section instead:

```markdown
Worked examples, the limitations of `type: "script"`, where addon output goes, and how to diagnose a greyed-out item are in [`Samples/addons/README.md`](../Samples/addons/README.md).
```

- [ ] **Step 5: Correct the "Launching exe addons" step 4**

Still in `docs/veneer-file-format.md`, the numbered list under **Launching exe addons** ends with *"Starts the Veneer HTTP server first if it isn't already running."* That is wrong: `LaunchAddon` opens the Veneer **panel**, it does not start the server. And after Task 6 the `url` path does not even do that.

Replace it with: *"Opens the Veneer panel if it is closed, because that is where addon output is written. `type: "url"` addons do not, since they produce no output."*

Outside the strict feature scope, but this file is being reconciled and the statement is actively misleading.

- [ ] **Step 6: Verify the table against the class**

Open `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` and check the `VeneerAddon` properties against the table, both directions: **every property has a row, every row has a property.** There should be exactly ten of each — `name`, `type`, `path`, `menu`, `scenario`, `args`, `env`, `workingDirectory`, `script`, `url`.

- [ ] **Step 7: Add the sample**

Create `Samples/addons/link-menu.rsproj.veneer`:

```json
{
  "addons": [
    { "name": "Project wiki",    "type": "url", "url": "https://wiki.example.org/catchment", "menu": "Help" },
    { "name": "Veneer API root", "type": "url", "url": "http://localhost:%VENEER_PORT%/",    "menu": "Help" },
    { "name": "Email support",   "type": "url", "url": "mailto:support@example.org",         "menu": "Help" }
  ]
}
```

- [ ] **Step 8: Update the samples README, all four places**

`Samples/addons/README.md` is the how-to, so a worked example belongs here as well as the reference row — but adding only that would leave three passages stale, which is the same drift this task exists to correct in the other file.

1. **Add a `type: "url"` section** after the `type: "script"` section, with a worked example, the allowlist, and the literal-scheme rule.
2. **Line 5** — "worked examples of the **two** launch modes" becomes three.
3. **Line 61** — the injected-variables applicability list reads "`path`, `args`, `workingDirectory`, `env` values and script lines". Add `url`.
4. **Lines 110-112** — the greyed-out-item causes list needs the four new reasons: `url` with `path` or `script`, `url` on a non-`url` type, `type: "url"` with no `url`, and a `url` outside the allowlist.

- [ ] **Step 9: Commit**

```bash
git add docs/veneer-file-format.md Samples/addons/link-menu.rsproj.veneer Samples/addons/README.md
git commit -m "docs: document url addons and reconcile the addon field table

The reference documented five addon fields; the schema has ten. Adds
args, env, workingDirectory, script and url, corrects the type row --
which still claimed only exe was implemented and that other values were
silently ignored -- and makes path conditional rather than required.

Also adds the injected variables, which the reference never mentioned,
and cross-links Samples/addons/README.md rather than duplicating its
limitations and output sections."
```

---

## Task 8: Manual verification in Source

Nothing in Tasks 3-6 has unit coverage — `ShellLink` launches a browser and the rest is WinForms. **Do not report this work complete without this task.**

- [ ] **Step 1: Build the plugin**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly
```
Expected: exit code 0. Then start Source with the built plugin and open a test project.

- [ ] **Step 2: Confirm the regression first, before verifying the fix**

`git stash` this work, build, and click an HTML report item and the Veneer logo.

Expected: **both fail** — the spec predicts an unhandled-exception dialog or a silently dead item. Record what actually happens, then `git stash pop`.

This step exists because the `UseShellExecute` diagnosis was inferred from the runtime default, never observed in Source. If both links work here, the diagnosis is wrong somewhere and the spec's framing needs revisiting — the change is still correct, but say so rather than quietly moving on.

- [ ] **Step 3: The three URL forms**

Copy `Samples/addons/link-menu.rsproj.veneer` next to a project as `<project>.rsproj.veneer` and reload.

Expected: a `Help` menu with three items. `Project wiki` opens the browser. `Veneer API root` opens `http://localhost:<configured port>/` — with the **configured** port substituted, not a literal `%VENEER_PORT%`. `Email support` opens the mail client.

- [ ] **Step 4: The panel is not opened**

Close the Veneer Web Server Monitoring panel, then click a URL addon.

Expected: the link opens and **the panel stays closed**. Then click an `exe` addon: the panel *does* open, because that is where its output goes.

- [ ] **Step 5: Rejected URLs are disabled, not launched**

Add `{ "name": "Bad", "type": "url", "url": "file://server/share/x.pdf", "menu": "Help" }` and reload.

Expected: `Bad` is **greyed out** with a tooltip naming the allowlist. Clicking it does nothing. Repeat with `"url": "C:\\Windows\\System32\\cmd.exe"` — **cmd must not launch.**

- [ ] **Step 6: Reports open on the configured port**

Set a non-default port (`options.defaultPort`, or the `VENEER_PORT` environment variable), put an `.htm` file in the project directory, reload, and click the report item.

Expected: it opens on the **configured** port. Before this work it always used 9876.

- [ ] **Step 7: The logo**

Expected: the logo item at the bottom of the last Veneer menu opens flowmatters.com.au.

- [ ] **Step 8: Record the results**

Append a "Verified" note to this plan stating what was checked, on which Source version, and **what Step 2 actually showed**. Commit:

```bash
git add docs/superpowers/plans/2026-07-31-veneer-url-addons.md
git commit -m "docs: record manual verification of url addons"
```

If any step fails, **stop and report** rather than patching blind.

---

## Task 9: Port to `legacy_ci`

A `legacy_ci` worktree already exists at `C:\src\projects\Veneer-legacy`. See [`branch-porting-guide.md`](../../../branch-porting-guide.md).

**Files (on that branch):** the same seven touched on `master` — `Addons/AddonUrl.cs`, `DomainActions/ShellLink.cs`, `Tests/AddonUrlTests.cs`, `Addons/VeneerConfiguration.cs`, `DomainActions/AddonLauncher.cs`, `VeneerMenu.cs`, `Tests/AddonValidationTests.cs` — plus `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`, `docs/veneer-file-format.md` and `Samples/addons/`.

- [ ] **Step 1: Confirm the branch state**

Run: `git -C C:\src\projects\Veneer-legacy log --oneline -5` and `git -C C:\src\projects\Veneer-legacy status --short`

The menu-ordering port (`91fd68a`) should be present. The tree should have no modified tracked files.

- [ ] **Step 2: Copy the two new source files and the test fixture**

`AddonUrl.cs`, `ShellLink.cs` and `Tests/AddonUrlTests.cs` need no adaptation — plain C#, nothing newer than 7.3, and the tests use only the portable NUnit subset.

- [ ] **Step 3: Apply the edits to the four modified files**

`VeneerConfiguration.cs`, `AddonLauncher.cs`, `VeneerMenu.cs`, `Tests/AddonValidationTests.cs`. Anchor on method names — that branch's `VeneerMenu.cs` has a **different method order** from `master`'s, and some lines carry trailing whitespace that must be matched exactly.

The `UseShellExecute = true` assignment is a **no-op** there: .NET Framework 4.8 already defaults it to `true`, which is exactly why the two call sites work on that branch and not on `master`. Keep it anyway so the branches stay identical.

- [ ] **Step 4: Add the `<Compile Include>` entries**

The non-SDK csproj enumerates its sources. Add `Addons\AddonUrl.cs`, `DomainActions\ShellLink.cs` and `Tests\AddonUrlTests.cs`. `master`'s SDK-style csproj needs none.

- [ ] **Step 5: Verify what can be verified**

That branch **does not currently build** in this environment — pristine `legacy_ci` at HEAD fails with a pre-existing `MC1000` WPF markup-compiler error, unrelated to this work. Confirm that is still the case by building before your changes, so you do not attribute it to the port.

Then verify what is verifiable:
```
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /nologo /target:library /langversion:7.3 /warnaserror /out:$env:TEMP\out.dll <AddonUrl.cs> <a stub VeneerAddon>
```
Expected: exit 0, proving the C# 7.3 constraint holds.

- [ ] **Step 6: Commit**

Use the same message bodies as `master`, and **state plainly in the commit message what was and was not verified** — the tests have not been run on that branch.

---

## Definition of done

- [ ] `dotnet test` passes with 33 new cases — **134 total** if the baseline was still 101.
- [ ] `AddonUrl` has no RiverSystem, TIME or WinForms dependency.
- [ ] `ShellLink.TryOpen` is the only place setting `UseShellExecute` **`= true`**. `AddonLauncher` sets it to `false` in two places, deliberately — do not "fix" those to satisfy this box.
- [ ] `docs/veneer-file-format.md`'s addon table has exactly one row per `VeneerAddon` property, checked both directions.
- [ ] Task 8's manual checks all pass and are recorded here, **including what Step 2 showed**.
- [ ] No `PROTOCOL_VERSION` bump, no `docs/api/` change.
- [ ] No `git add -A`. `CLAUDE.md`, `build.bat`, `specs/` and `all.txt` still untracked.
