# Merged `.veneer` Layers and File-Level `env` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the three `.veneer` locations additive rather than one replacing another, and add a file-level `env` block so a committed sidecar can name addons while a personal home file supplies the machine paths they use.

**Architecture:** `VeneerConfigurationResolver.Resolve` stops choosing between candidates and reports three independent layers, most specific first. That single ordering continues to drive both `Merge`'s first-wins precedence and addon concatenation, so `Merge`'s existing scalar logic is untouched — it only gains a dictionary. The merged `env` travels on `AddonContext` and enters `AddonEnvironment.BuildEffective` as a new layer between Veneer's injected variables and the addon's own `env`, reusing the snapshot pattern already there.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`) on the feature branch; .NET Framework 4.8 / C# 7.3 on `legacy_ci`. Newtonsoft.Json. NUnit.

**Spec:** [`docs/superpowers/specs/2026-08-21-veneer-config-env-merge-design.md`](../specs/2026-08-21-veneer-config-env-merge-design.md)

---

## Prerequisites

### Build and test command

Run from the worktree root, **always with an absolute path** — a previous session's shell silently reset its working directory and produced a false-positive result from a different repository:

```
dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

**Baseline: `Passed: 181`, 0 failed.** Confirm this before starting. Counts below
treat each `[TestCase]` row as one case, which is how the runner counts them.

### Constraints that fail the build if ignored

- **C# 7.3 only.** Every file here is hand-ported to .NET Framework 4.8 later. No target-typed `new`, switch expressions, `??=`, nullable reference types, ranges. `?.`, `??`, `?:` and `$"..."` are fine.
- **NUnit portability.** `legacy_ci` builds against NUnit from 2.6.4 up. Use `Assert.That(...)` with `Is.*` only. No `Does.Contain`, `StringAssert`, `Assert.Multiple`, `CollectionAssert`.
- **`TreatWarningsAsErrors` is true in Debug.** Any new warning fails the build. `MSB3277` warnings are pre-existing noise — ignore them.
- **Do not bump `PROTOCOL_VERSION`** in `VeneerStatus.cs` and **do not edit `docs/api/`**. No REST surface changes.

### Two things a planner would otherwise get wrong

**Script lines never go through `Expand`.** `AddonLauncher.LaunchScript` writes
`addon.script` to stdin raw; `AddonScript.Generate` does not expand it. Script
bodies see `%TOOLS_ROOT%` only because `ApplyEnvironment` sets the merged
dictionary as the child process's real environment and `cmd.exe` substitutes at
run time. Do not write a test asserting `AddonEnvironment.Expand` runs over script
bodies — it does not.

**Dictionaries must be `StringComparer.OrdinalIgnoreCase`.** Windows environment
variables are case-insensitive and `BuildEffective` already builds its dictionary
that way. A case-sensitive merge would let `Path` and `PATH` coexist.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `Addons/VeneerConfigurationResolver.cs` | Pure resolution and merging | `Resolve` reshaped to three layers; `ConfigCandidates` re-slotted; `Merge` gains `env`; `SupersededSidecar` removed |
| `Addons/VeneerConfiguration.cs` | I/O adapter and DTOs | `env` field added; `ConfigurationFilename` redefined; `SupersededSidecar` assignment removed |
| `DomainActions/AddonContext.cs` | Launch inputs, primitives only | Gains `Env` |
| `DomainActions/AddonEnvironment.cs` | Environment assembly and expansion | `BuildEffective` gains a layer |
| `VeneerMenu.cs` | Menu integration | `BuildAddonContext` populates `Env` |
| `AutoStart/ProjectLoadListener.cs` | Load-time defaults and diagnostics | Superseding clause removed |
| `Tests/VeneerConfigurationResolverTests.cs` | Resolver tests | Resolve block rewritten; `env` merge tests added |
| `Tests/AddonEnvironmentTests.cs` | Environment tests | File-level `env` tests added |

---

## Task 0: Confirm the starting state

