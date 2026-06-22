# Unify `compile_all.py` — design

**Date:** 2026-06-22
**Status:** Approved design, pre-implementation
**Author:** Joel Rahman (with Claude)

## Problem

Two diverged copies of `compile_all.py` exist:

1. **Upstream Veneer** (`Veneer/compile_all.py`, this repo) — discovers locally **installed** Source
   versions (`C:\Program Files\eWater\Source X.Y.Z`, DLLs flat in the dir), and has the full
   **WCF ↔ CoreWCF branch-switching** machinery needed to build both the legacy stack
   (`legacy_ci` branch, WCF, .NET 4.8, MSBuild) and the modern stack (`master` branch, CoreWCF,
   .NET 8, `dotnet build`).

2. **FIRM fork** (`FIRM_Veneer_Builds/compile_all.py`, a private build-harness repo) — a fork of an
   *older* upstream version. It discovers Source binaries from a checked-out **git repo of model
   binaries** (`BinSourceX.Y.Z` directories, with DLLs under a `Source/` subdir) instead of installed
   versions, and uses `logging`. It is **missing** the branch-switching / CoreWCF / .NET 8 support.

The FIRM pipeline's users now need to support **Source 6**, which requires the CoreWCF/.NET 8 Veneer
(`master` branch) in addition to the legacy WCF builds. The fork can't do this.

## Goal

Produce a **single unified `compile_all.py`**, owned by the Veneer repo (point of truth), that:

- Supports both the WCF (`legacy_ci`) and CoreWCF (`master`) build stacks, selected per Source version.
- Supports both reference layouts — installed Source (default) and the binaries-repo layout
  (via generic command-line overrides) — **without leaking any knowledge of the private
  `FIRM_ModelBinaries` repo into the public Veneer repo.**
- Uses git **worktrees** rather than in-place stash/checkout for cross-branch builds, so the dev's
  working tree is never mutated and the running script is never swapped out mid-run.
- Continues to support compiling **dirty working trees** for local development.

## Non-goals

- Changing the plugin `#if` constant contract (the `V*` / `BEFORE_V*` constants the Veneer source
  relies on) beyond reconciling `MAX_VERSION`.
- Adding the .NET 8 SDK to CI agents or adding Source 6 binaries to `FIRM_ModelBinaries` — these are
  external prerequisites for the Source 6 goal, tracked separately.
- Refactoring unrelated parts of the Veneer build.

## Where the script lives

- Committed to the Veneer repo on **`master` only**. Worktrees mean it never needs to exist on
  `legacy_ci`: the orchestrator runs from the `master` checkout and builds the `legacy_ci` worktree,
  which does not itself invoke the script.
- **Convention: run `compile_all.py` from the `master` worktree.** Its job is inherently cross-branch.
- The FIRM `azure-pipelines.yml` (private) drops its `copy compile_all.py Veneer` step and checks out
  `master` (not `legacy_ci`) to obtain the script.

## Design

### 1. Reference layout — installed by default, neutral overrides

No auto-detection (auto-detecting `BinSource` would bake a private convention into the public repo).
Instead, default to the installed layout and expose two generic flags:

- `--source-dir-prefix` (default `"Source "`): prefix stripped from each version directory's basename
  to derive the version string, and used to build the discovery glob (`<prefix>[1-9]*`) and the
  `.ignore` globs. Installed layout uses the default; the private pipeline passes `"BinSource"`.
- `--reference-subdir` (default `""`): subdirectory inside each version dir holding the main Source
  DLLs. Empty = flat (installed). The private pipeline passes `"Source"`. The plugin references
  subdir stays `<dir>/Plugins` in both layouts (matches current upstream behavior).

Keep `valid_version()` + `MAX_VERSION` filtering (a harmless safety net the fork has and upstream
dropped). Keep `copy_references(..., min_files=0)` for the Plugins dir so layouts without plugins
don't assert-fail.

The public repo thus only knows "you can point me at a differently-named tree whose refs are in a
subdir" — generic and innocuous. All FIRM specifics live in the private pipeline YAML.

### 2. Branch grouping

Reuse upstream's logic: each discovered version is assigned to a branch group by a version threshold.

- `--corewcf-min-version` (default `6.0`): versions `>= 6.0` → CoreWCF group (`master`); below → WCF
  group (`legacy_ci`).
- `--wcf-branch` (default `legacy_ci`), `--corewcf-branch` (default `master`).
- Custom (`.include`) entries without an effective version default to the WCF group.

### 3. Worktree-aware orchestration (reuse-before-create)

At startup, enumerate existing worktrees (`git worktree list --porcelain`) and build a
`branch → worktree path` map. For each non-empty branch group:

1. **Worktree for that branch already exists → build it in place, as-is.** No checkout, no stash, no
   patch. Its own working changes (dirty or clean) are compiled directly. This is the intended dev
   setup: a long-lived `legacy_ci` worktree alongside `master`, each edited and built independently.
