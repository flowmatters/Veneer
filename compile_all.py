'''
Compile a given solution against ALL installed
versions of Source (aside from any explicitly
ignored)

Pass an sln file (Visual Studio Solution) of plugins for eWater Source to the script and override 
any path parameters as necessary.

Creates a directory tree of the plugins compiled against each version, eg:

Compiled
|
+--- Source 3.5.0.646
+--- Source 3.4.3.540

where each directory contains the compiled output + dependencies - dependencies available from Source itself.

By default, all installed versions of Source will be targeted. To ignore versions that are known to be incompatible,
add a file, named like the solution file, with an additional extension: .ignore. Each line of this file should be a pattern for ignoring Source versions. For example, if your solution is MyPlugins.sln, you could have a file named MyPlugins.sln.ignore with contents like
2*
3.0*
Catchments

to ignore all releases of Source 2, any releases of 3.0 and Source Catchments.

Note: To make use of this script, all projects in the solution need to have the following characteristics:
1: The projects should pick up all Source dependencies (eg TIME.dll, RiverSystem.dll) from a directory that
is writable by you and ISN'T the installed location of Source (which is tied to a particular version).
Eg <solutionpath>\\References

2: The projects should all output to a common directory. If there is just one project, or one project that
depends on your others, then this can be a bin\Debug folder. If there are multiple, independent projects,
then this should be a separate directory, eg: <solutionpath>\Output

For usage information, run with -help:

python compile_all.py -h
'''
import os
import sys
import argparse
import subprocess
import tempfile
import logging
from glob import glob
from shutil import copyfile,rmtree,copytree
from typing import List, Optional, Tuple, Dict

logger = logging.getLogger(__name__)

# Excepts solution in CWD!

def parse_version_string(basename, prefix):
	"""Strip `prefix` from a version-directory basename to get the version string.
	'Source 6.1.0' + 'Source ' -> '6.1.0'; 'BinSource6.1.0' + 'BinSource' -> '6.1.0'.
	Returns the basename unchanged if the prefix is absent."""
	if basename.startswith(prefix):
		return basename[len(prefix):]
	return basename

def discover_versions(ewater, prefix):
	"""Return sorted version directories under `ewater` matching `<prefix>*`,
	filtered to plausible Source version numbers."""
	candidates = sorted(glob(os.path.join(ewater, prefix + '*')))
	return [v for v in candidates
		if valid_version(parse_version_string(os.path.basename(v), prefix).split('.'))]

def unique_versions(all_versions:List[str], num_elements:int, prefix:str) -> List[str]:
	# Iterate sorted so the LAST write per key is the lexicographically-last full
	# path — makes "keep the highest build number" order-independent.
	uniq_versions = {}
	for v in sorted(all_versions):
		v_num = parse_version_string(os.path.basename(v), prefix)
		v_num = '.'.join(v_num.split('.')[:num_elements])
		uniq_versions[v_num] = v
	return sorted(uniq_versions.values())

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

def resolve_in_worktree(worktree_path, relpath):
	"""Resolve a build path (refpath/source/solution) relative to a worktree root."""
	return os.path.normpath(os.path.join(worktree_path, relpath))

LAST_FAILS_FN = '_last_fails.txt'
def clear_directory(folder):
	for the_file in os.listdir(folder):
		file_path = os.path.join(folder, the_file)
		try:
			if os.path.isfile(file_path):
				os.unlink(file_path)
			elif os.path.isdir(file_path): rmtree(file_path)
		except Exception as e:
			print(e)
		
