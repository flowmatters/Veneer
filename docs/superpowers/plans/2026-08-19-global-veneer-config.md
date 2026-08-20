# Global `.veneer` Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `.veneer` configuration live in a per-user directory (`~/.veneer`) as well as beside the `.rsproj`, so a model shared over git can carry per-user addons that survive a fresh clone.

**Architecture:** A new pure `VeneerConfigurationResolver` in `Addons/` owns everything interesting — resolving the config directory, picking the project layer from two candidates, and merging layers — with the file system entering only through injected `Func<string,bool>` / `Func<string,string>` delegates, exactly as `MenuLayout` is deliberately free of RiverSystem and WinForms. `VeneerConfiguration.Load` shrinks to an I/O adapter that reads each discovered file, parses it in isolation, and hands the layers to the resolver. Consumers receive a `ResolvedVeneerConfiguration` with no `targetScenario` field, because the per-file `targetScenario` is pushed down into each addon during the merge.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`) on `master`; .NET Framework 4.8 / C# 7.3 on `legacy_ci`. Newtonsoft.Json. WinForms menu-bar integration. NUnit 4.x.

**Spec:** [`docs/superpowers/specs/2026-08-19-global-veneer-config-design.md`](../specs/2026-08-19-global-veneer-config-design.md)

---

## Prerequisites

### Build and test command

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

**Baseline at commit `f62aa28`: `Passed: 134`, 0 failed** — verified immediately before writing this plan. This plan adds 47 cases, so the expected end state is **181**. Counts below include each `[TestCase]` row separately, which is how the runner counts them. Append `--filter "FullyQualifiedName~VeneerConfigurationResolverTests"` to scope. `MSB3277` reference-version warnings are pre-existing noise.

### `TreatWarningsAsErrors` is true in Debug

`NoWarn` covers only `1591` and `1587`. Do not leave unused locals behind. An unused `using` is **not** a compiler warning, so it will not break the build.

### C# 7.3 only

`legacy_ci` targets .NET Framework 4.8 with C# 7.3 and Task 11 ports there. **Do not use** target-typed `new()`, switch expressions, index-from-end, ranges, or nullable reference types. Object initialisers, `?.`, `??`, `$"…"`, `out var` and `bool?` are all fine.

### NUnit must stay portable

`Tests/AddonAssert.cs` documents the constraint: `legacy_ci` builds against whatever NUnit the targeted Source version bundles, **from 2.6.4 up**. So use only `Assert.That(x, Is.True/False/Null/Not.Null/EqualTo(...))` and `[TestCase]`. **Do not use** `Does.Contain`, `StringAssert`, `Assert.Multiple`, or `CollectionAssert`. For substring assertions use the existing `AddonAssert.Contains(actual, expected, because)`. For array comparisons, join to a string and compare with `Is.EqualTo`, the way `MenuLayoutTests` does.

### Line numbers drift

Cited line numbers are valid at `f62aa28`. Anchor edits on **method names and quoted snippets**, which are stable.

### Do not touch the REST API

This feature adds no endpoint and changes no payload, so **do not** bump `PROTOCOL_VERSION` in `VeneerStatus.cs` and **do not** edit `docs/api/`.

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `Addons/VeneerConfigurationResolver.cs` | **New.** Pure config-directory resolution, layer discovery, JSON parse, layer merge. Plus the carriers `ConfigCandidates`, `VeneerConfigurationLayer`, `ResolvedVeneerConfiguration`. | 1, 2, 4, 6 |
| `Addons/VeneerConfiguration.cs` | On-disk schema types (`VeneerConfiguration`, `VeneerAddon`, `VeneerOptions`) plus the thin I/O adapter `Load` / `ConfigurationFilename` / `ConfigDirectory`, and the scenario-filter helpers. | 3, 5, 6 |
| `DomainActions/AddonContext.cs` | Gains `ConfigDirectory`. | 7 |
| `DomainActions/AddonEnvironment.cs` | Injects `VENEER_CONFIG_DIR`. | 7 |
| `VeneerMenu.cs` | Call-site updates only: option assignment, filter arguments, `BuildAddonContext`. | 3, 5, 7 |
| `AutoStart/ProjectLoadListener.cs` | Call-site update plus the new `LogConfigurationChain`. | 3, 8 |
| `Tests/VeneerConfigurationResolverTests.cs` | **New.** All 44 resolver cases. | 1, 2, 4, 5, 6 |
| `Tests/AddonEnvironmentTests.cs` | 2 new cases for `VENEER_CONFIG_DIR`. | 7 |
| `docs/veneer-file-format.md`, `Samples/addons/README.md` | Documentation. | 9 |

Tasks 1–2 and 4–6 all extend the same new resolver file and the same new test file. That is deliberate: the unit is "discovery and merge", and splitting it across files by method would scatter one responsibility.

---

## Task 0: Confirm the starting state

**Files:** none

- [ ] **Step 1: Verify the baseline**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 134, Failed: 0`. If the count differs, stop and reconcile before continuing — every later step quotes a running total.

- [ ] **Step 2: Confirm the anchors this plan edits**

Run: `git rev-parse --short HEAD`
Expected: `f62aa28` or a descendant.

Confirm these exist, by name not line number:
- `VeneerConfiguration.ConfigurationFilename(RiverSystemProject)` containing `Replace(".rsproj", ".rsproj.veneer")`
- `VeneerConfiguration.AddonAppliesTo` and `EffectiveFilter`, both taking a `VeneerConfiguration config` parameter
- `VeneerMenu.PopulateReportMenu` containing `WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts;`
- `VeneerMenu.BuildAddonContext`
- `ProjectLoadListener.ApplyDefaultsFromEnvironmentAndConfig` and `ApplyScenarioChange`

---

## Task 1: Resolve the configuration directory

**Files:**
- Create: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`
- Create: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**4 new cases (one test is a 2-row `[TestCase]`). Running total: 138.**

- [ ] **Step 1: Write the failing tests**

Create `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class VeneerConfigurationResolverTests
    {
        private static Func<string, string> Env(string configDir)
        {
            return name => name == "VENEER_CONFIG_DIR" ? configDir : null;
        }

        [Test]
        public void ConfigDirectory_PrefersTheEnvironmentVariable()
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(@"D:\shared\veneer"), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"D:\shared\veneer"));
        }

        [Test]
        public void ConfigDirectory_FallsBackToDotVeneerUnderTheProfile()
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(null), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"C:\Users\joel\.veneer"));
        }

        // Whitespace, not just empty: an unset variable read through a batch file
        // frequently arrives as " ", and treating that as a directory would send
        // every lookup to the process working directory.
        [TestCase("")]
        [TestCase("   ")]
        public void ConfigDirectory_TreatsBlankEnvironmentValueAsUnset(string configured)
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(configured), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"C:\Users\joel\.veneer"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure**, `CS0103` / `CS0246` — `VeneerConfigurationResolver` does not exist.

- [ ] **Step 3: Write the minimal implementation**