- [ ] **Step 1: Run the suite**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 181, Failed: 0`.

If the count differs, stop and reconcile — every later step quotes a running total.

---

## Task 1: Three additive layers

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs`
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**13 cases rewritten, 13 added back. Running total: 181** (unchanged).

This task is deliberately one unit rather than several. Removing `SupersededSidecar` from `ConfigCandidates` breaks `Load`, which breaks `LogConfigurationChain`; splitting them would leave the tree not compiling between commits.

- [ ] **Step 1: Replace the Resolve tests**

In `Tests/VeneerConfigurationResolverTests.cs`, delete every test from
`Resolve_SidecarOnly` through `Resolve_GlobalProjectFileIsNamedForTheProjectFileOnly`
inclusive — the whole block between the `Chain` helper and the `Layer` helper.
Keep the `CONFIG`/`PROJECT`/`SIDECAR`/`GLOBAL` constants, `Existing` and `Chain`.

Rename the `GLOBAL_PROJECT` constant to `HOME_PROJECT` (same value) and replace the
deleted block with:

```csharp
        [Test]
        public void Resolve_FindsAllThreeLayersMostSpecificFirst()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, HOME_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT + " | " + SIDECAR + " | " + GLOBAL));
        }

        // The whole point of the change: a home file that supplies only env must
        // leave the sidecar's addons in place rather than replacing them.
        [Test]
        public void Resolve_HomeProjectFileAndSidecarBothLoad()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT + " | " + SIDECAR));
        }

        [Test]
        public void Resolve_SidecarOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(SIDECAR));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
            Assert.That(c.HomeProjectLayer, Is.Null);
        }

        [Test]
        public void Resolve_HomeProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT));
            Assert.That(c.SidecarLayer, Is.Null);
        }

        [Test]
        public void Resolve_GlobalOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.HomeProjectLayer, Is.Null);
            Assert.That(c.SidecarLayer, Is.Null);
        }

        [Test]
        public void Resolve_SidecarAndGlobalWithoutHomeProject()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Resolve_NoFilesAtAll()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing());
            Assert.That(Chain(c), Is.EqualTo(""));
            Assert.That(c.HomeProjectLayer, Is.Null);
            Assert.That(c.SidecarLayer, Is.Null);
            Assert.That(c.GlobalLayer, Is.Null);
        }

        // An unsaved project has no project-specific layer but must still receive
        // the wildcard file -- that is the point of a wildcard file.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_WithoutAProjectFileStillFindsGlobal(string projectFile)
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, projectFile, Existing(SIDECAR, HOME_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
        }

        [Test]
        public void Resolve_WithoutAConfigDirectoryFindsOnlyTheSidecar()
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
        public void Resolve_HomeProjectFileIsNamedForTheProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"D:\elsewhere\ExampleProject.rsproj", Existing(HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT),
                        "matching is by file name, so a project of the same name anywhere matches");
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`

Expected: **build failure** — `ConfigCandidates` has no `HomeProjectLayer` or
`SidecarLayer`. Report the error codes you saw.

- [ ] **Step 3: Re-slot `ConfigCandidates`**

Replace the whole `ConfigCandidates` class:

```csharp
    /// <summary>
    /// The files that will be loaded, most specific first. All three are
    /// additive: none replaces another, so a home file supplying only env leaves
    /// the sidecar's addons in place.
    /// </summary>
    public class ConfigCandidates
    {
        public string HomeProjectLayer;
        public string SidecarLayer;
        public string GlobalLayer;

        public string[] Paths
        {
            get
            {
                var result = new List<string>();
                if (HomeProjectLayer != null) result.Add(HomeProjectLayer);
                if (SidecarLayer != null) result.Add(SidecarLayer);
                if (GlobalLayer != null) result.Add(GlobalLayer);
                return result.ToArray();
            }
        }
    }
```

- [ ] **Step 4: Rewrite `Resolve`**

Replace the whole method, doc comment included:

