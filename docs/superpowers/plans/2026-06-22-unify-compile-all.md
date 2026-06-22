# Unify `compile_all.py` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the diverged upstream/FIRM-fork copies of `compile_all.py` with a single worktree-aware script (owned by Veneer `master`) that builds both the WCF (`legacy_ci`) and CoreWCF (`master`) stacks against any Source version, supports both installed and binaries-repo reference layouts via neutral flags, and enables the FIRM pipeline to build Source 6.

**Architecture:** Transform the existing upstream `compile_all.py` in place. Refactor its top-level script body into testable pure functions plus a thin `main()`. Add a layout abstraction (prefix-strip version parsing + reference subdir). Replace the in-place git stash/checkout/patch branch-switching subsystem with worktree-based orchestration: reuse existing worktrees and build them in place; create a throwaway worktree (with best-effort dirty-diff carry) only as a fallback. Keep the existing CoreWCF helpers (`build_command`, `ensure_stub_build_imports`, `flatten_subdirectory`, size-aware reference filtering) verbatim.

**Tech Stack:** Python 3 (stdlib only for the script: `argparse`, `subprocess`, `glob`, `shutil`, `logging`), `pytest` for unit tests (new dev dependency), MSBuild + `dotnet` build (invoked, not tested), git worktrees.

**Spec:** `docs/superpowers/specs/2026-06-22-unify-compile-all-design.md`

**Repos touched:**
- `C:\src\projects\Veneer` (point of truth — script + tests + this plan). Commit on `master`.
- `C:\src\projects\mdba-firm\FIRM_Veneer_Builds` (private CI — `azure-pipelines.yml`, and retire the fork). Separate repo, separate commits.

**Conventions:**
- The existing `compile_all.py` uses **TAB indentation** — all edits must use tabs, not spaces.
- Use the `logging` logger (`logger.info(...)`), not `print`, throughout the unified script (port from the fork).
- Pure functions go above `main()`; the module must be importable without side effects (no argparse/build at import time) so tests can import it.

---

## File Structure

- **Modify:** `C:\src\projects\Veneer\compile_all.py` — the unified script. Currently has branch-switching + CoreWCF; we refactor it for testability, add the layout abstraction, and swap the branch-switch subsystem for worktrees.
- **Create:** `C:\src\projects\Veneer\tests\test_compile_all.py` — pytest unit tests for the pure functions.
- **Create:** `C:\src\projects\Veneer\tests\__init__.py` — empty (makes import path predictable).
- **Modify (other repo):** `C:\src\projects\mdba-firm\FIRM_Veneer_Builds\azure-pipelines.yml` — checkout `master`, drop the copy step, add layout + `--dotnet` flags, keep `--refpath ../Output`.
- **Delete (other repo, final cleanup):** `C:\src\projects\mdba-firm\FIRM_Veneer_Builds\compile_all.py` — the retired fork. Update that repo's `CLAUDE.md` to point at the Veneer-owned script.

---

## Task 1: Make the module importable + set up pytest

Refactor so importing `compile_all` runs no argparse and no build. Everything from the current `parser = argparse...` line down to the results summary moves into a `main()` function called under `if __name__ == '__main__':`. This unblocks unit testing of every helper.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Create: `C:\src\projects\Veneer\tests\__init__.py`
- Create: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Add logging setup near the top of `compile_all.py`** (after imports, before the helpers), porting the fork's logger:

```python
import logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)
```

- [ ] **Step 2: Wrap the script body in `main()`**. Move the entire block starting at `parser = argparse.ArgumentParser(...)` through the final results-summary `print`/`open(LAST_FAILS_FN,...)` into `def main():`. Add at end of file:

```python
if __name__ == '__main__':
	main()
```

Leave all module-level helper `def`s and constants where they are (above `main`). Do not change logic yet — this is a pure move. (Per-`print`→`logger.info` conversion happens opportunistically as you touch lines in later tasks; bulk-converting here is optional but keep it mechanical.)

- [ ] **Step 3: Create `tests/__init__.py`** (empty file).

- [ ] **Step 4: Create `tests/test_compile_all.py` with an import smoke test:**