Create `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// Discovery and merge logic for .veneer configuration. Deliberately free of
    /// RiverSystem, TIME, WinForms and System.IO.File dependencies -- the file
    /// system enters only through injected delegates -- so that the whole
    /// precedence table is testable without a loaded Source scenario. Path.Combine
    /// is string manipulation and touches no disk.
    /// </summary>
    public static class VeneerConfigurationResolver
    {
        public const string GLOBAL_FILENAME = "global.veneer";
        public const string CONFIG_DIR_VARIABLE = "VENEER_CONFIG_DIR";
        public const string DEFAULT_CONFIG_DIR_NAME = ".veneer";
        public const string CONFIG_EXTENSION = ".veneer";

        /// <summary>
        /// Never creates the directory and never checks that it exists -- a
        /// missing directory simply contributes no layers. Returns null when
        /// there is no profile to fall back on, which callers treat as
        /// "no global configuration".
        /// </summary>
        public static string ConfigDirectory(Func<string, string> getEnv, Func<string> userProfile)
        {
            var configured = getEnv(CONFIG_DIR_VARIABLE);
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var profile = userProfile();
            if (string.IsNullOrWhiteSpace(profile))
                return null;

            return Path.Combine(profile, DEFAULT_CONFIG_DIR_NAME);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 138, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs
git commit -m "feat: resolve the Veneer configuration directory"
```

---

## Task 2: Discover the two layers

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**13 new cases (11 tests, one of them a 3-row `[TestCase]`). Running total: 151.**

This is the precedence table from the spec: the project layer is one slot with two candidates, the global-project file wins it, and `global.veneer` is appended independently.

- [ ] **Step 1: Write the failing tests**

Add to `VeneerConfigurationResolverTests`:

```csharp
        private const string CONFIG = @"C:\Users\joel\.veneer";
        private const string PROJECT = @"C:\models\ExampleProject.rsproj";
        private const string SIDECAR = @"C:\models\ExampleProject.rsproj.veneer";
        private const string GLOBAL_PROJECT = @"C:\Users\joel\.veneer\ExampleProject.rsproj.veneer";
        private const string GLOBAL = @"C:\Users\joel\.veneer\global.veneer";

        private static Func<string, bool> Existing(params string[] paths)
        {
            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        private static string Chain(ConfigCandidates candidates)
        {
            return string.Join(" | ", candidates.Paths);
        }

        [Test]
        public void Resolve_SidecarOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(SIDECAR));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_GlobalProjectFileReplacesTheSidecar()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT),
                        "the sidecar must not also appear -- the project layer is one slot");
            Assert.That(c.SupersededSidecar, Is.EqualTo(SIDECAR));
        }

        // SupersededSidecar drives a log line that says "superseding". With no
        // sidecar on disk nothing was displaced and the line would be a lie.
        [Test]
        public void Resolve_GlobalProjectFileAloneSupersedesNothing()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_NoFilesAtAll()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing());
            Assert.That(Chain(c), Is.EqualTo(""));
            Assert.That(c.ProjectLayer, Is.Null);
            Assert.That(c.GlobalLayer, Is.Null);
        }

        [Test]
        public void Resolve_GlobalIsAppendedAfterTheProjectLayer()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Resolve_GlobalIsAppendedAfterAGlobalProjectFileToo()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT + " | " + GLOBAL));
            Assert.That(c.SupersededSidecar, Is.EqualTo(SIDECAR));
        }

        [Test]
        public void Resolve_GlobalAlone()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.ProjectLayer, Is.Null);
        }

        // An unsaved project has no project layer but must still receive the
        // wildcard file -- that is the point of a wildcard file.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_WithoutAProjectFileStillFindsGlobal(string projectFile)
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, projectFile, Existing(SIDECAR, GLOBAL_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_WithoutAConfigDirectoryFallsBackToTheSidecar()
        {
            var c = VeneerConfigurationResolver.Resolve(null, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
        }

        // The old ConfigurationFilename used FullFilename.Replace(".rsproj",
        // ".rsproj.veneer"), which substitutes EVERY occurrence -- so this project
        // looked for C:\models.rsproj.veneer\a.rsproj.veneer, in a directory that
        // does not exist.
        [Test]
        public void Resolve_DoesNotRewriteADirectoryNamedLikeAProject()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"C:\models.rsproj\a.rsproj",
                Existing(@"C:\models.rsproj\a.rsproj.veneer"));
            Assert.That(Chain(c), Is.EqualTo(@"C:\models.rsproj\a.rsproj.veneer"));
        }

        [Test]
        public void Resolve_GlobalProjectFileIsNamedForTheProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"D:\elsewhere\ExampleProject.rsproj", Existing(GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT),
                        "matching is by file name, so a project of the same name anywhere matches");
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure**, `CS0117`/`CS0246` — `Resolve` and `ConfigCandidates` do not exist.

- [ ] **Step 3: Write the minimal implementation**

Add to `VeneerConfigurationResolver`:

```csharp
        /// <summary>
        /// The project layer is one slot with two candidates: a file named for the
        /// project in the configuration directory REPLACES the sidecar, which is
        /// the escape hatch for a repository whose committed sidecar you do not
        /// want. The global layer is additive and never replaces anything.
        /// </summary>
        public static ConfigCandidates Resolve(
            string configDir, string projectFullFilename, Func<string, bool> exists)
        {
            var result = new ConfigCandidates();

            if (!string.IsNullOrWhiteSpace(projectFullFilename))
            {
                // Path.GetFileName, not FullFilename.Replace(".rsproj", ...):
                // Replace substitutes every occurrence, so a project under a
                // directory named "models.rsproj" had its directory rewritten too.
                var projectFileName = Path.GetFileName(projectFullFilename);
                var directory = Path.GetDirectoryName(projectFullFilename) ?? string.Empty;
                var sidecar = Path.Combine(directory, projectFileName + CONFIG_EXTENSION);

                var globalProject = configDir == null
                    ? null
                    : Path.Combine(configDir, projectFileName + CONFIG_EXTENSION);

                if (globalProject != null && exists(globalProject))
                {
                    result.ProjectLayer = globalProject;
                    // Only when a sidecar is actually on disk: this drives a log
                    // line saying "superseding", which is a lie if nothing was.
                    if (exists(sidecar))
                        result.SupersededSidecar = sidecar;
                }
                else if (exists(sidecar))
                {
                    result.ProjectLayer = sidecar;
                }
            }

            if (configDir != null)
            {
                var global = Path.Combine(configDir, GLOBAL_FILENAME);
                if (exists(global))
                    result.GlobalLayer = global;
            }

            return result;
        }
