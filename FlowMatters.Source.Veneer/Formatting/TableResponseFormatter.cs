using System;
using System.Data;
using System.Linq;
using System.Net;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Text;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer.ExchangeObjects;
using Microsoft.Scripting.Utils;

namespace FlowMatters.Source.Veneer.Formatting
{
    public class TableResponseFormatter : IDispatchMessageFormatter
    {
        public void DeserializeRequest(Message message, object[] parameters)
        {
            throw new NotSupportedException("This is a reply-only formatter");
        }

        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            DataTable table = (DataTable) result;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(String.Join(",",
                Enumerable.Range(0, table.Columns.Count).Select(i => table.Columns[i].ColumnName)));
            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine(String.Join(",", Enumerable.Range(0, table.Columns.Count).Select(i => row[i].ToString())));
            }

            Message reply = Message.CreateMessage(messageVersion, null, new RawBodyWriter(sb.ToString()));
            reply.Properties.Add(WebBodyFormatMessageProperty.Name,
                new WebBodyFormatMessageProperty(WebContentFormat.Raw));
            HttpResponseMessageProperty httpResp = new HttpResponseMessageProperty();
            reply.Properties.Add(HttpResponseMessageProperty.Name, httpResp);
            httpResp.Headers[HttpResponseHeader.ContentType] = "text/csv";
            return reply;
        }
    }
}