# Merged `.veneer` Layers and File-Level `env` — Design

**Date:** 2026-08-21
**Status:** Approved, ready for implementation planning
**Supersedes parts of:** [`2026-08-19-global-veneer-config-design.md`](2026-08-19-global-veneer-config-design.md)

## Problem

The global `.veneer` work landed a project layer that is **one slot with two
candidates**: `<configDir>/<name>.rsproj.veneer` *replaces* the sidecar when both
exist. That serves "I don't want this model's committed configuration", but it
defeats the case that actually turns up in practice.

A team commits a sidecar describing the addons. Each modeller needs those same
addons to point at a different place on their own machine — a tools directory, a
Python interpreter, a scratch drive. Today a home file supplying only that path
would wipe out the sidecar's addons entirely, because it replaces rather than
merges.

The two files need to combine: the sidecar says *what the addons are*, the home
file says *where things live on this machine*, and neither needs to know the other
exists.

## Decisions

Settled during brainstorming; recorded here because each closes off alternatives a
planner might otherwise reopen.

| Decision | Choice | Rejected |
|---|---|---|
| Layering | Three layers, always additive. Nothing replaces anything. | Merge-by-default with a `replaceSidecar` opt-out; a separate `.local.veneer` file |
| Mechanism | A file-level `env` block, expanded by the existing `%NAME%` machinery | A dedicated `variables` block; a single `basePath` option |
| Precedence | Specific beats general, one rule for every field | Personal-beats-shared; a split rule differing by field kind |
| Expansion | File-level values resolve process env and `%VENEER_*%` only | Letting them resolve each other |
| Plumbing | Merged `env` travels on `AddonContext` | Pushing it down into each addon, as `targetScenario` is |

The **replace** behaviour is removed, not deprecated. There is no escape hatch for
suppressing a sidecar; a user who wants one edits or deletes the sidecar.

## Layer model

Three layers. Each is optional and contributes independently.

| # | Layer | Path |
|---|-------|------|
| 1 | Home project | `<configDir>/<name>.rsproj.veneer` |
| 2 | Sidecar | `<projectDir>/<name>.rsproj.veneer` |
| 3 | Global | `<configDir>/global.veneer` |

`<configDir>` is unchanged: `VENEER_CONFIG_DIR` if set, otherwise
`%USERPROFILE%\.veneer`. Veneer never creates it. Matching is still by file name
only, so two projects sharing a file name share layer 1.

**One ordering serves both purposes.** Layers are listed most-specific-first, and
that single order decides both which layer wins a contested field and the order
addons are concatenated into menus. `Merge`'s existing rule — "layers arrive
most-specific-first, the first to specify a field wins" — is therefore unchanged.

The visible consequence: a home project file's addons appear **above** the
sidecar's in a menu. This is accepted rather than corrected. The alternative is
carrying a precedence rank and a separate menu rank through
`VeneerConfigurationLayer` and `Merge`, which is real complexity for a case that
mostly does not arise — in the motivating scenario the home file contributes no
addons at all. `global.veneer` remains last, as before.

No existing behaviour is broken by this ordering: because the home project file
currently replaces the sidecar, their addons have never coexisted, so no
expectation about their relative order exists.

## The `env` block

A new optional top-level field, alongside `addons`, `options` and
`targetScenario`:

```jsonc
{
  "env": {
    "TOOLS_ROOT": "D:\\my\\tools",
    "PYTHON": "C:\\Py311\\python.exe"
  }
}
```

Type: `Dictionary<string, string>`. Absent and empty are equivalent. Newtonsoft
coerces scalar values, so `"PORT": 9876` yields the string `"9876"` rather than
throwing.

The motivating case end to end:

```jsonc
// ~/.veneer/ExampleProject.rsproj.veneer   (personal, never committed)
{ "env": { "TOOLS_ROOT": "D:\\my\\tools" } }

// <projectDir>/ExampleProject.rsproj.veneer   (committed to git)
{ "addons": [ { "name": "Calibrate", "type": "exe",
                "path": "%TOOLS_ROOT%/calibrate.bat" } ] }
```

### Merging

`env` merges **per key**, first-wins, using the layer order above — the same rule
`options` already uses field-wise. A home file overriding one variable does not
discard the rest of the sidecar's block.

`ResolvedVeneerConfiguration.env` is never null, so consumers need no null check.

### Expansion

`AddonEnvironment.BuildEffective` gains one layer:

```
1. process environment
2. Veneer's injected VENEER_PORT, VENEER_PROJECT_DIR,
   VENEER_PROJECT_FILE, VENEER_CONFIG_DIR
3. merged file-level env     <- new; expanded against a snapshot of 1+2
4. the addon's own env       <- expanded against a snapshot of 1+2+3
```

The snapshot mechanism already in `BuildEffective` supplies the required
semantics. Step 3 cannot see itself, so the result never depends on JSON key
order or on which layer a key came from. This makes
`"TOOLS_ROOT": "%VENEER_CONFIG_DIR%\\tools"` work while
`"SUB": "%TOOLS_ROOT%\\sub"` in the same block does not — the latter is left
literal, which is visible rather than silent.