def flatten_subdirectory(parent, subdir_name, ref_sizes=None):
	"""Merge contents of parent/subdir_name/ into parent/, then remove the subdirectory.
	The .NET 8 SDK copies all referenced assemblies into the output, so the subdirectory
	contains Source DLLs that we don't want in the compiled output. We skip files that are
	identical to Source references (same name AND size). Files with the same name but
	different size are kept — Veneer needs a different version than Source ships."""
	subdir_path = os.path.join(parent, subdir_name)
	if not os.path.isdir(subdir_path):
		return
	ref_sizes = ref_sizes or {}
	print('Flattening %s/ into %s' % (subdir_name, parent))
	copied = 0
	skipped = 0
	for item in os.listdir(subdir_path):
		src = os.path.join(subdir_path, item)
		dst = os.path.join(parent, item)
		if not os.path.isdir(src) and is_same_as_reference(src, ref_sizes):
			skipped += 1
			continue
		if os.path.isdir(src):
			if os.path.exists(dst):
				# Merge directory contents, filtering references
				for sub_item in os.listdir(src):
					sub_src = os.path.join(src, sub_item)
					sub_dst = os.path.join(dst, sub_item)
					if not os.path.isdir(sub_src) and is_same_as_reference(sub_src, ref_sizes):
						skipped += 1
						continue
					if os.path.isdir(sub_src):
						if os.path.exists(sub_dst):
							rmtree(sub_dst)
						copytree(sub_src, sub_dst)
					else:
						copyfile(sub_src, sub_dst)
					copied += 1
			else:
				copytree(src, dst)
				copied += 1
		else:
			copyfile(src, dst)
			copied += 1
	print('  Copied %d items, skipped %d Source references' % (copied, skipped))
	rmtree(subdir_path)

def copy_references(source, dest, min_files=1):
	logger.info('Copying references from %s to %s' % (source, dest))
	if not os.path.exists(dest):
		os.mkdir(dest)
	assemblies = glob(source + os.path.sep + "*.exe") + glob(source + os.path.sep + "*.dll")
	assert len(assemblies) >= min_files, f'Expected at least {min_files} binaries in {source}'
	for a in assemblies:
		copyfile(a, dest + os.path.sep + os.path.basename(a))
	return [os.path.basename(a) for a in assemblies]

def build_reference_sizes(ref_basenames, refpath):
	"""Build a dict of {basename: filesize} for Source reference assemblies.
	Used to distinguish identical Source DLLs (skip) from different-version
	NuGet DLLs that happen to share a name (keep)."""
	sizes = {}
	for name in ref_basenames:
		# Check main refpath and Plugins subdir
		for candidate in [os.path.join(refpath, name), os.path.join(refpath, 'Plugins', name)]:
			if os.path.isfile(candidate):
				sizes[name] = os.path.getsize(candidate)
				break
	return sizes

def is_same_as_reference(filepath, ref_sizes):
	"""Return True if the file matches a Source reference (same name and size)."""
	name = os.path.basename(filepath)
	if name not in ref_sizes:
		return False
	return os.path.getsize(filepath) == ref_sizes[name]

def get_custom(include_pattern):
	if len(include_pattern) == 2:
		return (include_pattern[0],include_pattern[1],True)

	return include_pattern[0],include_pattern[1],include_pattern[2]

# --- Version parsing helpers ---

def get_major_minor_version(version_str: str) -> Tuple[int, int]:
	"""Parse a version string like 'Source 6.1.0.12345' or '5.61.0' into (major, minor)."""
	# Strip 'Source ' prefix if present
	v = version_str.strip()
	if v.lower().startswith('source '):
		v = v[len('source '):]
	parts = v.split('.')
	return (int(parts[0]), int(parts[1]))

def determine_branch(version_info_entry: tuple, corewcf_min_version: Tuple[int, int]) -> str:
	"""Return 'wcf' or 'corewcf' based on version threshold.
	version_info_entry is (fullpath, version_name, custom).
	Custom entries without effective_version default to 'wcf'."""
	fullpath, version, custom = version_info_entry
	is_custom = True if hasattr(custom, '__len__') else custom

	effective_version = None
	if is_custom and hasattr(custom, '__len__'):
		effective_version = custom
	elif not is_custom:
		# Standard installed version: parse from version name like 'Source 6.1.0.12345'
		effective_version = version

	if effective_version is None:
		# Custom entry without effective_version — default to WCF
		return 'wcf'

	try:
		major, minor = get_major_minor_version(effective_version)
		if (major, minor) >= corewcf_min_version:
			return 'corewcf'
		else:
			return 'wcf'
	except (ValueError, IndexError):
		return 'wcf'

