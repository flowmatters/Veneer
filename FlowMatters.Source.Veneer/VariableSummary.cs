using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions.Variables;
using TIME.DataTypes;
using TIME.Tools.Reflection;

namespace FlowMatters.Source.Veneer
{
    [DataContract]
    public class VariableSummary
    {
        public VariableSummary(AbstractFunctionVariable v,RiverSystemScenario scenario)
        {
            Scenario = scenario;
            Variable = v;
            Name = v.Name;
            FullName = v.FullName;
            ID = v.id;
            VariableType = v.GetType().Name;
 
            if (v is BilinearVariable)
            {
                VeneerSupported = false;
            }
            else if (v is ContextVariable)
            {
                VeneerSupported = false;
            }
            else if (v is ModelledVariable)
            {
                VeneerSupported = false;
            }
            else if (v is PatternVariable)
            {
                VeneerSupported = false;
            }
            else if (v is LinearVariable)
            {
                VeneerSupported = true;
                PiecewiseFunctionData = new SimplePiecewise(v as LinearVariable);
                PiecewiseFunction = String.Format("/variables/{0}/Piecewise", FullName.Replace("$", ""));
            }
            else if (v is TimeSeriesVariable)
            {
                VeneerSupported = true;
                var tsV = (TimeSeriesVariable) v;
                VeneerDebugInfo += kvp("DisplayName", tsV.DisplayName) +
                                   kvp("ResultUnit", tsV.ResultUnit.Name);
                TimeSeriesData = new SimpleTimeSeries(FindTimeSeries());
                TimeSeries = String.Format("/variables/{0}/TimeSeries", FullName.Replace("$", ""));
            }
        }


        private RiverSystemScenario Scenario;

        private TimeSeries FindTimeSeries()
        {
            ReflectedItem ri = ReflectedItem.NewItem("Value", Variable);
            string gddName = Scenario.Network.DataManager.GetUsageFullName(ri);
            if (String.IsNullOrEmpty(gddName)) return null;

            string[] split = gddName.Split('.');
            if (split.Length <= 1) return null;

            string groupName = string.Join(".", split.Take(split.Length - 1));
            GenericDataDetails GDD =
                Scenario.Network.DataManager.DataGroups.Where(g => g.Name == groupName)
                    .Select(@group => @group.GetUsage(split.Last()))
                    .FirstOrDefault(gdd => gdd != null);
//                        DataUsage DU = GDD.Usages.First(x => x.ReflectedItem.Equals(ri));
            return GDD.AssociatedData.FirstOrDefault(d => d.DataInformation.Name.Replace(".","_") == split.Last())
                .Data.TimeSeries;
        }

        public void UpdateTimeSeries(SimpleTimeSeries newTS)
        {
            var actual = FindTimeSeries();
            actual.init(newTS.AsDate(newTS.StartDate), newTS.AsDate(newTS.EndDate), TimeStep.Daily);
            foreach (TimeSeriesEvent e in newTS.Events)
            {
                actual[newTS.AsDate(e.Date)] = e.Value;
            }
        }

        public void UpdatePiecewise(SimplePiecewise newPiecewise)
        {
            LinearVariable linV = (LinearVariable) Variable;
            newPiecewise.ApplyTo(linV);
        }

        private string kvp(string key, string val)
        {
            return key + '=' + val + ';';
        }

        [DataMember] public string Name;
        [DataMember] public string FullName;
        [DataMember] public string VariableType;
        [DataMember] public int ID;
        [DataMember] public bool VeneerSupported;
        [DataMember] public string VeneerDebugInfo;
        [DataMember] public string TimeSeries;
        [DataMember] public string PiecewiseFunction;
        public SimpleTimeSeries TimeSeriesData;
        public SimplePiecewise PiecewiseFunctionData { get; set; }

        private AbstractFunctionVariable Variable;
    }
}
