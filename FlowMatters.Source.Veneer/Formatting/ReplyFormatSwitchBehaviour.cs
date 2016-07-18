using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace FlowMatters.Source.Veneer.Formatting
{
    public class ReplyFormatSwitchBehaviour : WebHttpBehavior
    {
        protected override IDispatchMessageFormatter GetReplyDispatchFormatter(
            OperationDescription operationDescription, ServiceEndpoint endpoint)
        {
            switch (operationDescription.Name)
            {
                case "GetTimeSeries":
                case "GetAggregatedTimeSeries":
                    return new TimeSeriesResponseFormatter()
                    {
                        DefaultDispatchMessageFormatter = base.GetReplyDispatchFormatter(operationDescription, endpoint)
                    };
                default:
                    return base.GetReplyDispatchFormatter(operationDescription, endpoint);
            }
        }
    }
}