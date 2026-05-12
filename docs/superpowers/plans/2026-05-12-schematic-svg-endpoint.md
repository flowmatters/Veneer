# Schematic SVG Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two REST endpoints — `GET /network/schematic.svg` (an SVG diagram of the node-link network in schematic coordinates with per-element `$tag$` placeholders) and `GET /network/schematic.svg/tags` (a JSON sidecar mapping element names to sanitised tag names). The SVG can be consumed by a Hydrograph `DynamicSvgWidgetComponent` to drive colour/width/opacity of every link and styleable node from a dashboard table row.

**Architecture:** A pure `SchematicSvgBuilder` consumes a `Network` plus a `SchematicNetworkConfigurationPersistent` and emits both the SVG string and the sidecar DTO. The endpoint layer in `SourceService` is the only place that knows about Source state and WCF; it fetches the shared scenario, gets the schematic via `ScriptHelpers.GetSchematic`, returns 404 if absent, and otherwise hands off to the builder. Six embedded SVG `<symbol>` snippets are loaded once at startup and used via `<use href="#…">` for the restylable node types. The rest fall back to the existing PNG resources at `/resources/{icon}`.

**Tech Stack:** C# 7.3, .NET Framework 4.8, classic WCF (`System.ServiceModel.Web`), NUnit (already referenced — used for pure-function tests of the sanitiser and viewBox math, runnable from an IDE).

**Spec:** `docs/superpowers/specs/2026-05-12-schematic-svg-endpoint-design.md`

**Branch:** `legacy_ci`. On this branch, REST endpoints are declared directly on `SourceService` via `[OperationContract]` + `[WebInvoke]`/`[WebGet]` attributes — `ISourceService.cs` is a near-empty stub and is **not** modified. Porting to `master`/CoreWCF (where endpoints must be declared on `ISourceService`) is a separate exercise per `branch-porting-guide.md`.

**Testing reality:** This repository has no separate test project and no CI test runner; the assembly does reference `nunit.framework`. Strategy: each task is gated by a clean MSBuild compile (`TreatWarningsAsErrors=true` in Debug), pure-function tests added as `[TestFixture]` classes inside the production assembly under a `Tests/` subfolder (runnable from ReSharper/Rider; skipped by default by the build), and end-to-end smoke verification via `curl` + a small `veneer-py` script at the end.

---

## File Structure

**Created:**
- `FlowMatters.Source.Veneer/Formatting/SchematicSvgBuilder.cs` — pure builder that produces both SVG string and sidecar DTO from `(Network, SchematicNetworkConfigurationPersistent)`. No WCF or `_sharedScenario` knowledge.
- `FlowMatters.Source.Veneer/Formatting/SchematicNameSanitiser.cs` — name → sanitised tag-name conversion plus collision resolution. Standalone static class so the unit tests stay tight.
- `FlowMatters.Source.Veneer/Formatting/NodeIconLibrary.cs` — `Dictionary<string, string>` keyed by `NodeModel` short type name → shape id (e.g. `"StorageNodeModel"` → `"triangle"`). Plus a lazy loader for the six embedded SVG resources, exposing `GetSymbolMarkup(string shapeId)`.
- `FlowMatters.Source.Veneer/ExchangeObjects/SchematicTagMap.cs` — JSON DTO for the sidecar (`SchematicTagMap`, `SchematicNodeTag`, `SchematicLinkTag`).
- `FlowMatters.Source.Veneer/Resources/NodeIcons/circle.svg`
- `FlowMatters.Source.Veneer/Resources/NodeIcons/triangle.svg`
- `FlowMatters.Source.Veneer/Resources/NodeIcons/diamond.svg`
- `FlowMatters.Source.Veneer/Resources/NodeIcons/hexagon.svg`
- `FlowMatters.Source.Veneer/Resources/NodeIcons/plus.svg`
- `FlowMatters.Source.Veneer/Resources/NodeIcons/trapezoid.svg`
- `FlowMatters.Source.Veneer/Tests/SchematicNameSanitiserTests.cs` — NUnit `[TestFixture]` for the sanitiser and collision resolver.
- `FlowMatters.Source.Veneer/Tests/SchematicSvgBuilderTests.cs` — NUnit `[TestFixture]` for viewBox math + a sidecar/SVG consistency check.

**Modified:**
- `FlowMatters.Source.Veneer/UriTemplates.cs` — add two `const string` entries.
- `FlowMatters.Source.Veneer/SourceService.cs` — add two endpoint methods near the existing `GetNetwork`/`GetNetworkGeographic` block (around line 234–249).
- `FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs` — bump `PROTOCOL_VERSION` (REST API surface changed; per project memory rule).
- `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj` — register all new `.cs` files as `<Compile Include="…" />` and the six SVG files as `<EmbeddedResource Include="…" />`. The csproj is classic (not SDK-style), so explicit registration is required or the files won't be compiled/embedded.

---

## Build & verification commands (referenced throughout)

Single-version Debug build (the gate for every task):

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln /p:Configuration=Debug
```

A clean compile (zero warnings, zero errors — `TreatWarningsAsErrors=true` in Debug) is the automated gate. If `References/` is missing, run `build.bat` once first to populate it from an installed Source version.

Running the NUnit fixtures (optional, manual): open the solution in Rider/Visual Studio with ReSharper and use the inline test runner against the built DLL. The tests are pure-function — they instantiate no Source state and need no References to resolve at test time beyond what the build already pulls in.

---

## Task 1: Add UriTemplates entries

**Why first:** Leaf change. Subsequent tasks reference these constants; defining them first means later code compiles without forward-declaration tricks.

**Files:**
- Modify: `FlowMatters.Source.Veneer/UriTemplates.cs:41-43`

- [ ] **Step 1: Add the two constants next to the existing network templates**

In `UriTemplates.cs`, after the existing `NetworkGeographic` line (line 43), add:

```csharp
public const string SchematicSvg = "/network/schematic.svg";

