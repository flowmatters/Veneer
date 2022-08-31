using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.ExchangeObjects;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystem.ManagedExtensions;

namespace FlowMatters.Source.Veneer.DomainActions
{
    public static class ModelTabulator
    {
        public static Dictionary<string, Func<RiverSystemScenario, DataTable>> Functions = new Dictionary
            <string, Func<RiverSystemScenario, DataTable>>
            {
                {"fus", FunctionalUnits}
            };

        public static DataTable FunctionalUnits(RiverSystemScenario scenario)
        {
            var result = new DataTable();

            result.Columns.Add("Catchment", typeof(string));
            scenario.Network.FunctionalUnitConfiguration.fuDefinitions.ForEachItem(fud=>result.Columns.Add(fud.Name,typeof(double)));

            foreach(ICatchment ic in scenario.Network.Catchments)
            {
                Catchment c = ic as Catchment;
                if (c == null) continue;

                var row = result.NewRow();
                row["Catchment"] = c.Name;
                c.FunctionalUnits.ForEachItem(fu =>
                {
                    row[fu.definition.Name] = fu.areaInSquareMeters;
                });
                result.Rows.Add(row);
            }
            return result;
        }

        public static ModelTableIndex Index()
        {
            var result = new ModelTableIndex();
            result.Tables = new[]
            {
                new ModelTableIndexItem("Functional Units", "/tables/fus")
            };
            return result;
        }
    }
}
