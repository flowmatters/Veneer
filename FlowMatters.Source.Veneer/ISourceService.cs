using System.Data;
using System.IO;
using System.ServiceModel;
using CoreWCF.Web;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer.ExchangeObjects;

namespace FlowMatters.Source.WebServer
{
    [ServiceContract]
    public interface ISourceService
    {
        //[OperationContract]
        //[WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        //string GetRoot();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "OPTIONS", UriTemplate = "*")]
        void GetOptions();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        VeneerStatus GetRoot();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/shutdown")]
        void ShutdownServer();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Scenario)]
        void SetScenario(string scenario);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Files)]
        Stream GetFile(string fn);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.FilesQuery)]
        Stream GetFileQuery(string fn, string version);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Resources)]
        Stream GetResource(string resourceName);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Network)]
        GeoJSONNetwork GetNetwork();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.NetworkGeographic)]
        GeoJSONNetwork GetNetworkGeographic();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Node)]
        GeoJSONFeature GetNode(string nodeId);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Link)]
        GeoJSONFeature GetLink(string linkId);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Runs)]
        RunLink[] GetRunList();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Runs, RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        [FaultContract(typeof(SimulationFault))]
        void TriggerRun(RunParameters parameters);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.RunResults)]
        RunSummary GetRunResults(string runId);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "DELETE", UriTemplate = UriTemplates.RunResults)]
        void DeleteRun(string runId);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TimeSeries)]
        TimeSeriesResponse GetTimeSeries(string runId, string networkElement, string recordingElement,
                                                string variable, string fromDate, string toDate, string precision,
                                                string aggregation, string aggfn);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json,
                   UriTemplate = UriTemplates.AggregatedTimeSeries)]
        TimeSeriesResponse GetAggregatedTimeSeries(string runId, string networkElement, string recordingElement,
                                                   string variable, string aggregation, string fromDate, string toDate,
                                                   string precision);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TabulatedResults)]
        DataTable GetTabulatedResults(string runId, string networkElement, string recordingElement,
                                             string variable, string functions);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/functions/{functionName}", RequestFormat = WebMessageFormat.Json)]
        void SetFunction(string functionName, FunctionValue value);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/functions", ResponseFormat = WebMessageFormat.Json)]
        FunctionValue[] GetFunctionList();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables", ResponseFormat = WebMessageFormat.Json)]
        VariableSummary[] GetInputList();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}", ResponseFormat = WebMessageFormat.Json)]
        VariableSummary GetInput(string variableName);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/TimeSeries",
                   ResponseFormat = WebMessageFormat.Json)]
        SimpleTimeSeries GetInputTimeSeries(string variableName);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/TimeSeries",
                   RequestFormat = WebMessageFormat.Json)]
        void ChangeInputTimeSeries(string variableName, SimpleTimeSeries newTimeSeries);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/Piecewise", ResponseFormat = WebMessageFormat.Json)]
        SimplePiecewise GetPiecewiseLinear(string variableName);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/Piecewise",
                   RequestFormat = WebMessageFormat.Json)]
        void ChangePiecewiseLinear(string variableName, SimplePiecewise newPiecewise);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.InputSets, ResponseFormat = WebMessageFormat.Json)]
        InputSetSummary[] GetInputSets();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.InputSets, RequestFormat = WebMessageFormat.Json)]
        void CreateInputSet(InputSetSummary newInputSet);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.InputSet, RequestFormat = WebMessageFormat.Json)]
        public void UpdateInputSet(string inputSetName, InputSetSummary summary);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.RunInputSet, RequestFormat = WebMessageFormat.Json)]
        void RunInputSet(string inputSetName, string action);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/recorders", RequestFormat = WebMessageFormat.Json)]
        void UpdateRecorders(RecordingInstructions ri);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSources, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataGroupItem[] GetDataSources();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSourceGroup, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataGroupItem GetDataSource(string dataSourceGroup);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.DataSources, RequestFormat = WebMessageFormat.Json)]
        void CreateDataSource(SimpleDataGroupItem newItem);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.DataSourceGroup, ResponseFormat = WebMessageFormat.Json)]
        void UpdateDataSource(string dataSourceGroup, SimpleDataGroupItem newItem);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "DELETE", UriTemplate = UriTemplates.DataSourceGroup,
                   ResponseFormat = WebMessageFormat.Json)]
        void DeleteDataSource(string dataSourceGroup);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItem, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataItem GetDataGroupItem(string dataSourceGroup, string inputSet);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupMultipleItemDetails, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataDetails[] GetMultipleDataGroupItemDetails(string dataSourceGroup, string name);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItemDetails, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataDetails GetDataGroupItemDetails(string dataSourceGroup, string inputSet, string item);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/ironpython", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        IronPythonResponse RunIronPython(IronPythonScript script);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.ScenarioTablesIndex,
                   ResponseFormat = WebMessageFormat.Json)]
        ModelTableIndex ModelTableIndex();

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.ScenarioTables, ResponseFormat = WebMessageFormat.Json)]
        public DataTable ModelTable(string table);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Configuration, ResponseFormat = WebMessageFormat.Json)]
        string[] GetConfiguration(string element);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.Projection, ResponseFormat = WebMessageFormat.Json)]
        void AssignProjection(ProjectionInfo p);

        // TODO: RM-20834 RM-21455 Attributes moved here from SourceService
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.CustomEndPoint, ResponseFormat = WebMessageFormat.Json)]
        IronPythonResponse RunCustomEndPoint(string action, string[] parameters);
    }
}