```

And add the carrier, in the same file, after the resolver class:

```csharp
    /// <summary>
    /// The files that will be loaded, in resolution order, plus the sidecar a
    /// global project file displaced -- reported so the user can be told why the
    /// file next to their .rsproj is being ignored.
    /// </summary>
    public class ConfigCandidates
    {
        public string ProjectLayer;
        public string GlobalLayer;
        public string SupersededSidecar;

        public string[] Paths
        {
            get
            {
                var result = new List<string>();
                if (ProjectLayer != null) result.Add(ProjectLayer);
                if (GlobalLayer != null) result.Add(GlobalLayer);
                return result.ToArray();
            }
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 151, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs
git commit -m "feat: discover the project and global .veneer layers"
```

---

## Task 3: Make `VeneerOptions` fields nullable

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` (class `VeneerOptions`)
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (in `PopulateReportMenu`)
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs` (in `ApplyDefaultsFromEnvironmentAndConfig`)

**0 new cases. Running total: 151.**

**This task has no unit-test surface and that is expected.** Both consumers are WinForms/RiverSystem statics that cannot be constructed in a test. The verification is that the project compiles under `TreatWarningsAsErrors` and the existing 151 pass. The *behaviour* this enables is tested in Task 4, against `Merge`.

As plain `bool`, absent and `false` deserialize identically, so "project overrides global where it specifies a value" is inexpressible — a global `allowScripts: true` could never be turned off by a project file. The JSON format does not change.

- [ ] **Step 1: Make the fields nullable**

In `Addons/VeneerConfiguration.cs`, replace the whole `VeneerOptions` class:

```csharp
    /// <summary>
    /// Nullable so that "absent" and "false" are distinguishable. Merging layers
    /// needs that distinction, and so does leaving a value alone that the GUI or
    /// an environment variable already set.
    /// </summary>
    public class VeneerOptions
    {
        public bool? autoStart;
        public bool? allowScripts;
        public int? defaultPort;
    }
```

- [ ] **Step 2: Fix the `VeneerMenu` consumer**

In `VeneerMenu.PopulateReportMenu`, replace:

```csharp
                if (config?.options!= null)
                {
                    WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts;
                    WebServerStatusControl.DefaultPort = config.options.defaultPort > 0
                        ? config.options.defaultPort
                        : WebServerStatusControl.DefaultPort;
                }
```

with:

```csharp
                // Per field, not per block. The old code assigned allowScripts
                // unconditionally, so any .veneer file with an options block that
                // omitted the field reset it to false -- overwriting a value set
                // from VENEER_ALLOW_SCRIPTS or the GUI.
                if (config?.options != null)
                {
                    if (config.options.allowScripts != null)
                        WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts.Value;

                    if (config.options.defaultPort != null && config.options.defaultPort.Value > 0)
                        WebServerStatusControl.DefaultPort = config.options.defaultPort.Value;
                }
```

- [ ] **Step 3: Fix the `ProjectLoadListener` consumer**

In `ApplyDefaultsFromEnvironmentAndConfig`, replace:

```csharp
            if (config?.options != null && config.options.defaultPort > 0)
            {
                port = config.options.defaultPort;
            }
```

with:

```csharp
            if (config?.options != null && config.options.defaultPort.GetValueOrDefault() > 0)
            {
                port = config.options.defaultPort.Value;
            }
```

- [ ] **Step 4: Run the suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 151, Failed: 0`, and **no** compiler errors. If `CS0266`/`CS0019` appears, a third consumer of `VeneerOptions` exists that this plan missed — find it with `git grep "\.options\."` and apply the same treatment.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs FlowMatters.Source.Veneer/VeneerMenu.cs FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs
git commit -m "fix: distinguish unset from false in VeneerOptions

An options block that omitted allowScripts reset DefaultAllowScripts to
false, overwriting whatever VENEER_ALLOW_SCRIPTS or the GUI had set.
Nullable fields also make layer merging expressible."
```

---

## Task 4: Merge the layers

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**13 new cases. Running total: 164.**

- [ ] **Step 1: Write the failing tests**

Add to `VeneerConfigurationResolverTests`:

```csharp
        private static VeneerConfigurationLayer Layer(
            string path, string targetScenario, params VeneerAddon[] addons)
        {
            return new VeneerConfigurationLayer
            {
                Path = path,
                Configuration = new VeneerConfiguration
                {
                    targetScenario = targetScenario,
                    addons = addons
                }
            };
        }

        private static VeneerAddon Addon(string name, string scenario)
        {
            return new VeneerAddon { name = name, type = "exe", path = "x.bat", scenario = scenario };
        }

        private static VeneerConfigurationLayer OptionsLayer(
            string path, bool? allowScripts, int? defaultPort)
        {
            return new VeneerConfigurationLayer
            {
                Path = path,
                Configuration = new VeneerConfiguration
                {
                    options = new VeneerOptions { allowScripts = allowScripts, defaultPort = defaultPort }
                }
            };
        }

        private static string Names(ResolvedVeneerConfiguration resolved)
        {
            var names = new List<string>();
            foreach (var addon in resolved.addons) names.Add(addon.name);
            return string.Join(",", names);
        }

        [Test]
        public void Merge_ConcatenatesProjectAddonsBeforeGlobal()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("proj1", null), Addon("proj2", null)),
                Layer(GLOBAL, null, Addon("mine", null))
            });
            Assert.That(Names(resolved), Is.EqualTo("proj1,proj2,mine"));
        }

        [Test]
        public void Merge_RecordsSourceFilesInOrder()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null), Layer(GLOBAL, null)
            });
            Assert.That(string.Join(" | ", resolved.SourceFiles),
                        Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Merge_KeepsDuplicateAddonNames()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("Reports", null)),
                Layer(GLOBAL, null, Addon("Reports", null))
            });
            Assert.That(Names(resolved), Is.EqualTo("Reports,Reports"),
                        "two menu items is visible and diagnosable; silently dropping one is not");
        }

        // The whole reason targetScenario is pushed down instead of merged: a
        // wildcard file's default must not gate the project's addons.
        [Test]
        public void Merge_TargetScenarioStaysInsideItsOwnLayer()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("project", null)),
                Layer(GLOBAL, "Operations", Addon("global", null))
            });
            Assert.That(resolved.addons[0].scenario, Is.Null);
            Assert.That(resolved.addons[1].scenario, Is.EqualTo("Operations"));
        }

        [Test]
        public void Merge_PerAddonScenarioBeatsItsLayerTargetScenario()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, "Operations", Addon("calib", "Calibration"))
            });
            Assert.That(resolved.addons[0].scenario, Is.EqualTo("Calibration"));
        }

        [Test]
        public void Merge_ProjectOptionsBeatGlobalOptions()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, false, 9000),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(false),
                        "an explicit project false must beat a global true");
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9000));
        }

        [Test]
        public void Merge_GlobalOptionsFillFieldsTheProjectOmits()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, null, 9000),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(true));
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9000));
        }

        [Test]
        public void Merge_FieldSetByNeitherLayerStaysNull()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, null, null),
                OptionsLayer(GLOBAL, null, null)
            });
            Assert.That(resolved.options.allowScripts, Is.Null);
            Assert.That(resolved.options.defaultPort, Is.Null);
            Assert.That(resolved.options.autoStart, Is.Null);
        }

        [Test]
        public void Merge_LayerWithNoOptionsBlockDoesNotBlockFallback()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(true));
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9877));
        }

        // Load must be able to hand Merge whatever survived parsing, including
        // nothing, and get back something the consumers can dereference.
        [Test]
        public void Merge_OfNoLayersReturnsAnEmptyConfiguration()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>());
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.addons.Length, Is.EqualTo(0));
            Assert.That(resolved.options, Is.Not.Null);
            Assert.That(resolved.SourceFiles.Length, Is.EqualTo(0));
        }

        [Test]
        public void Merge_OfNullIsEmptyRatherThanAThrow()
        {
            var resolved = VeneerConfigurationResolver.Merge(null);
            Assert.That(resolved.addons.Length, Is.EqualTo(0));
            Assert.That(resolved.options, Is.Not.Null);
        }

        [Test]
        public void Merge_SkipsNullAddonEntries()
        {
            // A trailing comma in the JSON array produces a null element.
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("real", null), null)
            });
            Assert.That(Names(resolved), Is.EqualTo("real"));
        }

        [Test]
        public void Merge_LayerWithNoAddonsContributesOnlyItsSourceFile()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("only", null)),
                new VeneerConfigurationLayer { Path = GLOBAL, Configuration = new VeneerConfiguration() }
            });
            Assert.That(Names(resolved), Is.EqualTo("only"));
            Assert.That(resolved.SourceFiles.Length, Is.EqualTo(2));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure** — `Merge`, `VeneerConfigurationLayer` and `ResolvedVeneerConfiguration` do not exist.

