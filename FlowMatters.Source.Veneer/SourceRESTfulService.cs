﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public class SourceRESTfulService : AbstractSourceServer
    {
        private WebServiceHost _host;
        private SourceService _singletonInstance;
        private RiverSystemScenario _scenario;

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
            
            //AppendHeader("Access-Control-Allow-Origin", "*");
            _host.AddServiceEndpoint(typeof(SourceService), binding, string.Format("http://localhost:{0}/", _port));

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