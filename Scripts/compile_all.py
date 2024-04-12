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
Eg <solutionpath>\References

2: The projects should all output to a common directory. If there is just one project, or one project that
depends on your others, then this can be a bin\Debug folder. If there are multiple, independent projects,
then this should be a separate directory, eg: <solutionpath>\Output

For usage information, run with -help:

python compile_all.py -h
'''
import os
import sys
import argparse
from glob import glob
from shutil import copyfile,rmtree,copytree
from typing import List

def unique_versions(all_versions:List[str],num_elements:int) -> List[str]:
	uniq_versions = {}
	for v in all_versions:
		v_num = os.path.basename(v).split(' ')[1]
		v_num = '.'.join(v_num.split('.')[:num_elements])
		uniq_versions[v_num] = v
	return sorted(uniq_versions.values())

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
		
def copy_references(source,dest):
	if not os.path.exists(dest):
		os.mkdir(dest)

	assemblies = glob(source+os.path.sep+"*.exe") + glob(source+os.path.sep+"*.dll")
	for a in assemblies: copyfile(a,dest+os.path.sep+os.path.basename(a))

	return [os.path.basename(a) for a in assemblies]

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
		  default='C:\\Program Files\eWater')
parser.add_argument('--msbuild','-m',
		  help="Specify path to the msbuild.exe",
		  default='"D:\\Program Files (x86)\\Microsoft Visual Studio\\2019\\Community\\MSBuild\\Current\\Bin\\MsBuild.exe"')
		  #C:\\Program Files (x86)\\MSBuild\\14.0\\Bin\\MsBuild.exe"')
parser.add_argument("--refpath","-r",
	      help="Specify path to place references",
	      default="References")
parser.add_argument('--fail','-f',help="Fail fast: Stop after first failure",action='store_true',default=False)
parser.add_argument('--keep','-k',help='Keep references in output directory',action='store_true',default=False)
parser.add_argument('--copy_to_source','-c',help='Copy outputs to Source directory WHEN COMPILING AGAINST CUSTOM VERSION OF SOURCE',action='store_true',default=False)
parser.add_argument('solution',help="Path to Solution (.sln) file")

args = parser.parse_args()

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
all_versions = sorted(glob(args.ewater + os.path.sep + "Source*"))
print("*** FOUND %d INSTALLED VERSIONS OF SOURCE" % (len(all_versions)))
print("\n".join(all_versions))

ignore_fn = args.solution + ".ignore"
ignored = []
if os.path.exists(ignore_fn):
	ignore_patterns = open(args.solution + ".ignore").readlines()
	for ip in ignore_patterns:
		ignored += glob(args.ewater + os.path.sep + "Source " + ip.strip())
	ignored = sorted(set(ignored))
	print("*** IGNORING %d VERSIONS ***" % (len(ignored)))
	print("\n".join(ignored))

include_fn = args.solution + ".include"
include_patterns = []
if os.path.exists(include_fn):
	include_patterns = [tuple(l.strip().split(',')) for l in open(include_fn).readlines() if len(l)]
	print("*** INCLUDING %d EXTRA DIRECTORIES ***" % (len(include_patterns)))
	print("\n".join([label + '('+directory+')' for (directory,label) in include_patterns]))
	include_patterns = [(d,l,True) for (d,l) in include_patterns]

versions_to_compile = sorted(set(all_versions) - set(ignored))
versions_to_compile = unique_versions(versions_to_compile,3)

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

results = {}
for (fullpath,version,custom) in version_info:
	print("*** COMPILING AGAINST %s" % (version))
	if os.path.exists(args.refpath):
		print("Removing references from %s"%args.refpath)
		clear_directory(args.refpath)
	if os.path.exists(args.source):
		print("Removing previous build from %s"%args.source)
		clear_directory(args.source)

	references = copy_references(fullpath,args.refpath)
	references += copy_references(os.path.join(fullpath,'Plugins'),os.path.join(args.refpath,'Plugins'))
	for extra_ref in extra_refs:
		full_ref_path = os.path.join(extra_ref,'Compiled',version)
		print('Copying reference output from ' + full_ref_path)
		references += copy_references(full_ref_path,args.refpath)
#	references = copy_references(args.refpath,args.source)
#	references += copy_references(os.path.join(args.refpath,'Plugins'),os.path.join(args.source,'Plugins'))
	
	flags = []
	if custom:
		flags=['LOCAL_CODE',version]
	else:
		MAX_VERSION=[7,20,4]
		version_components=version.split(' ')[1].split('.')
		flags=['V'+'_'.join(version_components[0:n+1]) for n in range(len(version_components))]
		before_flag='BEFORE_V'
		for (ix,vc) in enumerate(version_components[:3]):
			vc_num = int(vc)
			flags += [before_flag + str(num) for num in range(vc_num+1,MAX_VERSION[ix])]
			before_flag += vc + '_'
	for f in flags:
		print('Defining custom compilation constant: %s'%f)
	
	compilation_flags = '/p:DefineConstants="%s"'%(';'.join(flags))
	cmd_line='"%s %s %s"'%(args.msbuild,compilation_flags,args.solution)
	print(cmd_line)
	results[version] = os.system(cmd_line)
	if results[version] == 0:
		version_dest = args.destination+os.path.sep+version
		if not os.path.exists(version_dest):
			os.makedirs(version_dest)
		
		if args.keep:
			copy_references(args.refpath,version_dest)

		for artifact in [fn for fn in glob(args.source+os.path.sep+"*") if args.keep or not os.path.basename(fn) in references]:
			try:
				the_dest = version_dest+os.path.sep+os.path.basename(artifact)
				if os.path.isdir(artifact):
					if os.path.exists(the_dest):
						rmtree(the_dest)
					copytree(artifact,the_dest)
				else:
					copyfile(artifact,the_dest)
			except:
				print('Error copying %s to %s'%(artifact,the_dest))
				raise

		if custom and args.copy_to_source:
			
			for artifact in [fn for fn in glob(args.source+os.path.sep+"*")]:
				copyfile(artifact,os.path.join(fullpath,os.path.basename(artifact)))
	elif args.fail:
		break
failures = sorted([k for (k,v) in results.items() if v > 0])
successes = sorted([k for (k,v) in results.items() if v == 0])
print("*** RESULTS ***")
print("%d successful builds out of %d versions" % (len(successes),len(version_info)))
if failures:
	print("FAILED TO BUILD AGAINST %d VERSIONS" % (len(failures)))
	print("\n".join(failures))
	open(LAST_FAILS_FN,'w').writelines('\n'.join(failures))