- [ ] **Step 3: Write the minimal implementation**

Add to `VeneerConfigurationResolver`:

```csharp
        /// <summary>
        /// Layers arrive project-first, so "the first layer to specify a field
        /// wins" is exactly "the project layer beats the global one".
        ///
        /// Mutates each addon's `scenario` to push its layer's targetScenario
        /// down. Safe because Load deserializes a fresh object graph on every
        /// call; nothing else holds a reference to these addons.
        /// </summary>
        public static ResolvedVeneerConfiguration Merge(IList<VeneerConfigurationLayer> layers)
        {
            var result = new ResolvedVeneerConfiguration();
            if (layers == null) return result;

            var addons = new List<VeneerAddon>();
            var sources = new List<string>();
            var options = new VeneerOptions();

            foreach (var layer in layers)
            {
                if (layer == null || layer.Configuration == null) continue;
                sources.Add(layer.Path);

                if (layer.Configuration.addons != null)
                {
                    foreach (var addon in layer.Configuration.addons)
                    {
                        if (addon == null) continue;

                        // Push targetScenario down now, while we still know which
                        // file this addon came from. Concatenated into one list
                        // there is no longer any correct single value for it.
                        if (string.IsNullOrEmpty(addon.scenario))
                            addon.scenario = layer.Configuration.targetScenario;

                        addons.Add(addon);
                    }
                }

                var layerOptions = layer.Configuration.options;
                if (layerOptions != null)
                {
                    if (options.allowScripts == null) options.allowScripts = layerOptions.allowScripts;
                    if (options.defaultPort == null) options.defaultPort = layerOptions.defaultPort;
                    if (options.autoStart == null) options.autoStart = layerOptions.autoStart;
                }
            }

            result.addons = addons.ToArray();
            result.options = options;
            result.SourceFiles = sources.ToArray();
            return result;
        }
```

And add the two remaining carriers, after `ConfigCandidates`:

```csharp
    public class VeneerConfigurationLayer
    {
        public string Path;
        public VeneerConfiguration Configuration;
    }

    /// <summary>
    /// The effective configuration for a project. Has no targetScenario: it was
    /// pushed into each addon during the merge, and across layers there is no
    /// correct single value for one. addons and options are never null, so
    /// consumers can test individual option fields rather than the block.
    /// </summary>
    public class ResolvedVeneerConfiguration
    {
        public VeneerAddon[] addons = new VeneerAddon[0];
        public VeneerOptions options = new VeneerOptions();
        public string[] SourceFiles = new string[0];
        public string SupersededSidecar;
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 164, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs
git commit -m "feat: merge .veneer layers with targetScenario push-down"
```

---

## Task 5: Make the scenario filter pure and testable

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` (`EffectiveFilter`, `AddonAppliesTo`)
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (the two calls in `PopulateReportMenu`)
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**10 new cases (4 tests; two are multi-row `[TestCase]`s). Running total: 174.**

The sixth `AppliesTo_MatchesCaseInsensitively` row, `[TestCase("Operations", "", false)]`, was added during Task 5's code review: the refactor swapped a `currentScenario == null` guard for an `IsNullOrEmpty` one, so the empty case deserves pinning alongside the null case. Totals from here on include it.

**This task lands a deliberate one-commit regression.** After it, `AddonAppliesTo`
no longer consults `config.targetScenario`, but `Load` does not push it down until
Task 6 — so between the two commits a `.veneer` file with a top-level
`targetScenario` stops filtering. Nothing goes red, because no test covers the old
two-argument path. Do not "fix" it here; Task 6 closes it. Do not stop at Task 5.

Push-down leaves the `VeneerConfiguration config` parameter dead, so both helpers lose it. `AddonAppliesTo` still needs a `RiverSystemScenario`, which cannot be constructed in a unit test — so the comparison moves into a pure `AppliesTo(addon, string)` and `AddonAppliesTo` becomes a one-line wrapper. That is the same split `AddonContext` already uses to keep launch logic testable.

- [ ] **Step 1: Write the failing tests**

Add to `VeneerConfigurationResolverTests`:

```csharp
        [Test]
        public void EffectiveFilter_IsTheAddonScenarioAfterPushDown()
        {
            Assert.That(VeneerConfiguration.EffectiveFilter(Addon("a", "Operations")),
                        Is.EqualTo("Operations"));
            Assert.That(VeneerConfiguration.EffectiveFilter(Addon("a", null)), Is.Null);
        }

        [Test]
        public void EffectiveFilter_OfNullAddonIsNull()
        {
            Assert.That(VeneerConfiguration.EffectiveFilter(null), Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        public void AppliesTo_UnfilteredAddonIsAlwaysEnabled(string filter)
        {
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), "Anything"), Is.True);
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), null), Is.True);
        }

        [TestCase("Operations", "Operations", true)]
        [TestCase("Operations", "operations", true)]
        [TestCase("OPERATIONS", "Operations", true)]
        [TestCase("Operations", "Calibration", false)]
        [TestCase("Operations", null, false)]
        public void AppliesTo_MatchesCaseInsensitively(string filter, string active, bool expected)
        {
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), active),
                        Is.EqualTo(expected));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure** — `EffectiveFilter` takes two arguments and `AppliesTo` does not exist.

- [ ] **Step 3: Rewrite the helpers**

In `Addons/VeneerConfiguration.cs`, replace both `AddonAppliesTo` and `EffectiveFilter` with:

```csharp
        /// <summary>
        /// After VeneerConfigurationResolver.Merge, every addon carries its own
        /// effective filter -- its layer's targetScenario was pushed into it. So
        /// there is nothing left for a config argument to contribute.
        /// </summary>
        public static string EffectiveFilter(VeneerAddon addon)
        {
            return addon == null ? null : addon.scenario;
        }

        /// <summary>
        /// Pure counterpart of AddonAppliesTo, so the matching rule is testable
        /// without a loaded RiverSystemScenario.
        /// </summary>
        public static bool AppliesTo(VeneerAddon addon, string activeScenarioName)
        {
            var filter = EffectiveFilter(addon);

            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(activeScenarioName)) return false;

            return string.Equals(activeScenarioName, filter, StringComparison.OrdinalIgnoreCase);
        }

        public static bool AddonAppliesTo(VeneerAddon addon, RiverSystemScenario currentScenario)
        {
            return AppliesTo(addon, currentScenario?.Name);
        }
```