public const string SchematicSvgTags = "/network/schematic.svg/tags";
```

- [ ] **Step 2: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/UriTemplates.cs
git commit -m "Add UriTemplates entries for schematic SVG endpoints"
```

---

## Task 2: Implement `SchematicNameSanitiser`

**Why next:** Pure function with no Source dependencies. Easy to test in isolation. Used by `SchematicSvgBuilder` for both node and link tag-name generation.

**Files:**
- Create: `FlowMatters.Source.Veneer/Formatting/SchematicNameSanitiser.cs`
- Create: `FlowMatters.Source.Veneer/Tests/SchematicNameSanitiserTests.cs`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj` (register both new files)

- [ ] **Step 1: Write the sanitiser**

`FlowMatters.Source.Veneer/Formatting/SchematicNameSanitiser.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.Formatting
{
    internal static class SchematicNameSanitiser
    {
        private static readonly Regex NonAlphaNum = new Regex("[^A-Za-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Sanitise a single Source element name to the tag-name character class.
        /// Lowercase; runs of non-alphanumerics collapse to a single '_'; trim leading/trailing '_'.
        /// Returns an empty string if the input contains no alphanumerics.
        /// </summary>
        public static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var collapsed = NonAlphaNum.Replace(name, "_").ToLowerInvariant();
            return collapsed.Trim('_');
        }

        /// <summary>
        /// Sanitise a list of names, falling back to "<fallbackPrefix>_<index>" when sanitisation yields
        /// an empty string, and de-colliding duplicates by appending "_2", "_3", ... in order.
        /// Returns one tag-name per input, same order.
        /// </summary>
        public static List<string> SaniseAndDeCollide(IList<string> names, string fallbackPrefix)
        {
            var taken = new HashSet<string>();
            var result = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                var raw = Sanitise(names[i]);
                if (raw.Length == 0) raw = fallbackPrefix + "_" + i;

                var candidate = raw;
                int suffix = 2;
                while (taken.Contains(candidate))
                {
                    candidate = raw + "_" + suffix;
                    suffix++;
                }
                taken.Add(candidate);
                result.Add(candidate);
            }
            return result;
        }
    }
}
```

- [ ] **Step 2: Write the NUnit fixture**

`FlowMatters.Source.Veneer/Tests/SchematicNameSanitiserTests.cs`:

```csharp
using System.Collections.Generic;
using FlowMatters.Source.Veneer.Formatting;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class SchematicNameSanitiserTests
    {
        [TestCase("Storage @ Site #3", "storage_site_3")]
        [TestCase("Link  -- 5",        "link_5")]
        [TestCase("Burrendong Dam",    "burrendong_dam")]
        [TestCase("  ALL CAPS  ",      "all_caps")]
        [TestCase("__leading-trailing__", "leading_trailing")]
        [TestCase("Already_snake_case", "already_snake_case")]
        [TestCase("123Start",          "123start")]
        [TestCase("",                  "")]
        [TestCase("---",               "")]
        [TestCase("café",              "caf")]   // non-ASCII stripped; remaining "caf" survives
        public void Sanitise_ProducesExpectedOutput(string input, string expected)
        {
            Assert.That(SchematicNameSanitiser.Sanitise(input), Is.EqualTo(expected));
        }

        [Test]
        public void DeCollide_AppendsNumericSuffixInOrder()
        {
            var input = new List<string> { "Storage 1", "Storage 1", "Storage 1", "Other" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "elem");
            Assert.That(result, Is.EqualTo(new[] { "storage_1", "storage_1_2", "storage_1_3", "other" }));
        }

        [Test]
        public void DeCollide_FallbackUsedForEmptySanitisation()
        {
            var input = new List<string> { "---", "Real Name", "***" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "link");
            Assert.That(result, Is.EqualTo(new[] { "link_0", "real_name", "link_2" }));
        }

        [Test]
        public void DeCollide_FallbacksCanThemselvesCollide()
        {
            // Two empty-sanitisation inputs at the same index? Not possible — fallback uses the input
            // index. But if a real name sanitises to "link_0", it should de-collide against fallback.
            var input = new List<string> { "Link 0", "***" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "link");
            Assert.That(result, Is.EqualTo(new[] { "link_0", "link_1" })); // fallback uses index 1
        }
    }
}
```

- [ ] **Step 3: Register both files in the csproj**

Open `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`. In the main `<ItemGroup>` containing the `<Compile Include="…" />` entries (the one with `AbstractSourceServer.cs` etc., starting around line 240), add — alphabetised-ish, near the existing `Formatting/` entries around line 280:

```xml
<Compile Include="Formatting\SchematicNameSanitiser.cs" />
<Compile Include="Tests\SchematicNameSanitiserTests.cs" />
```

- [ ] **Step 4: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors. (The NUnit fixture compiles because `nunit.framework` is already referenced at csproj line 110.)

- [ ] **Step 5: (Optional) Run the fixture from an IDE**

Open the solution in Rider/VS+ReSharper and run `SchematicNameSanitiserTests`. Expected: all assertions pass. If you don't have an IDE handy, the compile gate is the only check for this task — the function is small and exercised end-to-end by Task 5's builder tests.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Formatting/SchematicNameSanitiser.cs \
        FlowMatters.Source.Veneer/Tests/SchematicNameSanitiserTests.cs \
        FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj
git commit -m "Add SchematicNameSanitiser with collision resolution"
```

---

## Task 3: Author the six SVG icon shapes and embed them

**Why next:** Pure asset addition with no code path consuming them yet. Getting them in early means `NodeIconLibrary` (Task 4) can be implemented against real files, not placeholders.

**Files:**
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/circle.svg`
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/triangle.svg`
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/diamond.svg`
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/hexagon.svg`
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/plus.svg`
- Create: `FlowMatters.Source.Veneer/Resources/NodeIcons/trapezoid.svg`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj` (six `<EmbeddedResource>` entries)