def group_versions_by_branch(version_info: list, corewcf_min_version: Tuple[int, int]) -> Dict[str, list]:
	"""Group version_info entries into {'wcf': [...], 'corewcf': [...]}."""
	groups: Dict[str, list] = {'wcf': [], 'corewcf': []}
	for entry in version_info:
		branch_key = determine_branch(entry, corewcf_min_version)
		groups[branch_key].append(entry)
	return groups

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

# --- Git operation helpers ---

def get_current_branch() -> str:
	result = subprocess.run(['git', 'rev-parse', '--abbrev-ref', 'HEAD'],
		capture_output=True, text=True, check=True)
	return result.stdout.strip()

def has_tracked_changes() -> bool:
	result = subprocess.run(['git', 'diff', 'HEAD', '--quiet'])
	return result.returncode != 0

def create_patch() -> Optional[str]:
	"""Return git diff HEAD content, or None if no changes."""
	if not has_tracked_changes():
		return None
	result = subprocess.run(['git', 'diff', 'HEAD'], capture_output=True, text=True, check=True)
	return result.stdout if result.stdout.strip() else None

def save_patch_to_temp(content: str) -> str:
	"""Write patch content to a temp file (outside repo). Returns path."""
	fd, path = tempfile.mkstemp(suffix='.patch', prefix='compile_all_')
	with os.fdopen(fd, 'w') as f:
		f.write(content)
	return path

def git_stash_save() -> bool:
	"""Stash tracked and untracked changes. Returns True if something was stashed."""
	result = subprocess.run(['git', 'stash', 'push', '-m', 'compile_all_py_auto_stash'],
		capture_output=True, text=True)
	return 'No local changes' not in result.stdout

def git_stash_pop():
	subprocess.run(['git', 'stash', 'pop'], capture_output=True, text=True)

def git_checkout(branch: str):
	result = subprocess.run(['git', 'checkout', branch], capture_output=True, text=True)
	if result.returncode != 0:
		raise RuntimeError(f'git checkout {branch} failed: {result.stderr}')

def apply_patch(patch_path: str) -> bool:
	"""Apply patch with --reject mode. Returns True if applied cleanly."""
	result = subprocess.run(
		['git', 'apply', '--reject', '--whitespace=nowarn', patch_path],
		capture_output=True, text=True)
	if result.returncode != 0:
		print('WARNING: Patch did not apply cleanly. Some hunks may have been rejected.')
		print(result.stderr)
		return False
	return True

def revert_tracked_changes():
	subprocess.run(['git', 'checkout', '--', '.'], capture_output=True, text=True)

def clean_reject_files():
	"""Walk tree and delete all *.rej files."""
	for root, dirs, files in os.walk('.'):
		for f in files:
			if f.endswith('.rej'):
				try:
					os.remove(os.path.join(root, f))
				except OSError:
					pass

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

# --- Stub build imports for CoreWCF builds ---

STUB_IMPORT_PATHS = [
	'RiverSystem/Solutions/SDK.build.props',
	'RiverSystem/Solutions/SDK.build.targets',
	'RiverSystem/Solutions/NormalisePdbBuildRoot.targets',
]
STUB_CONTENT = '<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">\n</Project>\n'

def ensure_stub_build_imports(solution_path: str):
	"""Create stub .props/.targets files if the eWater RiverSystem repo is not present.
	The master branch csproj files import from $(RootPath)RiverSystem/Solutions/,
	where RootPath is two levels up from the project directory (one above solution dir)."""
	solution_dir = os.path.dirname(os.path.abspath(solution_path))
	root_path = os.path.abspath(os.path.join(solution_dir, '..'))
	for rel_path in STUB_IMPORT_PATHS:
		full_path = os.path.join(root_path, rel_path.replace('/', os.sep))
		if not os.path.exists(full_path):
			os.makedirs(os.path.dirname(full_path), exist_ok=True)
			with open(full_path, 'w') as f:
				f.write(STUB_CONTENT)
			print("  Created stub: %s" % full_path)

