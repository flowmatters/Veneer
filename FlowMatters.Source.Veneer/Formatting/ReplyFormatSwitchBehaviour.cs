using System;
using System.ServiceModel.Dispatcher;
using CoreWCF.Description;
using CoreWCF.Dispatcher;
using OperationDescription = System.ServiceModel.Description.OperationDescription;
using ServiceEndpoint = System.ServiceModel.Description.ServiceEndpoint;

namespace FlowMatters.Source.Veneer.Formatting
{
    // TODO: RM-20834 RM-21455 Implement
    //public class ReplyFormatSwitchBehaviour : WebHttpBehavior
    //{
    //    public ReplyFormatSwitchBehaviour(IServiceProvider serviceProvider) : base(serviceProvider)
    //    {
    //    }

    //    // TODO: RM-20834 RM-21455 Need the replacement for this, or somewhere to call it
    //    protected override IDispatchMessageFormatter GetReplyDispatchFormatter(
    //        OperationDescription operationDescription, ServiceEndpoint endpoint)
    //    {
    //        switch (operationDescription.Name)
    //        {
    //            case "GetTimeSeries":
    //            case "GetAggregatedTimeSeries":
    //                return new TimeSeriesResponseFormatter()
    //                {
    //                    DefaultDispatchMessageFormatter = base.GetReplyDispatchFormatter(operationDescription, endpoint)
    //                };
    //            case "GetTabulatedResults":
    //            case "ModelTable":
    //                return new TableResponseFormatter();
    //            default:
    //                return base.GetReplyDispatchFormatter(operationDescription, endpoint);
    //        }
    //    }
    //}
}