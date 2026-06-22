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