```python
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import compile_all  # must import with no side effects

def test_module_imports_without_running():
	assert hasattr(compile_all, 'main')
	assert callable(compile_all.main)
```

- [ ] **Step 5: Run the test — verify it passes (proves no import-time side effects):**

Run: `cd C:/src/projects/Veneer && python -m pytest tests/test_compile_all.py -v`
Expected: PASS. If it errors with argparse usage/exit or attempts a build, the move in Step 2 is incomplete.

- [ ] **Step 6: Commit:**

```bash
git add compile_all.py tests/__init__.py tests/test_compile_all.py
git commit -m "refactor: make compile_all importable + add pytest harness"
```

---

## Task 2: `MAX_VERSION` hoist + `valid_version` with non-numeric guard

Hoist `MAX_VERSION` to module scope as `[7, 99, 4]` (superset; see spec §6) and add `valid_version()` (ported from fork) that tolerates non-numeric components — needed because the new prefix-strip discovery (Task 3) can surface e.g. `"Catchments"`.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing tests:**

```python
def test_valid_version_accepts_in_range():
	assert compile_all.valid_version(['6', '1', '0'])

def test_valid_version_rejects_over_max():
	assert not compile_all.valid_version(['8', '0', '0'])  # major > 7

def test_valid_version_rejects_non_numeric():
	assert not compile_all.valid_version(['Catchments'])

def test_max_version_is_module_level():
	assert compile_all.MAX_VERSION == [7, 99, 4]
```

- [ ] **Step 2: Run — verify failure** (`AttributeError: valid_version` / wrong MAX_VERSION):

Run: `python -m pytest tests/test_compile_all.py -k "valid_version or max_version" -v`
Expected: FAIL.

- [ ] **Step 3: Implement.** Add module-level constant and function (tabs!):

```python
MAX_VERSION = [7, 99, 4]

def valid_version(components):
	for comp, max_v in zip(components[:len(MAX_VERSION)], MAX_VERSION):
		try:
			comp = int(comp)
		except (ValueError, TypeError):
			logger.info('%s is not a valid Source version number.' % '.'.join(map(str, components)))
			return False
		if comp > max_v:
			logger.info('%s exceeds MAX_VERSION.' % '.'.join(map(str, components)))
			return False
	return True
```

Remove the function-local `MAX_VERSION = [7, 50, 4]` currently inside `main()`'s build loop; the loop now reads the module-level constant.

- [ ] **Step 4: Run — verify pass.** Run: `python -m pytest tests/test_compile_all.py -k "valid_version or max_version" -v` → PASS.

- [ ] **Step 5: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: hoist MAX_VERSION to [7,99,4] + non-numeric-tolerant valid_version"
```

---

## Task 3: Layout abstraction — prefix-strip version parsing

Introduce `parse_version_string()` (single prefix-strip model replacing upstream's `split(' ')[1]` and the fork's `split('BinSource')[1]`) and rework `unique_versions()` to use it. Add the `--source-dir-prefix` flag.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing tests:**

```python
def test_parse_version_installed():
	assert compile_all.parse_version_string('Source 6.1.0.12345', 'Source ') == '6.1.0.12345'

def test_parse_version_binaries():
	assert compile_all.parse_version_string('BinSource6.1.0.12345', 'BinSource') == '6.1.0.12345'

def test_parse_version_no_prefix_match_returns_basename():
	assert compile_all.parse_version_string('Source Catchments', 'Source ') == 'Catchments'

def test_unique_versions_collapses_to_num_elements():
	dirs = ['/x/Source 6.1.0.111', '/x/Source 6.1.0.222', '/x/Source 5.30.0.1']
	out = compile_all.unique_versions(dirs, 3, 'Source ')
	# one entry per (major.minor.patch); lexicographically-last full path kept
	assert out == ['/x/Source 5.30.0.1', '/x/Source 6.1.0.222']
```

- [ ] **Step 2: Run — verify failure.** Run: `python -m pytest tests/test_compile_all.py -k "parse_version or unique_versions" -v` → FAIL.

- [ ] **Step 3: Implement:**

```python
def parse_version_string(basename, prefix):
	"""Strip `prefix` from a version-directory basename to get the version string.
	'Source 6.1.0' + 'Source ' -> '6.1.0'; 'BinSource6.1.0' + 'BinSource' -> '6.1.0'.
	Returns the basename unchanged if the prefix is absent."""
	if basename.startswith(prefix):
		return basename[len(prefix):]
	return basename
