#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5
#define BEFORE_RECORDING_ATTRIBUTES_REFACTOR
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using CoreWCF.Web;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using FlowMatters.Source.Veneer.Formatting;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem;
using RiverSystem.ApplicationLayer.Interfaces;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions;
using RiverSystem.ManagedExtensions;
using RiverSystem.PreProcessing.ProjectionInfo;
using TIME.Core;
using TIME.DataTypes;
using TIME.DataTypes.Polygons;
using TIME.DataTypes.Utils;
using TIME.Management;
using TIME.ScenarioManagement;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.WebServer
{
    // TODO: RM-20834 RM-21455 If a class is marked with ServiceContractAttribute, it must be the only type in the hierarchy with ServiceContractAttribute.
    //       because the ServiceContractAttribute is on ISourceService it can't be here
    //[System.ServiceModel.ServiceContract,ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    [System.ServiceModel.ServiceKnownType(typeof(double[]))]
    [System.ServiceModel.ServiceKnownType(typeof(double[][]))]
    [System.ServiceModel.ServiceKnownType(typeof(double[][][]))]
    [System.ServiceModel.ServiceKnownType(typeof(double[][][][]))]
    public class SourceService : ISourceService
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

        public void GetOptions()
        {
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Methods", "GET");
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Methods", "PUT");
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Methods", "POST");
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Methods", "DELETE");

            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Max-Age", "1728000");
        }

        public VeneerStatus GetRoot()
        {
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            Log("Requested /");
            return new VeneerStatus(Scenario);
        }

        public object GetRunResults()
        {
            throw new NotImplementedException();
        }

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

        public void SetScenario(string scenario)
        {
            if (RunningInGUI)
            {
                throw new InvalidOperationException("Cannot set scenario when running Veneer in Source user interface");
            }

            Log($"Setting scenario: {scenario}");
            var scenarios = Scenario.RiverSystemProject.GetRSScenarios();
            RiverSystemScenario newScenario;
            int scenarioIdx;
            bool isInt = Int32.TryParse(scenario, out scenarioIdx);
            if (isInt)
            {
                newScenario = scenarios[scenarioIdx].riverSystemScenario;
            }
            else
            {
                newScenario = scenarios.First(sc=>sc.ScenarioName==scenario).riverSystemScenario;
            }
            Scenario = newScenario;
        }

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

        public Stream GetFileQuery(string fn, string version)
        {
            return GetFile(fn);
        }

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

        public GeoJSONNetwork GetNetwork()
        {
            Log($"Requested network at {DateTime.Now:HH:mm:ss.fff}");
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return new GeoJSONNetwork(Scenario.Network);
        }

        public GeoJSONNetwork GetNetworkGeographic()
        {
            Log("Requested network in geographic coordinates");
            //            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return NetworkToGeographic.ToGeographic(Scenario.Network,Scenario.GeographicData.Projection as AbstractProjectionInfo);
        }

        public GeoJSONFeature GetNode(string nodeId)
        {
            Log($"Requested node {nodeId}");
            if (!int.TryParse(nodeId, out var id))
            {
                Log($"Failed to parse {nodeId} as a valid node index!");
                return null;
            }

            // To match GeoJSONFeature.NodeURL
            var matchingNode = Scenario.Network.nodes[id];
            if (matchingNode == null)
            {
                Log($"Failed to find node with id {nodeId}!");
                return null;
            }

            return new GeoJSONFeature(matchingNode as Node, Scenario, true);
        }

        public GeoJSONFeature GetLink(string linkId)
        {
            Log($"Requested link {linkId}");
            if (!int.TryParse(linkId, out var id))
            {
                Log($"Failed to parse {linkId} as a valid link index!");
                return null;
            }

            // To match GeoJSONFeature.LinkURL
            var matchingLink = Scenario.Network.links[id];
            if (matchingLink == null)
            {
                Log($"Failed to find link with id {linkId}!");
                return null;
            }

            return new GeoJSONFeature(matchingLink as Link, Scenario, true);
        }

        public RunLink[] GetRunList()
        {
            Log("Requested run list");
//            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            var runs = Scenario.Project.ResultManager.AllRuns().ToArray();
            RunLink[] links = new RunLink[runs.Length];
            for(int i = 0; i < runs.Length; i++)
            {
                var run = runs[i];
                links[i] = new RunLink
                    {
                        RunName = run.Name,
                        RunUrl = "/runs/" + run.RunNumber,
                        DateRun = run.DateRun.ToString(CultureInfo.InvariantCulture),
                        Scenario = run.Scenario.Name,
                        Status = run.RunResultIndicator.ToString()
                    };
            }

            Log(string.Format("Requested /runs, returning links to {0} runs",links.Length));
            return links;
        }

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

            var allRuns = Scenario.Project.ResultManager.AllRuns();
            var last = allRuns.Last();

            RunLogs[last.RunNumber] = messages.ToArray();
            Run r = RunsForId("latest")[0];

            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Location",
                                                                     WebOperationContext.Current.IncomingRequest.Headers
                                                                         ["Location"] +
                                                                     String.Format("runs/{0}", r.RunNumber));
        }

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
            if (RunLogs.ContainsKey(run.RunNumber))
            {
                log = RunLogs[run.RunNumber];
            }
            else
            {
                log = new []
                {
                    "Run log not available",
                    "Run log only available through Veneer for runs triggered in Veneer"
                };
            }

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

        // TODO: Can't delete a run. Should delete a job which may contain multiple runs.
        public void DeleteRun(string runId)
        {
            runId = runId.ToLower();
            Log(String.Format("Deleting run results ({0})", runId));
            int id = -1;
            if (runId == "all")
            {
                Scenario.Project.ResultManager.AllRuns().Select(r=>r.RunNumber).Reverse().ForEachItem(r=>DeleteRun(r.ToString()));
                return;
            }
            else if (runId == "latest")
            {
                id = Scenario.Project.ResultManager.AllRuns().Last().RunNumber;
            }
            else
            {
                id = int.Parse(runId);
                //RunLogs.Remove(id);
            }
            RunLogs.Remove(id);

#if V3 || V4
            Scenario.Project.ResultManager.RemoveRun(id);
#else
            var job = Scenario.Project.ResultManager.AllJobs.FirstOrDefault(j=>ListExtensions.FirstOrDefault(j.Runs,r=>r.RunNumber==id)!=null);
            if (job != null)
            {
                Scenario.Project.ResultManager.RemoveJob(job);
            }
#endif
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

        public TimeSeriesResponse GetTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable,string fromDate, string toDate,string precision,
                                              string aggregation, string aggfn)
        {
            Log(String.Format("Requested time series {0}/{1}/{2}/{3}",runId,networkElement,recordingElement,variable));
            return GetTimeSeriesInternal(runId, networkElement, recordingElement, variable, aggregation, aggfn, fromDate, toDate,precision);
        }

        public TimeSeriesResponse GetAggregatedTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable, string aggregation, string fromDate, string toDate, string precision)
        {
            Log(String.Format("Requested {4} time series {0}/{1}/{2}/{3}", runId, networkElement, recordingElement, variable,aggregation));
            return GetTimeSeriesInternal(runId, networkElement, recordingElement, variable, aggregation,"sum" , fromDate,toDate,precision);
        }

        private TimeSeriesResponse GetTimeSeriesInternal(string runId, string networkElement, string recordingElement,
            string variable, string aggregation,string aggregationFunction, string fromDate, string toDate, string precision)
        {
            Tuple<TimeSeriesLink, TimeSeries>[] result = MatchTimeSeries(runId, networkElement, recordingElement, variable);

            if (fromDate != null || toDate != null)
            {
                DateTime? from = ParsePartialDate(fromDate, false);
                DateTime? to = ParsePartialDate(toDate, true);
                result = TransformTimeSeriesCollection(result, (TimeSeries ts) => ts.extract(from??ts.Start,to??ts.End));
            }

            if (aggregation != null)
            {
                aggregationFunction = aggregationFunction ?? "sum";
                result = TransformTimeSeriesCollection(result, ts => AggregateTimeSeries(ts, aggregation, aggregationFunction));
            }

            if (precision != null)
            {
                var decimalPlaces = Int32.Parse(precision);
                result = TransformTimeSeriesCollection(result, ts => ts.Round(decimalPlaces) as TimeSeries);
            }

            if (result.Length == 1)
                return SimpleTimeSeries(result[0].Item2);
            return CreateMultipleTimeSeries(result);
        }

        private Tuple<TimeSeriesLink, TimeSeries>[] TransformTimeSeriesCollection(
            Tuple<TimeSeriesLink, TimeSeries>[] collection, Func<TimeSeries, TimeSeries> transform)
        {
            return collection.Select(pair=>new Tuple<TimeSeriesLink,TimeSeries>(pair.Item1,transform(pair.Item2))).ToArray();
        }

        private DateTime? ParsePartialDate(string dateString, bool endOfPeriod)
        {
            if (dateString == null)
            {
                return null;
            }

            var components = dateString.Split('-').Select(Int32.Parse).ToList();
            if (components.Count == 1)
            {
                components.Add(endOfPeriod?12:1);
                components.Add(endOfPeriod?31:1);
            } else if (components.Count == 2)
            {
                components.Add(endOfPeriod?DateTime.DaysInMonth(components[0],components[1]):1);
            }

            return new DateTime(components[0], components[1], components[2]);
        }

        public DataTable GetTabulatedResults(string runId, string networkElement, string recordingElement,
                                              string variable, string functions)
        {
            Log(String.Format("Requested tabulated results {0}/{1}/{2}/{3} with functions={4}", runId, networkElement, recordingElement, variable, functions));
            Tuple<TimeSeriesLink, TimeSeries>[] results = MatchTimeSeries(runId, WebUtility.HtmlDecode(networkElement), WebUtility.HtmlDecode(recordingElement), WebUtility.HtmlDecode(variable));

            var theFunctions = functions.Split(',');
            if (functions == UriTemplates.MatchAll)
            {
                theFunctions = TimeSeriesFunctions.Functions.Keys.ToArray();
            }

            return TimeSeriesFunctions.TabulateResults(theFunctions, results, 
                runId == UriTemplates.MatchAll, 
                networkElement == UriTemplates.MatchAll, 
                recordingElement == UriTemplates.MatchAll,
                variable == UriTemplates.MatchAll);
        }

        private TimeSeriesResponse CreateMultipleTimeSeries(Tuple<TimeSeriesLink, TimeSeries>[] result)
        {
            if (result.Length == 0)
                return TimeSeriesNotFound();
            return new MultipleTimeSeries(result);
        }

        public void SetFunction(string functionName, FunctionValue value)
        {
            Log(String.Format("Updating function {0}", functionName));
            functionName = "$" + functionName;
            var function = Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Functions, f => f.Name == functionName);
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

        public FunctionValue[] GetFunctionList()
        {
            Log("Requested function list");
            FunctionValue[] result = new FunctionValue[Scenario.Network.FunctionManager.Functions.Count];
            for (var i = 0; i < result.Length; i++)
            {
                Function fn = Scenario.Network.FunctionManager.Functions[i];
                FunctionValue fv = new FunctionValue();
                fv.Name = fn.Name;
                fv.Expression = fn.Expression;
                result[i] = fv;
            }
            return result;
        }

        public VariableSummary[] GetInputList()
        {
            Log("Requested Variable List");
            VariableSummary[] result = new VariableSummary[Scenario.Network.FunctionManager.Variables.Count];
            for (var i = 0; i < result.Length; i++)
                result[i] = new VariableSummary(Scenario.Network.FunctionManager.Variables[i],Scenario);
            return result;
        }

        public VariableSummary GetInput(string variableName)
        {
            Log("Requested Variable : " + variableName);
            return new VariableSummary(Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Variables, v => v.FullName == ("$" + variableName)), Scenario);
        }

        public SimpleTimeSeries GetInputTimeSeries(string variableName)
        {
            Log(String.Format("Requested time series for {0}", variableName));
            return (new VariableSummary(Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Variables, v => v.FullName == ("$" + variableName)), Scenario)).TimeSeriesData;
        }

        public void ChangeInputTimeSeries(string variableName, SimpleTimeSeries newTimeSeries)
        {
            Log(String.Format("Updating time series for {0}", variableName));
            VariableSummary summ =
                new VariableSummary(
                    Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Variables, v => v.FullName == ("$" + variableName)),
                    Scenario);
            summ.UpdateTimeSeries(newTimeSeries);
        }

        public SimplePiecewise GetPiecewiseLinear(string variableName)
        {
            Log(String.Format("Requested piecewise linear function for {0}", variableName));
            return (new VariableSummary(Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Variables, v => v.FullName == ("$" + variableName)), Scenario)).PiecewiseFunctionData;
        }

        public void ChangePiecewiseLinear(string variableName, SimplePiecewise newPiecewise)
        {
            Log(String.Format("Updating  piecewise linear function for {0}", variableName));
            VariableSummary summ =
                new VariableSummary(
                    Enumerable.FirstOrDefault(Scenario.Network.FunctionManager.Variables, v => v.FullName == ("$" + variableName)),
                    Scenario);
            summ.UpdatePiecewise(newPiecewise);
        }

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
                    Configuration = sets.Instructions(inputSet),
                    HierarchicalName = inputSet.HierarchicalName
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

        public void CreateInputSet(InputSetSummary newInputSet)
        {
            Log("Creating new Input Set: " + newInputSet.Name);

            var sets = new InputSets(Scenario);
            sets.Create(newInputSet);
        }

        public void UpdateInputSet(string inputSetName, InputSetSummary summary)
        {
            Log("Updating Input Set Commands for " + inputSetName);
            var sets = new InputSets(Scenario);
            InputSet set = sets.Find(inputSetName);
            sets.UpdateInstructions(set, summary.Configuration);
        }

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

        public SimpleDataGroupItem[] GetDataSources()
        {
            Log("Data sources");
            var dm = Scenario.Network.DataManager;
            return dm.DataGroups.Select(dg => new SimpleDataGroupItem(dg)).ToArray();
        }

        public SimpleDataGroupItem GetDataSource(string dataSourceGroup)
        {
            Log("Data source: " + dataSourceGroup);
            return GetSimpleDataSourceInternal(dataSourceGroup, false);
        }

        public void CreateDataSource(SimpleDataGroupItem newItem)
        {
            var dm = Scenario.Network.DataManager;
            var existing = Enumerable.FirstOrDefault(dm.DataGroups, ds => ds.Name == newItem.Name);

            if (existing != null)
            {
                newItem.ReplaceInScenario(Scenario, existing);
            }
            else
            {
                newItem.AddToScenario(Scenario);
            }
        }

        public void UpdateDataSource(string dataSourceGroup, SimpleDataGroupItem newItem)
        {
            newItem.Name = dataSourceGroup;
            this.CreateDataSource(newItem);
        }

        private SimpleDataGroupItem GetSimpleDataSourceInternal(string dataSourceGroup, bool summary)
        {
            dataSourceGroup = dataSourceGroup.Replace("%25", "%").Replace("%2F","/");
            var dm = Scenario.Network.DataManager;

            var res =
                Enumerable.FirstOrDefault(dm.DataGroups, ds => SimpleDataGroupItem.MakeID(ds) == (UriTemplates.DataSources + "/" + dataSourceGroup));
            if (res == null)
            {
                ResourceNotFound();
                return null;
            }

            return new SimpleDataGroupItem(res, summary);
        }

        public void DeleteDataSource(string dataSourceGroup)
        {
            var dm = Scenario.Network.DataManager;
            var existing = Enumerable.FirstOrDefault(dm.DataGroups, ds => ds.Name == dataSourceGroup);
            if (existing == null)
            {
                ResourceNotFound();
                return;
            }
            dm.RemoveGroup(existing);
        }

        public SimpleDataItem GetDataGroupItem(string dataSourceGroup,string inputSet)
        {
            var grp = GetSimpleDataSourceInternal(dataSourceGroup,false);
            if (grp == null)
                return null;

            var result =
                Enumerable.FirstOrDefault(grp.Items, item => item.MatchInputSet(inputSet));

            if(result==null)
                ResourceNotFound();
            return result;
        }

        public SimpleDataDetails[] GetMultipleDataGroupItemDetails(string dataSourceGroup, string name)
        {
            var grp = GetSimpleDataSourceInternal(dataSourceGroup,true);
            if (grp == null)
                return null;

            List<SimpleDataDetails> result = new List<SimpleDataDetails>();
            foreach (var item in grp.Items)
            {
                var matchingItem = Enumerable.FirstOrDefault(item.Details, d =>
                {
                    var safeName = URLSafeString(d.Name);
                    return safeName == name;
                });
                if (matchingItem == null)
                {
                    matchingItem = Enumerable.FirstOrDefault(item.Details, d =>
                    {
                        var safeName = URLSafeString(d.Name);
                        return Regex.IsMatch(safeName, name);
                    });
                }
                if (matchingItem != null)
                {
                    matchingItem.Name = item.Name + "/" + matchingItem.Name;
                    matchingItem.Expand();
                    result.Add(matchingItem);
                }
            }

            if(result.Count==0)
                ResourceNotFound();
            return result.ToArray();
        }

        public SimpleDataDetails GetDataGroupItemDetails(string dataSourceGroup, string inputSet,string item)
        {
            var retrieved = GetDataGroupItem(dataSourceGroup, inputSet);
            var result = Enumerable.FirstOrDefault(retrieved.Details, d => URLSafeString(d.Name) == item);

            if(result==null)
                ResourceNotFound();
            return result;
        }

        public IronPythonResponse RunIronPython(IronPythonScript script)
        {
            if (!AllowScript)
            {
                Log(String.Format("Attempt to run IronPython script, but AllowScript=false. Script:\n{0}", script.Script));
                WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Forbidden;
                return null;
            }
            Log(String.Format("Running IronyPython script:\n{0}",(script.Script.Length>80)?(script.Script.Substring(0,75)+"..."):script.Script));
            return runIronPythonWithScenario(script);
        }

        private IronPythonResponse runIronPythonWithScenario(IronPythonScript script)
        {
            scriptRunner.Scenario = Scenario;
            scriptRunner.ProjectHandler = ProjectHandler;
            return scriptRunner.Run(script);
        }

        public ModelTableIndex ModelTableIndex()
        {
            return ModelTabulator.Index();
        }

        public DataTable ModelTable(string table)
        {
            Log(String.Format("Requested {0} table", table));
            if (!ModelTabulator.Functions.ContainsKey(table))
            {
                Log(String.Format("Unknown table: {0}", table));
                WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotFound;
                return null;
            }
            return ModelTabulator.Functions[table](Scenario);
        }

        public string[] GetConfiguration(string element)
        {
            var table = Scenario.ProjectViewTable();
            element = element.ToLower();
            if (element == "networkelement")
            {
                
                return Enumerable.ToHashSet(table.Select(row => row.NetworkElementName)).ToArray();
            }

            if (element == "recordingelement")
            {
                return Enumerable.ToHashSet(table.Select(row => row.ElementName)).ToArray();
            }

            return new string[0];
        }

        public void AssignProjection(ProjectionInfo p)
        {
            p.AssignTo(Scenario);
        }

        public IronPythonResponse RunCustomEndPoint(string action, string[] parameters)
        {
            var custom = CustomEndPoints.FirstOrDefault(ep => ep.endpoint == action);
            if (custom == null)
            {
                return null;
            }

            return runIronPythonWithScenario(new IronPythonScript{Script= custom.GetScript(parameters)});
        }

        public void RegisterEndPoint(CustomEndPoint ep)
        {
            CustomEndPoints.Add(ep);
        }

        private List<CustomEndPoint> CustomEndPoints = new List<CustomEndPoint>();

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

            if (!string.IsNullOrEmpty(query.FunctionalUnit))
            {
                constraint[ProjectViewRow.RecorderFields.WaterFeatureType] = query.FunctionalUnit;
            }

            var table = Scenario.ProjectViewTable();
            var rows = table.Select(constraint);
            var state = record ? RecordingStates.RecordAll : RecordingStates.RecordNone;
            foreach (var row in rows)
            {
#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
                foreach (var recordable in row.ElementRecorder.RecordableAttributes)
                {
                    if ((query.RecordingVariable.Length == 0) ||
                        (recordable.FullKeyString.IndexOf(query.RecordingVariable, StringComparison.Ordinal) >= 0))
                    {
                        row.ElementRecorder.SetRecordingState(recordable.KeyString, recordable.KeyObject, state);
                    }

                }
#else
                foreach (var recordable in row.ElementRecorder.RecordableItems)
                {
                    var recordableItemDisplayString =
                        RecordableItemTransitionUtil.GetLegacyKeyString(recordable);
                    if ((query.RecordingVariable.Length == 0) ||
                        (recordableItemDisplayString.IndexOf(query.RecordingVariable, StringComparison.Ordinal) >= 0))
                    {
                        row.ElementRecorder.SetRecordingState(recordable.Key, recordable.KeyObject, state);
                    }

                }
#endif
            }
        }

        private TimeSeries AggregateTimeSeries(TimeSeries result, string aggregation, string aggregationFunction)
        {
            if (result == null)
                return null;
            aggregation = aggregation.ToLower();
            var origUnits = result.units;

            string name = result.name;
            TimeStep newTimeStep = GetTimeStep(aggregation,result.timeStep);

            var groups = result.GroupByTimeStep(newTimeStep);
            Tuple<DateTime, double>[] entries;
            if (aggregationFunction == "sum")
            {
                entries = groups.Select(ts => new Tuple<DateTime, double>(ts.timeForItem(0), ts.Sum())).ToArray();
            }
            else
            {
                entries = groups.Select(ts => new Tuple<DateTime, double>(ts.timeForItem(0), ts.average())).ToArray();
            }

            var dates = entries.Select(v => v.Item1).ToArray();
            var values = entries.Select(v => v.Item2).ToArray();

            result = new TimeSeries(dates[0], newTimeStep, values);

            //if (aggregation == "monthly")
            //    result = result.toMonthly();
            
            //if (aggregation == "annual")
            //    result = result.toAnnual();
            result.name = name;
            if (origUnits != null)
                result.units = origUnits;
            return result;
        }

        private TimeStep GetTimeStep(string aggregation,TimeStep fallback)
        {
            switch (aggregation)
            {
                case "annual":return TimeStep.Annual;
                case "month": return TimeStep.Monthly;
                case "day": return TimeStep.Daily;
            }

            return fallback;
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
#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
                result.AddRange(row.ElementRecorder.GetResultList().Where(er=>MatchesVariable(row,er,variable)).Select(
#else
                result.AddRange(row.ElementRecorder.GetResultsLookup().Where(er => MatchesVariable(row, er, variable)).Select(
#endif
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

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
        private bool MatchesVariable(ProjectViewRow row, KeyValuePair<AttributeRecordingState, TimeSeries> er, string variable)
#else
        private bool MatchesVariable(ProjectViewRow row, KeyValuePair<RecordableItem, TimeSeries> er, string variable)
#endif
        {
            if (variable == UriTemplates.MatchAll) return true;

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
            return (URLSafeString(er.Key.KeyString) == URLSafeString(variable)) ||
                ((er.Key.KeyString == "") && (row.ElementName == variable));
#else
            var recordableItemDisplayName = RecordableItemTransitionUtil.GetLegacyKeyString(er.Key);

            return (URLSafeString(recordableItemDisplayName) == URLSafeString(variable)) ||
                    ((recordableItemDisplayName == "") && (row.ElementName == variable));
#endif
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
            string functionalUnit;
            bool haveFU = UriTemplates.TryExtractFunctionalUnit(networkElement, out networkElement, out functionalUnit);
            bool matchesNetworkElement = (networkElement == UriTemplates.MatchAll) ||
                                         (URLSafeString(row.NetworkElementName) == URLSafeString(networkElement));
            bool satisfiesFU = !haveFU || (URLSafeString(row.WaterFeatureType) == URLSafeString(functionalUnit));
            bool matchesRecordingElement = (recordingElement == UriTemplates.MatchAll) ||
                                           (URLSafeString(row.ElementName) == URLSafeString(recordingElement));

            return matchesNetworkElement && matchesRecordingElement && satisfiesFU;
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

        public string Ping()
        {
            try
            {
                return "pong";
            }
            catch (Exception ex)
            {
                return $"Ping failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}