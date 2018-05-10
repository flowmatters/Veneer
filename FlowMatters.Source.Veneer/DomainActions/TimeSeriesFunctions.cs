using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.DomainActions
{
    public static class TimeSeriesFunctions
    {
        public static Dictionary<string, Func<TimeSeries, double>> Functions = new Dictionary<string, Func<TimeSeries, double>>
        {
            { "mean", ts=>ts.average() },
            { "sum",ts=>ts.total() },
            { "mean-annual", ts => ts.toAnnual().average()}
        };

        public static DataTable TabulateResults(string[] functions, Tuple<TimeSeriesLink, TimeSeries>[] results, bool runColumn, bool networkColumn, bool elementColumn,
            bool variableColumn)
        {
            var table = new DataTable();

            if (runColumn)
            {
                table.Columns.Add("Run", typeof(string));
            }

            if (networkColumn)
            {
                table.Columns.Add("NetworkElement", typeof(string));
            }

            if (elementColumn)
            {
                table.Columns.Add("RecordingElement", typeof(string));
            }

            if (variableColumn)
            {
                table.Columns.Add("RecordingVariable", typeof(string));
            }

            foreach (var fn in functions)
            {
                table.Columns.Add(fn, typeof(double));
            }

            foreach (var r in results)
            {
                var row = table.NewRow();

                var link = r.Item1;
                var ts = r.Item2;

                if (runColumn)
                {
                    row["Run"] = link.RunNumber;
                }

                if (networkColumn)
                {
                    row["NetworkElement"] = link.NetworkElement;
                }

                if (elementColumn)
                {
                    row["RecordingElement"] = link.RecordingElement;
                }

                if (variableColumn)
                {
                    row["RecordingVariable"] = link.RecordingVariable;
                }

                foreach (var fn in functions)
                {
                    if (TimeSeriesFunctions.Functions.ContainsKey(fn))
                    {
                        row[fn] = TimeSeriesFunctions.Functions[fn](ts);
                    }
                    else
                    {
                        row[fn] = Double.NaN;
                    }
                }

                table.Rows.Add(row);
            }

            return table;
        }
    }
}
