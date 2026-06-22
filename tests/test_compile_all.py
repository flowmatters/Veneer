import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import compile_all  # must import with no side effects

def test_module_imports_without_running():
	assert hasattr(compile_all, 'main')
	assert callable(compile_all.main)
