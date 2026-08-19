# Global `.veneer` configuration

## Problem

Veneer's addon and options configuration lives in a single sidecar file next to
the Source project: `ExampleProject.rsproj` is configured by
`ExampleProject.rsproj.veneer` in the same directory. `VeneerConfiguration.Load`
derives that one path and returns `null` when it is absent.

The sidecar location is wrong for two common situations.

**A model shared over git.** The `.rsproj` is committed; the `.veneer` file
either is not, in which case it does not survive a fresh clone, or is, in which
case every user of the repository shares one set of addons and one set of
options. A modeller who wants their own tools against a shared model has nowhere
to put them.

**Per-user tools that apply to every project.** A modeller with a personal
utility — a calibration launcher, a link to an internal wiki — must copy the
same addon entry into every project's sidecar and maintain them in parallel.

Both are solved by allowing configuration to live in the user's home directory,
in the same format, discovered in addition to (and in one case instead of) the
sidecar.

## Solution

A per-user configuration directory holding files in exactly the existing
`.veneer` format:

```
~/.veneer/global.veneer                  applies to every project
~/.veneer/ExampleProject.rsproj.veneer   applies to that project only
```

Configuration is resolved into **two layers**, merged into one effective
configuration each time it is loaded.

### The configuration directory

Resolved on every load, in this order:

1. The `VENEER_CONFIG_DIR` environment variable, if set to a non-empty value.
2. Otherwise `Environment.GetFolderPath(SpecialFolder.UserProfile)` joined with
   `.veneer` — `C:\Users\<user>\.veneer` on Windows.

Veneer never creates the directory. A missing directory is not an error; it
simply contributes no layers. The environment variable is an escape hatch for
roaming profiles and site deployments, and it is what makes the resolution logic
testable without faking a home directory.

### Layers

With `name = Path.GetFileName(project.FullFilename)`, for example
`ExampleProject.rsproj`:

| # | Layer | Path |
|---|-------|------|
| 1 | Project | `<configDir>\<name>.veneer` **if it exists**, otherwise `<projectDir>\<name>.veneer` |
| 2 | Global | `<configDir>\global.veneer` |

The project layer is a **single slot with two candidates**, not two layers. A
file named for the project in the configuration directory *replaces* the
sidecar. That is deliberate: it is the escape hatch for a repository whose
committed sidecar you do not want, and without it there would be no way to
suppress a sidecar that ships with a shared model.

The global layer is **additive**. It never replaces anything and is never
replaced.

Matching is by file name only. Two different projects that happen to share a
file name share the same `<configDir>\<name>.veneer`. This is accepted: the
alternative — encoding the full path or a hash of it into the file name —
produces names a user cannot type or recognise, and defeats the purpose of a
directory a human maintains by hand.

### Merging

The two layers, in the order above, produce one `ResolvedVeneerConfiguration`.

**`addons` — concatenated, project layer first, global appended.**

Order matters twice over: `MenuLayout.TopLevelMenus` derives menu-bar position
from the order menus first appear, and items within a menu appear in list order.
Project first means a shared model's menu layout is unaffected by whatever the
user happens to have in `~/.veneer`; personal extras land to the right of, and
below, the project's own entries.

There is no de-duplication by name. Two addons called the same thing produce two
menu items. That is visible and diagnosable; silently dropping one is not.

**`targetScenario` — pushed down into addons, not merged.**

Before concatenation, each layer stamps its own `targetScenario` onto any of its
addons whose `scenario` is empty. A per-addon `scenario` always wins, exactly as
today.

This is the only correct treatment. `targetScenario` is a default for the addons
in *its own file*; if `global.veneer` sets `targetScenario: "Operations"` and the
sidecar does not, flattening to a single value would silently gate the project's
addons on a scenario their author never mentioned. After push-down every addon
carries its own effective filter, so `ResolvedVeneerConfiguration` has no
`targetScenario` field at all — there is no correct single value for one.

Push-down populates precisely the field `EffectiveFilter` reads first, which
makes their `VeneerConfiguration config` parameter dead. Both lose it:

```csharp
public static string EffectiveFilter(VeneerAddon addon)
public static bool AddonAppliesTo(VeneerAddon addon, RiverSystemScenario currentScenario)
```

The alternative — deriving `ResolvedVeneerConfiguration` from
`VeneerConfiguration` so the existing two-argument signatures still bind — would
mean the resolved type inherits a `targetScenario` field that must never be read,
which is the trap push-down exists to remove. `VeneerMenu.cs:129` and `:131` are
the only callers.

**`options` — merged field by field, project layer wins.**