```

Rewrite `unique_versions` to take the prefix:

```python
def unique_versions(all_versions, num_elements, prefix):
	uniq_versions = {}
	for v in all_versions:
		v_num = parse_version_string(os.path.basename(v), prefix)
		v_num = '.'.join(v_num.split('.')[:num_elements])
		uniq_versions[v_num] = v
	return sorted(uniq_versions.values())
```

- [ ] **Step 4: Add the `--source-dir-prefix` argparse flag in `main()`** (near the other path flags):

```python
parser.add_argument('--source-dir-prefix', default='Source ',
	help='Prefix of version directories under --ewater (also used to derive the version string and .ignore globs). Default matches installed Source; pass e.g. "BinSource" for a binaries-repo layout.')
```

- [ ] **Step 5: Update `main()` call sites** to pass `args.source_dir_prefix` into `unique_versions(...)`. (Discovery/ignore globs are updated in Task 5; the build-loop version parse in Task 4.)

- [ ] **Step 6: Run — verify pass.** Run: `python -m pytest tests/test_compile_all.py -k "parse_version or unique_versions" -v` → PASS.

- [ ] **Step 7: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: prefix-strip version parsing via --source-dir-prefix"
```

---

## Task 4: Reference subdir + `min_files`; use prefix in build-loop version parse

Add `--reference-subdir` and port the fork's `min_files` parameter on `copy_references`. Update the build loop's version parse to use `parse_version_string`.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing test for `copy_references` min_files** (use `tmp_path`):

```python
def test_copy_references_min_files_zero_tolerates_empty(tmp_path):
	src = tmp_path / 'empty'; src.mkdir()
	dest = tmp_path / 'dest'
	# Should NOT raise when min_files=0 and no assemblies present
	result = compile_all.copy_references(str(src), str(dest), min_files=0)
	assert result == []

def test_copy_references_default_asserts_on_empty(tmp_path):
	src = tmp_path / 'empty2'; src.mkdir()
	dest = tmp_path / 'dest2'
	import pytest
	with pytest.raises(AssertionError):
		compile_all.copy_references(str(src), str(dest))  # default min_files=1
```

- [ ] **Step 2: Run — verify failure** (current `copy_references` has no `min_files`): `python -m pytest tests/test_compile_all.py -k copy_references -v` → FAIL.

- [ ] **Step 3: Implement `copy_references` with `min_files`** (port from fork):

```python
def copy_references(source, dest, min_files=1):
	logger.info('Copying references from %s to %s' % (source, dest))
	if not os.path.exists(dest):
		os.mkdir(dest)
	assemblies = glob(source + os.path.sep + "*.exe") + glob(source + os.path.sep + "*.dll")
	assert len(assemblies) >= min_files, f'Expected at least {min_files} binaries in {source}'
	for a in assemblies:
		copyfile(a, dest + os.path.sep + os.path.basename(a))
	return [os.path.basename(a) for a in assemblies]
```

- [ ] **Step 4: Add `--reference-subdir` flag in `main()`:**

```python
parser.add_argument('--reference-subdir', default='',
	help='Subdirectory inside each version dir holding the main Source DLLs. Empty = flat (installed layout); pass e.g. "Source" for a binaries-repo layout.')
```

- [ ] **Step 5: Update the main-references copy in the build loop** to honour the subdir, and Plugins to use `min_files=0`:

```python
main_ref_dir = os.path.join(fullpath, args.reference_subdir) if args.reference_subdir else fullpath
references = copy_references(main_ref_dir, effective_refpath)
references += copy_references(os.path.join(fullpath, 'Plugins'), os.path.join(effective_refpath, 'Plugins'), min_files=0)
```

(`effective_refpath` is introduced in Task 7; until then use `args.refpath`.)

- [ ] **Step 6: Update the build-loop version-component parse** from `version.split(' ')[1].split('.')` to:

