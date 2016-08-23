using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;

namespace FlowMatters.Source.Veneer.CORS
{
    public class CORSMessageInspector : IDispatchMessageInspector
    {
        private Dictionary<string, string> requiredHeaders;
        internal const string Origin = "Origin";

        public CORSMessageInspector(Dictionary<string, string> headers)
        {
            requiredHeaders = headers ?? new Dictionary<string, string>();
        }

        public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            List<String> knownOrigins = new List<string>
            {
                "http://www.flowmatters.com.au",
                "http://flowmatters.com.au",
                "http://hydrograph.io",
                "http://www.hydrograph.io",
                "http://staging.hydrograph.io"
            };

            HttpRequestMessageProperty httpProp = (HttpRequestMessageProperty)request.Properties[HttpRequestMessageProperty.Name];

            if (knownOrigins.Contains(httpProp.Headers[Origin]))
                return httpProp.Headers[Origin];
            return null;
        }

        public void BeforeSendReply(ref Message reply, object correlationState)
        {
            if (correlationState != null)
            {
                var httpHeader = reply.Properties["httpResponse"] as HttpResponseMessageProperty;
                httpHeader.Headers.Add("Access-Control-Allow-Origin", (string)correlationState);
                foreach (var item in requiredHeaders)
                {
                    httpHeader.Headers.Add(item.Key, item.Value);
                }
            }
        }
    }
}