- [ ] **Step 4: Update the two call sites**

In `VeneerMenu.PopulateReportMenu`, replace:

```csharp
                        if (!VeneerConfiguration.AddonAppliesTo(addon, config, currentScenario))
                        {
                            var filter = VeneerConfiguration.EffectiveFilter(addon, config);
```

with:

```csharp
                        if (!VeneerConfiguration.AddonAppliesTo(addon, currentScenario))
                        {
                            var filter = VeneerConfiguration.EffectiveFilter(addon);
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 174, Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs FlowMatters.Source.Veneer/VeneerMenu.cs FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs
git commit -m "refactor: make the addon scenario filter pure

Push-down leaves the config argument dead. Splitting the comparison into
AppliesTo(addon, string) makes the matching rule testable without a
loaded RiverSystemScenario."
```

---

## Task 6: Rewrite `Load` as an I/O adapter

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` (`ConfigurationFilename`, `Load`)
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs` (`TryParse`)
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (`ClearMenu`)
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**5 new cases (3 tests, one a 3-row `[TestCase]`). Running total: 179.**

`Load` itself has no unit-test surface — it needs a `RiverSystemProject`. Its one piece of independent logic, per-layer parse isolation, is extracted into `TryParse` and tested there.

- [ ] **Step 1: Write the failing tests**

Add to `VeneerConfigurationResolverTests`:

