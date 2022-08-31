using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem.ManagedExtensions;

namespace FlowMatters.Source.Veneer.Formatting
{
    public class TimeSeriesResponseFormatter : IDispatchMessageFormatter
    {
        public IDispatchMessageFormatter DefaultDispatchMessageFormatter { get; set; }

        // Reference from http://stackoverflow.com/a/23980924
        public void DeserializeRequest(Message message, object[] parameters)
        {
            throw new NotSupportedException("This is a reply-only formatter");
        }

        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            string accept = WebOperationContext.Current.IncomingRequest.Accept ?? "";

            if (accept.Contains("text/csv") || accept.Contains("application/csv"))
            {
                WebOperationContext.Current.OutgoingResponse.ContentType = accept;
                return CSVMessage(messageVersion, (TimeSeriesResponse) result);
            }

            WebOperationContext.Current.OutgoingResponse.ContentType =
                WebOperationContext.Current.OutgoingResponse.ContentType ?? "application/json";
            return DefaultDispatchMessageFormatter.SerializeReply(messageVersion, parameters, result);
        }

        public Message CSVMessage(MessageVersion messageVersion, TimeSeriesResponse origResult)
        {
            MultipleTimeSeries result = null;
            if (origResult is MultipleTimeSeries)
                result = (MultipleTimeSeries) origResult;
            else if (origResult is TimeSeriesFullSummary)
                result = new MultipleTimeSeries(origResult as TimeSeriesFullSummary);
            else // SimpleTimeSeries
                result = new MultipleTimeSeries(((SimpleTimeSeries) origResult).Slim());

            StringBuilder sb = new StringBuilder();
            sb.Append(BuildHeader(result));
            sb.AppendLine("-----------------------");
            sb.Append(BuildBody(result));

            Message reply = Message.CreateMessage(messageVersion, null, new RawBodyWriter(sb.ToString()));
            reply.Properties.Add(WebBodyFormatMessageProperty.Name,
                new WebBodyFormatMessageProperty(WebContentFormat.Raw));
            HttpResponseMessageProperty httpResp = new HttpResponseMessageProperty();
            reply.Properties.Add(HttpResponseMessageProperty.Name, httpResp);
            httpResp.Headers[HttpResponseHeader.ContentType] = "text/csv";
            return reply;
        }

        private string BuildHeader(MultipleTimeSeries multi)
        {
            string[] fields = new[]
            {
                "RunNumber","NetworkElement","FunctionalUnit",
                "RecordingElement","RecordingVariable",
                "Units","StartDate","EndDate","NoDataValue",
                "Min","Mean","Max","Sum","TimeStep"
            };

            StringBuilder result = new StringBuilder();
            foreach (string field in fields)
            {
                string[] values = multi.TimeSeries.Select(ts => SafeGet(ts, field)).ToArray();
                if (values.All(v => v == "-")) continue;

                string line = field + ",";
                string ditto = "...";
                if ((values.Length>1)&&(values.Distinct().Count() == 1)&&(values[0].Length>ditto.Length))
                {
                    line += values[0] + ',';
                    line += string.Join(",", Enumerable.Repeat(ditto, values.Length - 1));
                }
                else
                {
                    line += String.Join(",", values);
                }
                result.AppendLine(line);
            }

            return result.ToString();
        }

        private string SafeGet(object o, string field)
        {
            var result = o.GetMemberValue(field);
            return (result??"-").ToString();
        }

        private string BuildBody(MultipleTimeSeries multi)
        {
            SlimTimeSeries refTS = multi.TimeSeries[0] as SlimTimeSeries;
            if (refTS == null)
            {
                return "";
            }

            StringBuilder result = new StringBuilder();

            // Assumes
            // * If the first time series has values, then all have values
            // * all time series are the same length
            int len = refTS.Values.Length;

            for (int i = 0; i < len; i++)
            {
                string line = refTS.DateForTimeStep(i)+",";
                line += String.Join(",", multi.TimeSeries.OfType<SlimTimeSeries>().Select(ts => ts.Values[i]));
                result.AppendLine(line);
            }

            return result.ToString();
        }
    }

    class RawBodyWriter : BodyWriter
    {
        string contents;

        public RawBodyWriter(string contents)
            : base(true)
        {
            this.contents = contents;
        }

        protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
        {
            writer.WriteStartElement("Binary");
            byte[] bytes = Encoding.UTF8.GetBytes(this.contents);
            writer.WriteBase64(bytes, 0, bytes.Length);
            writer.WriteEndElement();
        }
    }
}