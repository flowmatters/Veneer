import os, sys
import pytest
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import compile_all  # must import with no side effects

def test_module_imports_without_running():
	assert hasattr(compile_all, 'main')
	assert callable(compile_all.main)

def test_valid_version_accepts_in_range():
	assert compile_all.valid_version(['6', '1', '0'])

def test_valid_version_rejects_over_max():
	assert not compile_all.valid_version(['8', '0', '0'])  # major > 7

def test_valid_version_rejects_non_numeric():
	assert not compile_all.valid_version(['Catchments'])

def test_max_version_is_module_level():
	assert compile_all.MAX_VERSION == [7, 99, 4]

def test_parse_version_installed():
	assert compile_all.parse_version_string('Source 6.1.0.12345', 'Source ') == '6.1.0.12345'

def test_parse_version_binaries():
	assert compile_all.parse_version_string('BinSource6.1.0.12345', 'BinSource') == '6.1.0.12345'

def test_parse_version_no_prefix_match_returns_basename():
	assert compile_all.parse_version_string('Source Catchments', 'Source ') == 'Catchments'

def test_unique_versions_collapses_to_num_elements():
	# Deliberately unsorted input to prove the function keeps the lexicographically-last
	# full path per (major.minor.patch), independent of input order.
	dirs = ['/x/Source 6.1.0.222', '/x/Source 5.30.0.1', '/x/Source 6.1.0.111']
	out = compile_all.unique_versions(dirs, 3, 'Source ')
	assert out == ['/x/Source 5.30.0.1', '/x/Source 6.1.0.222']  # .222 > .111 kept

def test_copy_references_min_files_zero_tolerates_empty(tmp_path):
	src = tmp_path / 'empty'; src.mkdir()
	dest = tmp_path / 'dest'
	# Should NOT raise when min_files=0 and no assemblies present
	result = compile_all.copy_references(str(src), str(dest), min_files=0)
	assert result == []

def test_copy_references_default_asserts_on_empty(tmp_path):
	src = tmp_path / 'empty2'; src.mkdir()
	dest = tmp_path / 'dest2'
	with pytest.raises(AssertionError):
		compile_all.copy_references(str(src), str(dest))  # default min_files=1

def test_discover_versions_filters_non_numeric(tmp_path):
	for name in ['Source 6.1.0.12345', 'Source 5.30.0.1', 'Source Catchments',
			'Source 9.0.0.1', 'SourceFoo']:
		(tmp_path / name).mkdir()
	out = [os.path.basename(p) for p in compile_all.discover_versions(str(tmp_path), 'Source ')]
	assert 'Source 6.1.0.12345' in out
	assert 'Source 5.30.0.1' in out
	assert 'Source Catchments' not in out   # non-numeric, filtered by valid_version
	assert 'Source 9.0.0.1' not in out      # major > 7, filtered by valid_version
	assert 'SourceFoo' not in out           # no space -> excluded by the "Source *" glob

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

def test_resolve_in_worktree_joins_and_normalizes():
	out = compile_all.resolve_in_worktree('/wt/master', '../Output')
	assert out == os.path.normpath('/wt/Output')

def test_resolve_in_worktree_relative_inside():
	out = compile_all.resolve_in_worktree('/wt/master', 'References')
	assert out == os.path.normpath('/wt/master/References')

def test_determine_branch_discovered_binsource6_is_corewcf():
	entry = ('/x/BinSource6.20.0.14258', 'BinSource6.20.0.14258', False)
	assert compile_all.determine_branch(entry, (6, 0), 'BinSource') == 'corewcf'

def test_determine_branch_discovered_installed_source6_is_corewcf():
	entry = ('/x/Source 6.1.0.14090', 'Source 6.1.0.14090', False)
	assert compile_all.determine_branch(entry, (6, 0), 'Source ') == 'corewcf'

def test_determine_branch_discovered_source5_is_wcf():
	entry = ('/x/BinSource5.30.0.12680', 'BinSource5.30.0.12680', False)
	assert compile_all.determine_branch(entry, (6, 0), 'BinSource') == 'wcf'

def test_determine_branch_custom_entry_uses_effective_version():
	# custom entry: (path, shortname, effective_version_string)
	entry = ('/x/whatever', 'Veneer_for_Source_6.1.0', '6.1.0')
	assert compile_all.determine_branch(entry, (6, 0), 'BinSource') == 'corewcf'

def test_group_versions_by_branch_threads_prefix():
	vi = [('/x/BinSource6.20.0.1', 'BinSource6.20.0.1', False),
		('/x/BinSource5.30.0.1', 'BinSource5.30.0.1', False)]
	groups = compile_all.group_versions_by_branch(vi, (6, 0), 'BinSource')
	assert [e[1] for e in groups['corewcf']] == ['BinSource6.20.0.1']
	assert [e[1] for e in groups['wcf']] == ['BinSource5.30.0.1']

def test_extract_build_errors_picks_diagnostics():
	lines = [
		"Determining projects to restore...",
		r"C:\a\1\s\Veneer\Foo.csproj : warning NU1701: blah blah",
		r"CSC : error CS9057: The analyzer assembly 'X.dll' references version '4.12.0.0' [Foo.csproj]",
		"    1 Error(s)",
		"Build FAILED.",
	]
	out = compile_all.extract_build_errors(lines)
	assert any('CS9057' in l for l in out)        # the real error is captured
	assert all('NU1701' not in l for l in out)    # warnings are excluded
	assert all('Error(s)' not in l for l in out)  # the count summary is not an error line

def test_extract_build_errors_dedupes_and_preserves_order():
	lines = ["X.csproj : error CS1: msg", "X.csproj : error CS1: msg", "Y.csproj : error CS2: other"]
	out = compile_all.extract_build_errors(lines)
	assert out == ["X.csproj : error CS1: msg", "Y.csproj : error CS2: other"]

def test_extract_build_errors_caps_at_max_lines():
	lines = ["F.csproj : error CS%d: m" % i for i in range(50)]
	out = compile_all.extract_build_errors(lines, max_lines=10)
	assert len(out) == 10

def test_extract_build_errors_fallback_to_tail_when_no_diagnostics():
	lines = ["configuring", "", "Segmentation fault", "make: *** [all] Error 1"]
	out = compile_all.extract_build_errors(lines, max_lines=2)
	# no coded diagnostics -> last 2 non-empty lines
	assert out == ["Segmentation fault", "make: *** [all] Error 1"]