2. **No worktree for that branch → create a throwaway one** (`git worktree add <temp> <branch>`),
   build it, then `git worktree remove`. **Only in this fallback** do we optionally carry the current
   tree's uncommitted changes via best-effort `git diff HEAD | git apply --reject` into the temp
   worktree (`.rej` tolerated; cross-branch apply is inherently lossy when files differ between
   branches). CI takes this fallback path naturally (fresh clone on `master`, no `legacy_ci` worktree).

This **eliminates** upstream's `git stash` / in-place `git checkout` / branch-restoration /
`.rej`-cleanup-in-real-tree machinery. The dev's primary working tree is never mutated or checked out,
so an interrupted run cannot strand them on the wrong branch with a half-applied patch.

Build order may prefer the current worktree first, but with worktrees there is no switching cost.

Optional (YAGNI unless requested): `--wcf-worktree` / `--corewcf-worktree` to point at known worktree
dirs explicitly; a flag to keep temp worktrees for debugging.

### 4. Build command (per branch group)

Move the whole script from `os.system` to `subprocess.run` with list args (kills Windows quoting
fragility; required for `dotnet`). DefineConstants joined with `%3B` (escaped `;`) as upstream does.

- **WCF group** → MSBuild (`--msbuild` path), `/p:DefineConstants=...`.
- **CoreWCF group** → `dotnet build` (`--dotnet`, default `dotnet`) with
  `/p:MSBuildWarningsAsMessages=MSB3277`.

CoreWCF builds first run `ensure_stub_build_imports()` (writes empty `.props`/`.targets` stubs so
master's csproj imports resolve without the full eWater RiverSystem repo) and remove legacy
`Packages/` dirs (the SDK auto-globs `*.xaml` and chokes on IronPython package XAML). The upstream
stale-`obj/` cleanup is **dropped** — separate worktrees mean separate `obj/`, so cross-branch target
leakage can't happen.

Each build executes with the worktree root as working directory and the solution path relative to it
(both branches have `Veneer.sln` at the same relative location).

### 5. Output harvesting

Keep upstream's branch-aware harvesting:

- **WCF**: basename-only reference filter (skip output files whose name matches a copied reference).
- **CoreWCF**: size-aware filter (`build_reference_sizes` + `is_same_as_reference`) — the .NET 8 SDK
  copies *all* referenced assemblies into output, so skip files identical to a Source reference (same
  name **and** size) but keep ones where Veneer ships a different version than Source. Then
  `flatten_subdirectory(dest, 'Veneer', ...)` merges the nested `VeneerCmd` output up so the deployed
  layout matches the WCF layout.
- `--keep` and `--copy_to_source` behave as today.
- **`--destination` resolved to an absolute path at startup** (shared harvest tree across all
  worktrees). `--refpath` / `--source` remain per-worktree (transient).

### 6. `MAX_VERSION` reconciliation

Standardize on the superset **`[7, 99, 4]`** (FIRM fork's value; upstream uses `[7, 50, 4]`), hoisted
to a single module-level constant. `MAX_VERSION` only controls how many `BEFORE_V<n>` forward-guard
constants are emitted; the plugin's `#if` blocks must see every constant they reference, and extra
ones are harmless, so the larger bound is the safe choice. **Verify during implementation** against
the actual `#if BEFORE_V*` usages in the Veneer plugin source.

### 7. Logging

Adopt the FIRM fork's `logging` module usage (a recent upstream-of-the-fork improvement) over
`print`, throughout the unified script.

## CI changes (private `FIRM_Veneer_Builds/azure-pipelines.yml`)

- `clone_veneer` step: check out **`master`** instead of `legacy_ci`; keep a full `git clone` so both
  branch refs are present for `git worktree add`.
- **Remove** the `copy FIRM_Veneer_Builds\compile_all.py Veneer` line.
- Invoke with neutral overrides, e.g.:
  `python compile_all.py --source-dir-prefix BinSource --reference-subdir Source --ewater ../FIRM_ModelBinaries/Binaries --msbuild %msbuildpath% --dotnet dotnet Veneer.sln`
- Run twice (retry-failures-first via `_last_fails.txt`) as today.

### External prerequisites (not part of this script)

- .NET 8 SDK available on the `mdiras-azdo-windows2022-prd` agent.
- `BinSource6.x` binaries added to `FIRM_ModelBinaries`.

## Risks / open questions

- **Cross-branch dirty apply is lossy** (fallback path only). Acceptable — matches current upstream
  behavior; the long-lived-worktree workflow avoids it entirely.
- **`MAX_VERSION` set** must cover all `BEFORE_V*` constants referenced by the plugin source — verify.
- **`Veneer.sln` solution structure** must satisfy the two conventions the script assumes (references
  picked up from a writable refs dir, all projects emit to a common output dir) on both branches.
- The `.include` / `.refs` / `.ignore` sidecar-file behavior is preserved; confirm the FIRM pipeline's
  sidecar files (if any) still parse under the unified discovery.
