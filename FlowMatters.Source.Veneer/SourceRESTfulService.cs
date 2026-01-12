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
using Newtonsoft.Json;
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
        private SourceService _singletonInstance;
        private RiverSystemScenario _scenario;
        private List<int> _registeredOnPorts = new List<int>();
         
        public bool AllowRemoteConnections { get; set; }

        public override SourceService Service
        {
            get { return _singletonInstance; }
        }

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
                _singletonInstance = new SourceService();
                _singletonInstance.LogGenerator += _singletonInstance_LogGenerator;
                _singletonInstance.Scenario = Scenario;
                _host = new WebServiceHost(_singletonInstance);
                _host.UnknownMessageReceived += _host_UnknownMessageReceived;
                binding.CrossDomainScriptAccessEnabled = true;
                if (!AllowRemoteConnections)
                    binding.HostNameComparisonMode = HostNameComparisonMode.Exact;

                //AppendHeader("Access-Control-Allow-Origin", "*");
                ServiceEndpoint endpoint = _host.AddServiceEndpoint(typeof(SourceService), binding, string.Format("http://localhost:{0}/", _port));
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
                    _singletonInstance.LogGenerator -= _singletonInstance_LogGenerator;
                    _singletonInstance = null;
                    _host = null;
                    endpoint = null;
                    GC.Collect();

                    _port++; // Keep retrying until we run out of allocated ports
                }
                catch (AddressAccessDeniedException)
                {
                    Log(String.Format("For details, see: https://github.com/flowmatters/veneer"));

                    if (AllowRemoteConnections)
                    {
                        Log("If you require external connections, you must select a port where Veneer has permissions to accept external connections.");
                        Log(
                            String.Format(
                                "Veneer does not have permission to accept external (ie non-local) connections on port {0}",
                                _port));
                    }
                    else
                    {
                        Log("Alternatively, enable 'Allow Remote Connections' and restart Veneer.");
                        Log("To establish a local-only connection, select a port where Veneer is NOT registered for external connections.");
                        Log(String.Format(
                                "This is most likely because Veneer is registered to accept external/non-local connections on port {0}.", _port));
                        Log(String.Format(
                                "Veneer does not have permission to accept local-only connections on port {0}",
                                _port));
                    }
                    Log(String.Format("COULD NOT START VENEER ON PORT {0}", _port));
                }
                catch (Exception e)
                {
                    Log("COULD NOT START VENEER");
                    Log(e.Message);
                    Log(e.StackTrace);
                    if(e.InnerException != null)
                    {
                        Log("INNER EXCEPTION:");
                        Log(e.InnerException.Message);
                        Log(e.InnerException.StackTrace);
                    }
                }
            } while (failedAddressInUse);
        }

        private void RetrieveVeneerStatus()
        {
            using( WebClient wc = new WebClient())
            {
                var notifications = wc.DownloadString(STATUS_URL);
                dynamic status = JsonConvert.DeserializeObject(notifications);
                JArray messages = status.message;
                messages.Reverse().Select(e=>e.ToString()).ForEachItem(Log);
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
            Log(string.Format("Unknown message received: {0}",e.Message));
        }

        void _host_Faulted(object sender, EventArgs e)
        {
            Log("Service faulted");
            Stop();
            Start();
        }

        void _singletonInstance_LogGenerator(object sender, string msg)
        {
            Log(msg);
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
                if (_singletonInstance != null)
                    _singletonInstance.Scenario = _scenario;
            }
        }
    }

}