**Authoring rules for all six files:**
- Root element is a single `<symbol id="veneer-icon-<shape>" viewBox="-1 -1 2 2">…</symbol>` — **not** `<svg>`. The builder will inline this inside the document's `<defs>`.
- No `fill` or `stroke` attributes on the geometry. Those come from the `<use>` element at consumption time so per-node `$tag$` colours apply.
- Single primitive or path. Stroke width handled via `vector-effect="non-scaling-stroke"` so it doesn't scale with `<use>`'s width/height.

- [ ] **Step 1: Write `circle.svg`**

```xml
<symbol id="veneer-icon-circle" viewBox="-1 -1 2 2">
  <circle cx="0" cy="0" r="0.9" vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 2: Write `triangle.svg`** (equilateral, point up)

```xml
<symbol id="veneer-icon-triangle" viewBox="-1 -1 2 2">
  <polygon points="0,-0.9 0.78,0.45 -0.78,0.45" vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 3: Write `diamond.svg`**

```xml
<symbol id="veneer-icon-diamond" viewBox="-1 -1 2 2">
  <polygon points="0,-0.9 0.9,0 0,0.9 -0.9,0" vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 4: Write `hexagon.svg`** (point-up regular hexagon)

```xml
<symbol id="veneer-icon-hexagon" viewBox="-1 -1 2 2">
  <polygon points="0,-0.9 0.78,-0.45 0.78,0.45 0,0.9 -0.78,0.45 -0.78,-0.45" vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 5: Write `plus.svg`** (fat plus — width 0.5 arm, full extent 0.9)

```xml
<symbol id="veneer-icon-plus" viewBox="-1 -1 2 2">
  <polygon points="-0.25,-0.9 0.25,-0.9 0.25,-0.25 0.9,-0.25 0.9,0.25 0.25,0.25 0.25,0.9 -0.25,0.9 -0.25,0.25 -0.9,0.25 -0.9,-0.25 -0.25,-0.25"
           vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 6: Write `trapezoid.svg`** (isosceles, wider base at bottom)

```xml
<symbol id="veneer-icon-trapezoid" viewBox="-1 -1 2 2">
  <polygon points="-0.55,-0.6 0.55,-0.6 0.9,0.6 -0.9,0.6" vector-effect="non-scaling-stroke" />
</symbol>
```

- [ ] **Step 7: Register all six as embedded resources in the csproj**

In `FlowMatters.Source.Veneer.csproj`, find the existing `<ItemGroup>` containing the `<EmbeddedResource Include="Properties\Resources.resx">` entry (around line 329). Add a new sibling `<ItemGroup>` block after it:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\NodeIcons\circle.svg" />
  <EmbeddedResource Include="Resources\NodeIcons\triangle.svg" />
  <EmbeddedResource Include="Resources\NodeIcons\diamond.svg" />
  <EmbeddedResource Include="Resources\NodeIcons\hexagon.svg" />
  <EmbeddedResource Include="Resources\NodeIcons\plus.svg" />
  <EmbeddedResource Include="Resources\NodeIcons\trapezoid.svg" />
</ItemGroup>
```

- [ ] **Step 8: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors. The build will fail if any of the six files are absent (because the csproj now references them).

- [ ] **Step 9: Verify the resources are embedded**

Quick sanity check that the embedded names will be what Task 4 expects. From a PowerShell or bash prompt:

```bash
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln /p:Configuration=Debug /t:Build /v:detailed 2>&1 | grep -i "NodeIcons"
```

Or open the built DLL in ILSpy/dotPeek; expect resource names like `FlowMatters.Source.Veneer.Resources.NodeIcons.circle.svg`. Task 4 hard-codes this name pattern.

- [ ] **Step 10: Commit**

```bash
git add FlowMatters.Source.Veneer/Resources/NodeIcons/ \
        FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj
git commit -m "Add six embedded SVG node icon shapes"
```

---

## Task 4: Implement `NodeIconLibrary`

**Why next:** Bridges the embedded SVGs to the builder. Standalone — only depends on Task 3's resources being present.

