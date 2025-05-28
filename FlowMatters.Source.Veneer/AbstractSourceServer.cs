using System;
using System.Threading.Tasks;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public abstract class AbstractSourceServer
    {
        protected int _port;
        protected int _sslPort => _port + 1000;
        public int Port => _port;
        public abstract SourceService Service { get;  }
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