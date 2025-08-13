using CoreWCF.Description;
using CoreWCF.Dispatcher;
using CoreWCF.Channels;

namespace FlowMatters.Source.Veneer.Formatting
{
    public class ReplyFormatSwitchBehaviour : IEndpointBehavior
    {
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, CoreWCF.Dispatcher.ClientRuntime clientRuntime)
        {
        }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, CoreWCF.Dispatcher.EndpointDispatcher endpointDispatcher)
        {
            foreach (var operation in endpoint.Contract.Operations)
            {
                var formatter = GetReplyDispatchFormatter(operation, endpoint);
                if (formatter != null)
                {
                    endpointDispatcher.DispatchRuntime.Operations[operation.Name].Formatter = formatter;
                }
            }
        }

        public void Validate(ServiceEndpoint endpoint)
        {
        }

        private IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint)
        {
            switch (operationDescription.Name)
            {
                case "GetTimeSeries":
                case "GetAggregatedTimeSeries":
                    return new TimeSeriesResponseFormatter();
                case "GetTabulatedResults":
                case "ModelTable":
                    return new TableResponseFormatter();
                default:
                    return null;
            }
        }
    }
}