using System;
using RiverSystem;

namespace FlowMatters.Source.WebServer
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public abstract class AbstractSourceServer
    {
        protected int _port;
        public int Port { get { return _port; } }

        public abstract bool AllowScript { get; set; }
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;

        protected AbstractSourceServer(int port)
        {
            _port = port;
        }

        public event ServerLogListener LogGenerator;
        public abstract void Start();
        public abstract void Stop();
        public abstract RiverSystemScenario Scenario { get; set; }

        public bool Running { get; protected set; }

        protected void Log(string query, LogLevel level = LogLevel.Info)
        {
            if (LogGenerator != null)
                LogGenerator(this, string.Format("[{0}] {1}",DateTime.Now.ToLongTimeString(),query), level);
        }
    }

    public delegate void ServerLogListener(object sender, string msg, LogLevel level);
}