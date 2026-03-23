using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using System.Reflection;
using FlowMatters.Source.Veneer.CORS;
using FlowMatters.Source.Veneer.Formatting;
using FlowMatters.Source.Veneer.RemoteScripting;
using Newtonsoft.Json;
using RiverSystem.ApplicationLayer.Interfaces;
using Newtonsoft.Json.Linq;
using RiverSystem;
using RiverSystem.ManagedExtensions;

namespace FlowMatters.Source.WebServer
{
    public class SourceRESTfulService : AbstractSourceServer
    {
        public const int DEFAULT_PORT = 9876;
        public const string STATUS_URL = "https://www.flowmatters.com.au/veneer/status.json";
        private WebServiceHost _host;
        private RiverSystemScenario _scenario;
        private List<int> _registeredOnPorts = new List<int>();
        private bool _allowScript;

        public bool AllowRemoteConnections { get; set; }

        public bool RunningInGUI { get; set; } = true;

        public IProjectHandler<RiverSystemProject> ProjectHandler { get; set; }

        public CustomEndPoint[] CustomEndpoints { get; set; }

        public SourceRESTfulService(int port) : base(port)
        {

        }

        public override void Start()
        {
            bool failedAddressInUse;
            do
            {
                failedAddressInUse = false;
                LeaveDotsAndSlashesEscaped();
                WebHttpBinding binding = new WebHttpBinding();

                binding.MaxReceivedMessageSize = 1024 * 1024 * 1024; // 1 gigabyte

                // Initialize static state before starting the service
                InitializeStaticServiceState();

                // With PerCall mode, we pass the service
                _host = new WebServiceHost(typeof(SourceService));
                _host.UnknownMessageReceived += _host_UnknownMessageReceived;
                binding.CrossDomainScriptAccessEnabled = true;
                if (!AllowRemoteConnections)
                    binding.HostNameComparisonMode = HostNameComparisonMode.Exact;

                //AppendHeader("Access-Control-Allow-Origin", "*");
                ServiceEndpoint endpoint = _host.AddServiceEndpoint(typeof(SourceService), binding,
                    string.Format("http://localhost:{0}/", _port));
                endpoint.Behaviors.Add(new ReplyFormatSwitchBehaviour());
                endpoint.Behaviors.Add(new EnableCrossOriginResourceSharingBehavior());

                try
                {
                    Running = false;
                    _host.Open();
                    Log("Veneer, by Flow Matters: https://www.flowmatters.com.au");
                    try
                    {
                        RetrieveVeneerStatus();
                    }
                    catch
                    {
                        // Pass
                    }

                    Log(string.Format("Started Source RESTful Service on port:{0}", _port));
                    Running = true;
                    _host.Faulted += _host_Faulted;
                }
                catch (AddressAlreadyInUseException)
                {
                    failedAddressInUse = true;
                    binding = null;
                    _host = null;
                    endpoint = null;
                    GC.Collect();

                    _port++; // Keep retrying until we run out of allocated ports
                }
                catch (AddressAccessDeniedException)
                {
                    Log(String.Format("For details, see: https://github.com/flowmatters/veneer"), LogLevel.Error);

                    if (AllowRemoteConnections)
                    {
                        Log(
                            "If you require external connections, you must select a port where Veneer has permissions to accept external connections.", LogLevel.Error);
                        Log(
                            String.Format(
                                "Veneer does not have permission to accept external (ie non-local) connections on port {0}",
                                _port), LogLevel.Error);
                    }
                    else
                    {
                        Log("Alternatively, enable 'Allow Remote Connections' and restart Veneer.", LogLevel.Error);
                        Log(
                            "To establish a local-only connection, select a port where Veneer is NOT registered for external connections.", LogLevel.Error);
                        Log(String.Format(
                            "This is most likely because Veneer is registered to accept external/non-local connections on port {0}.",
                            _port), LogLevel.Error);
                        Log(String.Format(
                            "Veneer does not have permission to accept local-only connections on port {0}",
                            _port), LogLevel.Error);
                    }

                    Log(String.Format("COULD NOT START VENEER ON PORT {0}", _port), LogLevel.Error);
                }
                catch (Exception e)
                {
                    Log("COULD NOT START VENEER", LogLevel.Error);
                    Log(e.Message, LogLevel.Error);
                    Log(e.StackTrace, LogLevel.Error);
                    if (e.InnerException != null)
                    {
                        Log("INNER EXCEPTION:", LogLevel.Error);
                        Log(e.InnerException.Message, LogLevel.Error);
                        Log(e.InnerException.StackTrace, LogLevel.Error);
                    }
                }
            } while (failedAddressInUse);
        }

        private void InitializeStaticServiceState()
        {
            SourceService.InitializeSharedState(
                scenario: _scenario,
                projectHandler: ProjectHandler,
                allowScript: AllowScript,
                runningInGUI: RunningInGUI,
                customEndpoints: CustomEndpoints
            );

            SourceService.SetLogHandler((sender, message, level) => Log(message, level));
        }

        public override bool AllowScript
        {
            get => _allowScript;
            set
            {
                _allowScript = value;
                InitializeStaticServiceState();
            }
        }

        private void RetrieveVeneerStatus()
        {
            using (WebClient wc = new WebClient())
            {
                var notifications = wc.DownloadString(STATUS_URL);
                dynamic status = JsonConvert.DeserializeObject(notifications);
                JArray messages = status.message;
                messages.Reverse().Select(e => e.ToString()).ForEachItem(m => Log(m));
            }
        }

        private void LeaveDotsAndSlashesEscaped()
        {
            var getSyntaxMethod =
                typeof(UriParser).GetMethod("GetSyntax", BindingFlags.Static | BindingFlags.NonPublic);
            if (getSyntaxMethod == null)
            {
                throw new MissingMethodException("UriParser", "GetSyntax");
            }

            var uriParser = getSyntaxMethod.Invoke(null, new object[] { "http" });

            var setUpdatableFlagsMethod =
                uriParser.GetType().GetMethod("SetUpdatableFlags", BindingFlags.Instance | BindingFlags.NonPublic);
            if (setUpdatableFlagsMethod == null)
            {
                throw new MissingMethodException("UriParser", "SetUpdatableFlags");
            }

            setUpdatableFlagsMethod.Invoke(uriParser, new object[] { 0 });
        }

        void _host_UnknownMessageReceived(object sender, UnknownMessageReceivedEventArgs e)
        {
            Log(string.Format("Unknown message received: {0}", e.Message), LogLevel.Warning);
        }

        void _host_Faulted(object sender, EventArgs e)
        {
            Log("Service faulted", LogLevel.Error);
            Stop();
            Start();
        }

        public override void Stop()
        {
            if (_host != null)
            {
                Log("Stopping Service");
                _host.Close();
                _host = null;
            }

            Running = false;
        }

        public override RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                _scenario = value;

                // Update the static scenario state if the service is already running
                if (Running)
                {
                    SourceService.UpdateSharedScenario(_scenario);
                }
            }
        }
    }
}