Step 4 *can* see step 3, so an individual addon may write
`"env": { "RUN_DIR": "%TOOLS_ROOT%\\runs" }`. An addon's own `env` wins on
conflict, being the most specific of all.

The merged result reaches `path`, `args`, `workingDirectory` and `url` through the
existing `AddonEnvironment.Expand` calls in `AddonLauncher`, which is what makes a
committed sidecar's `%TOOLS_ROOT%` in `path` resolve. Unknown variables continue
to expand to themselves.

**Script lines take a different route, and a plan must not assume otherwise.**
`LaunchScript` writes `addon.script` to stdin unexpanded — `Expand` is never
called on it. Script bodies see the variables because `ApplyEnvironment` sets the
merged dictionary as the child process's real environment and `cmd.exe` performs
its own `%VAR%` substitution at run time. The user-visible outcome is the same,
including the "unknown variable stays literal" behaviour, but any test asserting
`Expand` runs over script bodies would be asserting something false.

### Two accepted consequences

**These are real environment variables.** They are in the dictionary handed to the
child process, not merely expansion tokens — deliberate, so a script can read
`%TOOLS_ROOT%` without it being spliced into `args`. The cost is that a
mistyped key is exported rather than rejected.

**A file-level `env` may overwrite Veneer's own `VENEER_*` variables**, because it
is applied after them. Reserving those four names was considered and rejected: it
is a rule to document and enforce for a case with no real use. A file can
therefore make `%VENEER_PORT%` disagree with the running port.

## Plumbing

- `VeneerConfiguration` gains a file-level `env` field.
- `ResolvedVeneerConfiguration` gains `env`, defaulted to an empty dictionary.
- `AddonContext` gains a matching property.
- `VeneerMenu.BuildAddonContext` populates it from `VeneerConfiguration.Load(Scenario).env`.

That last point adds a fifth `Load` call site, at launch rather than at menu-open.
This is correct rather than merely tolerable: it picks up an edit made after the
menu was opened, consistent with the documented promise that files are re-read
rather than cached.

`Merge` still mutates each addon's `scenario` to push `targetScenario` down per
layer. That is unchanged and remains safe for the same reason — `Load` builds a
fresh object graph per call and nothing caches layers.

## Diagnostics

`LogConfigurationChain` keeps its project-keyed dedupe and loses only the
`(ignoring the sidecar ...)` clause, which describes a behaviour that no longer
exists. It now names up to three files.

**Out of scope:** a diagnostic answering "which file set `TOOLS_ROOT`?". The chain
line names the contributing files, and an unresolved variable expands to itself
visibly. Worth revisiting only if it proves insufficient in practice.

## Removed

- `ConfigCandidates.SupersededSidecar` and `ResolvedVeneerConfiguration.SupersededSidecar`
- the superseding branch and wording in `LogConfigurationChain`
- `ConfigCandidates.ProjectLayer`/`GlobalLayer`, replaced by three slots
- the resolver tests asserting replacement
- the "one slot, two candidates" framing throughout the documentation

## Redefined, not removed

`ConfigurationFilename` returns the sidecar path if it exists, otherwise null —
its meaning before the global-config work. Its current definition, "the effective
project layer", describes a notion that ceases to exist. It is pre-existing public
API with no in-tree caller, so redefining is safer than deleting.

## Compatibility

**No migration path is required.** The replace behaviour exists only on the
unmerged `feature/global-veneer-config` branch and its `legacy_ci` port. It has
never been released, so nobody has run it. This is a design change before release,
not a breaking change.

## Error handling

Nothing new. A malformed `env` block fails `TryParse`, and the existing per-layer
isolation logs it once and skips that layer while the others still apply. A `null`
env reads as absent.

## Testing

The existing seam holds: everything interesting stays in pure functions reachable
without a scenario or a WinForms object.

**Resolver** — three-layer discovery replacing the two-candidate matrix; `env`
merged first-wins per key; per-key rather than per-block override; absent and
empty blocks; a layer contributing `env` but no addons.

**`AddonEnvironment`** — the new step-3 layer; file-level values expanding
`%VENEER_CONFIG_DIR%` and process variables; file-level values *not* resolving
against each other, asserted independent of key order; addon `env` resolving
against file-level; addon `env` winning a conflict; a file-level value overriding
a `VENEER_*` name.

Roughly 20–25 new cases against several deleted, all in the two existing test
files.

## Knock-on work

- Task 10's manual verification script needs its replace-related steps rewritten.
- The `legacy_ci` port must be redone on top of this rather than merged as it
  stands.
- `docs/veneer-file-format.md` and `Samples/addons/README.md` need the two-layer
  and replacement material rewritten, including the sentence "your personal
  entries appear after the project's own", which becomes false for the home
  project file.
