using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem;
using RiverSystem.ApplicationLayer.Interfaces;
using RiverSystem.Controls.Icons;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions;
using RiverSystem.Functions.Variables;
using RiverSystem.ManagedExtensions;
using RiverSystem.ScenarioExplorer.ParameterSet;
using TIME.Core;
using TIME.DataTypes;
using TIME.Management;
using TIME.ScenarioManagement;

namespace FlowMatters.Source.WebServer
{
    [ServiceContract,ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    [ServiceKnownType(typeof(double[]))]
    [ServiceKnownType(typeof(double[][]))]
    [ServiceKnownType(typeof(double[][][]))]
    [ServiceKnownType(typeof(double[][][][]))]
    public class SourceService //: ISourceService
    {
        public Dictionary<int,string[]> RunLogs = new Dictionary<int,string[]>();

        public RiverSystemScenario Scenario { get; set; }
        private ScriptRunner scriptRunner = new ScriptRunner();

        public bool AllowScript { get; set; }

        public bool RunningInGUI { get; set; }

        public event ServerLogListener LogGenerator;

        public IProjectHandler<RiverSystemProject> ProjectHandler { get; set; }

        public SourceService()
        {
            AllowScript = false;
            RunningInGUI = true;
        }

        [OperationContract]
        [WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        public string GetRoot()
        {
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            Log("Requested /");
            return "Root node of service";
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/shutdown")]
        public void ShutdownServer()
        {
            Log("Shutdown Requested");
            if (!RunningInGUI)
            {
                Environment.Exit(0);
            }
            else
            {
                Log("Shutdown not supported");
            }

            throw new Exception("Shutdown not supported");
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Files)]
        public Stream GetFile(string fn)
        {
            Log(string.Format("Requested {0}",fn));

            MemoryStream ms = new MemoryStream();
            string basePath = Scenario.Project.FileDirectory;
            string filename = basePath + "\\" + fn;
            WebOperationContext.Current.OutgoingResponse.ContentType = ResourceHelpers.ContentTypeForFilename(filename);
            byte[] contents = File.ReadAllBytes(filename);
            ms.Write(contents,0,contents.Length);
            ms.Position = 0;
            return ms;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.FilesQuery)]
        public Stream GetFileQuery(string fn, string version)
        {
            return GetFile(fn);
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate = UriTemplates.Resources)]
        public Stream GetResource(string resourceName)
        {
            Log(string.Format("Requested resource {0}", resourceName));
            MemoryStream ms = new MemoryStream();
            Assembly rsFormsAssembly = Assembly.GetEntryAssembly();

            Bitmap resource = ResourceHelpers.FindByName(resourceName);

            resource.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            WebOperationContext.Current.OutgoingResponse.ContentType = "image/png";
            return ms;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Network)]
        public GeoJSONNetwork GetNetwork()
        {
            Log("Requested network");
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return new GeoJSONNetwork(Scenario.Network);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Node)]
        public GeoJSONFeature GetNode(string nodeId)
        {
            Log(string.Format("Requested node {0} (NOT IMPLEMENTED)", nodeId));
            return null;                
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Link)]
        public GeoJSONFeature GetLink(string linkId)
        {
            Log(string.Format("Requested link {0} (NOT IMPLEMENTED)", linkId));
            return null;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Runs)]
        public RunLink[] GetRunList()
        {
            Log("Requested run list");
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            var runs = Scenario.Project.ResultManager.AllRuns().ToArray();
            RunLink[] links = new RunLink[runs.Length];
            for(int i = 0; i < runs.Length; i++)
            {
                links[i] = new RunLink
                    {
                        RunName = runs[i].Name,
                        RunUrl = "/runs/" + runs[i].RunNumber
                    };
            }

            Log(string.Format("Requested /runs, returning links to {0} runs",links.Length));
            return links;
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Runs,
         RequestFormat = WebMessageFormat.Json,ResponseFormat = WebMessageFormat.Json)]
        [FaultContract(typeof(SimulationFault))]
        public void TriggerRun(RunParameters parameters)
        {
            Log("Triggering a run.");
            ScenarioInvoker si = new ScenarioInvoker { Scenario = Scenario };

            ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
            LogAction runLogger = (sender, args) =>
            {
                messages.Enqueue(args.Entry.Message);
            };
            TIME.Management.Log.MessageRecieved += runLogger;

            try
            {
                si.RunScenario(parameters, RunningInGUI, LogGenerator);
            }
            catch (Exception e)
            {
                Log("Run Failed");
                Log(e.Message);
                Log(e.StackTrace);
                throw new WebFaultException<SimulationFault>(new SimulationFault(e), HttpStatusCode.InternalServerError);
            }
            finally
            {
                TIME.Management.Log.MessageRecieved -= runLogger;
            }

            RunLogs[Scenario.Project.ResultManager.AllRuns().Last().RunNumber] = messages.ToArray();
            Run r = RunsForId("latest")[0];

            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Location",
                                                                     WebOperationContext.Current.IncomingRequest.Headers
                                                                         ["Location"] +
                                                                     String.Format("runs/{0}", r.RunNumber));
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.RunResults)]
        public RunSummary GetRunResults(string runId)
        {
            Log(String.Format("Requested run results ({0})",runId));
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            string msg = "";
            string[] log;
            Run run = RunsForId(runId)[0];

            if (runId.ToLower() == "latest")
            {
                msg = "latest run";
            }
            else
            {
                msg = string.Format("run with id={0}", runId);
//                var idx = int.Parse(runId) - 1;
//                log = RunLogs[idx];
            }
            log = RunLogs[run.RunNumber];


            Log("Requested " + msg);

            if (run == null)
            {
                Log(string.Format("Run {0} not found", runId));
                return null;
            }
            var result = new RunSummary(run);
            result.RunLog = log;
            return result;
        }

        [OperationContract]
        [WebInvoke(Method = "DELETE", UriTemplate = UriTemplates.RunResults)]
        public void DeleteRun(string runId)
        {
            Log(String.Format("Deleting run results ({0})", runId));
            int id = -1;
            if (runId == "latest")
            {
                id = Scenario.Project.ResultManager.AllRuns().Last().RunNumber;
            }
            else
            {
                id = int.Parse(runId);
                //RunLogs.Remove(id);
            }
            RunLogs.Remove(id);
            Scenario.Project.ResultManager.RemoveRun(id);
        }

        private Run[] RunsForId(string id)
        {
            if(id.ToLower() == "latest")
                return new Run[] {Scenario.Project.ResultManager.AllRuns().LastOrDefault()};

            if (id.ToLower() == UriTemplates.MatchAll.ToLower())
            {
                var x = Scenario.Project.ResultManager.AllRuns();
                return x.ToArray();
            }
            return new Run[] { Scenario.Project.ResultManager.AllRuns().FirstOrDefault(x => x.RunNumber.ToString(CultureInfo.InvariantCulture) == id)};
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TimeSeries)]
        public TimeSeriesResponse GetTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable)
        {
            Log(String.Format("Requested time series {0}/{1}/{2}/{3}",runId,networkElement,recordingElement,variable));
            Tuple<TimeSeriesLink, TimeSeries>[] result = MatchTimeSeries(runId, networkElement, recordingElement, variable);

            if (result.Length == 1) 
                return SimpleTimeSeries(result[0].Item2);
            return CreateMultipleTimeSeries(result);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json,
            UriTemplate = UriTemplates.AggregatedTimeSeries)]
        public TimeSeriesResponse GetAggregatedTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable, string aggregation)
        {
            Log(String.Format("Requested {4} time series {0}/{1}/{2}/{3}", runId, networkElement, recordingElement, variable,aggregation));
            Tuple<TimeSeriesLink, TimeSeries>[] result = MatchTimeSeries(runId, WebUtility.HtmlDecode(networkElement), WebUtility.HtmlDecode(recordingElement), WebUtility.HtmlDecode(variable));

            result = result.Select(res=>new Tuple<TimeSeriesLink,TimeSeries>(res.Item1,AggregateTimeSeries(res.Item2, aggregation))).ToArray();
            if(result.Length==1)
                return SimpleTimeSeries(result[0].Item2);
            return CreateMultipleTimeSeries(result);
        }

        private TimeSeriesResponse CreateMultipleTimeSeries(Tuple<TimeSeriesLink, TimeSeries>[] result)
        {
            if (result.Length == 0)
                return TimeSeriesNotFound();
            return new MultipleTimeSeries(result);
        }

        [OperationContract]
        [WebInvoke(Method="PUT",UriTemplate = "/functions/{functionName}",RequestFormat = WebMessageFormat.Json)]
        public void SetFunction(string functionName, FunctionValue value)
        {
            Log(String.Format("Updating function {0}", functionName));
            functionName = "$" + functionName;
            var function = Scenario.Network.FunctionManager.Functions.FirstOrDefault(f => f.Name == functionName);
            if (function != null)
            {
                Log(String.Format("Setting ${0}={1}", functionName, value.Expression));
                function.Expression = value.Expression;
            }
            else
            {
                Log(String.Format("Function not found {0}", functionName));
                Log("Available Functions:");
                foreach (var fn in Scenario.Network.FunctionManager.Functions)
                {
                    Log(String.Format("Name={0} / FullName = {1}", fn.Name, fn.FullName));
                }
            }
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/functions", ResponseFormat = WebMessageFormat.Json)]
        public FunctionValue[] GetFunctionList()
        {
            Log("Requested function list");
            FunctionValue[] result = new FunctionValue[Scenario.Network.FunctionManager.Functions.Count];
            for (var i = 0; i < result.Count(); i++)
            {
                Function fn = Scenario.Network.FunctionManager.Functions[i];
                FunctionValue fv = new FunctionValue();
                fv.Name = fn.Name;
                fv.Expression = fn.Expression;
                result[i] = fv;
            }
            return result;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables", ResponseFormat = WebMessageFormat.Json)]
        public VariableSummary[] GetInputList()
        {
            Log("Requested Variable List");
            VariableSummary[] result = new VariableSummary[Scenario.Network.FunctionManager.Variables.Count];
            for (var i = 0; i < result.Count(); i++)
                result[i] = new VariableSummary(Scenario.Network.FunctionManager.Variables[i],Scenario);
            return result;
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate="/variables/{variableName}",ResponseFormat=WebMessageFormat.Json)]
        public VariableSummary GetInput(string variableName)
        {
            Log("Requested Variable : " + variableName);
            return new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)), Scenario);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/TimeSeries", ResponseFormat = WebMessageFormat.Json)]
        public SimpleTimeSeries GetInputTimeSeries(string variableName)
        {
            Log(String.Format("Requested time series for {0}", variableName));
            return (new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)), Scenario)).TimeSeriesData;
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/TimeSeries",
            RequestFormat = WebMessageFormat.Json)]
        public void ChangeInputTimeSeries(string variableName, SimpleTimeSeries newTimeSeries)
        {
            Log(String.Format("Updating time series for {0}", variableName));
            VariableSummary summ =
                new VariableSummary(
                    Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)),
                    Scenario);
            summ.UpdateTimeSeries(newTimeSeries);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/Piecewise", ResponseFormat = WebMessageFormat.Json)]
        public SimplePiecewise GetPiecewiseLinear(string variableName)
        {
            Log(String.Format("Requested piecewise linear function for {0}", variableName));
            return (new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)), Scenario)).PiecewiseFunctionData;
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/Piecewise",
            RequestFormat = WebMessageFormat.Json)]
        public void ChangePiecewiseLinear(string variableName, SimplePiecewise newPiecewise)
        {
            Log(String.Format("Updating  piecewise linear function for {0}", variableName));
            VariableSummary summ =
                new VariableSummary(
                    Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)),
                    Scenario);
            summ.UpdatePiecewise(newPiecewise);
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate=UriTemplates.InputSets,ResponseFormat = WebMessageFormat.Json)]
        public InputSetSummary[] GetInputSets()
        {
            Log("Requested input sets");
            var sets = new InputSets(Scenario);
            InputSetSummary[] result = new InputSetSummary[sets.All.Count];
            for (int i=  0; i < result.Length;i++)
            {
                var inputSet = sets.All[i];
                result[i] = new InputSetSummary
                {
                    URL = String.Format("{0}/{1}",UriTemplates.InputSets,URLSafeString(inputSet.Name)),
                    Name = inputSet.Name,
                    Configuration = sets.Instructions(inputSet)
                };

                string fn = sets.Filename(inputSet);

                if (!String.IsNullOrEmpty(fn))
                {
                    result[i].Filename = fn;
                    result[i].ReloadOnRun = sets.ReloadOnRun(inputSet);
                }
            }
            return result;
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.InputSets, RequestFormat = WebMessageFormat.Json)]
        public void CreateInputSet(InputSetSummary newInputSet)
        {
            Log("Creating new Input Set: " + newInputSet.Name);

            var sets = new InputSets(Scenario);
            sets.Create(newInputSet);
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = UriTemplates.InputSet, RequestFormat = WebMessageFormat.Json)]
        public void UpdateInputSet(string inputSetName, InputSetSummary summary)
        {
            Log("Updating Input Set Commands for " + inputSetName);
            var sets = new InputSets(Scenario);
            InputSet set = sets.Find(inputSetName);
            sets.UpdateInstructions(set, summary.Configuration);
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.RunInputSet,RequestFormat = WebMessageFormat.Json)]
        public void RunInputSet(string inputSetName,string action)
        {
            if (action != "run")
            {
                throw new InvalidOperationException("Cannot perform action " + action + " on input sets");
            }
            Log("Applying inout set " + inputSetName);
            var sets = new InputSets(Scenario);
            sets.Run(inputSetName);
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/recorders", RequestFormat = WebMessageFormat.Json)]
        public void UpdateRecorders(RecordingInstructions ri)
        {

            Log("Updating recorders");
            ProjectViewTable table = Scenario.ProjectViewTable();

            foreach (string s in ri.RecordNone)
            {
                Log("OFF: " + s);
                SwitchRecording(ri.Parse(s), false);
            }
            foreach (string s in ri.RecordAll)
            {
                Log("ON: " + s);
                SwitchRecording(ri.Parse(s), true);
            }
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSources,ResponseFormat = WebMessageFormat.Json)]
        public SimpleDataGroupItem[] GetDataSources()
        {
            var dm = Scenario.Network.DataManager;

            return dm.DataGroups.Select(dg => new SimpleDataGroupItem(dg)).ToArray();
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataSourceGroup, ResponseFormat = WebMessageFormat.Json)]
        public SimpleDataGroupItem GetDataSource(string dataSourceGroup)
        {
            return GetSimpleDataSourceInternal(dataSourceGroup, false);
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.DataSources, RequestFormat = WebMessageFormat.Json)]
        public void CreateDataSource(SimpleDataGroupItem newItem)
        {
            var dm = Scenario.Network.DataManager;

            var existing = dm.DataGroups.FirstOrDefault(ds => ds.Name == newItem.Name);

            if(existing!=null)
            {
                dm.RemoveGroup(existing);
            }

            newItem.AddToScenario(Scenario);
        }

        private SimpleDataGroupItem GetSimpleDataSourceInternal(string dataSourceGroup, bool summary)
        {
            var dm = Scenario.Network.DataManager;

            var res =
                dm.DataGroups.FirstOrDefault(
                    ds => SimpleDataGroupItem.MakeID(ds) == (UriTemplates.DataSources + "/" + dataSourceGroup));
            if (res == null)
                ResourceNotFound();
            return new SimpleDataGroupItem(res, summary);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItem, ResponseFormat = WebMessageFormat.Json)]
        public SimpleDataItem GetDataGroupItem(string dataSourceGroup,string inputSet)
        {
            var grp = GetSimpleDataSourceInternal(dataSourceGroup,false);
            if (grp == null)
                return null;

            var result =
                grp.Items.FirstOrDefault(item => item.MatchInputSet(inputSet));

            if(result==null)
                ResourceNotFound();
            return result;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupMultipleItemDetails, ResponseFormat = WebMessageFormat.Json)]
        public SimpleDataDetails[] GetMultipleDataGroupItemDetails(string dataSourceGroup, string name)
        {
            var grp = GetSimpleDataSourceInternal(dataSourceGroup,true);
            if (grp == null)
                return null;

            List<SimpleDataDetails> result = new List<SimpleDataDetails>();
            foreach (var item in grp.Items)
            {
                var tmp = item.Details.FirstOrDefault(d =>
                {
                    var safeName = URLSafeString(d.Name);
                    return safeName == name || Regex.IsMatch(safeName, name);
                });
                if (tmp != null)
                {
                    tmp.Name = item.Name + "/" + tmp.Name;
                    tmp.Expand();
                    result.Add(tmp);
                }
            }

            if(result.Count==0)
                ResourceNotFound();
            return result.ToArray();
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.DataGroupItemDetails, ResponseFormat = WebMessageFormat.Json)]
        public SimpleDataDetails GetDataGroupItemDetails(string dataSourceGroup, string inputSet,string item)
        {
            var retrieved = GetDataGroupItem(dataSourceGroup, inputSet);
            var result = retrieved.Details.FirstOrDefault(d => URLSafeString(d.Name) == item);

            if(result==null)
                ResourceNotFound();
            return result;
        }

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/ironpython", 
            RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        public IronPythonResponse RunIronPython(IronPythonScript script)
        {
            if (!AllowScript)
            {
                Log(String.Format("Attempt to run IronPython script, but AllowScript=false. Script:\n{0}", script.Script));
                WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Forbidden;
                return null;
            }
            Log(String.Format("Running IronyPython script:\n{0}",(script.Script.Length>80)?(script.Script.Substring(0,75)+"..."):script.Script));
            scriptRunner.Scenario = Scenario;
            scriptRunner.ProjectHandler = ProjectHandler;
            return scriptRunner.Run(script);
        }

        private void SwitchRecording(TimeSeriesLink query, bool record)
        {
            Dictionary<ProjectViewRow.RecorderFields, object> constraint = new Dictionary<ProjectViewRow.RecorderFields, object>();

            if (query.NetworkElement.Length > 0)
            {
                constraint[ProjectViewRow.RecorderFields.NetworkElementName] = query.NetworkElement;
            }

            if (query.RecordingElement.Length > 0)
            {
                constraint[ProjectViewRow.RecorderFields.ElementName] = query.RecordingElement;
            }

            var table = Scenario.ProjectViewTable();
            var rows = table.Select(constraint);
            var state = record ? RecordingStates.RecordAll : RecordingStates.RecordNone;
            foreach (var row in rows)
            {
                foreach (var recordable in row.ElementRecorder.RecordableAttributes)
                {
                    if ((query.RecordingVariable.Length == 0) ||
                        (recordable.FullKeyString.IndexOf(query.RecordingVariable, StringComparison.Ordinal) >= 0))
                    {
                        row.ElementRecorder.SetRecordingState(recordable.KeyString,recordable.KeyObject,state);
                    }
                }
            }
        }

        private TimeSeries AggregateTimeSeries(TimeSeries result, string aggregation)
        {
            if (result == null)
                return null;
            aggregation = aggregation.ToLower();
            var origUnits = result.units;

            string name = result.name;
            if (aggregation == "monthly")
                result = result.toMonthly();

            if (aggregation == "annual")
                result = result.toAnnual();
            result.name = name;
            if (origUnits != null)
                result.units = origUnits;
            return result;
        }

        private SimpleTimeSeries SimpleTimeSeries(TimeSeries result)
        {
            //WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return (result == null) ? TimeSeriesNotFound() : new SimpleTimeSeries(result);
        }
        
        private Tuple<TimeSeriesLink,TimeSeries>[] MatchTimeSeries(string runId, string networkElement, string recordingElement, string variable)
        {
            List<Tuple<TimeSeriesLink, TimeSeries>> result = new List<Tuple<TimeSeriesLink, TimeSeries>>();
            Run[] runs = RunsForId(runId);
            if (runs[0] == null) return result.ToArray();
            IEnumerable<Tuple<int,ProjectViewRow>> rows =
                runs.SelectMany(run=>run.RunParameters.Where(
                    r => MatchesElements(r, networkElement, recordingElement)).Select(row=>new Tuple<int,ProjectViewRow>(run.RunNumber,row)));

//            if (row == null) return null;

            foreach (Tuple<int, ProjectViewRow> entry in rows)
            {
                var row = entry.Item2;
                var runNumber = entry.Item1;
                result.AddRange(row.ElementRecorder.GetResultList().Where(er=>MatchesVariable(row,er,variable)).Select(
                    er =>
                    {
                        return new Tuple<TimeSeriesLink, TimeSeries>(RunSummary.BuildLink(er.Value,row,er.Key,runNumber),er.Value);
                    }));
            }
            return result.ToArray();
            //return row.ElementRecorder.GetResultList().FirstOrDefault(er => 
            //    (URLSafeString(er.Key.KeyString) == URLSafeString(variable))||
            //    ((er.Key.KeyString=="")&&(row.ElementName==variable))).Value;            
        }

        private bool MatchesVariable(ProjectViewRow row, KeyValuePair<AttributeRecordingState, TimeSeries> er, string variable)
        {
            if(variable == UriTemplates.MatchAll) return true;

            return (URLSafeString(er.Key.KeyString) == URLSafeString(variable)) ||
                ((er.Key.KeyString == "") && (row.ElementName == variable));
        }

        private SimpleTimeSeries TimeSeriesNotFound()
        {
            ResourceNotFound();
            return null;
        }

        private static void ResourceNotFound()
        {
            WebOperationContext ctx = WebOperationContext.Current;
            ctx.OutgoingResponse.StatusCode = System.Net.HttpStatusCode.NotFound;
        }

        private static bool MatchesElements(ProjectViewRow row, string networkElement, string recordingElement)
        {
            bool matchesNetworkElement = (networkElement == UriTemplates.MatchAll) ||
                                         (URLSafeString(row.NetworkElementName) == URLSafeString(networkElement));
            bool matchesRecordingElement = (recordingElement == UriTemplates.MatchAll) ||
                                           (URLSafeString(row.ElementName) == URLSafeString(recordingElement));
            return matchesNetworkElement && matchesRecordingElement;
        }

        public static string URLSafeString(string src)
        {
            return src.Replace("#","").Replace("/","%2F").Replace(":","");
        }

        protected void Log(string query)
        {
            if (LogGenerator != null)
                LogGenerator(this, query);
        }
    }
}