**Files:**
- Create: `FlowMatters.Source.Veneer/Formatting/NodeIconLibrary.cs`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`

- [ ] **Step 1: Implement the library**

`FlowMatters.Source.Veneer/Formatting/NodeIconLibrary.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FlowMatters.Source.Veneer.Formatting
{
    /// <summary>
    /// Maps NodeModel short type names to SVG shape ids, and loads the embedded shape definitions
    /// on first use.
    /// </summary>
    internal static class NodeIconLibrary
    {
        // NodeModel short type name → shape id. Unmapped types fall back to PNG via /resources/.
        private static readonly Dictionary<string, string> TypeToShape =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "InflowNodeModel",                  "plus" },
                { "ConfluenceNodeModel",              "circle" },
                { "GaugeNodeModel",                   "trapezoid" },
                { "StorageNodeModel",                 "triangle" },
                { "SupplyPointNodeModel",             "diamond" },
                { "MinimumFlowRequirementNodeModel",  "hexagon" },
                { "MaximumFlowConstraintNodeModel",   "hexagon" },
                // Variants found in different Source versions can be added here as discovered.
            };

        private static readonly string[] AllShapes =
            { "circle", "triangle", "diamond", "hexagon", "plus", "trapezoid" };

        private static readonly Lazy<Dictionary<string, string>> SymbolMarkup =
            new Lazy<Dictionary<string, string>>(LoadAll);

        /// <summary>Returns the shape id for a NodeModel short type name, or null if unmapped.</summary>
        public static string GetShapeFor(string nodeModelTypeName)
        {
            string shape;
            return TypeToShape.TryGetValue(nodeModelTypeName ?? "", out shape) ? shape : null;
        }

        /// <summary>Returns the full SVG markup (the &lt;symbol&gt;…&lt;/symbol&gt;) for a shape id.</summary>
        public static string GetSymbolMarkup(string shapeId)
        {
            return SymbolMarkup.Value[shapeId];
        }

        /// <summary>Returns markup for all shapes (used to populate &lt;defs&gt;).</summary>
        public static IEnumerable<string> GetAllSymbolMarkup()
        {
            foreach (var s in AllShapes) yield return SymbolMarkup.Value[s];
        }

        private static Dictionary<string, string> LoadAll()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var asm = typeof(NodeIconLibrary).Assembly;
            foreach (var shape in AllShapes)
            {
                var resourceName = "FlowMatters.Source.Veneer.Resources.NodeIcons." + shape + ".svg";
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing embedded SVG resource: " + resourceName);
                    using (var reader = new StreamReader(stream))
                    {
                        result[shape] = reader.ReadToEnd().Trim();
                    }
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 2: Register the file in the csproj**

In `FlowMatters.Source.Veneer.csproj`, add to the main `<Compile>` group near the other `Formatting/` entries:

```xml
<Compile Include="Formatting\NodeIconLibrary.cs" />
```

- [ ] **Step 3: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/Formatting/NodeIconLibrary.cs \
        FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj
git commit -m "Add NodeIconLibrary mapping node types to embedded SVG symbols"
```

---

## Task 5: Implement sidecar DTOs

**Why next:** Small leaf type used by Task 6's builder. Defining it first keeps Task 6 focused on logic, not data-shape decisions.

**Files:**
- Create: `FlowMatters.Source.Veneer/ExchangeObjects/SchematicTagMap.cs`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`

- [ ] **Step 1: Write the DTOs**

`FlowMatters.Source.Veneer/ExchangeObjects/SchematicTagMap.cs`:

```csharp
using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class SchematicTagMap
    {
        [DataMember(Name = "viewBox")]
        public double[] ViewBox { get; set; }

        [DataMember(Name = "nodes")]
        public SchematicNodeTag[] Nodes { get; set; }

        [DataMember(Name = "links")]
        public SchematicLinkTag[] Links { get; set; }
    }

    [DataContract]
    public class SchematicNodeTag
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "tags")]
        public string[] Tags { get; set; }

        [DataMember(Name = "icon_kind")]
        public string IconKind { get; set; }  // "svg" or "png"

        [DataMember(Name = "icon_shape", EmitDefaultValue = false)]
        public string IconShape { get; set; }  // null for icon_kind == "png"

        [DataMember(Name = "hg_value")]
        public string HgValue { get; set; }
    }

    [DataContract]
    public class SchematicLinkTag
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "tags")]
        public string[] Tags { get; set; }

        [DataMember(Name = "hg_value")]
        public string HgValue { get; set; }
    }
}
```

- [ ] **Step 2: Register the file in the csproj**

Add to the `<Compile>` group near the other `ExchangeObjects/` entries:

```xml
<Compile Include="ExchangeObjects\SchematicTagMap.cs" />
```

- [ ] **Step 3: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/ExchangeObjects/SchematicTagMap.cs \
        FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj
git commit -m "Add SchematicTagMap DTOs for sidecar response"
```

---

## Task 6: Implement `SchematicSvgBuilder`

**Why next:** The core of the work. All earlier tasks fed in — sanitiser, icon library, DTOs, embedded shapes — so this task is pure assembly.

**Files:**
- Create: `FlowMatters.Source.Veneer/Formatting/SchematicSvgBuilder.cs`
- Create: `FlowMatters.Source.Veneer/Tests/SchematicSvgBuilderTests.cs`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`

The builder is a single static method `Build(Network, SchematicNetworkConfigurationPersistent)` returning a `SchematicSvgResult` (a tiny record-style class holding the SVG string and the sidecar DTO).

- [ ] **Step 1: Write the builder skeleton and result type**

`FlowMatters.Source.Veneer/Formatting/SchematicSvgBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer;
using Microsoft.CSharp.RuntimeBinder;
using RiverSystem;
using RiverSystem.Forms.SchematicBuilder;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.Veneer.Formatting
{
    internal class SchematicSvgResult
    {
        public string Svg { get; set; }
        public SchematicTagMap Sidecar { get; set; }
    }

    internal static class SchematicSvgBuilder
    {
        private const string DefaultLinkStroke = "#888888";
        private const string DefaultLinkStrokeWidth = "2";
        private const string DefaultOpacity = "1";
        private const string DefaultNodeFill = "#cccccc";
        private const string DefaultNodeStroke = "#333333";

        public static SchematicSvgResult Build(Network network, SchematicNetworkConfigurationPersistent schematic, string resourceBaseUrl)
        {
            // resourceBaseUrl is the scheme+authority of the Veneer host (e.g. "http://localhost:9876"),
            // used to make PNG-fallback <image href="..."> absolute. The widget inlines the SVG into the
            // dashboard's DOM, so relative URLs would resolve against the dashboard's origin and miss.
            // Same-document <use href="#veneer-icon-..."> fragment references are unaffected.
            // 1. Resolve schematic coordinates for every node.
            var nodes = network.nodes.Cast<Node>().ToList();
            var locations = nodes
                .Select(n => SchematicLocationForNode(n, schematic))
                .ToList();

            // 2. Compute bounding box.
            var bbox = ComputeBoundingBox(locations);

            // 3. Sanitise names.
            var nodeTagNames = SchematicNameSanitiser.SaniseAndDeCollide(
                nodes.Select(n => n.Name).ToList(), "node");
            var links = network.links.Cast<Link>().ToList();
            var linkTagNames = SchematicNameSanitiser.SaniseAndDeCollide(
                links.Select(l => l.Name).ToList(), "link");

            // 4. Emit SVG and sidecar in lockstep.
            var sb = new StringBuilder();
            var sidecar = new SchematicTagMap
            {
                ViewBox = new[] { bbox.MinX, bbox.MinY, bbox.Width, bbox.Height }
            };

            EmitSvgHeader(sb, bbox);
            EmitDefs(sb);
            EmitStyle(sb);
            sidecar.Links = EmitLinks(sb, nodes, links, locations, linkTagNames);
            sidecar.Nodes = EmitNodes(sb, nodes, locations, nodeTagNames, bbox.IconSize, resourceBaseUrl);
            EmitSvgFooter(sb);

            return new SchematicSvgResult { Svg = sb.ToString(), Sidecar = sidecar };
        }

        // -- coordinate helpers ----------------------------------------------------------------

        private class BBox
        {
            public double MinX, MinY, Width, Height, IconSize;
        }

        private static BBox ComputeBoundingBox(List<PointF> sourceLocations)
        {
            // Source schematic Y grows upward; SVG y grows downward. We negate Y at emit time.
            // For the viewBox, that means after flip: minY' = -maxY_source, maxY' = -minY_source.
            if (sourceLocations.Count == 0 ||
                (sourceLocations.All(p => Math.Abs(p.X - sourceLocations[0].X) < 1e-9 &&
                                          Math.Abs(p.Y - sourceLocations[0].Y) < 1e-9)))
            {
                // Degenerate: single point or empty — use a 100×100 centred viewBox.
                var cx = sourceLocations.Count > 0 ? sourceLocations[0].X : 0.0;
                var cy = sourceLocations.Count > 0 ? -sourceLocations[0].Y : 0.0;
                return new BBox
                {
                    MinX = cx - 50, MinY = cy - 50, Width = 100, Height = 100,
                    IconSize = 100.0 / 80.0
                };
            }

            var minX = sourceLocations.Min(p => (double)p.X);
            var maxX = sourceLocations.Max(p => (double)p.X);
            var minYsrc = sourceLocations.Min(p => (double)p.Y);
            var maxYsrc = sourceLocations.Max(p => (double)p.Y);

            // Flipped Y
            var minY = -maxYsrc;
            var maxY = -minYsrc;

            var rawWidth = maxX - minX;
            var rawHeight = maxY - minY;
            // Pad 5% of max dimension on each side
            var pad = 0.05 * Math.Max(rawWidth, rawHeight);
            var padded = new BBox
            {
                MinX = minX - pad,
                MinY = minY - pad,
                Width = rawWidth + 2 * pad,
                Height = rawHeight + 2 * pad,
            };
            var diag = Math.Sqrt(rawWidth * rawWidth + rawHeight * rawHeight);
            padded.IconSize = diag / 80.0;
            return padded;
        }

        private static PointF SchematicLocationForNode(Node n, SchematicNetworkConfigurationPersistent schematic)
        {
            return schematic.ExistingFeatureShapeProperties
                .Where(shape => shape.Feature == n)
                .Select(shape => shape.Location)
                .FirstOrDefault();
        }

        // -- SVG emission ---------------------------------------------------------------------

        private static void EmitSvgHeader(StringBuilder sb, BBox bbox)
        {
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"");
            sb.Append(F(bbox.MinX)); sb.Append(' ');
            sb.Append(F(bbox.MinY)); sb.Append(' ');
            sb.Append(F(bbox.Width)); sb.Append(' ');
            sb.Append(F(bbox.Height));
            sb.Append("\">");
        }

        private static void EmitDefs(StringBuilder sb)
        {
            sb.Append("<defs>");
            foreach (var symbolMarkup in NodeIconLibrary.GetAllSymbolMarkup())
                sb.Append(symbolMarkup);
            sb.Append("</defs>");
        }

        private static void EmitStyle(StringBuilder sb)
        {
            // Dashboard widget toggles .hg-selected on click; the rule below provides a fallback.
            sb.Append("<style>[data-hg-value]{cursor:pointer;}.hg-selected{stroke:#1a73e8;stroke-width:3;}</style>");
        }

        private static SchematicLinkTag[] EmitLinks(
            StringBuilder sb,
            List<Node> nodes,
            List<Link> links,
            List<PointF> locations,
            List<string> linkTagNames)
        {
            var nodeIndex = new Dictionary<Node, int>();
            for (int i = 0; i < nodes.Count; i++) nodeIndex[nodes[i]] = i;

            var sidecarLinks = new List<SchematicLinkTag>(links.Count);
            sb.Append("<g class=\"veneer-links\">");
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                var tag = linkTagNames[i];
                int fromIdx, toIdx;
                if (!nodeIndex.TryGetValue((Node)link.UpstreamNode, out fromIdx) ||
                    !nodeIndex.TryGetValue((Node)link.DownstreamNode, out toIdx))
                {
                    // Defensive: link to a node not in network.nodes — skip.
                    continue;
                }
                var fromLoc = locations[fromIdx];
                var toLoc = locations[toIdx];

                var nameEsc = HtmlEscape(link.Name);
                sb.Append("<line x1=\"").Append(F(fromLoc.X))
                  .Append("\" y1=\"").Append(F(-fromLoc.Y))
                  .Append("\" x2=\"").Append(F(toLoc.X))
                  .Append("\" y2=\"").Append(F(-toLoc.Y))
                  .Append("\" stroke=\"$link_").Append(tag).Append("_stroke$\"")
                  .Append(" stroke-width=\"$link_").Append(tag).Append("_stroke_width$\"")
                  .Append(" opacity=\"$link_").Append(tag).Append("_opacity$\"")
                  .Append(" data-hg-value=\"link:").Append(tag).Append("\"")
                  .Append(" data-default-link_").Append(tag).Append("_stroke=\"").Append(DefaultLinkStroke).Append("\"")
                  .Append(" data-default-link_").Append(tag).Append("_stroke_width=\"").Append(DefaultLinkStrokeWidth).Append("\"")
                  .Append(" data-default-link_").Append(tag).Append("_opacity=\"").Append(DefaultOpacity).Append("\"")
                  .Append(" data-default-link_").Append(tag).Append("_label=\"").Append(nameEsc).Append("\"")
                  .Append("><title>$link_").Append(tag).Append("_label$</title></line>");

                sidecarLinks.Add(new SchematicLinkTag
                {
                    Name = link.Name,
                    TagName = tag,
                    Tags = new[] { "stroke", "stroke_width", "opacity", "label" },
                    HgValue = "link:" + tag,
                });
            }
            sb.Append("</g>");
            return sidecarLinks.ToArray();
        }

        private static SchematicNodeTag[] EmitNodes(
            StringBuilder sb,
            List<Node> nodes,
            List<PointF> locations,
            List<string> nodeTagNames,
            double iconSize,
            string resourceBaseUrl)
        {
            var sidecarNodes = new List<SchematicNodeTag>(nodes.Count);
            sb.Append("<g class=\"veneer-nodes\">");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var tag = nodeTagNames[i];
                var loc = locations[i];

                var modelTypeName = NodeModelTypeName(n);
                var shape = NodeIconLibrary.GetShapeFor(modelTypeName);

                var x = loc.X - iconSize / 2.0;
                var y = -loc.Y - iconSize / 2.0;
                var nameEsc = HtmlEscape(n.Name);

                if (shape != null)
                {
                    sb.Append("<use href=\"#veneer-icon-").Append(shape).Append("\"")
                      .Append(" x=\"").Append(F(x))
                      .Append("\" y=\"").Append(F(y))
                      .Append("\" width=\"").Append(F(iconSize))
                      .Append("\" height=\"").Append(F(iconSize)).Append("\"")
                      .Append(" fill=\"$node_").Append(tag).Append("_fill$\"")
                      .Append(" stroke=\"$node_").Append(tag).Append("_stroke$\"")
                      .Append(" opacity=\"$node_").Append(tag).Append("_opacity$\"")
                      .Append(" data-hg-value=\"node:").Append(tag).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_fill=\"").Append(DefaultNodeFill).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_stroke=\"").Append(DefaultNodeStroke).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_opacity=\"").Append(DefaultOpacity).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_label=\"").Append(nameEsc).Append("\"")
                      .Append("><title>$node_").Append(tag).Append("_label$</title></use>");

                    sidecarNodes.Add(new SchematicNodeTag
                    {
                        Name = n.Name,
                        TagName = tag,
                        Tags = new[] { "fill", "stroke", "opacity", "label" },
                        IconKind = "svg",
                        IconShape = shape,
                        HgValue = "node:" + tag,
                    });
                }
                else
                {
                    var iconRes = WebUtility.UrlEncode(ResourceNameForNode(n));
                    sb.Append("<image href=\"").Append(resourceBaseUrl).Append("/resources/").Append(iconRes).Append("\"")
                      .Append(" x=\"").Append(F(x))
                      .Append("\" y=\"").Append(F(y))
                      .Append("\" width=\"").Append(F(iconSize))
                      .Append("\" height=\"").Append(F(iconSize)).Append("\"")
                      .Append(" opacity=\"$node_").Append(tag).Append("_opacity$\"")
                      .Append(" data-hg-value=\"node:").Append(tag).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_opacity=\"").Append(DefaultOpacity).Append("\"")
                      .Append(" data-default-node_").Append(tag).Append("_label=\"").Append(nameEsc).Append("\"")
                      .Append("><title>$node_").Append(tag).Append("_label$</title></image>");

                    sidecarNodes.Add(new SchematicNodeTag
                    {
                        Name = n.Name,
                        TagName = tag,
                        Tags = new[] { "opacity", "label" },
                        IconKind = "png",
                        IconShape = null,
                        HgValue = "node:" + tag,
                    });
                }
            }
            sb.Append("</g>");
            return sidecarNodes.ToArray();
        }

        private static void EmitSvgFooter(StringBuilder sb) => sb.Append("</svg>");

        // -- type-name extraction (mirrors GeoJSONFeature.ResourceName) ----------------------

        private static string NodeModelTypeName(Node n)
        {
            var model = RetrieveNodeModel(n);
            return model != null ? model.GetType().Name : null;
        }

        private static string ResourceNameForNode(Node n)
        {
            object o = (object)RetrieveNodeModel(n) ?? n.FlowPartitioning;
            return o.GetType().Name;
        }

        private static NodeModel RetrieveNodeModel(dynamic n)
        {
            try { return n.NodeModel; }
            catch (RuntimeBinderException) { return n.NodeModels[0]; }
        }

        // -- formatting helpers --------------------------------------------------------------

        private static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
```

- [ ] **Step 2: Write the NUnit fixture for the testable bits**

The end-to-end builder needs a `Network` + `SchematicNetworkConfigurationPersistent`, which are awkward to instantiate without a loaded Source scenario. So the fixture targets only the parts that don't need that: viewBox math via a small `internal` accessor exposed for tests, and a sidecar/SVG consistency check fed by an in-process call from the integration smoke test (Task 8).

For now, write a viewBox-focused fixture. Add this `internal` accessor inside `SchematicSvgBuilder` (right above the `BBox` class):

```csharp
internal static double[] ComputeViewBoxForTesting(IEnumerable<PointF> points)
{
    var bbox = ComputeBoundingBox(points.ToList());
    return new[] { bbox.MinX, bbox.MinY, bbox.Width, bbox.Height, bbox.IconSize };
}
```

Then the fixture:

`FlowMatters.Source.Veneer/Tests/SchematicSvgBuilderTests.cs`:

```csharp
using System.Drawing;
using FlowMatters.Source.Veneer.Formatting;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class SchematicSvgBuilderTests
    {
        [Test]
        public void ViewBox_TwoPoints_FlipsYAndPads()
        {
            // Source schematic: (0,0) bottom-left, (100,50) top-right.
            // After Y-flip: (0,0) becomes (0,0), (100,50) becomes (100,-50).
            // Bbox before pad: x in [0,100], y in [-50,0]; width=100, height=50.
            // Pad = 5% of max(100,50) = 5. So expected viewBox: -5, -55, 110, 60.
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(
                new[] { new PointF(0, 0), new PointF(100, 50) });

            Assert.That(result[0], Is.EqualTo(-5.0).Within(1e-9));   // minX
            Assert.That(result[1], Is.EqualTo(-55.0).Within(1e-9));  // minY (flipped)
            Assert.That(result[2], Is.EqualTo(110.0).Within(1e-9)); // width
            Assert.That(result[3], Is.EqualTo(60.0).Within(1e-9));  // height
            // iconSize = sqrt(100^2 + 50^2) / 80 ≈ 1.3975...
            Assert.That(result[4], Is.EqualTo(System.Math.Sqrt(12500) / 80.0).Within(1e-9));
        }

        [Test]
        public void ViewBox_SinglePoint_FallsBackTo100x100()
        {
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(new[] { new PointF(42, 7) });
            // Center on (42, -7) (after flip), 100×100 around it.
            Assert.That(result[0], Is.EqualTo(-8.0).Within(1e-9));    // minX = 42 - 50
            Assert.That(result[1], Is.EqualTo(-57.0).Within(1e-9));   // minY = -7 - 50
            Assert.That(result[2], Is.EqualTo(100.0).Within(1e-9));
            Assert.That(result[3], Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void ViewBox_AllCoincident_FallsBackTo100x100()
        {
            var result = SchematicSvgBuilder.ComputeViewBoxForTesting(
                new[] { new PointF(0, 0), new PointF(0, 0), new PointF(0, 0) });
            Assert.That(result[2], Is.EqualTo(100.0).Within(1e-9));
            Assert.That(result[3], Is.EqualTo(100.0).Within(1e-9));
        }
    }
}
```

- [ ] **Step 3: Register both files in the csproj**

```xml
<Compile Include="Formatting\SchematicSvgBuilder.cs" />
<Compile Include="Tests\SchematicSvgBuilderTests.cs" />
```

- [ ] **Step 4: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors. If you see a `TreatWarningsAsErrors` failure on `dynamic` usage (the `RetrieveNodeModel` helper mirrors `GeoJSONFeature.cs:79`), that's pre-existing pattern and should compile cleanly.

- [ ] **Step 5: (Optional) Run the fixture from an IDE**

Open the solution in Rider/VS+ReSharper, run `SchematicSvgBuilderTests`. Expected: three passing assertions.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Formatting/SchematicSvgBuilder.cs \
        FlowMatters.Source.Veneer/Tests/SchematicSvgBuilderTests.cs \
        FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj
git commit -m "Add SchematicSvgBuilder producing SVG and sidecar"
```

---

## Task 7: Wire the endpoints into `SourceService` and bump PROTOCOL_VERSION

**Why next:** Final assembly. Everything else compiles cleanly; this puts the new code on the wire.

**Files:**
- Modify: `FlowMatters.Source.Veneer/SourceService.cs` (insert after the existing network endpoints, around line 249)
- Modify: `FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs:18` (bump `PROTOCOL_VERSION`)

- [ ] **Step 1: Add the two endpoint methods to `SourceService`**

Open `SourceService.cs` and find the block of network endpoints around lines 233–265 (`GetNetwork`, `GetNetworkGeographic`, `GetNode`, `GetLink`). Insert the two new methods immediately after `GetNetworkGeographic` (after the closing `}` at line 249).

Also add the necessary `using` directives at the top of the file. The existing file already has most of these; add only the missing ones:

```csharp
using FlowMatters.Source.Veneer.Formatting;
using FlowMatters.Source.Veneer.RemoteScripting;
using RiverSystem.Forms.SchematicBuilder;
```

(`FlowMatters.Source.Veneer.RemoteScripting` is for `ScriptHelpers.GetSchematic`. The others may already be present.)

Then insert the methods:

```csharp
[OperationContract]
[WebInvoke(Method = "GET", UriTemplate = UriTemplates.SchematicSvg)]
public Stream GetSchematicSvg()
{
    Log("Requested schematic SVG");
    if (Scenario == null)
        return WriteJsonError(HttpStatusCode.NotFound, "no scenario loaded");

    var schematic = ScriptHelpers.GetSchematic(Scenario);
    if (schematic == null || schematic.ExistingFeatureShapeProperties == null ||
        schematic.ExistingFeatureShapeProperties.Count == 0)
    {
        return WriteJsonError(HttpStatusCode.NotFound,
            "scenario has no schematic; use /network for geographic coordinates");
    }

    var resourceBaseUrl = GetResourceBaseUrl();
    var result = SchematicSvgBuilder.Build(Scenario.Network, schematic, resourceBaseUrl);
    WebOperationContext.Current.OutgoingResponse.ContentType = "image/svg+xml";
    return new MemoryStream(Encoding.UTF8.GetBytes(result.Svg));
}

