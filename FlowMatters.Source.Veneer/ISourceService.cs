using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;

namespace FlowMatters.Source.WebServer
{
    [ServiceContract]
    public interface ISourceService
    {
        [OperationContract]
        [WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        string GetRoot();

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = "/runs")]
        object GetRunList();

            [OperationContract]
            [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = "/runs/{runId}")]
        object GetRunResults();
    }
}
