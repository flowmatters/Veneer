using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem;
using RiverSystem.Controls.Icons;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions.Variables;
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
    class SourceService //: ISourceService
    {
        public RiverSystemScenario Scenario { get; set; }

        public event ServerLogListener LogGenerator;

        [OperationContract]
        [WebGet(UriTemplate = "/", ResponseFormat = WebMessageFormat.Json)]
        public string GetRoot()
        {
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            Log("Requested /");
            return "Root node of service";
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.FilesD)]
        public Stream GetFileD(string dir, string fn)
        {
            return GetFile(dir + "/" + fn);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.FilesDD)]
        public Stream GetFileDD(string dir1, string dir2, string fn)
        {
            return GetFile(dir1 + "/" + dir2 + "/" + fn);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = UriTemplates.Files)]
        public Stream GetFile(string fn)
        {
            Log(string.Format("Requested {0}",fn));

            MemoryStream ms = new MemoryStream();
            string basePath = Scenario.Project.FileDirectory;
            string filename = basePath + "\\" + fn;
            WebOperationContext.Current.OutgoingResponse.ContentType = ContentTypeForFilename(filename);
            byte[] contents = File.ReadAllBytes(filename);
            ms.Write(contents,0,contents.Length);
            ms.Position = 0;
            return ms;
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate = UriTemplates.Resources)]
        public Stream GetResources(string resourceName)
        {
            Log(string.Format("Requested resource {0}", resourceName));
            MemoryStream ms = new MemoryStream();
            Assembly rsFormsAssembly = Assembly.GetEntryAssembly();

            Bitmap resource = FindByName(resourceName);

            resource.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            WebOperationContext.Current.OutgoingResponse.ContentType = "image/png";
            return ms;
        }

        private Bitmap FindByName(string s)
        {
            Type modelType = Finder.typesInherting(typeof(IDomainObject)).Where(t => t.Name == s).FirstOrDefault();

            if (modelType == null)
            {
                s += "240";
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Assembly assembly = assemblies.First(a => a.FullName.Contains("RiverSystem.Controls"));
                Type resourcesType = assembly.GetType("RiverSystem.Controls.Properties.Resources");
                PropertyInfo resourceProperty = resourcesType.GetProperty(s, BindingFlags.Public | BindingFlags.Static);
                Bitmap result = (Bitmap) resourceProperty.GetValue(null, new object[0]);

                return result;
            }

            return (Bitmap) IconLookup.GetIcon(modelType);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Network)]
        public GeoJSONNetwork GetNetwork()
        {
            Log("Requested network");
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return new GeoJSONNetwork(Scenario.Network);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Node)]
        public GeoJSONFeature GetNode(string nodeId)
        {
            return null;                
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Link)]
        public GeoJSONFeature GetLink(string linkId)
        {
            return null;
        }
        private static string ContentTypeForFilename(string fn)
        {
            string contentType = "";
            FileInfo fileInfo = new FileInfo(fn);
            string extension = fileInfo.Extension;
            switch (extension)
            {
                case ".html":
                case ".htm":
                    contentType = "text/html";
                    break;
                case ".jpeg":
                case ".jpg":
                    contentType = "image/jpeg";
                    break;
                case ".png":
                    contentType = "image/png";
                    break;
                case ".json":
                    contentType = "application/json";
                    break;
                case ".js":
                    contentType = "application/javascript";
                    break;
                case ".css":
                    contentType = "text/css";
                    break;
            }
            return contentType;
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.Runs)]
        public RunLink[] GetRunList()
        {
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
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
        [WebInvoke(Method = "POST", UriTemplate = UriTemplates.Runs, RequestFormat = WebMessageFormat.Json)]
        public void TriggerRun(RunParameters parameters)
        {
            ScenarioInvoker si = new ScenarioInvoker{Scenario=Scenario};
            si.RunScenario(parameters);
            Run r = RunForId("latest");

            WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.Redirect;
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Location",
                                                                     WebOperationContext.Current.IncomingRequest.Headers
                                                                         ["Location"] +
                                                                     String.Format("/runs/{0}", r.RunNumber));
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.RunResults)]
        public RunSummary GetRunResults(string runId)
        {
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            string msg = "";
            if (runId.ToLower() == "latest")
                msg = "latest run";
            else
                msg = string.Format("run with id={0}", runId);

            Run run = RunForId(runId);

            Log("Requested " + msg);

            if (run == null)
            {
                Log(string.Format("Run {0} not found", runId));
                return null;
            }

            return new RunSummary(run);
        }

        private Run RunForId(string id)
        {
            if(id.ToLower() == "latest")
                return Scenario.Project.ResultManager.AllRuns().LastOrDefault();
            
            return Scenario.Project.ResultManager.AllRuns().FirstOrDefault(x => x.RunNumber.ToString(CultureInfo.InvariantCulture) == id);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, UriTemplate = UriTemplates.TimeSeries)]
        public SimpleTimeSeries GetTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable)
        {
            TimeSeries result = MatchTimeSeries(runId, networkElement, recordingElement, variable);

            return SimpleTimeSeries(result);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json,
            UriTemplate = UriTemplates.AggregatedTimeSeries)]
        public SimpleTimeSeries GetAggregatedTimeSeries(string runId, string networkElement, string recordingElement,
                                              string variable, string aggregation)
        {
            TimeSeries result = MatchTimeSeries(runId, WebUtility.HtmlDecode(networkElement), WebUtility.HtmlDecode(recordingElement), WebUtility.HtmlDecode(variable));

            result = AggregateTimeSeries(result, aggregation);
            return SimpleTimeSeries(result);
        }

        [OperationContract]
        [WebInvoke(Method="PUT",UriTemplate = "/functions/{functionName}",RequestFormat = WebMessageFormat.Json)]
        public void SetFunction(string functionName, FunctionValue value)
        {
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
            FunctionValue[] result = new FunctionValue[Scenario.Network.FunctionManager.Functions.Count];
            for (var i = 0; i < result.Count(); i++)
            {
                var fn = Scenario.Network.FunctionManager.Functions[i];
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
            VariableSummary[] result = new VariableSummary[Scenario.Network.FunctionManager.Variables.Count];
            for (var i = 0; i < result.Count(); i++)
                result[i] = new VariableSummary(Scenario.Network.FunctionManager.Variables[i],Scenario);
            return result;
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate="/variables/{variableName}",ResponseFormat=WebMessageFormat.Json)]
        public VariableSummary GetInput(string variableName)
        {
            return new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$"+variableName)),Scenario);
        }

        [OperationContract]
        [WebInvoke(Method = "GET", UriTemplate = "/variables/{variableName}/TimeSeries", ResponseFormat = WebMessageFormat.Json)]
        public SimpleTimeSeries GetInputTimeSeries(string variableName)
        {
            return (new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)), Scenario)).TimeSeriesData;
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/TimeSeries",
            RequestFormat = WebMessageFormat.Json)]
        public void ChangeInputTimeSeries(string variableName, SimpleTimeSeries newTimeSeries)
        {
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
            return (new VariableSummary(Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)), Scenario)).PiecewiseFunctionData;
        }

        [OperationContract]
        [WebInvoke(Method = "PUT", UriTemplate = "/variables/{variableName}/Piecewise",
            RequestFormat = WebMessageFormat.Json)]
        public void ChangePiecewiseLinear(string variableName, SimplePiecewise newPiecewise)
        {
            VariableSummary summ =
                new VariableSummary(
                    Scenario.Network.FunctionManager.Variables.FirstOrDefault(v => v.FullName == ("$" + variableName)),
                    Scenario);
            summ.UpdatePiecewise(newPiecewise);
        }

        [OperationContract]
        [WebInvoke(Method="GET",UriTemplate="/inputSets",ResponseFormat = WebMessageFormat.Json)]
        public InputSetSummary[] InputSetShenanigans()
        {
            ParameterSetManager Manager = Scenario.PluginDataModels.OfType<ParameterSetManager>().First();
            InputSetSummary[] result = new InputSetSummary[Scenario.Network.InputSets.Count];
            for (int i = 0; i < result.Length; i++)
            {
                InputSet inputSet = Scenario.Network.InputSets[i];
                InputSetParameterSet ps = Manager.ParameterSets.First(x => x.InputSet == Scenario.Network.InputSets[i]);

                result[i] = new InputSetSummary
                {
                    Name = inputSet.Name,
                    Default = inputSet.IsDefault,
                    Configuration = ps.Parameters.Configuration.Instructions.Split(Environment.NewLine.ToCharArray())
                };

                
            }
            return result;
        }

        private TimeSeries AggregateTimeSeries(TimeSeries result, string aggregation)
        {
            if (result == null)
                return null;
            aggregation = aggregation.ToLower();
            if (aggregation == "monthly")
                return result.toMonthly();

            if (aggregation == "annual")
                return result.toAnnual();

            return result;
        }

        private SimpleTimeSeries SimpleTimeSeries(TimeSeries result)
        {
            WebOperationContext.Current.OutgoingResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            return (result == null) ? TimeSeriesNotFound() : new SimpleTimeSeries(result);
        }

        private TimeSeries MatchTimeSeries(string runId, string networkElement, string recordingElement, string variable)
        {
            Run run = RunForId(runId);
            if (run == null) return null;

            ProjectViewRow row =
                run.RunParameters.FirstOrDefault(
                    r => MatchesElements(r, networkElement, recordingElement));

            if (row == null) return null;

            return row.ElementRecorder.GetResultList().FirstOrDefault(er => URLSafeString(er.Key.KeyString) == URLSafeString(variable)).Value;            
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
            return (URLSafeString(row.NetworkElementName) == URLSafeString(networkElement)) && 
                (URLSafeString(row.ElementName) == URLSafeString(recordingElement));
        }

        public static string URLSafeString(string src)
        {
            return src.Replace("#","").Replace("/","%2F");
        }

        protected void Log(string query)
        {
            if (LogGenerator != null)
                LogGenerator(this, query);
        }
    }

    [Serializable]
    public class RunParameters : ISerializable
    {
        public RunParameters()
        {
                
        }

        public RunParameters(SerializationInfo info, StreamingContext context)
        {
            foreach (var entry in info)
            {
                properties.Add(entry.Name, entry.Value);
            }
        }

        private Dictionary<string,object> properties = new Dictionary<string, object>();
        public Dictionary<string, object> Params { get { return properties; } }
        
        public void Add(string key, object value)
        {
            properties[key] = value;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            foreach (KeyValuePair<string, object> kvp in properties)
                info.AddValue(kvp.Key,kvp.Value);
        }

        //[DataMember] public string Start;
        //[DataMember] public string End;
        //[DataMember] public int ForecastLength;
        //[DataMember] public string InputSet;

    }

    [DataContract]
    public class InputSetSummary
    {
        [DataMember] public string Name;
        [DataMember] public bool Default;
        [DataMember] public string[] Configuration;
    }

    [DataContract]
    public class FunctionValue
    {
        [DataMember] public string Name;
        [DataMember] public string Expression;
    }
}