```csharp
        /// <summary>
        /// Three additive layers, most specific first: a file named for the
        /// project in the configuration directory, the sidecar beside the
        /// .rsproj, then the global wildcard.
        ///
        /// One ordering serves twice -- it decides both which layer wins a
        /// contested field and the order addons are concatenated into menus. That
        /// is why a home project file's addons appear above the sidecar's.
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

                if (configDir != null)
                {
                    var homeProject = Path.Combine(configDir, projectFileName + CONFIG_EXTENSION);
                    if (exists(homeProject))
                        result.HomeProjectLayer = homeProject;
                }

                var sidecar = Path.Combine(directory, projectFileName + CONFIG_EXTENSION);
                if (exists(sidecar))
                    result.SidecarLayer = sidecar;
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

- [ ] **Step 5: Drop `SupersededSidecar` from the resolved configuration**

In `ResolvedVeneerConfiguration`, delete the line:

```csharp
        public string SupersededSidecar;
```

- [ ] **Step 6: Drop it from `Load` and redefine `ConfigurationFilename`**

In `Addons/VeneerConfiguration.cs`, in `Load(RiverSystemProject project)`, delete:

```csharp
            resolved.SupersededSidecar = candidates.SupersededSidecar;
```

so the tail of the method reads:

```csharp
            return VeneerConfigurationResolver.Merge(layers);
```

Then replace `ConfigurationFilename(RiverSystemProject project)` and its doc
comment with:

```csharp
        /// <summary>
        /// The sidecar beside the .rsproj, if it exists. Not "the effective
        /// configuration file": with three additive layers there is no single
        /// such file. Public API with no in-tree caller, kept because the
        /// question it answers is still a real one.
        /// </summary>
        public static string ConfigurationFilename(RiverSystemProject project)
        {
            return Candidates(project).SidecarLayer;
        }
```

- [ ] **Step 7: Drop the superseding clause from the diagnostic**

In `AutoStart/ProjectLoadListener.cs`, in `LogConfigurationChain`, delete:

```csharp
            // Which file is doing the superseding is not obvious from a bare
            // "superseding X" -- name the loser and say it is being ignored.
            if (config.SupersededSidecar != null)
                chain += " (ignoring the sidecar " + config.SupersededSidecar + ")";
```

and delete the paragraph of its doc comment beginning "The project is part of the
key" **only if** it references superseding — it does not, so leave the doc comment
otherwise intact.

- [ ] **Step 8: Run the suite**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 181, Failed: 0`.

If any `SupersededSidecar` reference remains, the compiler will say so. Search with
`git grep -n "SupersededSidecar"` and expect no hits outside `docs/`.

- [ ] **Step 9: Commit**

```bash
git add FlowMatters.Source.Veneer/
git commit -m "feat: make all three .veneer layers additive

A file named for the project in the configuration directory used to
replace the sidecar, so a home file supplying only env wiped out the
project's addons. All three layers now merge, most specific first."
```

---

## Task 2: File-level `env` in the format and the merge

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs`
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs`

**5 new cases. Running total: 186.**

- [ ] **Step 1: Write the failing tests**

Add to `VeneerConfigurationResolverTests`, after the existing `Merge_` tests. Note
the fixture already has `Layer`, `Addon`, `OptionsLayer` and `Names` helpers —
reuse them; add only `EnvLayer`:

```csharp
        private static VeneerConfigurationLayer EnvLayer(
            string path, params string[] keysAndValues)
        {
            var env = new Dictionary<string, string>();
            for (var i = 0; i < keysAndValues.Length; i += 2)
                env[keysAndValues[i]] = keysAndValues[i + 1];

            return new VeneerConfigurationLayer
            {
                Path = path,
                Configuration = new VeneerConfiguration { env = env }
            };
        }

        [Test]
        public void Merge_EnvIsEmptyWhenNoLayerSetsIt()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("a", null))
            });
            Assert.That(resolved.env, Is.Not.Null);
            Assert.That(resolved.env.Count, Is.EqualTo(0));
        }

        [Test]
        public void Merge_EnvTakesTheMostSpecificLayersValue()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                EnvLayer(HOME_PROJECT, "TOOLS_ROOT", @"D:\mine"),
                EnvLayer(SIDECAR, "TOOLS_ROOT", @".\tools")
            });
            Assert.That(resolved.env["TOOLS_ROOT"], Is.EqualTo(@"D:\mine"));
        }

        // Per key, not per block: a home file overriding one variable must not
        // discard the rest of the sidecar's block.
        [Test]
        public void Merge_EnvOverrideIsPerKeyNotPerBlock()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                EnvLayer(HOME_PROJECT, "TOOLS_ROOT", @"D:\mine"),
                EnvLayer(SIDECAR, "TOOLS_ROOT", @".\tools", "MODEL_ID", "example")
            });
            Assert.That(resolved.env["TOOLS_ROOT"], Is.EqualTo(@"D:\mine"));
            Assert.That(resolved.env["MODEL_ID"], Is.EqualTo("example"),
                        "the key the home file did not mention must survive");
        }

        // Environment variables are case-insensitive on Windows, and
        // BuildEffective builds its dictionary that way.
        [Test]
        public void Merge_EnvKeysAreCaseInsensitive()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                EnvLayer(HOME_PROJECT, "Tools_Root", @"D:\mine"),
                EnvLayer(SIDECAR, "TOOLS_ROOT", @".\tools")
            });
            Assert.That(resolved.env.Count, Is.EqualTo(1));
            Assert.That(resolved.env["TOOLS_ROOT"], Is.EqualTo(@"D:\mine"));
        }

        // The motivating case: the layer with the env contributes no addons, and
        // the layer with the addons contributes no env.
        [Test]
        public void Merge_EnvLayerWithNoAddonsStillContributesItsEnv()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                EnvLayer(HOME_PROJECT, "TOOLS_ROOT", @"D:\mine"),
                Layer(SIDECAR, null, Addon("Calibrate", null))
            });
            Assert.That(Names(resolved), Is.EqualTo("Calibrate"));
            Assert.That(resolved.env["TOOLS_ROOT"], Is.EqualTo(@"D:\mine"));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~VeneerConfigurationResolverTests"`
Expected: **build failure** — `VeneerConfiguration` has no `env`, and
`ResolvedVeneerConfiguration` has no `env`.

- [ ] **Step 3: Add the format field**

In `Addons/VeneerConfiguration.cs`, add to `VeneerConfiguration`, below
`targetScenario`:

```csharp
        /// <summary>
        /// Variables for every addon in every layer, not just this file's own.
        /// A committed sidecar can name %TOOLS_ROOT% while a personal home file
        /// supplies the path, with neither file knowing about the other.
        /// </summary>
        public Dictionary<string, string> env;
```

- [ ] **Step 4: Add the merged field**

In `ResolvedVeneerConfiguration`, below `options`:

```csharp
        public Dictionary<string, string> env =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 5: Merge it**

In `Merge`, alongside the existing `var options = new VeneerOptions();`, add:

```csharp
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

Inside the `foreach (var layer in layers)` loop, below the `layerOptions` block,
add:

```csharp
                var layerEnv = layer.Configuration.env;
                if (layerEnv != null)
                {
                    foreach (var kv in layerEnv)
                    {
                        // First layer to set a key wins, decided per key so that a
                        // home file overriding one variable keeps the rest.
                        if (!env.ContainsKey(kv.Key))
                            env[kv.Key] = kv.Value;
                    }
                }
```

And below `result.options = options;`:

```csharp
            result.env = env;
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 186, Failed: 0`.