```python
version_components = parse_version_string(version, args.source_dir_prefix).split('.')
```

- [ ] **Step 7: Run — verify pass.** Run: `python -m pytest tests/test_compile_all.py -k copy_references -v` → PASS.

- [ ] **Step 8: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: --reference-subdir + copy_references min_files; prefix-based loop parse"
```

---

## Task 5: Discovery + `.ignore` globs use the prefix

Update version discovery and `.ignore` handling in `main()` to build globs from `--source-dir-prefix`, and filter via `valid_version()`.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Extract a pure helper for the discovery glob + filter, and test it:**

```python
def discover_versions(ewater, prefix):
	"""Return sorted version directories under `ewater` matching `<prefix>*`,
	filtered to plausible Source version numbers."""
	candidates = sorted(glob(os.path.join(ewater, prefix + '*')))
	return [v for v in candidates
		if valid_version(parse_version_string(os.path.basename(v), prefix).split('.'))]
```

Test (with `tmp_path` creating fake dirs):

```python
def test_discover_versions_filters_non_numeric(tmp_path):
	for name in ['Source 6.1.0.12345', 'Source 5.30.0.1', 'Source Catchments', 'Source 9.0.0.1']:
		(tmp_path / name).mkdir()
	out = [os.path.basename(p) for p in compile_all.discover_versions(str(tmp_path), 'Source ')]
	assert 'Source 6.1.0.12345' in out
	assert 'Source 5.30.0.1' in out
	assert 'Source Catchments' not in out   # non-numeric, filtered
	assert 'Source 9.0.0.1' not in out      # major > 7, filtered
```

- [ ] **Step 2: Run — verify failure**, then implement `discover_versions`, then verify pass.

Run: `python -m pytest tests/test_compile_all.py -k discover_versions -v`

- [ ] **Step 3: Wire `discover_versions` into `main()`**, replacing the current `glob(... "Source*")` (and the fork's `"BinSource[1-9]*"`) discovery line.

- [ ] **Step 4: Update `.ignore` glob construction in `main()`** to use the prefix:

```python
ignored += glob(os.path.join(args.ewater, args.source_dir_prefix + ip.strip()))
```

- [ ] **Step 5: Confirm `.include` parsing keeps the comment-tolerant form** (upstream's): `if len(l) and not l.startswith('#')`. (No change if already present; this is the superset behavior per spec.)

- [ ] **Step 6: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: prefix-based discovery + ignore globs with valid_version filter"
```

---

## Task 6: Parse `git worktree list --porcelain`

Pure parser mapping branch name → worktree path, skipping detached-HEAD worktrees.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing test** (sample mirrors the real format observed in this repo):

```python
def test_parse_worktree_list_maps_branch_to_path():
	sample = (
		"worktree C:/src/projects/Veneer\n"
		"HEAD c54f6d4\n"
		"branch refs/heads/master\n"
		"\n"
		"worktree C:/src/projects/Veneer-legacy\n"
		"HEAD fbae695\n"
		"branch refs/heads/legacy_ci\n"
		"\n"
	)
	out = compile_all.parse_worktree_list(sample)
	assert out == {'master': 'C:/src/projects/Veneer',
		'legacy_ci': 'C:/src/projects/Veneer-legacy'}

def test_parse_worktree_list_skips_detached():
	sample = (
		"worktree /tmp/wt\n"
		"HEAD abc123\n"
		"detached\n"
		"\n"
	)
	assert compile_all.parse_worktree_list(sample) == {}
```

- [ ] **Step 2: Run — verify failure**, then implement:

```python
def parse_worktree_list(porcelain_output):
	"""Parse `git worktree list --porcelain` into {short_branch_name: worktree_path}.
	Worktrees in detached-HEAD state (no `branch` line) are omitted."""
	worktrees = {}
	path = None
	for line in porcelain_output.splitlines():
		if line.startswith('worktree '):
			path = line[len('worktree '):].strip()
		elif line.startswith('branch ') and path is not None:
			ref = line[len('branch '):].strip()      # e.g. refs/heads/legacy_ci
			worktrees[ref.rsplit('/', 1)[-1]] = path
		elif line.strip() == '':
			path = None
	return worktrees

def list_worktrees():
	result = subprocess.run(['git', 'worktree', 'list', '--porcelain'],
		capture_output=True, text=True, check=True)
	return parse_worktree_list(result.stdout)
```

