﻿using System.Windows.Forms;
using System.Windows.Forms.Integration;
using RiverSystem;
#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5 || GBRSource
using RiverSystem.Controls;
#else
using RiverSystem.Options;
#endif
using RiverSystem.SubjectProxies;
using TIME.Core.Metadata;

namespace FlowMatters.Source.WebServerPanel
{
    [Aka("Web Server Monitoring"),DisplayPath(RiverSystemOptions.GENERAL_TOOL),MenuPlugin("Veneer.Properties.Resources.Logo_Only")]
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
