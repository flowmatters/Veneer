using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumers;
using RiverSystem.ApplicationLayer.Creation;
using RiverSystem.PluginManager;
using CommandLine;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.RemoteScripting;
using Newtonsoft.Json;
using RiverSystem.ApplicationLayer;
using RiverSystem.ApplicationLayer.Interfaces;
using RiverSystem.ApplicationLayer.Persistence.ZipContainer;
using RiverSystem.ManagedExtensions;
using TIME.DataTypes;
using System.Text;
using System.Runtime.CompilerServices;
using TIME.Management;

namespace FlowMatters.Source.VeneerCmd
{
    public class Program
    {
        private static PluginManager _pluginManager;
        private static IConfiguration _configuration;

        private static readonly List<string> _searchPaths = new List<string>()
                                                            {
                                                                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."),  // Output folder
                                                                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Plugins"),  // Output/Plugins folder
                                                                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Plugins", "CommunityPlugins") // Output/Plugins/CommunityPlugins folder
                                                            };

        /// <summary>
        /// This code runs before Main(). Initializes the assembly resolver to load required assemblies for TIME and then catch failing assembly loading and redirect appropriately.
        /// </summary>
        [ModuleInitializer]
        public static void InitializeAssemblyResolver()
        {
            // Have to manually load TIME on its own first in order to access the AssemblyManager
            LoadRequiredAssembly("TIME");
            LoadRequiredAssemblyWithAssemblyManager("TIME");

            // Catch any assemblies that fail to load and try them in the Source directory itself
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Extract the assembly name without version info
            var assemblyName = new AssemblyName(args.Name).Name;

            return LoadRequiredAssemblyWithAssemblyManager(assemblyName);
        }

        /// <summary>
        /// Attempt to load the required assembly using the AssemblyManager in either Output, Output/Plugins or Output/Plugins/CommunityPlugins
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        private static void LoadRequiredAssembly(string assemblyName)
        {
            foreach (var searchPath in _searchPaths)
            {
                var assemblyPath = Path.Combine(searchPath, assemblyName + ".dll");
                if (File.Exists(assemblyPath))
                {
                    try
                    {
                        Assembly.LoadFrom(assemblyPath);
                        Console.WriteLine($@"Loaded required assembly: {assemblyName}");
                        break;
                    }
                    catch
                    {
                        // Continue to next path if loading fails
                    }
                }
            }
        }

        /// <summary>
        /// Attempt to load the required assembly using the AssemblyManager in either Output, Output/Plugins or Output/Plugins/CommunityPlugins
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        private static Assembly LoadRequiredAssemblyWithAssemblyManager(string assemblyName)
        {
            foreach (var searchPath in _searchPaths)
            {
                var assemblyPath = Path.Combine(searchPath, assemblyName + ".dll");
                if (File.Exists(assemblyPath))
                {
                    try
                    {
                        var assembly = AssemblyManager.AssemblyLoadFrom(assemblyPath);
                        Console.WriteLine($@"Loaded required assembly with assembly manager: {assemblyName}");
                        return assembly;
                    }
                    catch
                    {
                        // Continue to next path if loading fails
                    }
                }
            }

            return null;
        }

        static void Main(string[] args)
        {
            // Required for log4net used in RiverSystem.Persistence
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Set up configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            Constants.SetLargeDataOptions();
            try
            {
                TempDirectoryLayout.DeleteProcessIdDirectories(TempDirectoryLayout.ConstBaseTempDirectory);
            }
            catch
            {
                
            }

            var options = new Options();
            var result = CommandLine.Parser.Default.ParseArguments<Options>(args);
            if (result != null)
            {
                try
                {
                    options = result.Value;
                    RunWithOptions(options);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                    Console.WriteLine("Unhandled exception: {0}", e.Message);
                }
                return;
            }
            // Display the default usage information
            //Console.WriteLine(options.GetUsage());
        }

        private static void RunWithOptions(Options options)
        {
// consume Options instance properties
            var fn = (options.ProjectFiles.Count>0)?options.ProjectFiles[0]:null;
            LoadPlugins(options.PluginsToLoad,!options.SkipRegisteredPlugins);

            var customEndpoints = Array.Empty<CustomEndPoint>();
            if (options.CustomEndPointFiles != null)
            {
                var files = options.CustomEndPointFiles.Split(',');
                customEndpoints = files.SelectMany(f => JsonConvert.DeserializeObject<List<CustomEndPoint>>(File.ReadAllText(f))).ToArray();
            }

            RiverSystemScenario scenario;
            if (fn == null)
            {
                scenario = CreateEmptyScenario(options);
            } else if (!File.Exists(fn))
            {
                Console.WriteLine("Cannot find project file: {0}", fn);
                return;
            }
            else
            {
                scenario = LoadScenario(options, fn);
            }


            Show(scenario.Name);

            var _server = new SourceRESTfulService((int)options.Port);
            _server.Scenario = scenario;
            _server.LogGenerator += ServerLogEvent;
            _server.AllowRemoteConnections = options.RemoteAccess;
            _server.AllowSsl = options.AllowSsl;

            _server.Start();
            _server.Service.AllowScript = options.AllowScripts;
            _server.Service.RunningInGUI = false;
            _server.Service.ProjectHandler = projectHandler;

            customEndpoints.ForEachItem(ep=> _server.Service.RegisterEndPoint(ep));
            if (customEndpoints.Length>0)
            {
                Console.WriteLine($"Registered {customEndpoints.Length} custom endpoints");
            }

            Show("Server started. Ctrl-C to exit, or POST /shutdown command");
            while (true)
            {
                Console.ReadLine();
            }
        }

