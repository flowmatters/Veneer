using System;
using System.Reflection;
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

        private static WebServerStatusPanel _activePanel;
        public static WebServerStatusPanel ActivePanel => _activePanel;

        public WebServerStatusPanel()
        {
            _activePanel = this;
            InitializeComponent();
            _webServerStatusControl = new WebServerStatusControl();
            Controls.Add(new ElementHost {Child = _webServerStatusControl,Dock=DockStyle.Fill});
            Load += WebServerStatusPanel_Load;
        }

        private bool _dockConfigured;

        private void WebServerStatusPanel_Load(object sender, EventArgs e)
        {
            if (_dockConfigured) return;
            _dockConfigured = true;

            var parentForm = FindForm();
            if (parentForm == null) return;

            try
            {
                // Set HideOnClose so closing hides the panel (server keeps running)
                var hideOnCloseProp = parentForm.GetType().GetProperty("HideOnClose");
                if (hideOnCloseProp != null)
                    hideOnCloseProp.SetValue(parentForm, true);

                // Re-dock from Float to DockBottom — DockState.DockBottom = 4 in WeifenLuo
                var dockStateProp = parentForm.GetType().GetProperty("DockState");
                if (dockStateProp != null)
                {
                    var dockStateType = dockStateProp.PropertyType;
                    var dockBottom = Enum.ToObject(dockStateType, 4);
                    dockStateProp.SetValue(parentForm, dockBottom);
                }
            }
            catch
            {
                // If docking fails (e.g. different WeifenLuo version), panel still works as float
            }
        }

        public bool IsServerRunning => _webServerStatusControl?.Running == true;

        public void ActivateWindow()
        {
            var parentForm = FindForm();
            if (parentForm == null) return;

            // If the wrapper is a DockContent that's been hidden (HideOnClose), re-show it
            var isHiddenProp = parentForm.GetType().GetProperty("IsHidden");
            if (isHiddenProp != null && (bool)isHiddenProp.GetValue(parentForm))
            {
                // DockContent.Show() restores the panel to its previous dock state
                var showMethod = parentForm.GetType().GetMethod("Show", Type.EmptyTypes);
                showMethod?.Invoke(parentForm, null);
            }

            parentForm.Activate();
            parentForm.BringToFront();
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
