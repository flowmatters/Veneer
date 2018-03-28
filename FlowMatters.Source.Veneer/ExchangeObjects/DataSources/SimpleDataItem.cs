using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;
using FlowMatters.Source.WebServer;
using IronPython.Modules;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.DataManagement.DataManager.DataDetails;
using RiverSystem.ManagedExtensions;
using TIME.Core;
using TIME.Core.Units.Defaults;
using TIME.DataTypes;
using TIME.DataTypes.IO.CsvFileIo;
using TIME.DataTypes.TimeSeriesImplementation;

namespace FlowMatters.Source.Veneer.ExchangeObjects.DataSources
{
    [DataContract]
    public class SimpleDataItem
    {
        public const int SLIM_TS_THRESHOLD=50;

        public SimpleDataItem()
        {
            
        }

        public SimpleDataItem(InputSetItem isi,bool summary=true)
        {
            Name = isi.Name;
            InputSets = isi.InputSets.Select(iset => iset.Name).ToArray();
#if V3 || V4_0 || V4_1 || V4_2_0 
            bool slim = isi.DataSource.PersistedData.Count >= SLIM_TS_THRESHOLD;
            Details = isi.DataSource.PersistedData.Select(ddi => new SimpleDataDetails(ddi,summary,slim)).ToArray();
#else
            bool slim = isi.DataSource.Data.Count >= SLIM_TS_THRESHOLD;
            Details = isi.DataSource.Data.Select(ddi => new SimpleDataDetails(ddi, summary)).ToArray();
#endif
        }

        public SimpleDataItem(GenericDataDetails gdd)
        {
            Name = gdd.Name;
            bool slim = gdd.AssociatedData.Count >= SLIM_TS_THRESHOLD;
            Details = gdd.AssociatedData.Select(ddi => new SimpleDataDetails(ddi,false,slim)).ToArray();
        }

        public bool MatchInputSet(string inputSet)
        {
            return InputSets.Any(iSet => SourceService.URLSafeString(iSet) == inputSet);
        }

        internal void AddToGroup(RiverSystemScenario scenario, DataGroupItem dataGroup,int index)
        {
            DataSourceItem sourceItem = dataGroup.InputSetItems[index].DataSource;
            sourceItem.Name = Name;

            TimeSeries[] allTS = ParseCSV();
            foreach (var ts in allTS)
            {
                DataDetailsItem dataItem = new DataDetailsItem
                {
                    Data = new TimeSeriesPersistent { TimeSeries = ts },
                    DataInformation = new GeneratedDataDetails { Name = ts.name }
                };
                sourceItem.Data.Add(dataItem);

                var gdd = new GenericDataDetails { Name = ts.name };
                gdd.AssociatedData.Add(dataItem);
                dataGroup.DataDetails.Add(gdd);
            }
        }

        private TimeSeries[] ParseCSV()
        {
            if (DetailsAsCSV == null)
            {
                return new TimeSeries[0];
            }

            string[] unitsStrings = (UnitsForNewTS ?? "").Split(',');
            Unit[] columnUnits = new Unit[0];
            if (unitsStrings.Length > 0)
            {
                columnUnits = unitsStrings.Select(s => Unit.parse(s)).ToArray();
            }
            string[] lines = DetailsAsCSV.Split(new string[] {"\n","\r"},StringSplitOptions.RemoveEmptyEntries);
            string header = lines[0];
            lines = lines.Skip(1).ToArray();

            string[] columnNames = SplitCsvLine(header).Skip(1).ToArray();
            string[][] elements = lines.Select(SplitCsvLine).ToArray();
            string[] dates = elements.Select(l => l[0]).ToArray();
            elements = elements.Select(l => l.Skip(1).ToArray()).ToArray();
            DateTime startT = DateTime.Parse(dates.First(), CultureInfo.InvariantCulture);
            //DateTime endT = DateTime.Parse(dates.Last(),CultureInfo.InvariantCulture);
            var ts = TimeStep.Daily;
            if (dates.Length > 1)
            {
                DateTime secondT = DateTime.Parse(dates[1], CultureInfo.InvariantCulture);
                var delta = secondT - startT;
                ts = TimeStep.FromSeconds(delta.TotalSeconds);
            }

            return columnNames.Indices().Select(i =>
            {
                var name = columnNames[i];
                var values = elements.Select(line =>
                {
                    double d;
                    if(!double.TryParse(line[i], out d))
                        d = double.NaN;
                    return d;
                }).ToArray();
                var result = new TimeSeries(startT, ts,values);
                result.name = name;
                if (columnUnits.Length > 0)
                    result.units = columnUnits[i%columnUnits.Length];
                return result;
            }).ToArray();
        }

        private static string[] SplitCsvLine(string line)
        {
            string lineToUse = line;
            if (line == "")
                return new string[] { };
            if (lineToUse[0] == ',')
                lineToUse = " " + lineToUse;
            int oldLength;
            do
            {
                oldLength = lineToUse.Length;
                lineToUse = lineToUse.Replace(",,", ", ,");
            } while (lineToUse.Length > oldLength);
            return lineToUse.Split(',');
        }

        [DataMember]
        public string Name;

        [DataMember]
        public string[] InputSets;

        [DataMember]
        public SimpleDataDetails[] Details;

        [DataMember] public string DetailsAsCSV;
        [DataMember] public string UnitsForNewTS;
    }
}
