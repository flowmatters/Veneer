using System.Collections.Generic;
using CoreWCF.Channels;
using CoreWCF.Description;
using CoreWCF.Dispatcher;

namespace FlowMatters.Source.Veneer.CORS
{
    public class EnableCrossOriginResourceSharingBehavior : IEndpointBehavior
    {
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
        }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            var requiredHeaders = new Dictionary<string, string>();

            requiredHeaders["Access-Control-Request-Method"] = "POST,GET,PUT,DELETE,OPTIONS";
            requiredHeaders["Access-Control-Allow-Headers"] = "X-Requested-With,Content-Type";

            endpointDispatcher.DispatchRuntime.MessageInspectors.Add(new CORSMessageInspector(requiredHeaders));
        }

        public void Validate(ServiceEndpoint endpoint)
        {
        }
    }
}