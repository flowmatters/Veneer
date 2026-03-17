using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Netron.GraphLib;
using RiverSystem;
using RiverSystem.Api;

namespace FlowMatters.Source.Veneer.Addons
{
    public class VeneerConfiguration
    {
        public VeneerAddon[] addons;
        public VeneerOptions options;

        public static string ConfigurationFilename(RiverSystemScenario scenario)
        {
            return ConfigurationFilename(scenario?.RiverSystemProject);
        }

        public static string ConfigurationFilename(RiverSystemProject project){
            if (project?.FullFilename == null)
            {
                return null;
            }

            var result = project.FullFilename.Replace(".rsproj", ".rsproj.veneer");
            if (File.Exists(result))
            {
                return result;
            }

            return null;
        }

        public static VeneerConfiguration Load(RiverSystemScenario scenario)
        {
            return Load(scenario?.RiverSystemProject);
        }

        public static VeneerConfiguration Load(RiverSystemProject project)
        {
            var filename = ConfigurationFilename(project);
            if (filename == null)
            {
                return null;
            }
            var json = File.ReadAllText(filename);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<VeneerConfiguration>(json);
        }
    }

    public class VeneerAddon
    {
        public string name { get; set; }

        public string type { get; set; }

        public string path { get; set; }

        public string menu { get; set; }

    }

    public class VeneerOptions
    {
        public bool autoStart;
        public bool allowScripts;
        public int defaultPort;
    }
}