[OperationContract]
[WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.SchematicSvgTags)]
public SchematicTagMap GetSchematicSvgTags()
{
    Log("Requested schematic SVG tags");
    if (Scenario == null)
    {
        WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
        return null;
    }

    var schematic = ScriptHelpers.GetSchematic(Scenario);
    if (schematic == null || schematic.ExistingFeatureShapeProperties == null ||
        schematic.ExistingFeatureShapeProperties.Count == 0)
    {
        WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
        return null;
    }

    var result = SchematicSvgBuilder.Build(Scenario.Network, schematic, GetResourceBaseUrl());
    return result.Sidecar;
}

private static Stream WriteJsonError(HttpStatusCode status, string message)
{
    WebOperationContext.Current.OutgoingResponse.StatusCode = status;
    WebOperationContext.Current.OutgoingResponse.ContentType = "application/json";
    var json = "{\"error\":\"" + message.Replace("\"", "\\\"") + "\"}";
    return new MemoryStream(Encoding.UTF8.GetBytes(json));
}

private static string GetResourceBaseUrl()
{
    // Scheme + authority of the request that hit us; used to make PNG icon hrefs absolute so
    // the dashboard widget (which inlines the SVG into its own document) can still resolve them.
    var uri = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.RequestUri;
    return uri.GetLeftPart(UriPartial.Authority);
}
```

Notes on integration:
- `Scenario` is the public `RiverSystemScenario Scenario` accessor on `SourceService` (resolved from `_sharedScenario` per the per-call constructor at line 94).
- `ScriptHelpers.GetSchematic` at line 298 returns a newly-created empty `SchematicNetworkConfigurationPersistent` if none is stored — hence the `ExistingFeatureShapeProperties.Count == 0` check, which is the actual signal that no schematic has been authored.
- Returning `Stream` for the SVG keeps the WCF dispatcher from JSON-wrapping the response; the existing `GetResource` endpoint (line 218–231) is the precedent.
- `WriteJsonError` is local to the SVG endpoint; if the codebase already has a JSON error helper, prefer that (none exists at the time of writing).
- `SchematicTagMap` is in `FlowMatters.Source.Veneer.ExchangeObjects` — make sure that `using` is present.

- [ ] **Step 2: Bump `PROTOCOL_VERSION`**

In `FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs:18`, change:

```csharp
public const int PROTOCOL_VERSION = 20260201;
```

to today's date in `YYYYMMDD` form:

```csharp
public const int PROTOCOL_VERSION = 20260512;
```

- [ ] **Step 3: Build and confirm clean compile**

Run the build command. Expected: zero warnings, zero errors. If you see a missing-type error for `SchematicTagMap` or similar, double-check the `using` directives added in Step 1.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/SourceService.cs \
        FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs
git commit -m "Add /network/schematic.svg and /tags endpoints"
```