- [ ] **Step 3: Run — verify pass.** Run: `python -m pytest tests/test_compile_all.py -k worktree_list -v` → PASS.

- [ ] **Step 4: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: parse git worktree list --porcelain into branch->path map"
```

---

## Task 7: Compute the build plan (pure orchestration logic)

The decision layer: given branch groups + the worktree map, produce an ordered list of build groups, each marked reuse-existing-worktree or create-temp. This is the testable core of the new orchestration.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing tests:**

```python
def _groups(wcf, corewcf):
	# minimal version_info entries: (fullpath, version, custom)
	mk = lambda n: ('/x/%s' % n, n, False)
	return {'wcf': [mk(v) for v in wcf], 'corewcf': [mk(v) for v in corewcf]}

def test_plan_reuses_existing_worktrees():
	groups = _groups(['Source 5.30.0.1'], ['Source 6.1.0.1'])
	branch_names = {'wcf': 'legacy_ci', 'corewcf': 'master'}
	worktrees = {'master': '/wt/master', 'legacy_ci': '/wt/legacy'}
	plan = compile_all.compute_build_plan(groups, branch_names, worktrees, current_branch='master')
	by_key = {g['branch_key']: g for g in plan}
	assert by_key['corewcf']['worktree_path'] == '/wt/master' and not by_key['corewcf']['is_temp']
	assert by_key['wcf']['worktree_path'] == '/wt/legacy' and not by_key['wcf']['is_temp']
	# current branch group ordered first
	assert plan[0]['branch_key'] == 'corewcf'

def test_plan_marks_missing_worktree_temp():
	groups = _groups(['Source 5.30.0.1'], ['Source 6.1.0.1'])
	branch_names = {'wcf': 'legacy_ci', 'corewcf': 'master'}
	worktrees = {'master': '/wt/master'}  # no legacy_ci worktree
	plan = compile_all.compute_build_plan(groups, branch_names, worktrees, current_branch='master')
	wcf = [g for g in plan if g['branch_key'] == 'wcf'][0]
	assert wcf['worktree_path'] is None and wcf['is_temp']

def test_plan_omits_empty_groups():
	groups = _groups([], ['Source 6.1.0.1'])
	plan = compile_all.compute_build_plan(groups, {'wcf': 'legacy_ci', 'corewcf': 'master'},
		{'master': '/wt/master'}, current_branch='master')
	assert [g['branch_key'] for g in plan] == ['corewcf']
```

- [ ] **Step 2: Run — verify failure**, then implement:

```python
def compute_build_plan(branch_groups, branch_names, worktrees, current_branch):
	"""Return an ordered list of build-group dicts:
	{branch_key, target_branch, worktree_path (None => create temp), is_temp, versions}.
	Existing worktrees are reused (built in place); missing ones are flagged for
	temp creation. The current branch's group is ordered first."""
	plan = []
	for branch_key in ('wcf', 'corewcf'):
		versions = branch_groups.get(branch_key) or []
		if not versions:
			continue
		target_branch = branch_names[branch_key]
		wt = worktrees.get(target_branch)
		plan.append({
			'branch_key': branch_key,
			'target_branch': target_branch,
			'worktree_path': wt,
			'is_temp': wt is None,
			'versions': versions,
		})
	plan.sort(key=lambda g: 0 if g['target_branch'] == current_branch else 1)
	return plan
```

- [ ] **Step 3: Run — verify pass.** Run: `python -m pytest tests/test_compile_all.py -k build_plan -v` → PASS.

- [ ] **Step 4: Commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: compute_build_plan (reuse-before-create worktree decisions)"
```

---

## Task 8: Per-worktree path resolution helper

Tiny pure helper so `--refpath`/`--source`/solution resolve relative to each worktree root (spec §4).

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`
- Test: `C:\src\projects\Veneer\tests\test_compile_all.py`

- [ ] **Step 1: Write failing test:**

```python
def test_resolve_in_worktree_joins_and_normalizes():
	out = compile_all.resolve_in_worktree('/wt/master', '../Output')
	assert out == os.path.normpath('/wt/Output')

