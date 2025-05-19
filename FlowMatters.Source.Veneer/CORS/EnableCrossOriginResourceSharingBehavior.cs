using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreWCF.Channels;
using CoreWCF.Description;
using CoreWCF.Dispatcher;

namespace FlowMatters.Source.Veneer.CORS
{
    // TODO: RM-20834 RM-21455 Implement
    //public class EnableCrossOriginResourceSharingBehavior : BehaviorExtensionElement, IEndpointBehavior
    //{
    //    public override Type BehaviorType
    //    {
    //        get { return typeof (EnableCrossOriginResourceSharingBehavior); }
    //    }

    //    public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
    //    {
    //    }

    //    public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
    //    {
    //    }

    //    public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
    //    {
    //        var requiredHeaders = new Dictionary<string, string>();

    //        requiredHeaders["Access-Control-Request-Method"] = "POST,GET,PUT,DELETE,OPTIONS";
    //        requiredHeaders["Access-Control-Allow-Headers"] = "X-Requested-With,Content-Type";

    //        endpointDispatcher.DispatchRuntime.MessageInspectors.Add(new CORSMessageInspector(requiredHeaders));
    //    }

    //    public void Validate(ServiceEndpoint endpoint)
    //    {
    //    }

    //    protected override object CreateBehavior()
    //    {
    //        return new EnableCrossOriginResourceSharingBehavior();
    //    }
    //}
}