---

## Task 8: End-to-end smoke verification

**Why last:** The compile gate has signed off on every other task. Smoke verification proves the wire is actually live and that the SVG renders.

**Files:**
- None modified. This task is verification only.

- [ ] **Step 1: Launch Veneer against a real Source scenario with a schematic**

Open Source 5.x (or your usual version), open a project file that has an authored schematic (e.g. an existing Veneer test scenario in `..\..\Output\` or a manually-prepared one), and start the Veneer server via the Tools menu. Note the port (default 9876).

- [ ] **Step 2: Curl the SVG endpoint**

```bash
curl -i http://localhost:9876/network/schematic.svg | head -40
```

Expected:
- `HTTP/1.1 200 OK`
- `Content-Type: image/svg+xml`
- Body starts with `<svg xmlns="http://www.w3.org/2000/svg" viewBox="`
- A `<defs>` block containing all six `<symbol>` definitions.
- At least one `<line>` and one `<use>` or `<image>` element.

If you see `404`, check: (a) is there a scenario loaded? (b) does the scenario have an authored schematic — i.e. has anyone opened the schematic view and arranged nodes? An untouched project's `ExistingFeatureShapeProperties` is empty and yields the documented 404.

- [ ] **Step 3: Curl the sidecar**

```bash
curl -s http://localhost:9876/network/schematic.svg/tags | python -m json.tool | head -50
```

Expected:
- A JSON object with `viewBox`, `nodes`, `links` keys.
- Each node entry has `name`, `tag_name`, `tags`, `icon_kind`, `hg_value`. For `icon_kind: "svg"` entries, `icon_shape` is also present.
- Each link entry has `name`, `tag_name`, `tags`, `hg_value`.

- [ ] **Step 4: Consistency check — every sidecar tag appears in the SVG**

A small script to assert the two responses agree (run from anywhere with curl + bash + grep):

```bash
curl -s http://localhost:9876/network/schematic.svg > /tmp/schematic.svg
curl -s http://localhost:9876/network/schematic.svg/tags > /tmp/tags.json

python3 - <<'PY'
import json, re, sys
svg = open('/tmp/schematic.svg').read()
tags = json.load(open('/tmp/tags.json'))
missing = []
for n in tags['nodes']:
    for t in n['tags']:
        placeholder = f"$node_{n['tag_name']}_{t}$"
        default = f"data-default-node_{n['tag_name']}_{t}="
        if placeholder not in svg or default not in svg:
            missing.append(f"node {n['tag_name']} {t}")
for l in tags['links']:
    for t in l['tags']:
        placeholder = f"$link_{l['tag_name']}_{t}$"
        default = f"data-default-link_{l['tag_name']}_{t}="
        if placeholder not in svg or default not in svg:
            missing.append(f"link {l['tag_name']} {t}")
if missing:
    print("MISSING:")
    for m in missing: print("  " + m)
    sys.exit(1)
print(f"OK: {len(tags['nodes'])} nodes, {len(tags['links'])} links, all tags and defaults present.")
PY
```

Expected: `OK: N nodes, M links, all tags and defaults present.`

- [ ] **Step 5: Visual check — open the SVG in a browser**

```bash
start /tmp/schematic.svg   # Windows
```

(Or open via file:// URL.) Expected: the entire network rendered as a grey skeleton — all defaults applied because no substitution happened. Links are grey lines; styleable nodes show their geometric shape; PNG-fallback nodes show their existing icon at reduced visibility. The layout should match the schematic view inside Source.

- [ ] **Step 6: 404 paths**

Briefly close the project in Source (no scenario loaded) and re-curl:

```bash
curl -i http://localhost:9876/network/schematic.svg
```

Expected: `HTTP/1.1 404 Not Found`, JSON body `{"error":"no scenario loaded"}`.

Open a brand-new Source project without ever opening the schematic view, then curl again. Expected: `404 Not Found` with `{"error":"scenario has no schematic; use /network for geographic coordinates"}`.

- [ ] **Step 7: (Optional) DOMPurify sanity check**

Take `/tmp/schematic.svg` from Step 4 and paste it into the DOMPurify playground (https://cure53.de/purify) with the `USE_PROFILES: { svg: true, svgFilters: true }` option and `ADD_TAGS: ['use']`. Expected: no `<line>`, `<use>`, `<image>`, `<symbol>`, `<defs>`, `<style>`, `data-default-*`, or `data-hg-value` attributes are stripped. If something is stripped, file an issue and re-evaluate the icon shapes / structure.

- [ ] **Step 8: No commit needed for this task** — it's verification only.

---

## Out of scope (will not be implemented in this plan)

The spec lists these as forward references; this plan does not address them:

- Subsetting via query parameters (`?between=<nodeA>&<nodeB>`)
- Geographic-coordinate sibling endpoint
- Catchments
- Per-node icon-size overrides
- Restylable bitmap icons (tinting via filters)
- Port to `master`/CoreWCF (separate exercise per `branch-porting-guide.md`)
