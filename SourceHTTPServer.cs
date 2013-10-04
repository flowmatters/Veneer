using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using FlowMatters.Source.WebServer.ProcessRequests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using RiverSystem;
using TIME.DataTypes;

namespace FlowMatters.Source.WebServer
{
    public class SourceHTTPServer : AbstractSourceServer
    {
        HttpListener listener = new HttpListener();
        private RiverSystemScenario _scenario;
        private ParseDataRequests _parser;

        public SourceHTTPServer(int port):base(port)
        {
            listener.Prefixes.Add(String.Format("http://localhost:{0}/",_port));
//            listener.
        }

        public override RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                _scenario = value;
                _parser = new ParseDataRequests(_scenario);
            }
        }

        public override void Start()
        {
            Log(string.Format("Starting Server on {0}",_port));
            listener.Start();
            Listen();
        }

        public override void Stop()
        {
            listener.Stop();            
            Log("Stopping Server");
        }

        public void Listen()
        {
            //bool keepListening = true;
            //while (keepListening)
            //{
            //    ProcessRequest(listener.GetContext());
            //}
            IAsyncResult result = listener.BeginGetContext(ListenerCallback, listener);
            //result.AsyncWaitHandle.WaitOne();
        }

        private void ListenerCallback(IAsyncResult result)
        {
            HttpListenerContext context = listener.EndGetContext(result);
            Listen();
            string query = context.Request.Url.OriginalString;
            ProcessRequest(context);

            Log(query);
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            string query = context.Request.Url.OriginalString.Replace("http://","");
            query = query.Replace(context.Request.Url.Host, "");
            query = query.Replace(":" + context.Request.Url.Port, "");
            string response = "";
            TimeSeries result = _parser.GetResults(query);
            context.Response.AppendHeader("Access-Control-Allow-Origin", "*");

            if (result == null)
            {
                response = string.Format("<HTML><BODY><P>You asked for {0}...</P><P>Here it is</P>", query);
                response += "<p>Couldn't find a matching time series :(.</P>";
                response += "</BODY></HTML>";
                context.Response.ContentType = "text/html";
            }
            else
            {
                context.Response.ContentType = "text/json";
                response = JSONTimeSeries(result);
            }

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(response);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.Close();
            }

        }

        private static string JSONTimeSeries(TimeSeries ts)
        {
            //StringBuilder sb = new StringBuilder();
            //StringWriter sw = new StringWriter(sb);

            JObject result = new JObject(
                    new JProperty("Name",ts.name),
                    new JProperty("Start",ts.Start),
                    new JProperty("End",ts.End),
                    new JProperty("NoDataValue",ts.NullValue),
                    new JProperty("TimeStep",ts.timeStep.Name)
                );

            object[] entries = new JObject[ts.count()];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new JObject(
                        new JProperty("Date",ts.timeForItem(i)),
                        new JProperty("Value",ts[i])
                    );
            }

            result.Add("Entries", new JArray(entries));
            return result.ToString();
            //using (JsonWriter writer = new JsonTextWriter(sw))
            //{
            //    writer.Formatting = Formatting.Indented;
                
            //    writer.WriteStartObject();
            //    writer.
            //}
        }

        public static void Main(string[] args)
        {
            var server = new SourceHTTPServer(9876);
            server.Listen();
            Console.ReadLine();
        }
    }
}
