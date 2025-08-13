using System.Data;
using System.IO;
using System.ServiceModel;
using CoreWCF.Web;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer;
using FlowMatters.Source.WebServer.ExchangeObjects;

namespace FlowMatters.Source.Veneer
{
    [ServiceContract]
    public interface ISourceService
    {
        //[OperationContract]
        //[WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        //string GetRoot();

        [OperationContract]
        [WebInvoke(Method = "OPTIONS", UriTemplate = "*")]
        void GetOptions();

        [OperationContract]
        [WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        VeneerStatus GetRoot();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/shutdown")]
        void ShutdownServer();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Scenario)]
        void SetScenario(string scenario);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Files)]
        Stream GetFile(string fn);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.FilesQuery)]
        Stream GetFileQuery(string fn, string version);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Resources)]
        Stream GetResource(string resourceName);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Network)]
        GeoJSONNetwork GetNetwork();

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.NetworkGeographic)]
        GeoJSONNetwork GetNetworkGeographic();

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Node)]
        GeoJSONFeature GetNode(string nodeId);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Link)]
        GeoJSONFeature GetLink(string linkId);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Runs)]
        RunLink[] GetRunList();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Runs, RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        [FaultContract(typeof(SimulationFault))]
        void TriggerRun(RunParameters parameters);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.RunResults)]
        RunSummary GetRunResults(string runId);

        [OperationContract]
        [WebInvoke(Method = "DELETE", UriTemplate = UriTemplates.RunResults)]
        void DeleteRun(string runId);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TimeSeries)]
        TimeSeriesResponse GetTimeSeries(string runId, string networkElement, string recordingElement,
                                                string variable, string fromDate, string toDate, string precision,
                                                string aggregation, string aggfn);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json,
                   UriTemplate = UriTemplates.AggregatedTimeSeries)]
        TimeSeriesResponse GetAggregatedTimeSeries(string runId, string networkElement, string recordingElement,
                                                   string variable, string aggregation, string fromDate, string toDate,
                                                   string precision);

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TabulatedResults)]
        DataTable GetTabulatedResults(string runId, string networkElement, string recordingElement,
                                             string variable, string functions);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/functions/{functionName}", RequestFormat = WebMessageFormat.Json)]
        void SetFunction(string functionName, FunctionValue value);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/functions", ResponseFormat = WebMessageFormat.Json)]
        FunctionValue[] GetFunctionList();

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables", ResponseFormat = WebMessageFormat.Json)]
        VariableSummary[] GetInputList();

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}", ResponseFormat = WebMessageFormat.Json)]
        VariableSummary GetInput(string variableName);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/TimeSeries",
                   ResponseFormat = WebMessageFormat.Json)]
        SimpleTimeSeries GetInputTimeSeries(string variableName);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/TimeSeries",
                   RequestFormat = WebMessageFormat.Json)]
        void ChangeInputTimeSeries(string variableName, SimpleTimeSeries newTimeSeries);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/Piecewise", ResponseFormat = WebMessageFormat.Json)]
        SimplePiecewise GetPiecewiseLinear(string variableName);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/Piecewise",
                   RequestFormat = WebMessageFormat.Json)]
        void ChangePiecewiseLinear(string variableName, SimplePiecewise newPiecewise);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.InputSets, ResponseFormat = WebMessageFormat.Json)]
        InputSetSummary[] GetInputSets();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.InputSets, RequestFormat = WebMessageFormat.Json)]
        void CreateInputSet(InputSetSummary newInputSet);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.InputSet, RequestFormat = WebMessageFormat.Json)]
        public void UpdateInputSet(string inputSetName, InputSetSummary summary);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.RunInputSet, RequestFormat = WebMessageFormat.Json)]
        void RunInputSet(string inputSetName, string action);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/recorders", RequestFormat = WebMessageFormat.Json)]
        void UpdateRecorders(RecordingInstructions ri);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSources, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataGroupItem[] GetDataSources();

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSourceGroup, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataGroupItem GetDataSource(string dataSourceGroup);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.DataSources, RequestFormat = WebMessageFormat.Json)]
        void CreateDataSource(SimpleDataGroupItem newItem);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.DataSourceGroup, ResponseFormat = WebMessageFormat.Json)]
        void UpdateDataSource(string dataSourceGroup, SimpleDataGroupItem newItem);

        [OperationContract]
        [WebInvoke(Method = "DELETE", UriTemplate = UriTemplates.DataSourceGroup,
                   ResponseFormat = WebMessageFormat.Json)]
        void DeleteDataSource(string dataSourceGroup);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItem, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataItem GetDataGroupItem(string dataSourceGroup, string inputSet);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupMultipleItemDetails, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataDetails[] GetMultipleDataGroupItemDetails(string dataSourceGroup, string name);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItemDetails, ResponseFormat = WebMessageFormat.Json)]
        SimpleDataDetails GetDataGroupItemDetails(string dataSourceGroup, string inputSet, string item);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/ironpython", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        IronPythonResponse RunIronPython(IronPythonScript script);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.ScenarioTablesIndex,
                   ResponseFormat = WebMessageFormat.Json)]
        ModelTableIndex ModelTableIndex();

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.ScenarioTables, ResponseFormat = WebMessageFormat.Json)]
        public DataTable ModelTable(string table);

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Configuration, ResponseFormat = WebMessageFormat.Json)]
        string[] GetConfiguration(string element);

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.Projection, ResponseFormat = WebMessageFormat.Json)]
        void AssignProjection(ProjectionInfo p);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.CustomEndPoint, ResponseFormat = WebMessageFormat.Json)]
        IronPythonResponse RunCustomEndPoint(string action, string[] parameters);

        [OperationContract]
        [WebGet(UriTemplate = "/ping", ResponseFormat = WebMessageFormat.Json)]
        string Ping();
    }
}