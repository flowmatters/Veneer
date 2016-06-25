using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumers;
using RiverSystem.ApplicationLayer.Creation;
using RiverSystem.PluginManager;
using CommandLine;
using CommandLine.Text;
using FlowMatters.Source.WebServer;

namespace FlowMatters.Source.VeneerCmd
{
    class Program
    {
        static void Main(string[] args)
        {
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
            _server.Start();
            _server.Service.AllowScript = options.AllowScripts;
            _server.Service.RunningInGUI = false;

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

        private static RiverSystemProject LoadProject(string fn,Options arguments)
        {
            var callback = new CommandLineProjectCallback(arguments);
            var loader = ProjectHandlerFactory.CreateProjectHandler<RiverSystemProject>(callback);
            callback.OutputFileName = fn;
            Show($"Opening project file: {fn}");
            loader.OpenProject();
            Show("Loading project");
            loader.LoadProject(false);
            Show("Project Loaded");
            var project = loader.ProjectMetaStructure.Project;
            return project;
        }

        private static void LoadPlugins()
        {
            Show("Loading plugins");
            PluginRegisterUtility.LoadPlugins();
            Show("Plugins loaded");
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

        public CommandLineProjectCallback(Options arguments)
        {
            _arguments = arguments;
        }

        public override bool BackupProject(string currentFileName, out string newFileName)
        {
            base.BackupProject(currentFileName, out newFileName);
            return _arguments.BackupRSPROJ;
        }
    }

    public class Options
    {
        [Option('p',"port",DefaultValue = 9876u,HelpText= "Port for Veneer server")]
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
