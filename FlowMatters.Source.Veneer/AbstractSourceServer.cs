using System;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public abstract class AbstractSourceServer
    {
        protected int _port;
        public int Port => _port;
        public abstract bool AllowScript { get; set; }
        protected AbstractSourceServer(int port)
        {
            _port = port;
        }

        public event ServerLogListener LogGenerator;
        public abstract Task Start();
        public abstract Task Stop();
        public abstract RiverSystemScenario Scenario { get; set; }

        public bool Running { get; protected set; }

        protected void Log(string query)
        {
            if (LogGenerator != null)
                LogGenerator(this, string.Format("[{0}] {1}",DateTime.Now.ToLongTimeString(),query));
        }
    }

    public delegate void ServerLogListener(object sender, string msg);
}