        private static RiverSystemScenario CreateEmptyScenario(Options options)
        {
            var callback = new CommandLineProjectCallback(options, _pluginManager);
            var loader = ProjectHandlerFactory.CreateProjectHandler<RiverSystemProject>(callback);
            callback.OutputFileName = "new-project.rsproj";
            loader.CreateProject();
            projectHandler = loader;

            var project = RiverSystemProject.CreateProject("Created Project");
            projectHandler.ProjectMetaStructure.Project = project;
            var scenario = (RiverSystemScenario)project.ConstructNewScenario();
            var scenarioContainer = new RiverSystemScenarioContainer(scenario);
            project.AddScenario(scenarioContainer);

            return scenario;
        }

        private static RiverSystemScenario LoadScenario(Options options, string fn)
        {
            var project_dir = Path.GetDirectoryName(fn);
            if (project_dir == "")
                project_dir = ".";
            Directory.SetCurrentDirectory(project_dir);

            RiverSystemProject project = LoadProject(fn, options);
            Show(project.Name);

            RiverSystemScenarioContainer scenario;
            var allScenarios = project.GetRSScenarios();

            if (options.AvailableScenarios)
            {
                Show(String.Join(Environment.NewLine, allScenarios.Select(s => s.ScenarioName)));
                Environment.Exit(0);
            }

            if (options.ScenarioToLoad == null)
            {
                scenario = allScenarios[0];
            }
            else
            {
                int scenarioNumber = -1;
                if (int.TryParse(options.ScenarioToLoad, out scenarioNumber))
                {
                    scenario = allScenarios[scenarioNumber - 1];
                }
                else
                {
                    scenario = Enumerable.FirstOrDefault(allScenarios, s => s.ScenarioName == options.ScenarioToLoad);
                }
            }
            return scenario.riverSystemScenario;
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

        private static void LoadPlugins(string pluginsToLoad, bool loadRegistered=true)
        {
            string[] additionalPlugins = {};

            if (pluginsToLoad != null)
            {
                additionalPlugins = pluginsToLoad.Split(',');
            }
            Show("Loading plugins");

#if V3 || V4_0 || V4_1 || V4_2 || V4_3_0
            var manager = PluginRegisterUtility.LoadPlugins();
            // NASTY HACK to counter the fact that other Source servers may be trying to rewrite the plugin file at the same time...
            int delay = 1; // 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048
            for (int attempt = 0; attempt < MAX_PLUGIN_LOAD_ATTEMPTS; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(delay);
                    if (attempt % INCREMENT_PLUGIN_LOADING_DELAY == 0)
                        delay *= 2;
                }

                manager.LoadPreviouslyLoaded();
                if (manager.ActivePlugins.Count() > 0)
                    break;
            }
#else
            if (!loadRegistered)
            {
                PluginManager.ManagementFile = Path.GetTempFileName();
            }
            var manager = PluginManager.Instance;
#endif

            // TODO: RM-20834 RM-21455 Below needs testing
            var allDomainPluginsToLoad = new List<string>();

            foreach (string plugin in additionalPlugins)
            {
                Console.Write("Loading from command line {0}... ", plugin);

                var result = manager.InstallPlugin(plugin, out var domainPluginsToLoad, false, false);
                allDomainPluginsToLoad.AddRange(domainPluginsToLoad);
                Console.WriteLine(result.Status.IsLoaded?"Loaded":result.Status.ErrorMsg);
            }

            foreach (var plugin in allDomainPluginsToLoad)
            {
                // Don't care about loading extra plugins here
                manager.InstallPlugin(plugin, out _, false, false);
            }

            foreach (var plugin in manager.ActivePlugins)
            {
                Show($"Loaded {plugin.Path}");
            }

            Show($"Plugins loaded ({manager.ActivePlugins.Count()}/{manager.Plugins.Count})");
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
        [Option('p',"port",Default = (uint)SourceRESTfulService.DEFAULT_PORT,HelpText= "Port for Veneer server")]
        public uint Port { get; set; }

        [Option('r', "remote-access",HelpText ="Allow access from other computers", Default = false)]
        public bool RemoteAccess { get; set; }

        [Option('r', "enable-ssl", HelpText = "Enable SSL access", Default = false)]
        public bool AllowSsl { get; set; }

        [Option('s',"allow-scripts",HelpText = "Allow submission of Iron Python scripts", Default = false)]
        public bool AllowScripts { get; set; }

        [Option('b', "backup-rsproj", HelpText = "Backup .rsproj file", Default = false)]
        public bool BackupRSPROJ { get; set; }

        [Option('a', "available-models", HelpText = "List available models (scenarios) then exit", Default = false)]
        public bool AvailableScenarios { get; set; }

        [Option('m', "model", HelpText = "Model (scenario) to use", Default = null)]
        public string ScenarioToLoad { get; set; }

        [Option('l',"load-plugin",HelpText = "Load plugins in addition to configured plugins",Default =null)]
        public string PluginsToLoad { get; set; }

        [Option('x',"skip-registered",HelpText="Skip loading of already registered plugins",Default =false)]
        public bool SkipRegisteredPlugins { get; set; }

        [Option('c', "custom-endpoints", HelpText = "Custom endpoints to enable, specified as command separated list of filenames", Default = null)]
        public string CustomEndPointFiles{ get; set; }

        // TODO: RM-20834 RM-21455 Uncertain about replacement here
        //[ValueList(typeof(List<string>), MaximumElements = 1)]
        [Value(0, Max = 1)]
        public IList<string> ProjectFiles { get; set; }

        //[Help]
        //public string GetUsage()
        //{
        //    var usage = new StringBuilder();
        //    usage.AppendLine("Veneer by Flow Matters");
        //    return usage.ToString();
        //}
    }
}