A field set by the project layer wins. A field the project layer omits falls back
to the global layer. A field neither layer sets stays unset, and the consumer
leaves its existing default alone.

This requires `VeneerOptions` fields to become nullable (`bool?`, `int?`). As
plain `bool`, absent and `false` deserialize identically, so "project overrides
global where it specifies a value" is not expressible: a global `allowScripts:
true` could never be turned off by a project file. The JSON format does not
change — Newtonsoft maps an absent key to `null` and a present one to its value.

Making the fields nullable also fixes a current defect. `VeneerMenu.cs:150`
assigns `WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts`
unconditionally, so any `.veneer` file with an `options` block that omits
`allowScripts` resets the flag to `false` — overwriting a value set from the
`VENEER_ALLOW_SCRIPTS` environment variable or the GUI. Under the new rule the
assignment happens only when a layer specified the field.

### Relative paths and `%VENEER_CONFIG_DIR%`

Addon `path` and `workingDirectory` continue to resolve against the **project
directory**, wherever the addon was declared. Resolution rules are identical in
all three files.

This is the right default because `<configDir>\<name>.veneer` stands in for the
sidecar: an addon there referring to `tools/calibrate.bat` means the tool inside
the cloned repository, which is what project-relative gives. Resolving relative
to the declaring file would break exactly that case, and would make the same JSON
snippet behave differently depending on which directory it sits in.

For tools that live with the configuration rather than with the model, a new
injected variable `%VENEER_CONFIG_DIR%` expands to the resolved configuration
directory, so a global addon can say:

```json
{ "name": "My calibration", "type": "exe", "path": "%VENEER_CONFIG_DIR%/tools/calibrate.bat" }
```

It joins `%VENEER_PORT%`, `%VENEER_PROJECT_DIR%` and `%VENEER_PROJECT_FILE%`, and
like them is available in `path`, `args`, `workingDirectory`, `url`, `env` values
and script lines — all of which already route through `AddonEnvironment.Expand`.

## Components

### New: `Addons/VeneerConfigurationResolver.cs`

Pure. No RiverSystem, TIME, WinForms or `System.IO.File` dependency, following
the rationale already written into `MenuLayout`: the interesting logic must be
unit-testable without a loaded Source scenario.

```csharp
public static string ConfigDirectory(Func<string, string> getEnv, Func<string> userProfile)
public static ConfigCandidates Resolve(string configDir, string projectFullFilename, Func<string, bool> exists)
public static bool TryParse(string json, out VeneerConfiguration config, out string error)
public static ResolvedVeneerConfiguration Merge(IList<VeneerConfigurationLayer> layers)
```

`Resolve` returns a `ConfigCandidates` carrying the ordered list of paths that
exist, plus `SupersededSidecar` — the sidecar path when a global project file
displaced an existing one, null otherwise. File-system access enters only through
the injected `exists` predicate, so the entire precedence table can be tested
against an in-memory set of paths.

`Merge` sees only the layers. It fills `addons`, `options` and `SourceFiles` (the
layer paths, in order); `Load` copies `SupersededSidecar` across from the
`ConfigCandidates` afterwards. Splitting it that way keeps `Merge` a pure
function of its layers, which is what the merge tests exercise.

`ConfigCandidates` and `VeneerConfigurationLayer` (`Path` + `Configuration`) are
plain carriers declared alongside it.

### New: `ResolvedVeneerConfiguration`

Declared in `VeneerConfigurationResolver.cs` alongside the other carriers, so
`VeneerConfiguration.cs` holds only the on-disk schema types.

```csharp
public VeneerAddon[] addons;
public VeneerOptions options;
public string[] SourceFiles;
public string SupersededSidecar;
```

`addons` and `options` keep their lower-case names so the three consuming call
sites read unchanged. `SourceFiles` and `SupersededSidecar` exist for the
diagnostic line.

### Changed: `Addons/VeneerConfiguration.cs`

- `Load(RiverSystemProject)` and `Load(RiverSystemScenario)` now return
  `ResolvedVeneerConfiguration`. `Load` does only directory resolution,
  `File.Exists`, `File.ReadAllText` and per-layer `TryParse`; everything else
  delegates to the resolver.
- `Load` never returns null. With no layers it returns a
  `ResolvedVeneerConfiguration` whose `addons` is an empty array and whose
  `options` is a `VeneerOptions` with every field null. `addons` and `options` are
  never null, which is what lets the consumers test individual option fields
  rather than the block. The existing `config?.` call sites keep working either
  way.
- `EffectiveFilter` and `AddonAppliesTo` drop their `VeneerConfiguration`
  parameter, as described under **Merging**.
- `ConfigurationFilename(project)` stays public but now returns the *effective
  project-layer* path — the global override if present, else the sidecar, else
  null — so the name remains honest under the new scheme.
- Its `FullFilename.Replace(".rsproj", ".rsproj.veneer")` is replaced by
  `Path.GetFileName` composition. `Replace` substitutes **every** occurrence, so a
  project at `C:\models.rsproj\a.rsproj` currently looks for
  `C:\models.rsproj.veneer\a.rsproj.veneer`. The global lookup needs the bare file
  name regardless, so the fix falls out of the rewrite.
- `VeneerOptions` fields become `bool? allowScripts`, `int? defaultPort`,
  `bool? autoStart`.

### Changed: call sites

| Site | Change |
|---|---|
| `VeneerMenu.PopulateReportMenu` (`:79`) | type of `config`; conditional assignment of `DefaultAllowScripts` and `DefaultPort` |
| `VeneerMenu.PopulateReportMenu` (`:129`, `:131`) | drop the `config` argument to `AddonAppliesTo` / `EffectiveFilter` |
| `VeneerMenu.RequiredMenus` (`:378`) | type of `config` only |
| `ProjectLoadListener.ApplyDefaultsFromEnvironmentAndConfig` (`:192`) | type of `config`; `defaultPort.GetValueOrDefault() > 0`; calls `LogConfigurationChain` |
| `ProjectLoadListener.ApplyScenarioChange` (`:156`) | calls `LogConfigurationChain` |

### Changed: addon environment

`AddonContext` gains `ConfigDirectory`. `AddonEnvironment.BuildEffective` sets
`env["VENEER_CONFIG_DIR"]`, alongside the three existing injected variables.
`VeneerMenu.BuildAddonContext` populates it from the resolver.

## Error handling

Each layer is parsed independently. A malformed or unreadable file is logged once
and that layer is skipped; the remaining layers still apply. Today a malformed
`.veneer` throws out of `Load`, which runs on every dropdown open; with up to two
files feeding the menu, one bad file must not take out the other.

An absent configuration directory, absent files, and a null `FullFilename` are
all normal states. They yield fewer layers and never an exception.

A project that has never been saved (`FullFilename == null`) has no project layer
but **does** receive `global.veneer`, where `Load` returns `null` outright today.
That is the point of a wildcard file: the menu items appear.

They cannot all be *launched* there, though, and the spec is explicit about it
rather than leaving it to be discovered. `AddonLauncher.Launch` refuses an empty
`ProjectDirectory` before any path resolution (`AddonLauncher.cs:35-42`),
deliberately — an empty project directory would leave a relative `FileName` that
Windows resolves against the parent's cwd and `PATH`, so an addon named
`python.exe` could silently launch something else. So in a never-saved project a
global `exe` or `script` addon reports "no project directory is available" even
when its `path` is rooted or `%VENEER_CONFIG_DIR%`-based; only `type: "url"`
addons, which take the separate `LaunchUrl` entry point, actually run. Relaxing
that guard is out of scope; the limitation is documented instead.

## Diagnostics

With up to two files feeding one menu, "where did this item come from?" and "why
is my sidecar being ignored?" are questions a user will ask.

A new `ProjectLoadListener.LogConfigurationChain(resolved)` emits a
`TIME.Management.Log.WriteInfo` naming each contributing file in resolution
order, plus an explicit line when a global project file supersedes a sidecar that
exists on disk.

It is called from **two** places, because neither alone covers the case that
prompts the question:

- `ApplyDefaultsFromEnvironmentAndConfig`, reached via `ScenarioLoaded()` on the
  `FirstSighting` transition — the first project of a session.
- `ApplyScenarioChange`, the `Rebind` / `Cleared` path (`:132-141`). Opening a
  different project while one is loaded goes here and never reaches
  `ScenarioLoaded`, yet "why is my sidecar being ignored?" is most likely to be
  asked immediately after a project switch. This path does its own
  `VeneerConfiguration.Load` for the purpose; it does **not** re-apply
  `options` defaults, which stays a load-time concern.

`LogConfigurationChain` holds the last chain it logged in a static field and
writes only when the chain differs. `Rebind` also fires for a scenario change
*within* one project, where the resolved files are identical — deduplicating on
the chain keeps that silent while still reporting a genuine project switch. This
is the only per-dropdown-safe design too, which is why the diagnostic does not
live in `VeneerMenu`.

This is **GUI only**. `VeneerCmd` calls
`InitialiseOnLoadAttribute.MarkInitialised()` (`Program.cs:197`) before any plugin
attribute is constructed, precisely so `ProjectLoadListener` is never created —
and `VeneerConfiguration.Load` has no call site in `FlowMatters.Source.VeneerCmd`
at all. Headless runs consume no `.veneer` configuration today and this feature
does not change that; see **Out of scope**.

No echo to the Veneer panel: the panel is not reliably constructed at project
load, and `WebServerStatusControl.ActiveInstance` is a `master`-only member, so
routing through it would cost the `legacy_ci` port a divergence for no gain.

No REST endpoint changes, therefore no `PROTOCOL_VERSION` bump and no
`docs/api/` edits.

## Testing

NUnit, in the existing `FlowMatters.Source.Veneer/Tests/` folder.

**New `VeneerConfigurationResolverTests`:**

- `ConfigDirectory`: `VENEER_CONFIG_DIR` set, unset, and set to empty string.
- Candidate matrix: sidecar only; global project file only; both present (global
  wins, sidecar reported as superseded); neither; each of those crossed with
  `global.veneer` present and absent.
- Null and empty `projectFullFilename` — global layer only.
- A project under a directory literally named `models.rsproj`, covering the old
  `Replace` defect.
- Merge: addon concatenation order (project first); `targetScenario` push-down
  confined to its own layer; a per-addon `scenario` beating its layer's
  `targetScenario`; options precedence including a project `false` beating a
  global `true`; a field unset in both layers staying null; duplicate addon names
  both surviving; an empty layer list.
- `TryParse` on malformed JSON returning false with a non-empty error, and on
  `{}` returning an empty configuration.

**Extended `AddonEnvironmentTests`:** `%VENEER_CONFIG_DIR%` expansion, including
a null `ConfigDirectory` yielding empty string, matching the existing treatment
of `VENEER_PROJECT_DIR`.

## Documentation

`docs/veneer-file-format.md`:

- Rewrite **Filename and discovery** to describe the two layers and the
  configuration directory.
- New **Global configuration** section: the two file names, the replace rule for
  the project layer, the additive rule for the global layer, merge semantics for
  `addons`, `targetScenario` and `options`, and `VENEER_CONFIG_DIR`.
- Add `%VENEER_CONFIG_DIR%` to **Injected variables**.
- Note in **Options** that an omitted field now falls back to the global layer
  and then to Veneer's default, rather than being forced to `false`/ignored.

`Samples/addons/README.md`: a worked example of a personal `global.veneer`
alongside a shared model.

## Port to `legacy_ci`

`Addons/VeneerConfiguration.cs`, `DomainActions/AddonContext.cs` and
`DomainActions/AddonEnvironment.cs` are byte-identical between `master` and
`legacy_ci`, so they copy across along with the new resolver and its tests.
`VeneerMenu.cs` differs, including
`BuildAddonContext` itself — `legacy_ci` uses `Control` directly where `master`
uses `EffectiveControl` — so the one-line `ConfigDirectory` addition and the
`AddonAppliesTo` / `EffectiveFilter` call-site edits are applied by hand on that
branch rather than copied.

All new code is framework-agnostic: no WCF or CoreWCF surface, no async, no
`ISourceService` entry, so none of the adaptations in `branch-porting-guide.md`
apply. The `master` implementation will therefore be written in **C# 7.3
compatible style** — no target-typed `new`, switch expressions, nullable
reference types or `is not` — making the port:

1. Copy `VeneerConfigurationResolver.cs`, `VeneerConfiguration.cs`,
   `AddonContext.cs`, `AddonEnvironment.cs` and the two test files.
2. Add `<Compile Include>` entries for the new files to the non-SDK `.csproj`.
3. Hand-apply the `VeneerMenu.cs` edits (config type, conditional option
   assignment, the two filter call sites, `ConfigDirectory` in
   `BuildAddonContext`) and the `ProjectLoadListener.cs` edits.
4. Copy the documentation changes.

## Out of scope

- A machine-wide (`%PROGRAMDATA%`) tier. `VENEER_CONFIG_DIR` covers site
  deployment without a third layer to explain against the replace rule.
- Consuming `.veneer` configuration in headless `FlowMatters.Source.VeneerCmd`.
  It has no `VeneerConfiguration.Load` call site today and never constructs
  `ProjectLoadListener`, so wiring global configuration in would be a new feature
  in its own right, not a consequence of this one.
- Relaxing `AddonLauncher`'s empty-`ProjectDirectory` guard so global addons can
  launch in a never-saved project.
- Exposing the resolved source files over the REST API.
- Any way for a layer to *remove* an addon contributed by another layer.
- Resolving the project layer by anything other than file name.