def test_resolve_in_worktree_relative_inside():
	out = compile_all.resolve_in_worktree('/wt/master', 'References')
	assert out == os.path.normpath('/wt/master/References')
```

- [ ] **Step 2: Run — verify failure**, then implement:

```python
def resolve_in_worktree(worktree_path, relpath):
	"""Resolve a build path (refpath/source/solution) relative to a worktree root."""
	return os.path.normpath(os.path.join(worktree_path, relpath))
```

- [ ] **Step 3: Run — verify pass; commit:**

```bash
git add compile_all.py tests/test_compile_all.py
git commit -m "feat: resolve_in_worktree path helper"
```

---

## Task 9: Worktree execution + remove the stash/checkout/patch subsystem

The integration task: rewrite `main()`'s orchestration to consume `compute_build_plan`, build each group in its worktree (reused or temp), and delete the old in-place branch-switching machinery. Add `--dry-run` so the plan can be inspected without builds.

**Files:**
- Modify: `C:\src\projects\Veneer\compile_all.py`

- [ ] **Step 1: Add flags** (`--dotnet` and `--corewcf-min-version` already exist; keep them). Add:

```python
parser.add_argument('--dry-run', action='store_true', default=False,
	help='Print the resolved build plan (branch groups, worktrees, commands) and exit without building.')
parser.add_argument('--keep-temp-worktrees', action='store_true', default=False,
	help='Do not remove temporary worktrees after building (debugging).')
```

Remove the `--no-branch-switch`, `--wcf-branch`/`--corewcf-branch` defaults stay; the branch names feed `branch_names`.

- [ ] **Step 2: Delete the obsolete git helpers and their call sites:** `get_current_branch` stays (still needed for ordering); **remove** `has_tracked_changes` (replaced), `git_stash_save`, `git_stash_pop`, `git_checkout`, `revert_tracked_changes`, the in-place `compile_order`/`need_switch`/stash/patch block, and the `finally:` branch-restore block. Keep `create_patch`, `save_patch_to_temp`, `apply_patch`, `clean_reject_files` — reused by the temp-worktree dirty-carry below (move them near it).

- [ ] **Step 3: Implement the new orchestration in `main()`** (sketch; integrate with existing per-version build/harvest body, which moves into `build_version(...)`):

```python
worktrees = list_worktrees()
current_branch = get_current_branch()
branch_names = {'wcf': args.wcf_branch, 'corewcf': args.corewcf_branch}
branch_groups = group_versions_by_branch(version_info, corewcf_min_version)
plan = compute_build_plan(branch_groups, branch_names, worktrees, current_branch)

# log the plan
for g in plan:
	loc = g['worktree_path'] or '(temp worktree to be created)'
	logger.info('Group %s -> branch %s in %s: %d version(s)' % (
		g['branch_key'].upper(), g['target_branch'], loc, len(g['versions'])))
if args.dry_run:
	logger.info('--dry-run: not building.')
	return

destination = os.path.abspath(args.destination)   # shared harvest tree
results = {}
for g in plan:
	wt = g['worktree_path']
	created_temp = False
	if g['is_temp']:
		wt = make_temp_worktree(g['target_branch'])   # git worktree add <tmp> origin/<branch>
		created_temp = True
		carry_dirty_changes(wt)                        # best-effort; no-op if clean
	try:
		solution = resolve_in_worktree(wt, args.solution)
		effective_refpath = resolve_in_worktree(wt, args.refpath)
		effective_source = resolve_in_worktree(wt, args.source)
		if g['branch_key'] == 'corewcf':
			ensure_stub_build_imports(solution)
			for pkgs in glob(os.path.join(wt, '*', 'Packages')):
				if os.path.isdir(pkgs): rmtree(pkgs)
		for (fullpath, version, custom) in g['versions']:
			results[version] = build_version(
				g['branch_key'], fullpath, version, custom,
				solution, effective_refpath, effective_source, destination, wt, args)
			if results[version] != 0 and args.fail:
				break
	finally:
		if created_temp and not args.keep_temp_worktrees:
			subprocess.run(['git', 'worktree', 'remove', '--force', wt])