# --- Build command helper ---

def build_command(branch_key: str, args, compilation_flags: str) -> list:
	"""Return the build command as a list of arguments for subprocess.run."""
	if branch_key == 'corewcf':
		return [args.dotnet, 'build', compilation_flags,
			'/p:MSBuildWarningsAsMessages=MSB3277', args.solution]
	else:
		msbuild = args.msbuild.strip('"')
		return [msbuild, compilation_flags, args.solution]

def main():
	logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
	parser = argparse.ArgumentParser(formatter_class=argparse.ArgumentDefaultsHelpFormatter)
	parser.description = """\nCompile a Plugin solution against all installed versions of Source
\nCreated by Joel Rahman (joel@flowmatters.com.au).
\nLatest version is at https://gist.github.com/flowmatters/7000491"""

	parser.add_argument('--destination','-d',
		      help="Specify destination directory for compiled assemblies. Sub-directories will be created for each Source version.",
		      default='.\\Compiled')
	parser.add_argument('--source','-s',
			  help="Specify the folder that the Projects, in the Solution will compile to.",
			  default='.\\..\\Output\\Plugins\\CommunityPlugins')
	parser.add_argument('--ewater','-e',
			  help="Specify the base installation folder for all versions of eWater Source",
			  default='C:\\Program Files\\eWater')
	parser.add_argument('--msbuild','-m',
			  help="Specify path to the msbuild.exe",
			  default='"C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MsBuild.exe"')
			  #C:\\Program Files (x86)\\MSBuild\\14.0\\Bin\\MsBuild.exe"')
	parser.add_argument("--refpath","-r",
		      help="Specify path to place references",
		      default="References")
	parser.add_argument('--source-dir-prefix',
		      help='Prefix of version directories under --ewater (also used to derive the version string and .ignore globs). Default matches installed Source; pass e.g. "BinSource" for a binaries-repo layout.',
		      default='Source ')
	parser.add_argument('--reference-subdir', default='',
		help='Subdirectory inside each version dir holding the main Source DLLs. Empty = flat (installed layout); pass e.g. "Source" for a binaries-repo layout.')
	parser.add_argument('--fail','-f',help="Fail fast: Stop after first failure",action='store_true',default=False)
	parser.add_argument('--keep','-k',help='Keep references in output directory',action='store_true',default=False)
	parser.add_argument('--copy_to_source','-c',help='Copy outputs to Source directory WHEN COMPILING AGAINST CUSTOM VERSION OF SOURCE',action='store_true',default=False)
	parser.add_argument('--wcf-branch',help='Git branch for WCF (legacy) builds',default='legacy_ci')
	parser.add_argument('--corewcf-branch',help='Git branch for CoreWCF (.NET 8) builds',default='master')
	parser.add_argument('--corewcf-min-version',help='Minimum Source version for CoreWCF (major.minor)',default='6.0')
	parser.add_argument('--dotnet',help='Path to dotnet CLI for CoreWCF builds',default='dotnet')
	parser.add_argument('--no-branch-switch',help='Disable branch switching (compile everything on current branch)',action='store_true',default=False)
	parser.add_argument('solution',help="Path to Solution (.sln) file")

	args = parser.parse_args()

	# Parse corewcf-min-version into tuple
	corewcf_min_parts = args.corewcf_min_version.split('.')
	corewcf_min_version = (int(corewcf_min_parts[0]), int(corewcf_min_parts[1]))

	if os.path.exists(LAST_FAILS_FN):
		fails_previous_run = open(LAST_FAILS_FN,'r').read().splitlines()
		os.remove(LAST_FAILS_FN)
		print('Will run %d previous failed compiles first'%len(fails_previous_run))
		print(LAST_FAILS_FN)
	else:
		print('No record of previous fails. Will run in order found')
		fails_previous_run = []

	#if 'Veneer' in args.solution:
	#	args.source = '.'+args.source
	print('Expect compiled assemblies in %s (%s)'%(args.source,os.path.abspath(args.source)))
	all_versions = discover_versions(args.ewater, args.source_dir_prefix)
	logger.info("*** FOUND %d INSTALLED VERSIONS OF SOURCE" % (len(all_versions)))
	print("\n".join(all_versions))

	ignore_fn = args.solution + ".ignore"
	ignored = []
	if os.path.exists(ignore_fn):
		ignore_patterns = open(args.solution + ".ignore").readlines()
		for ip in ignore_patterns:
			ignored += glob(os.path.join(args.ewater, args.source_dir_prefix + ip.strip()))
		ignored = sorted(set(ignored))
		print("*** IGNORING %d VERSIONS ***" % (len(ignored)))
		print("\n".join(ignored))

	include_fn = args.solution + ".include"
	include_patterns = []
	if os.path.exists(include_fn):
		include_patterns = [tuple(l.strip().split(',')) for l in open(include_fn).readlines() if len(l) and not l.startswith('#')]
		print("*** INCLUDING %d EXTRA DIRECTORIES ***" % (len(include_patterns)))
		print("\n".join([patt[1] + '('+patt[0]+')' for patt in include_patterns]))
		include_patterns = [get_custom(include_pattern) for include_pattern in include_patterns]

	versions_to_compile = sorted(set(all_versions) - set(ignored))
	versions_to_compile = unique_versions(versions_to_compile, 3, args.source_dir_prefix)

	shortnames = [os.path.basename(v) for v in versions_to_compile]
	version_info = list(zip(versions_to_compile,shortnames,[False]*len(versions_to_compile)))
	version_info += include_patterns

	previous_fails = [v for v in version_info if v[1] in fails_previous_run]
	previous_success = [v for v in version_info if not v[1] in fails_previous_run]
	version_info = previous_fails + previous_success

	print("*** COMPILING AGAINST %d VERSIONS ***" % (len(version_info)))
	print("\n".join([t[1] for t in version_info]))

	refs_fn = args.solution + '.refs'
	extra_refs = []
	if os.path.exists(refs_fn):
		extra_refs = open(refs_fn).readlines()

	# --- Group versions by branch ---
	branch_groups = group_versions_by_branch(version_info, corewcf_min_version)
	branch_names = {
		'wcf': args.wcf_branch,
		'corewcf': args.corewcf_branch,
	}
	print("\n*** BRANCH ASSIGNMENT ***")
	print("  WCF (%s): %d versions" % (args.wcf_branch, len(branch_groups['wcf'])))
	for v in branch_groups['wcf']:
		print("    %s" % v[1])
	print("  CoreWCF (%s): %d versions" % (args.corewcf_branch, len(branch_groups['corewcf'])))
	for v in branch_groups['corewcf']:
		print("    %s" % v[1])

	# Determine compilation order: current branch first to minimize switches
	original_branch = get_current_branch()

	# Determine current branch key
	if original_branch == args.corewcf_branch:
		current_branch_key = 'corewcf'
	else:
		current_branch_key = 'wcf'

	# In no-branch-switch mode, only compile versions appropriate for the current branch
	if args.no_branch_switch:
		skipped_key = 'wcf' if current_branch_key == 'corewcf' else 'corewcf'
		skipped_versions = branch_groups[skipped_key]
		if skipped_versions:
			print("\n*** --no-branch-switch: skipping %d %s versions not appropriate for current branch (%s) ***" % (len(skipped_versions), skipped_key.upper(), original_branch))
			for v in skipped_versions:
				print("    SKIPPED: %s" % v[1])
		compile_order = [(current_branch_key, original_branch, branch_groups[current_branch_key])]
		if not branch_groups[current_branch_key]:
			compile_order = []
		print("\n*** --no-branch-switch: compiling %d versions on current branch (%s) using %s build ***" % (len(branch_groups[current_branch_key]), original_branch, current_branch_key))
	else:
		compile_order = []
		for branch_key in ['wcf', 'corewcf']:
			versions = branch_groups[branch_key]
			if not versions:
				continue
			compile_order.append((branch_key, branch_names[branch_key], versions))
		# Sort so current branch compiles first
		compile_order.sort(key=lambda x: 0 if x[1] == original_branch else 1)

	# Determine if we need to switch branches
	need_switch = not args.no_branch_switch and len(compile_order) > 1

	# Prepare git state (only if switching is needed)
	patch_path = None
	did_stash = False
	if need_switch:
		print("\n*** Preparing git state for branch switching ***")
		patch_content = create_patch()
		if patch_content:
			patch_path = save_patch_to_temp(patch_content)
			print("  Saved uncommitted changes to %s" % patch_path)
		did_stash = git_stash_save()
		if did_stash:
			print("  Stashed working directory changes")

	results = {}
	fail_fast_triggered = False
	current_on_branch = original_branch

	try:
		for (branch_key, target_branch, versions) in compile_order:
			# Determine effective branch key for build command
			if args.no_branch_switch:
				effective_branch_key = current_branch_key
			else:
				effective_branch_key = branch_key

			# Switch branch if needed
			if not args.no_branch_switch and current_on_branch != target_branch:
				print("\n*** Switching to branch '%s' for %s builds ***" % (target_branch, branch_key.upper()))
				git_checkout(target_branch)
				current_on_branch = target_branch
				if patch_path:
					ok = apply_patch(patch_path)
					if ok:
						print("  Applied uncommitted changes patch cleanly")
					else:
						print("  WARNING: Patch applied with rejections (continuing anyway)")

			# Ensure stub build imports exist for CoreWCF builds
			if effective_branch_key == 'corewcf':
				ensure_stub_build_imports(args.solution)
				# Remove legacy Packages directories (from packages.config NuGet restore).
				# SDK-style projects auto-include *.xaml from the entire tree, which picks up
				# XAML files inside IronPython packages and causes build errors.
				for packages_dir in glob('*' + os.sep + 'Packages'):
					if os.path.isdir(packages_dir):
						print("  Removing legacy Packages directory %s" % packages_dir)
						rmtree(packages_dir)

			# Inner loop: build each version
			for (fullpath, version, custom) in versions:
				is_custom = True if hasattr(custom, '__len__') else custom

				print("\n*** COMPILING AGAINST %s [%s] ***" % (version, effective_branch_key.upper()))
				if os.path.exists(args.refpath):
					print("Removing references from %s" % args.refpath)
					clear_directory(args.refpath)
				if os.path.exists(args.source):
					print("Removing previous build from %s" % args.source)
					clear_directory(args.source)
				# Clean obj directories to remove stale NuGet-generated targets
				# (e.g. master branch targets leaking into legacy_ci builds)
				for obj_dir in glob('*' + os.path.sep + 'obj'):
					if os.path.isdir(obj_dir):
						print("Removing stale obj directory %s" % obj_dir)
						rmtree(obj_dir)

				print('Copying main references')
				main_ref_dir = os.path.join(fullpath, args.reference_subdir) if args.reference_subdir else fullpath
				references = copy_references(main_ref_dir, args.refpath)

				print('Copying plugin references')
				references += copy_references(os.path.join(fullpath, 'Plugins'), os.path.join(args.refpath, 'Plugins'), min_files=0)
				for extra_ref in extra_refs:
					full_ref_path = os.path.join(extra_ref, 'Compiled', version)
					print('Copying reference output from ' + full_ref_path)
					references += copy_references(full_ref_path, args.refpath)
				print(f'Copied {len(references)} references')

				flags = []
				effective_version = None
				if is_custom:
					flags = ['LOCAL_CODE', version]
					if hasattr(custom, '__len__'):
						effective_version = custom.split('.')

				if effective_version or not is_custom:
					if effective_version:
						version_components = effective_version
					else:
						version_components = parse_version_string(version, args.source_dir_prefix).split('.')
					flags = ['V' + '_'.join(version_components[0:n+1]) for n in range(len(version_components))]
					before_flag = 'BEFORE_V'
					for (ix, vc) in enumerate(version_components[:3]):
						vc_num = int(vc)
						flags += [before_flag + str(num) for num in range(vc_num+1, MAX_VERSION[ix])]
						before_flag += vc + '_'
				for f in flags:
					print('Defining custom compilation constant: %s' % f)

				# Actual build!
				compilation_flags = '/p:DefineConstants=%s' % ('%3B'.join(flags))
				cmd_args = build_command(effective_branch_key, args, compilation_flags)
				print(subprocess.list2cmdline(cmd_args))
				results[version] = subprocess.run(cmd_args).returncode

				if results[version] == 0:
					version_dest = args.destination + os.path.sep + version
					if not os.path.exists(version_dest):
						os.makedirs(version_dest)

					if args.keep:
						copy_references(args.refpath, version_dest)

					# For CoreWCF builds, use size-aware filtering: the .NET 8 SDK copies
					# all referenced assemblies to the output, including Source DLLs. We skip
					# files identical to Source references (same name AND size) but keep files
					# where Veneer needs a different version than Source ships.
					ref_sizes = build_reference_sizes(references, args.refpath) if effective_branch_key == 'corewcf' else {}

					for artifact in glob(args.source + os.path.sep + "*"):
						basename = os.path.basename(artifact)
						if not args.keep:
							if ref_sizes:
								# CoreWCF: skip only if identical to Source reference
								if not os.path.isdir(artifact) and is_same_as_reference(artifact, ref_sizes):
									continue
							else:
								# WCF: original basename-only filter
								if basename in references:
									continue
						try:
							the_dest = version_dest + os.path.sep + basename
							if os.path.isdir(artifact):
								if os.path.exists(the_dest):
									rmtree(the_dest)
								copytree(artifact, the_dest)
							else:
								copyfile(artifact, the_dest)
						except:
							print('Error copying %s to %s' % (artifact, the_dest))
							raise

					# CoreWCF builds produce a nested Veneer/ subdirectory for VeneerCmd output.
					# Flatten it by merging its contents into the top-level destination so the
					# deployment structure matches the legacy WCF layout.
					if effective_branch_key == 'corewcf':
						flatten_subdirectory(version_dest, 'Veneer', ref_sizes=ref_sizes)

					if custom and args.copy_to_source:
						for artifact in [fn for fn in glob(args.source + os.path.sep + "*")]:
							copyfile(artifact, os.path.join(fullpath, os.path.basename(artifact)))
				elif args.fail:
					fail_fast_triggered = True
					break

			# Clean up applied patch changes before switching away
			if not args.no_branch_switch:
				revert_tracked_changes()
				clean_reject_files()

			if fail_fast_triggered:
				break

	finally:
		# Always restore original branch and stash
		if current_on_branch != original_branch:
			print("\n*** Restoring original branch '%s' ***" % original_branch)
			try:
				revert_tracked_changes()
				clean_reject_files()
				git_checkout(original_branch)
			except RuntimeError as e:
				print("WARNING: Failed to restore original branch: %s" % e)
		if did_stash:
			print("  Restoring stashed changes")
			git_stash_pop()
		if patch_path:
			try:
				os.remove(patch_path)
			except OSError:
				pass

	# --- Results summary ---
	failures = sorted([k for (k, v) in results.items() if v > 0])
	successes = sorted([k for (k, v) in results.items() if v == 0])
	print("\n*** RESULTS ***")
	print("%d successful builds out of %d versions" % (len(successes), len(version_info)))
	if failures:
		print("FAILED TO BUILD AGAINST %d VERSIONS" % (len(failures)))
		print("\n".join(failures))
		open(LAST_FAILS_FN, 'w').writelines('\n'.join(failures))


if __name__ == '__main__':
	main()