```csharp
        [Test]
        public void TryParse_ReadsAValidConfiguration()
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse(
                "{\"targetScenario\":\"Operations\"}", out config, out error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(config.targetScenario, Is.EqualTo("Operations"));
        }

        // One bad file must not take out the others -- and Load runs on every
        // dropdown open, so a throw here is a dialog every time the menu opens.
        [Test]
        public void TryParse_ReportsMalformedJsonRatherThanThrowing()
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse("{ not json", out config, out error);

            Assert.That(ok, Is.False);
            Assert.That(config, Is.Null);
            Assert.That(string.IsNullOrEmpty(error), Is.False);
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("")]
        public void TryParse_TreatsAnEmptyDocumentAsAnEmptyConfiguration(string json)
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse(json, out config, out error);

            Assert.That(ok, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.addons, Is.Null);
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure** — `TryParse` does not exist.

- [ ] **Step 3: Add `TryParse`**

Add to `VeneerConfigurationResolver`:

```csharp
        /// <summary>
        /// Never throws. Each layer is parsed in isolation so that one malformed
        /// file leaves the others working -- and because Load runs on every
        /// dropdown open, where an exception would surface as a dialog.
        /// </summary>
        public static bool TryParse(string json, out VeneerConfiguration config, out string error)
        {
            error = null;

            // Guarded rather than left to Newtonsoft: an empty or whitespace file
            // is a user saying "nothing here", not an error, and this does not
            // depend on how the parser happens to treat an empty document.
            if (string.IsNullOrWhiteSpace(json))
            {
                config = new VeneerConfiguration();
                return true;
            }

            try
            {
                config = Newtonsoft.Json.JsonConvert.DeserializeObject<VeneerConfiguration>(json);

                // The literal "null" is valid JSON and deserializes to null.
                if (config == null)
                    config = new VeneerConfiguration();

                return true;
            }
            catch (Exception ex)
            {
                config = null;
                error = ex.Message;
                return false;
            }
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 179, Failed: 0`

- [ ] **Step 5: Rewrite `ConfigurationFilename` and `Load`**

In `Addons/VeneerConfiguration.cs`, replace the whole block from `public static string ConfigurationFilename(RiverSystemScenario scenario)` through the end of `Load(RiverSystemProject project)` with:

```csharp
        /// <summary>
        /// The configuration directory in effect, for %VENEER_CONFIG_DIR% and for
        /// discovery. Null when there is neither VENEER_CONFIG_DIR nor a profile.
        /// </summary>
        public static string ConfigDirectory()
        {
            return VeneerConfigurationResolver.ConfigDirectory(
                Environment.GetEnvironmentVariable,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private static ConfigCandidates Candidates(RiverSystemProject project)
        {
            return VeneerConfigurationResolver.Resolve(
                ConfigDirectory(),
                project == null ? null : project.FullFilename,
                File.Exists);
        }

        public static string ConfigurationFilename(RiverSystemScenario scenario)
        {
            return ConfigurationFilename(scenario?.RiverSystemProject);
        }

        /// <summary>
        /// The effective project-layer file: the global override if there is one,
        /// otherwise the sidecar, otherwise null. Does not report global.veneer,
        /// which is an additional layer rather than "the" configuration file.
        /// </summary>
        public static string ConfigurationFilename(RiverSystemProject project)
        {
            return Candidates(project).ProjectLayer;
        }

        public static ResolvedVeneerConfiguration Load(RiverSystemScenario scenario)
        {
            return Load(scenario?.RiverSystemProject);
        }

        /// <summary>
        /// Never returns null. With no files it returns an empty resolved
        /// configuration, so consumers can dereference addons and options without
        /// a null check.
        /// </summary>
        public static ResolvedVeneerConfiguration Load(RiverSystemProject project)
        {
            var candidates = Candidates(project);
            var layers = new List<VeneerConfigurationLayer>();

            foreach (var path in candidates.Paths)
            {
                string json;
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    LogOnce("Veneer could not read '" + path + "': " + ex.Message);
                    continue;
                }

                VeneerConfiguration parsed;
                string error;
                if (!VeneerConfigurationResolver.TryParse(json, out parsed, out error))
                {
                    LogOnce("Veneer could not parse '" + path + "': " + error);
                    continue;
                }

                layers.Add(new VeneerConfigurationLayer { Path = path, Configuration = parsed });
            }

            var resolved = VeneerConfigurationResolver.Merge(layers);
            resolved.SupersededSidecar = candidates.SupersededSidecar;
            return resolved;
        }

        private static readonly HashSet<string> _loggedProblems = new HashSet<string>();

        /// <summary>
        /// Load runs on every menu open, so an unreadable file would otherwise log
        /// on every drop-down. Mirrors VeneerMenu.LogOnce, and is cleared from the
        /// same place, so a project change re-reports.
        /// </summary>
        private static void LogOnce(string message)
        {
            lock (_loggedProblems)
            {
                if (!_loggedProblems.Add(message)) return;
            }

            TIME.Management.Log.WriteError(typeof(VeneerConfiguration), message);
        }

        public static void ClearLoggedProblems()
        {
            lock (_loggedProblems)
            {
                _loggedProblems.Clear();
            }
        }
```

Add `using System.Collections.Generic;` and `using System.IO;` to the file if the compiler asks; both are likely already present.

- [ ] **Step 6: Clear the new log cache alongside the existing one**

In `VeneerMenu.ClearMenu`, find the call to `ClearLoggedProblems()` — or, if `ClearMenu` does not call it directly, find `VeneerMenu.ClearLoggedProblems` and its caller — and add next to it:

```csharp
            VeneerConfiguration.ClearLoggedProblems();
```

- [ ] **Step 7: Run the suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 179, Failed: 0`, no compiler errors. The `var config = VeneerConfiguration.Load(...)` call sites need no edit — they already use `var`.

- [ ] **Step 8: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/ FlowMatters.Source.Veneer/VeneerMenu.cs FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs
git commit -m "feat: load .veneer configuration from all discovered layers

Load becomes an I/O adapter over the resolver. Each layer parses in
isolation, so a malformed file no longer throws out of a method that runs
on every dropdown open."
```

---

## Task 7: Inject `%VENEER_CONFIG_DIR%`

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonContext.cs`
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs`
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (`BuildAddonContext`)
- Modify: `FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs`

**2 new cases. Running total: 181.**

- [ ] **Step 1: Write the failing tests**

In `Tests/AddonEnvironmentTests.cs`, add `ConfigDirectory` to the existing `Ctx()` helper:

```csharp
        private static AddonContext Ctx()
        {
            return new AddonContext
            {
                ProjectDirectory = @"C:\models\catchment",
                ProjectFile = @"C:\models\catchment\m.rsproj",
                ConfigDirectory = @"C:\Users\joel\.veneer",
                Port = 9876
            };
        }
```

Extend the existing `BuildEffective_InjectsVeneerVariables` with one more assertion:

```csharp
            Assert.That(env["VENEER_CONFIG_DIR"], Is.EqualTo(@"C:\Users\joel\.veneer"));
```

and add two new tests:

```csharp
        [Test]
        public void BuildEffective_NullConfigDirectoryBecomesEmptyString()
        {
            var context = Ctx();
            context.ConfigDirectory = null;
            var env = AddonEnvironment.BuildEffective(context, null);
            Assert.That(env["VENEER_CONFIG_DIR"], Is.EqualTo(""));
        }

        [Test]
        public void Expand_ResolvesConfigDirInAPath()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(AddonEnvironment.Expand(@"%VENEER_CONFIG_DIR%\tools\calibrate.bat", env),
                        Is.EqualTo(@"C:\Users\joel\.veneer\tools\calibrate.bat"));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~AddonEnvironmentTests"`
Expected: **build failure** — `AddonContext` has no `ConfigDirectory`.

- [ ] **Step 3: Add the field and the injection**

In `DomainActions/AddonContext.cs`, add to `AddonContext`:

```csharp
        public string ConfigDirectory { get; set; }
```

In `DomainActions/AddonEnvironment.BuildEffective`, add below the three existing injections:

```csharp
            env["VENEER_CONFIG_DIR"] = context.ConfigDirectory ?? string.Empty;
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 181, Failed: 0`

- [ ] **Step 5: Populate it from the menu**

In `VeneerMenu.BuildAddonContext`, add to the object initialiser:

```csharp
                ConfigDirectory = VeneerConfiguration.ConfigDirectory(),
```

- [ ] **Step 6: Re-run the suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 181, Failed: 0`

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/ FlowMatters.Source.Veneer/VeneerMenu.cs FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs
git commit -m "feat: inject %VENEER_CONFIG_DIR% into addon launches"
```

---

## Task 8: Report the configuration chain

**Files:**
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs`

**0 new cases. Running total: 181.**

No unit-test surface: `ProjectLoadListener` needs `MainForm.Instance`. Verified manually in Task 10.

- [ ] **Step 1: Add `LogConfigurationChain`**

In `AutoStart/ProjectLoadListener.cs`, add below `ApplyDefaultsFromEnvironmentAndConfig`:

```csharp
        private static string _lastLoggedChain;

        /// <summary>
        /// With two files feeding one menu, "where did this item come from?" and
        /// "why is my sidecar being ignored?" are questions a user will ask.
        ///
        /// Deduplicated on the chain rather than fired once per load, because the
        /// Rebind transition also covers a scenario change within one project,
        /// where the resolved files are identical and a second line would be noise.
        /// </summary>
        private void LogConfigurationChain(ResolvedVeneerConfiguration config)
        {
            var chain = config.SourceFiles.Length == 0
                ? "none"
                : String.Join(", ", config.SourceFiles);

            if (config.SupersededSidecar != null)
                chain += " (superseding " + config.SupersededSidecar + ")";

            if (chain == _lastLoggedChain) return;
            _lastLoggedChain = chain;

            TIME.Management.Log.WriteInfo(this, "Veneer configuration: " + chain);
        }
```

- [ ] **Step 2: Call it on first load**

At the end of `ApplyDefaultsFromEnvironmentAndConfig`, after `WebServerStatusControl.DefaultPort = port;`, add:

```csharp
            LogConfigurationChain(config);
```

- [ ] **Step 3: Call it on a project switch**

`ApplyScenarioChange` handles the `Rebind` / `Cleared` transitions and never reaches `ScenarioLoaded`, so opening a different project mid-session would otherwise log nothing — which is exactly when the question gets asked. Add as the **first** statement of `ApplyScenarioChange`:

```csharp
            // Diagnostics only. Option defaults stay a load-time concern, so this
            // deliberately does not re-apply them.
            LogConfigurationChain(VeneerConfiguration.Load(newScenario));
```

- [ ] **Step 4: Build**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 181, Failed: 0`, no compiler errors.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs
git commit -m "feat: log which .veneer files a project resolved to"
```

---

## Task 9: Documentation

**Files:**
- Modify: `docs/veneer-file-format.md`
- Modify: `Samples/addons/README.md`

**0 new cases. Running total: 181.**

- [ ] **Step 1: Rewrite "Filename and discovery"**

Replace the section (from the heading down to, but not including, `## Top-level structure`) with:

````markdown
## Filename and discovery

Veneer resolves configuration from **two layers**, merged into one effective
configuration. Every file uses the format described below.

| # | Layer | Where |
|---|-------|-------|
| 1 | Project | `<configDir>/<project>.rsproj.veneer` **if it exists**, otherwise the sidecar `<projectDir>/<project>.rsproj.veneer` |
| 2 | Global  | `<configDir>/global.veneer` |

`<configDir>` is the `VENEER_CONFIG_DIR` environment variable if set, otherwise
`%USERPROFILE%\.veneer`. Veneer never creates it; a missing directory simply
contributes no layers.

```
C:\models\ExampleProject.rsproj              the project
C:\models\ExampleProject.rsproj.veneer       sidecar         (layer 1, candidate 2)
%USERPROFILE%\.veneer\ExampleProject.rsproj.veneer           (layer 1, candidate 1)
%USERPROFILE%\.veneer\global.veneer                          (layer 2)
```

The match is exact: a `.veneer` file's name is the `.rsproj` filename with
`.veneer` appended. If no file exists anywhere, Veneer behaves with built-in
defaults — no addons, default port, scripts disabled, single `Reporting` menu.

Files are loaded from disk every time a relevant menu opens, so edits take effect
on the next dropdown without restarting Source. A malformed file is logged and
skipped; the other layer still applies.

## Global configuration

The project layer is **one slot with two candidates**. A file named for the
project in the configuration directory *replaces* the sidecar entirely — the
escape hatch for a model whose committed `.veneer` file you do not want. The
global layer is **additive**: it never replaces anything and is never replaced.

This is what lets a model be shared over git while each modeller keeps their own
tools: commit the `.rsproj`, and put your own addons in
`%USERPROFILE%\.veneer\`, where a fresh clone cannot disturb them.

Matching is by **file name only**. Two projects with the same file name in
different directories share the same `<configDir>/<name>.rsproj.veneer`.

### How the layers combine

| Field | Rule |
|---|---|
| `addons` | Concatenated, **project layer first**, global appended. No de-duplication — two addons with the same name produce two menu items. |
| `targetScenario` | Applies only to the addons **in its own file**. A `targetScenario` in `global.veneer` never gates the project's addons. |
| `options` | Merged field by field. The project layer wins where it sets a value; the global layer fills the rest; a field neither sets keeps Veneer's own default. |

Because addons are concatenated project-first, a shared model's menu-bar layout
is unaffected by whatever you have in `~/.veneer` — your personal entries appear
after the project's own.

**Relative paths still resolve against the project directory**, in every layer.
That is what makes `<configDir>/<name>.rsproj.veneer` a true stand-in for the
sidecar. For a tool that lives with your configuration rather than with the
model, use `%VENEER_CONFIG_DIR%` (see **Injected variables**).

**In a project that has never been saved**, only the global layer applies, and
only `type: "url"` addons can actually be launched — `exe` and `script` addons
report `no project directory is available`, because Veneer refuses to resolve a
program name with no directory to resolve it against.
````

- [ ] **Step 2: Add the variable**

In **Injected variables**, replace the first line with:

```markdown
`%VENEER_PORT%`, `%VENEER_PROJECT_DIR%`, `%VENEER_PROJECT_FILE%` and
`%VENEER_CONFIG_DIR%` expand inside `path`, `args`, `workingDirectory`, `url`,
`env` values and script lines.

`VENEER_CONFIG_DIR` is the resolved configuration directory — useful for a global
addon whose tool lives beside the configuration rather than in the model
directory: `"path": "%VENEER_CONFIG_DIR%/tools/calibrate.bat"`.
```

- [ ] **Step 3: Correct the Options table**

In the **Options** section, replace the paragraph introducing the table and the
`allowScripts` / `defaultPort` rows' Default column text so the table reads:

```markdown
These are **never** scenario-gated — they take effect whenever the project is
loaded.

Each field is independent. A field a layer omits falls through to the global
layer, and then to Veneer's default — omitting `allowScripts` no longer forces it
to `false`.

| Field          | Type | Default | Purpose |
|----------------|------|---------|---------|
| `allowScripts` | bool | unset → `false` | Pre-checks the "Allow scripts" toggle on the Veneer control, enabling Python script execution endpoints. Override at runtime via the GUI or the `VENEER_ALLOW_SCRIPTS` environment variable. |
| `defaultPort`  | int  | unset → `9876`  | Pre-fills the port number on the Veneer control. Values `≤ 0` are ignored. Override at runtime via the GUI or the `VENEER_PORT` environment variable. |
| `autoStart`    | bool | unset → `false` | **Currently defined in the schema but not consumed by Veneer.** To start Veneer automatically on project load, use the `VENEER_START_ON_LOAD` environment variable. |
```

- [ ] **Step 4: Add a worked example to the samples README**

Append to `Samples/addons/README.md`:

````markdown
## A personal `global.veneer` alongside a shared model

Put this at `%USERPROFILE%\.veneer\global.veneer` and it applies to every project
you open, without touching any repository:

```json
{
  "addons": [
    { "name": "My calibration", "type": "exe",
      "path": "%VENEER_CONFIG_DIR%/tools/calibrate.bat", "menu": "My Tools" },
    { "name": "Team wiki", "type": "url",
      "url": "https://wiki.example.org/models", "menu": "My Tools" }
  ],
  "options": { "defaultPort": 9877 }
}
```

`%VENEER_CONFIG_DIR%` is used for `path` because relative paths resolve against
the *project* directory, which is not where a personal tool lives.

To override a model's committed sidecar rather than add to it, name the file for
the project instead — `%USERPROFILE%\.veneer\ExampleProject.rsproj.veneer`. The
sidecar next to the `.rsproj` is then ignored entirely, and Source's log says so
on project load.
````

- [ ] **Step 5: Verify the docs against the code**

Read `VeneerOptions` and `VeneerConfigurationResolver` and confirm every field
name and default in the tables above matches. Check both directions: every
documented field exists, and every field in the code is documented.

- [ ] **Step 6: Commit**

```bash
git add docs/veneer-file-format.md Samples/addons/README.md
git commit -m "docs: document the global .veneer configuration directory"
```

---

## Task 10: Manual verification in Source

**Files:** none

**Nothing in Tasks 3, 6, 7, 8 has unit coverage** — the consumers are all WinForms and RiverSystem statics. This task is where the feature is actually observed working. Do not mark it done on reasoning.

- [ ] **Step 1: Build the plugin**

Run: `build.bat`
Expected: output under `Compiled\<version>\`. Install into Source's Plugin Manager.

- [ ] **Step 2: Global wildcard applies**

Create `%USERPROFILE%\.veneer\global.veneer` with one `type: "url"` addon under
menu `My Tools`. Open **any** project.
Expected: a `My Tools` menu appears with the item; clicking opens the link.

- [ ] **Step 3: Ordering is project-first**

Add a sidecar with an addon under menu `Models` to that project, then **reopen the
project** — top-level menu-bar entries are computed once, in
`InitialiseRequiredMenus`, so a new menu does not appear mid-session.
Expected: `Models` appears **left of** `My Tools` in the menu bar.

- [ ] **Step 4: The global project file replaces the sidecar**

Copy the sidecar to `%USERPROFILE%\.veneer\<name>.rsproj.veneer` and change the
addon's name. Reopen the project.
Expected: only the renamed item appears; the sidecar's is gone. Source's log
contains `Veneer configuration: …(superseding C:\…\<name>.rsproj.veneer)`.

- [ ] **Step 5: The diagnostic fires on a project switch**

With that project open, open a **different** project.
Expected: a second `Veneer configuration:` line naming the new project's files.
Then switch *scenarios* within one project — expected: **no** new line.

- [ ] **Step 6: Options fall back**

Put `"options": {"defaultPort": 9877}` in `global.veneer` and no `options` block
in the project layer. Reopen.
Expected: the Veneer panel's port field pre-fills `9877`.

Now `allowScripts`. **Do not try to reproduce the old bug** — it has no
in-session GUI-observable manifestation, so any such attempt passes identically on
a fixed and an unfixed build, which is worse than not checking at all. Three
things prevent it:

- `WebServerStatusControl` reads `DefaultAllowScripts` only in its **constructor**
  (`.xaml.cs:65`), and nothing writes back from the checkbox.
- The panel is never reconstructed once it exists. `Launch()` returns early
  whenever `WebServerStatusPanel.ActivePanel != null` (`.xaml.cs:338-343`),
  `ApplyScenarioChange` reuses `ActiveInstance` and only reassigns `.Scenario`,
  and the panel sets `HideOnClose = true` without ever clearing `_activePanel`.
- Restarting Source does not expose it either: the static resets to `false` and
  `StartVeneer` immediately sets it back.

Verify **the new rule** instead. This needs no environment variables, but it does
need a fresh session, because it depends on the panel not existing yet:

1. **Restart Source.** The port check above opened the panel to read its port
   field, and the panel is never reconstructed within a session.
2. Do **not** set `VENEER_START_ON_LOAD`, so no panel is constructed at load.
3. `global.veneer`: `"options": { "allowScripts": true }`.
4. Project layer: `"options": { "defaultPort": 9877 }` — sets a field, **omits**
   `allowScripts`.
5. Open the project, then open an addon dropdown. This runs `PopulateReportMenu`,
   which is what writes the statics.
6. *Now* open the panel for the first time — Tools > Veneer Server, or click an
   `exe` addon, which force-opens it.

Expected: "Allow scripts" comes up **checked** and the port pre-fills `9877` — the
project layer's options block did not clobber the global's `allowScripts`.

Then the contrast case: change the project layer to `"allowScripts": false`,
restart Source and repeat. A restart is the only way back to the first-open state —
`_activePanel`, `_activeInstance` and `DefaultAllowScripts` are process statics
that nothing clears.
Expected: the panel comes up **unchecked**.

The contrast case is only meaningful paired with the first. `DefaultAllowScripts`
initialises to `false`, so "unchecked" is also what you would see if nothing were
assigned at all — what the pair catches is **inverted precedence**, where a global
`true` beating a project `false` would show checked.

Together these exercise Task 3's per-field assignment and Task 4's
`Merge_GlobalOptionsFillFieldsTheProjectOmits` / `Merge_ProjectOptionsBeatGlobalOptions`
against the real GUI.

- [ ] **Step 7: `%VENEER_CONFIG_DIR%` expands**

Add an `exe` addon with `"path": "%VENEER_CONFIG_DIR%/tools/echo.bat"` and create
that batch file.
Expected: it launches and its output appears in the Veneer panel.

- [ ] **Step 8: A malformed file is survivable**

Put `{ not json` in `global.veneer` with a valid sidecar in place.
Expected: the sidecar's addons still appear; Source's log names the bad file
**once**, not once per dropdown.

- [ ] **Step 9: Record the outcome**

Add an execution-status table to the top of this plan recording what was
observed, in the style of `docs/superpowers/plans/2026-07-31-veneer-url-addons.md`.
State plainly any step that could not be run.

---

## Task 11: Port to `legacy_ci`

**Files:** the same files, on the `legacy_ci` branch, plus `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`

**0 new cases on `master`.**

`Addons/VeneerConfiguration.cs`, `DomainActions/AddonContext.cs` and
`DomainActions/AddonEnvironment.cs` are byte-identical between the branches at
`f62aa28`, so they copy. `VeneerMenu.cs` and `ProjectLoadListener.cs` differ —
including `BuildAddonContext` itself, where `legacy_ci` uses `Control` directly
and `master` uses `EffectiveControl` — so those are hand-edited.

- [ ] **Step 1: Branch**

```bash
git checkout legacy_ci
git checkout -b port/global-veneer-config
```

- [ ] **Step 2: Confirm the assumption still holds**

Step 3 copies five files wholesale from `master`, which is only safe if they were
identical **before** this feature. Compare against the pre-feature commit, not
against `master` — by now `master` has changed all five, so diffing `master` would
report every one as differing and send you hand-porting the lot.

```bash
for f in FlowMatters.Source.Veneer/DomainActions/AddonContext.cs \
         FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs \
         FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs \
         docs/veneer-file-format.md \
         Samples/addons/README.md; do
  echo "== $f"; git diff --stat f62aa28:$f legacy_ci:$f
done
```
Expected: empty output under every heading. (`git merge-base master legacy_ci`
works equally well as the baseline ref if `f62aa28` has been rewritten.)
For any that differ, hand-apply that file's changes instead of copying it.

- [ ] **Step 3: Copy the files that copy cleanly**

```bash
git checkout master -- \
  FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs \
  FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs \
  FlowMatters.Source.Veneer/DomainActions/AddonContext.cs \
  FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs \
  FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs \
  FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs \
  docs/veneer-file-format.md \
  Samples/addons/README.md
```

- [ ] **Step 4: Register the new file in the non-SDK csproj**

`legacy_ci` uses a legacy `.csproj`, so new files need an explicit entry. Next to
the existing `<Compile Include="Addons\VeneerConfiguration.cs" />`, add:

```xml
    <Compile Include="Addons\VeneerConfigurationResolver.cs" />
```

and next to the existing `Tests\` entries:

```xml
    <Compile Include="Tests\VeneerConfigurationResolverTests.cs" />
```

- [ ] **Step 5: Hand-apply the `VeneerMenu.cs` edits**

Four edits, all from earlier tasks — Task 3 Step 2, Task 5 Step 4, Task 6 Step 6,
Task 7 Step 5. Apply them to `legacy_ci`'s copy in place; do **not** copy the
whole file.

- [ ] **Step 6: Hand-apply the `ProjectLoadListener.cs` edits**

Task 3 Step 3 and all of Task 8.

- [ ] **Step 7: Build**

Run the `legacy_ci` build per `branch-porting-guide.md`.

**Expected outcome unknown:** at the time the URL-addons feature was ported,
`legacy_ci` did not build in this environment — pristine HEAD failed with an
`MC1000` WPF markup-compiler error. **First build pristine `legacy_ci`** and
record the result. If it fails identically before and after the port, that is
pre-existing and unrelated; say so explicitly rather than claiming the port
builds.

- [ ] **Step 8: Verify C# 7.3 compatibility regardless**

If the full build cannot run, compile the new file standalone:

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj /p:LangVersion=7.3
```

Full MSBuild, not `dotnet build`: `legacy_ci`'s project is non-SDK .NET Framework
4.8. Whatever else fails, the pass/fail criterion here is narrow — **no
language-version errors** (`CS8107`, `CS8370`, `CS8652`). This is a weaker check
than a working build; report it as such.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "port: global .veneer configuration from master"
```

- [ ] **Step 10: Record honestly**

Update the execution-status table with exactly what was and was not verified on
`legacy_ci`. If the 46 tests were not run there, say so.

---

## Notes for the implementer

**The one subtle thing in this plan** is that `Merge` mutates `addon.scenario`.
That is safe only because `Load` deserializes a fresh object graph on every call
and nothing else holds a reference. If you find yourself caching a parsed
`VeneerConfiguration` across loads, push-down becomes a bug — the second merge
would see the first merge's stamped value as if the author had written it.

**Do not add a de-duplication pass** over merged addons, however tempting two
identically named menu items look. The spec rules it out: a dropped item is
invisible, a duplicated one is diagnosable.

**Do not resolve relative addon paths against the config directory.** They resolve
against the project directory in every layer, which is what makes a global project
file a true stand-in for the sidecar.