If the count is not exactly 186, stop and report — do not adjust tests to reach it.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/
git commit -m "feat: merge a file-level env block across .veneer layers"
```

---

## Task 3: Feed the merged `env` into addon launches

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonContext.cs`
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs`

**8 new cases. Running total: 194.**

- [ ] **Step 1: Write the failing tests**

In `Tests/AddonEnvironmentTests.cs`, add this helper and these tests. Do **not**
change the existing `Ctx()` — these tests set `Env` explicitly so the existing
tests keep asserting the no-file-env case:

```csharp
        private static AddonContext CtxWithEnv(params string[] keysAndValues)
        {
            var context = Ctx();
            context.Env = new Dictionary<string, string>();
            for (var i = 0; i < keysAndValues.Length; i += 2)
                context.Env[keysAndValues[i]] = keysAndValues[i + 1];
            return context;
        }

        [Test]
        public void BuildEffective_InjectsFileLevelEnv()
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), null);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"D:\my\tools"));
        }

        [Test]
        public void BuildEffective_FileLevelEnvExpandsVeneerVariables()
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"%VENEER_CONFIG_DIR%\tools"), null);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"C:\Users\joel\.veneer\tools"));
        }

        // Asserted in both key orders: the result must not depend on which entry
        // the dictionary happens to yield first.
        [TestCase("AAA", "ZZZ")]
        [TestCase("ZZZ", "AAA")]
        public void BuildEffective_FileLevelEnvDoesNotResolveAgainstItself(
            string firstKey, string secondKey)
        {
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv(firstKey, @"D:\base", secondKey, "%" + firstKey + @"%\sub"), null);
            Assert.That(env[secondKey], Is.EqualTo("%" + firstKey + @"%\sub"),
                        "left literal, so a cross-reference is visible rather than order-dependent");
        }

        [Test]
        public void BuildEffective_AddonEnvResolvesAgainstFileLevelEnv()
        {
            var addonEnv = new Dictionary<string, string> { { "RUN_DIR", @"%TOOLS_ROOT%\runs" } };
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), addonEnv);
            Assert.That(env["RUN_DIR"], Is.EqualTo(@"D:\my\tools\runs"));
        }

        [Test]
        public void BuildEffective_AddonEnvWinsOverFileLevelEnv()
        {
            var addonEnv = new Dictionary<string, string> { { "TOOLS_ROOT", @"E:\override" } };
            var env = AddonEnvironment.BuildEffective(
                CtxWithEnv("TOOLS_ROOT", @"D:\my\tools"), addonEnv);
            Assert.That(env["TOOLS_ROOT"], Is.EqualTo(@"E:\override"));
        }

        // Deliberately permitted rather than reserved. Pinned so that removing the
        // ability becomes a visible decision rather than an accident.
        [Test]
        public void BuildEffective_FileLevelEnvCanOverrideAVeneerVariable()
        {
            var env = AddonEnvironment.BuildEffective(CtxWithEnv("VENEER_PORT", "1234"), null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("1234"));
        }

        [Test]
        public void BuildEffective_NullFileLevelEnvIsHarmless()
        {
            var context = Ctx();
            context.Env = null;
            var env = AddonEnvironment.BuildEffective(context, null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("9876"));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~AddonEnvironmentTests"`
Expected: **build failure** — `AddonContext` has no `Env`.

- [ ] **Step 3: Add the context property**

In `DomainActions/AddonContext.cs`, add below `ConfigDirectory`:

```csharp
        /// <summary>
        /// The file-level env merged from every .veneer layer. Distinct from an
        /// addon's own env, which is more specific and wins.
        /// </summary>
        public Dictionary<string, string> Env { get; set; }
```

Add `using System.Collections.Generic;` if the compiler asks.

- [ ] **Step 4: Add the layer to `BuildEffective`**

In `DomainActions/AddonEnvironment.cs`, immediately after the
`env["VENEER_CONFIG_DIR"] = ...` line and before the `if (addonEnv != null)`
block, insert:

```csharp
            // The .veneer files' own env, beneath the addon's. Snapshot first so
            // these cannot resolve against each other -- the result must not
            // depend on JSON key order or on which layer a key came from.
            if (context.Env != null)
            {
                var injected = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in context.Env)
                    env[kv.Key] = Expand(kv.Value, injected);
            }
```

Then update the doc comment on `BuildEffective` to read:

```csharp
        /// <summary>
        /// process environment + Veneer's injected variables + the .veneer files'
        /// file-level env + the addon's own env, each winning over the one before.
        /// Each layer is expanded against a snapshot of the layers above it, never
        /// against itself, so no result depends on JSON key order.
        /// </summary>
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 194, Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/
git commit -m "feat: layer the .veneer file-level env into addon launches"
```

---

## Task 4: Populate it from the menu

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (`BuildAddonContext`)

**0 new cases. Running total: 194.**

**No unit-test surface, and that is expected.** `BuildAddonContext` reads
`Scenario`, a RiverSystem static. The behaviour it enables is covered by Task 3
against `BuildEffective`, and observed for real in Task 6.

- [ ] **Step 1: Add the initialiser entry**

In `VeneerMenu.BuildAddonContext`, add below the `ConfigDirectory` line:

```csharp
                Env = VeneerConfiguration.Load(Scenario).env,
```

This is a fifth `Load` call site, at launch rather than menu-open. That is
deliberate: it picks up an edit made after the menu was opened, which is the
documented promise that files are re-read rather than cached.

- [ ] **Step 2: Run the suite**

Run: `dotnet test C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`
Expected: `Passed: 194, Failed: 0`, no compiler errors.

Also build the other project:
`dotnet build C:\src\projects\Veneer-global-config\FlowMatters.Source.VeneerCmd\FlowMatters.Source.VeneerCmd.csproj --nologo`

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "feat: pass the merged .veneer env to launching addons"
```

---

## Task 5: Documentation

**Files:**
- Modify: `docs/veneer-file-format.md`
- Modify: `Samples/addons/README.md`

**0 new cases. Running total: 194.**

The previous feature's documentation describes a two-layer model with a replacing
project layer. Every part of that is now wrong.

- [ ] **Step 1: Rewrite the layer table**

In `docs/veneer-file-format.md`, under `## Filename and discovery`, replace the
two-row table and the paragraph beginning "The project layer is **one slot with
two candidates**" with:

````markdown
| # | Layer | Where |
|---|-------|-------|
| 1 | Home project | `<configDir>/<project>.rsproj.veneer` |
| 2 | Sidecar | `<projectDir>/<project>.rsproj.veneer` |
| 3 | Global | `<configDir>/global.veneer` |

All three are **additive** — none replaces another. A file in your configuration
directory that supplies only `env` leaves the sidecar's addons untouched.
````

Delete the `## Global configuration` paragraph about "one slot with two
candidates" and the sentence "your personal entries appear after the project's
own", which is false for the home project file. Replace the latter with:

````markdown
Layers are listed most specific first, and that one ordering decides both which
layer wins a contested field and the order addons appear in a menu. So a home
project file's addons appear above the sidecar's, and `global.veneer`'s appear
last.
````

- [ ] **Step 2: Update the combination table**

Replace the `### How the layers combine` table with:

````markdown
| Field | Rule |
|---|---|
| `addons` | Concatenated in layer order, most specific first. No de-duplication — two addons with the same name produce two menu items. |
| `env` | Merged **per key**, most specific layer winning. Overriding one variable does not discard the rest of a layer's block. |
| `targetScenario` | Applies only to the addons **in its own file**. A `targetScenario` in `global.veneer` never gates the project's addons. |
| `options` | Merged field by field, most specific layer winning; a field no layer sets keeps Veneer's own default. |
````

- [ ] **Step 3: Document the `env` block**

Add a new `## Shared variables` section after `### How the layers combine`:

````markdown
## Shared variables

Any `.veneer` file may carry a top-level `env` block. Its entries apply to **every
addon in every layer**, not just the file's own, which is what lets a committed
sidecar name a variable that a personal file defines:

```jsonc
// ~/.veneer/ExampleProject.rsproj.veneer   (personal, never committed)
{ "env": { "TOOLS_ROOT": "D:\\my\\tools" } }

// ExampleProject.rsproj.veneer             (committed alongside the model)
{ "addons": [ { "name": "Calibrate", "type": "exe",
                "path": "%TOOLS_ROOT%/calibrate.bat" } ] }
```

These become real environment variables for the launched process, so a script can
read `%TOOLS_ROOT%` without it being spliced into `args`.

A value may itself use process variables and Veneer's own — for example
`"TOOLS_ROOT": "%VENEER_CONFIG_DIR%\\tools"`. It may **not** use another `env`
entry: `"SUB": "%TOOLS_ROOT%\\sub"` is left as written, so a cross-reference is
visible rather than dependent on file or key order. An addon's own `env` may use
these values, and wins where both set the same name.

Nothing is reserved, so an `env` block can override `%VENEER_PORT%` and its
siblings. There is rarely a reason to.
````

- [ ] **Step 4: Fix the discovery section's log example**

In `### Finding out what actually loaded`, delete the "ignoring the sidecar"
example block and the sentence introducing it. The chain line now simply lists up
to three files.

- [ ] **Step 5: Rewrite the sample**

In `Samples/addons/README.md`, replace the final paragraph beginning "To override
a model's committed sidecar" with:

````markdown
To supply machine-specific paths to a model whose `.veneer` file is committed,
name the file for the project — `%USERPROFILE%\.veneer\ExampleProject.rsproj.veneer` —
and give it an `env` block. The sidecar keeps providing the addons; your file
provides the paths they use.
````

- [ ] **Step 6: Verify the docs against the code**

Read `VeneerConfiguration`, `VeneerConfigurationResolver` and `AddonEnvironment`
and confirm every field name, default and rule stated in the document matches.
Check both directions: every documented field exists, and every field in the code
is documented. **Report discrepancies rather than silently editing the docs to
match.**

- [ ] **Step 7: Commit**

```bash
git add docs/veneer-file-format.md Samples/addons/README.md
git commit -m "docs: describe additive layers and the shared env block"
```

---

## Task 6: Manual verification in Source

**Files:** none.

**Nothing in Tasks 1, 4 and 5 has GUI coverage**, and Task 3's coverage stops at
`BuildEffective`. This task is where the feature is observed working. Do not mark
it done on reasoning.

This **replaces** Task 10 of the previous plan, whose steps 3–5 test replacement
behaviour that no longer exists.

- [ ] **Step 1: Build and install**

Run `build.bat`; install the output into Source's Plugin Manager.

- [ ] **Step 2: The motivating case**

Give a saved project a sidecar containing one `exe` addon with
`"path": "%TOOLS_ROOT%/echo.bat"` and no `env`. Put
`{ "env": { "TOOLS_ROOT": "D:\\my\\tools" } }` at
`%USERPROFILE%\.veneer\<name>.rsproj.veneer`, and create `D:\my\tools\echo.bat`.

Open the project and launch the addon.
Expected: the addon appears (proving the home file no longer replaced the sidecar)
and runs `D:\my\tools\echo.bat`, its output appearing in the Veneer panel.

- [ ] **Step 3: Both files contribute addons**

Add an addon to the home project file under menu `My Tools`, and reopen the
project — top-level menu entries are computed once, in `InitialiseRequiredMenus`,
so a new menu will not appear mid-session.
Expected: both addons are present, and `My Tools` sits **left of** the sidecar's
menu in the menu bar.

- [ ] **Step 4: Per-key override**

Add `"env": { "TOOLS_ROOT": ".\\tools", "MODEL_ID": "example" }` to the sidecar,
leaving the home file's `TOOLS_ROOT` in place.
Expected: the addon still runs `D:\my\tools\echo.bat`, and a script addon echoing
`%MODEL_ID%` prints `example` — the home file overrode one key without discarding
the other.

- [ ] **Step 5: The chain diagnostic**

Check Source's log on project load.
Expected: `Veneer configuration:` naming both files, home project first, with **no**
"ignoring the sidecar" text.

- [ ] **Step 6: A malformed file is survivable**

Put `{ not json` in the home project file.
Expected: the sidecar's addons still appear; the log names the bad file **once**,
not once per dropdown.

- [ ] **Step 7: Record the outcome**

Update the execution-status table at the top of this plan with what was observed.
State plainly any step that could not be run.

---

## Task 7: Port to `legacy_ci`

**Files:** all of the above, on branch `port/global-veneer-config`.

**0 new cases on the feature branch.**

The port branch already carries the *previous* design. Apply these changes on top
as a second commit rather than rebuilding the branch — the earlier port was
reviewed and its hand-edits are still correct except where this plan changes them.

- [ ] **Step 1: Copy the files that copy cleanly**

From worktree `C:\src\projects\Veneer-legacy-port`:

```bash
git checkout feature/global-veneer-config -- \
  FlowMatters.Source.Veneer/Addons/VeneerConfigurationResolver.cs \
  FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs \
  FlowMatters.Source.Veneer/DomainActions/AddonContext.cs \
  FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs \
  FlowMatters.Source.Veneer/Tests/VeneerConfigurationResolverTests.cs \
  FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs \
  docs/veneer-file-format.md \
  Samples/addons/README.md
```

These eight were already copied wholesale for the previous port and remain
branch-identical apart from this feature.

- [ ] **Step 2: Hand-apply the two `VeneerMenu.cs` changes**

`legacy_ci`'s `VeneerMenu.cs` differs genuinely — it uses `Control` where the
feature branch uses `EffectiveControl` — so do **not** copy it. Apply Task 4's
`Env = VeneerConfiguration.Load(Scenario).env,` line to `BuildAddonContext`.

No other `VeneerMenu.cs` change is needed; the four edits from the previous port
are unaffected.

- [ ] **Step 3: Hand-apply the `ProjectLoadListener.cs` change**

Delete the superseding clause from `LogConfigurationChain`, exactly as in Task 1
Step 7.

- [ ] **Step 4: Confirm the build behaves as before**

`legacy_ci` does not build in this environment. Establish the comparison honestly:

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" C:\src\projects\Veneer-legacy-port\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -v:m -nologo
```

Expected: a single `error MC1000` in the WPF markup compiler and **no others** —
identical to pristine `legacy_ci`. `MC1000` aborts before `csc` runs, so this
produces no C# diagnostics at all. Report any *additional* error as a real defect.

- [ ] **Step 5: Verify C# 7.3 compatibility the only way that works here**

Because the port cannot reach `csc`, compile the identical sources on the feature
branch with the language version pinned:

```
dotnet build C:\src\projects\Veneer-global-config\FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -p:LangVersion=7.3 -p:TreatWarningsAsErrors=false --nologo
```

Expected: the only errors are two `CS8703`s in `ISourceService.cs`, a file this
feature does not touch and the port does not copy. **Any language-version error in
a ported file is a real defect.** Report this as the weaker check it is.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "port: additive .veneer layers and the shared env block"
```

- [ ] **Step 7: Record honestly**

State in the commit message and the execution-status table exactly what was and
was not verified — in particular that the 194 tests were **not** run on this
branch.

---

## Notes for the implementer

**The ordering does double duty.** `Resolve` returns layers most-specific-first,
and that same order drives `Merge`'s first-wins precedence *and* addon
concatenation. Do not introduce a second ordering for menus; the consequence — a
home project file's addons appearing above the sidecar's — is an accepted design
decision, not a bug to fix.

**Do not let file-level `env` entries resolve against each other.** The snapshot in
`BuildEffective` is what prevents it. Removing the snapshot would make the result
depend on dictionary iteration order, which is exactly the failure the tests in
Task 3 Step 1 pin in both key orders.

**Do not reserve the `VENEER_*` names.** A file-level `env` overriding them is
permitted deliberately, and pinned by a test so that changing it is a visible
decision.

**Do not add a diagnostic naming which file set which variable.** It was
considered and ruled out of scope; the chain line names the contributing files and
an unresolved variable expands to itself visibly.

**`Merge` still mutates `addon.scenario`** to push `targetScenario` down per layer.
That remains safe only because `Load` builds a fresh object graph per call and
nothing caches layers. If you find yourself caching, copy the addons first.
