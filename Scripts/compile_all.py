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

By default, all installed versions of Source will be targeted. To ignore versions that are known to be incompatible, add a file, named like the solution file, with an additional extension: .ignore. Each line of this file should be a pattern for ignoring Source versions. For example, if your solution is MyPlugins.sln, you could have a file named MyPlugins.sln.ignore with contents like
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
from shutil import copyfile

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
		  default='.\\Output')
parser.add_argument('--ewater','-e',
		  help="Specify the base installation folder for all versions of eWater Source",
		  default='C:\\Program Files\eWater')
parser.add_argument('--msbuild','-m',
		  help="Specify path to the msbuild.exe",
		  default="c:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\MSBuild.exe")
parser.add_argument("--refpath","-r",
	      help="Specify path to place references",
	      default="References")
parser.add_argument('solution',help="Path to Solution (.sln) file")

args = parser.parse_args()

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

versions_to_compile = sorted(set(all_versions) - set(ignored))
shortnames = [os.path.basename(v) for v in versions_to_compile]

print("*** COMPILING AGAINST %d VERSIONS ***" % (len(versions_to_compile)))
print("\n".join(shortnames))

results = {}
for (fullpath,version) in zip(versions_to_compile,shortnames):
	print("*** COMPILING AGAINST %s" % (version))
	references = copy_references(fullpath,args.refpath)
	results[version] = os.system(args.msbuild + " " + args.solution)
	if results[version] == 0:
		version_dest = args.destination+os.path.sep+version
		if not os.path.exists(version_dest):
			os.makedirs(version_dest)
		for artifact in [fn for fn in glob(args.source+os.path.sep+"*") if not os.path.basename(fn) in references]:
			copyfile(artifact,version_dest+os.path.sep+os.path.basename(artifact))

failures = sorted([k for (k,v) in results.items() if v > 0])
successes = sorted([k for (k,v) in results.items() if v == 0])
print("*** RESULTS ***")
print("%d successful builds out of %d versions" % (len(successes),len(versions_to_compile)))
if failures:
	print("FAILED TO BUILD AGAINST %d VERSIONS" % (len(failures)))
	print("\n".join(failures))
