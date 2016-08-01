using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using FlowMatters.Source.Veneer.Formatting;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public class SourceRESTfulService : AbstractSourceServer
    {
        public const int DEFAULT_PORT = 9876;
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
            WebHttpBinding binding = new WebHttpBinding();

            binding.MaxReceivedMessageSize = 1024*1024;
            _singletonInstance = new SourceService();
            _singletonInstance.LogGenerator += _singletonInstance_LogGenerator;
            _singletonInstance.Scenario = Scenario;
            _host = new WebServiceHost(_singletonInstance);
            _host.UnknownMessageReceived += _host_UnknownMessageReceived;
            binding.CrossDomainScriptAccessEnabled = true;

            if(!AllowRemoteConnections)
                binding.HostNameComparisonMode = HostNameComparisonMode.Exact;

            //AppendHeader("Access-Control-Allow-Origin", "*");
            ServiceEndpoint endpoint = _host.AddServiceEndpoint(typeof(SourceService), binding, string.Format("http://localhost:{0}/", _port));
            endpoint.Behaviors.Add(new ReplyFormatSwitchBehaviour());

//            int sslPort = _port + 1000;
//            endpoint = _host.AddServiceEndpoint(typeof(SourceService), binding, string.Format("https://localhost:{0}/", sslPort));
//            endpoint.Behaviors.Add(new ReplyFormatSwitchBehaviour());

            try
            {
                _host.Open();
                Log("Veneer, by Flow Matters: http://www.flowmatters.com.au");
                Log(string.Format("Started Source RESTful Service on port:{0}", _port));
            }
            catch (AddressAlreadyInUseException)
            {
                _port++; // Keep retrying until we run out of allocated ports
                Start();
            }
            catch (Exception e)
            {
                Log("COULD NOT START VENEER");
                Log(e.Message);
                Log(e.StackTrace);
            }
            _host.Faulted += _host_Faulted;
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