```

- [ ] **Step 4: Implement `make_temp_worktree` and `carry_dirty_changes`:**

```python
def make_temp_worktree(branch):
	tmp = tempfile.mkdtemp(prefix='compile_all_wt_')
	# Prefer the remote ref so a fresh CI clone (only local `master`) still resolves it.
	ref = branch
	if subprocess.run(['git', 'rev-parse', '--verify', '--quiet', 'origin/' + branch],
			capture_output=True).returncode == 0:
		ref = 'origin/' + branch
	res = subprocess.run(['git', 'worktree', 'add', '--detach', tmp, ref],
		capture_output=True, text=True)
	if res.returncode != 0:
		raise RuntimeError('git worktree add failed for %s: %s' % (ref, res.stderr))
	logger.info('Created temp worktree %s at %s' % (tmp, ref))
	return tmp

def carry_dirty_changes(worktree):
	patch = create_patch()            # git diff HEAD of the current (orchestrator) tree
	if not patch:
		return
	patch_path = save_patch_to_temp(patch)
	res = subprocess.run(['git', '-C', worktree, 'apply', '--reject', '--whitespace=nowarn', patch_path],
		capture_output=True, text=True)
	if res.returncode != 0:
		logger.warning('Dirty changes did not apply cleanly to temp worktree (continuing): %s' % res.stderr)
	os.remove(patch_path)
