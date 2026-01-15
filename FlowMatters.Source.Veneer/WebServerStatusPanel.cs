using System.Windows.Forms;
using System.Windows.Forms.Integration;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.AutoStart;
using RiverSystem;
#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || V4_2_4 || V4_2_5
using RiverSystem.Controls;
#else
using RiverSystem.Options;
#endif
using RiverSystem.SubjectProxies;
using TIME.Core.Metadata;

namespace FlowMatters.Source.WebServerPanel
{
#if V3
    [Aka("Veneer Server"), DisplayPath("General Tool"),MenuPlugin("Veneer.Properties.Resources.Logo_Only")]
#else
    [Aka("Veneer Server"), InitialiseOnLoad(RiverSystemOptions.GENERAL_TOOL),MenuPlugin("Veneer.Properties.Resources.Logo_Only")]
#endif
    //    MenuPlugin("FlowMatters.Source.Veneer.Resources.Icon_Only_RGB_for_LI.png")]
    public partial class WebServerStatusPanel : UserControl, RiverSystemScenarioProxy.IRiverSystemScenarioHandler
    {
        private WebServerStatusControl _webServerStatusControl;

        public WebServerStatusPanel()
        {
            InitializeComponent();
            _webServerStatusControl = new WebServerStatusControl();
            Controls.Add(new ElementHost {Child = _webServerStatusControl,Dock=DockStyle.Fill});

        }

        public RiverSystemScenario Scenario
        {
            get { return _webServerStatusControl.Scenario; }
            set
            {
                _webServerStatusControl.Scenario = value;
            }
        }
    }
}
