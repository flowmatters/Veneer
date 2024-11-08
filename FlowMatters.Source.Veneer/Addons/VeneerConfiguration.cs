using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlowMatters.Source.Veneer.Addons
{
    public class VeneerConfiguration
    {
        public VeneerAddon[] addons;
    }

    public class VeneerAddon
    {
        public string name { get; set; }

        public string type { get; set; }

        public string path { get; set; }

    }
}
