using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumers;
using RiverSystem.ApplicationLayer.Creation;
using RiverSystem.PluginManager;
using CommandLine;
using CommandLine.Text;
using FlowMatters.Source.WebServer;
using RiverSystem.ApplicationLayer;
using RiverSystem.ApplicationLayer.Interfaces;
using RiverSystem.ApplicationLayer.Persistence.ZipContainer;
using TIME.DataTypes;

namespace FlowMatters.Source.VeneerCmd
{
    class Program
    {
        private static PluginManager _pluginManager;

        static void Main(string[] args)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            Constants.SetLargeDataOptions();
            try
            {
                TempDirectoryLayout.DeleteProcessIdDirectories(TempDirectoryLayout.ConstBaseTempDirectory);
            }
            catch
            {
                
            }

            //            CopyDLLs();
            var options = new Options();
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {
                if (options.ProjectFiles.Count > 0)
                {
                    try
                    {
                        RunWithOptions(options);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                        Console.WriteLine("Unhandled exception: {0}",e.Message);
                    }
                    return;
                }
            }
            // Display the default usage information
            Console.WriteLine(options.GetUsage());
        }

        //private static void CopyDLLs()
        //{
        //    if (!File.Exists("fbembed.dll"))
        //    {
        //        Show("Copying required DLLs locally");
        //        string dir = Path.GetDirectoryName((typeof (RiverSystemScenario).Assembly.Location));
        //        File.Copy($"{dir}\\fbembed.dll","fbembed.dll");
        //    }
        //}

        private static void RunWithOptions(Options options)
        {
// consume Options instance properties
            var fn = options.ProjectFiles[0];
            LoadPlugins();

            if (!File.Exists(fn))
            {
                Console.WriteLine("Cannot find project file: {0}",fn);
                return;
            }

            var project_dir = Path.GetDirectoryName(fn);
            if (project_dir == "")
                project_dir = ".";
            Directory.SetCurrentDirectory(project_dir);
            
            RiverSystemProject project = LoadProject(fn,options);
            Show(project.Name);
            var scenario = project.GetRSScenarios()[0];
            Show(scenario.ScenarioName);

            var _server = new SourceRESTfulService((int)options.Port);
            _server.Scenario = scenario.riverSystemScenario;
            _server.LogGenerator += ServerLogEvent;
            _server.AllowRemoteConnections = options.RemoteAccess;

            _server.Start();
            _server.Service.AllowScript = options.AllowScripts;
            _server.Service.RunningInGUI = false;
            _server.Service.ProjectHandler = projectHandler;
            Show("Server started. Ctrl-C to exit, or POST /shutdown command");
            while (true)
            {
                Console.ReadLine();
            }
        }

        private static void ServerLogEvent(object sender, string msg)
        {
            Show(msg);
        }

        private static IProjectHandler<RiverSystemProject> projectHandler;
         
        private static RiverSystemProject LoadProject(string fn,Options arguments)
        {
            var callback = new CommandLineProjectCallback(arguments,_pluginManager);
            var loader = ProjectHandlerFactory.CreateProjectHandler<RiverSystemProject>(callback);
            callback.OutputFileName = fn;
            Show(String.Format("Opening project file: {0}", fn));
            loader.OpenProject();
            Show("Loading project");
            loader.LoadProject(false);
            Show("Project Loaded");
            var project = loader.ProjectMetaStructure.Project;
            projectHandler = loader;
            return project;
        }

        const int MAX_PLUGIN_LOAD_ATTEMPTS = 120;
        const int INCREMENT_PLUGIN_LOADING_DELAY = 10;

        private static void LoadPlugins()
        {
            Show("Loading plugins");

#if V3 || V4_0 || V4_1 || V4_2 || V4_3_0
            var manager = PluginRegisterUtility.LoadPlugins();
#else
            var manager = PluginManager.Instance;
#endif
            // NASTY HACK to counter the fact that other Source servers may be trying to rewrite the plugin file at the same time...
            int delay = 1; // 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048
            for (int attempt = 0; attempt < MAX_PLUGIN_LOAD_ATTEMPTS; attempt++)
            {
                if (attempt > 0)
                {
//                    Show("No plugins loaded. Trying again to make sure its not a concurrency issue with other servers rewriting PLugins.xml");
                    Thread.Sleep(delay);
                    if (attempt % INCREMENT_PLUGIN_LOADING_DELAY == 0)
                        delay *= 2;
                }

                manager.LoadPreviouslyLoaded();
                if (manager.ActivePlugins.Count() > 0)
                    break;
            }
            foreach (var plugin in manager.ActivePlugins)
            {
                Show(String.Format("Loaded {0}",plugin.Path));               
            }
            Show(String.Format("Plugins loaded ({0}/{1})",manager.ActivePlugins.Count(),manager.Plugins.Count));
            _pluginManager = manager;
        }

        static void Show(string msg, bool flush = true)
        {
            Console.WriteLine(msg);
            if (flush)
            {
                Console.Out.Flush();
            }
        }
    }

    public class CommandLineProjectCallback : DefaultCallback
    {
        private Options _arguments;
        private PluginManager _plugins;

        public CommandLineProjectCallback(Options arguments,PluginManager plugins)
        {
            _arguments = arguments;
            _plugins = plugins;
        }

        public override bool BackupProject(string currentFileName, out string newFileName)
        {
            base.BackupProject(currentFileName, out newFileName);
            return _arguments.BackupRSPROJ;
        }

        public override void PreUpgradeProject<TProjectType>(ProjectMetaStructure<TProjectType> projectMetaStructure)
        {
            projectMetaStructure.PluginsDictionary = new MultiMap<string, Assembly>();
            foreach (var plugin in _plugins.ActivePlugins)
                projectMetaStructure.PluginsDictionary.Add(plugin.Name, plugin.Assemblies);
        }
    }

    public class Options
    {
        [Option('p',"port",DefaultValue = (uint)SourceRESTfulService.DEFAULT_PORT,HelpText= "Port for Veneer server")]
        public uint Port { get; set; }

        [Option('r', "remote-access",HelpText ="Allow access from other computers", DefaultValue= false)]
        public bool RemoteAccess { get; set; }

        [Option('s',"allow-scripts",HelpText = "Allow submission of Iron Python scripts",DefaultValue = false)]
        public bool AllowScripts { get; set; }

        [Option('b', "backup-rsproj", HelpText = "Backup .rsproj file", DefaultValue = false)]
        public bool BackupRSPROJ { get; set; }

        [ValueList(typeof(List<string>), MaximumElements = 1)]
        public IList<string> ProjectFiles { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            var usage = new StringBuilder();
            usage.AppendLine("Veneer by Flow Matters");
            return usage.ToString();
        }
    }
}