```

- [ ] **Step 5: Extract `build_version(...)`** from the existing per-version loop body — the references staging, DefineConstants flag computation, `build_command` invocation (`cwd=worktree`), branch-aware harvesting (WCF basename filter vs CoreWCF size-aware + `flatten_subdirectory`), and `--keep`/`--copy_to_source`. Return the build returncode. Use `effective_refpath`/`effective_source`/`destination` (absolute) instead of `args.refpath`/`args.source`/`args.destination`. Pass `cwd=wt` to `subprocess.run` for the build command. **No logic change** beyond the path/cwd parameterization.

- [ ] **Step 6: Smoke-test `--dry-run` against the real local worktrees** (no build, no binaries needed beyond a directory to discover — point `--ewater` at any dir, or use an `.ignore` to empty the set):

Run: `cd C:/src/projects/Veneer && python compile_all.py --dry-run --ewater C:/src/projects/Veneer/_nonexistent Veneer.sln`
Expected: logs an empty plan and "not building" without error, AND logs the two discovered worktrees (master, legacy_ci) via `list_worktrees`. Confirms orchestration wiring + worktree discovery work end-to-end against the real repo state.

- [ ] **Step 7: Run the full unit suite to confirm no regressions:**

Run: `python -m pytest tests/ -v` → all PASS.

- [ ] **Step 8: Commit:**

```bash
git add compile_all.py
git commit -m "feat: worktree-based orchestration; remove in-place stash/checkout subsystem"
```

---

## Task 10: Local end-to-end verification (real build)

Validate against real binaries + toolchains. Requires: `FIRM_ModelBinaries` checked out locally (binaries layout), MSBuild, and the .NET 8 SDK (`dotnet`). If any are unavailable locally, mark this task BLOCKED and defer to the CI run in Task 11.

**Files:** none (verification only).

- [ ] **Step 1: Installed-layout dry run** (uses defaults). Run from the `master` worktree:

`python compile_all.py --dry-run -e "C:\Program Files\eWater" Veneer.sln`
Expected: discovers installed `Source X.Y.Z` dirs, groups <6.0 → WCF / ≥6.0 → CoreWCF, reuses the `master` worktree, flags `legacy_ci` as reuse (it exists) — plan looks correct.

- [ ] **Step 2: Binaries-layout dry run** (FIRM flags):

`python compile_all.py --dry-run --source-dir-prefix BinSource --reference-subdir Source -e <path-to>/FIRM_ModelBinaries/Binaries --refpath ../Output Veneer.sln`
Expected: discovers `BinSourceX.Y.Z`, correct branch grouping incl. any Source 6, correct worktree assignment.

- [ ] **Step 3: Real build of one WCF + one CoreWCF version** (drop `--dry-run`; use a tight `.ignore` so only ~2 versions build). Confirm: WCF builds via MSBuild in the `legacy_ci` worktree, CoreWCF via `dotnet` in `master`, outputs harvested under `Compiled/<version>/`, CoreWCF `Veneer/` subdir flattened, no Source reference DLLs leaked into output (size-aware filter).

- [ ] **Step 4: Verify the dev's working trees are untouched** after the run: `git -C C:/src/projects/Veneer status` and `git -C C:/src/projects/Veneer-legacy status` show no unexpected branch change or leftover `.rej`/patch artifacts. (No commit — verification only. Record results in the PR/commit message.)

---

## Task 11: Update the FIRM CI pipeline (separate repo)

**Files:**
- Modify: `C:\src\projects\mdba-firm\FIRM_Veneer_Builds\azure-pipelines.yml`

- [ ] **Step 1: Change the Veneer checkout branch** in the `clone_veneer` step from `git checkout legacy_ci` to `git checkout master`. Keep the full `git clone` (no `fetchDepth`).

- [ ] **Step 2: Remove** the line `copy FIRM_Veneer_Builds\compile_all.py Veneer` from the build script step.

- [ ] **Step 3: Update both `python compile_all.py ...` invocations** to:

```
python compile_all.py --refpath ../Output --source-dir-prefix BinSource --reference-subdir Source --ewater ../FIRM_ModelBinaries/Binaries --msbuild %msbuildpath% --dotnet dotnet Veneer.sln
```

(Keep the double invocation for retry-failures-first.)

- [ ] **Step 4: Ensure `dotnet` (.NET 8 SDK) is available on the agent.** If not pre-installed on `mdiras-azdo-windows2022-prd`, add a `UseDotNet@2` task (`version: '8.x'`) before the build step. Note in the PR that this is an agent prerequisite for Source 6.

- [ ] **Step 5: Commit (in the FIRM repo):**

```bash
cd C:/src/projects/mdba-firm/FIRM_Veneer_Builds
git add azure-pipelines.yml
git commit -m "ci: use Veneer-owned compile_all.py on master; add binaries-layout + dotnet flags"
```

---

## Task 12: Retire the fork + update FIRM CLAUDE.md (separate repo)

Do this only **after** the pipeline (Task 11) is confirmed green, so a rollback is trivial.

**Files:**
- Delete: `C:\src\projects\mdba-firm\FIRM_Veneer_Builds\compile_all.py`
- Modify: `C:\src\projects\mdba-firm\FIRM_Veneer_Builds\CLAUDE.md`

- [ ] **Step 1: Delete the fork script.**

- [ ] **Step 2: Update that repo's `CLAUDE.md`** — the "Purpose"/"Architecture" sections currently describe the local `compile_all.py`; rewrite to state the script now lives in the Veneer repo (`master`) and that this repo only carries the pipeline definition + FIRM-specific invocation flags. Remove the now-inaccurate per-feature mechanics that live in the script.

- [ ] **Step 3: Commit (in the FIRM repo):**

```bash
cd C:/src/projects/mdba-firm/FIRM_Veneer_Builds
git add -A
git commit -m "chore: retire forked compile_all.py; script now owned by Veneer repo"
```

---

## Notes for the implementer

- **Tabs, not spaces** — `compile_all.py` uses tab indentation. Mixed indentation will break Python.
- **Verify-during-implementation items from the spec:** (1) `[7,99,4]` covers every `#if BEFORE_V*` used in the Veneer plugin source; (2) `Veneer.sln` csproj HintPaths actually resolve to the staged `--refpath` location under per-worktree resolution; (3) sidecar files (`.ignore`/`.include`/`.refs`) parse under unified discovery.
- **CoreWCF helpers carried over verbatim:** `build_command`, `ensure_stub_build_imports`, `flatten_subdirectory`, `build_reference_sizes`, `is_same_as_reference`, `STUB_*` constants — do not rewrite; only their call sites move into `build_version`.
- **Two repos, two commit streams** — Veneer (script/tests/plan) vs FIRM_Veneer_Builds (CI/fork retirement). Do not cross-commit.
