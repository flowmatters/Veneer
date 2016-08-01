using System;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public abstract class AbstractSourceServer
    {
        protected int _port;
        public int Port { get { return _port; } }
        public abstract SourceService Service { get;  }
        protected AbstractSourceServer(int port)
        {
            _port = port;
        }

        public event ServerLogListener LogGenerator;
        public abstract void Start();
        public abstract void Stop();
        public abstract RiverSystemScenario Scenario { get; set; }

        protected void Log(string query)
        {
            if (LogGenerator != null)
                LogGenerator(this, string.Format("[{0}] {1}",DateTime.Now.ToLongTimeString(),query));
        }
    }

    public delegate void ServerLogListener(object sender, string msg);
}