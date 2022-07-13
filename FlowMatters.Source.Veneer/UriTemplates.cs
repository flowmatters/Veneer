using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlowMatters.Source.WebServer
{
    class UriTemplates
    {
        public const string Recordable = "/location/{networkElement}/element/{recordingElement}/variable/{variable}";

        public const string TimeSeriesBase = RunResults + Recordable;

        public const string TimeSeriesQuery = "?from={fromDate}&to={toDate}&precision={precision}";

        public const string TimeSeries = TimeSeriesBase + TimeSeriesQuery;

        public const string AggregatedTimeSeries = TimeSeriesBase + "/aggregated/{aggregation}" + TimeSeriesQuery;

        public const string TabulatedResults = TimeSeriesBase + "/tabulated/{functions}";

        public const string RunResults = "/runs/{runId}";
        public const string Runs = "/runs";

        public const string Files = "/doc/{*fn}";

        public const string FilesD = "/doc/{dir}/{fn}";

        public const string FilesDD = "/doc/{dir1}/{dir2}/{fn}";

        public const string FilesQuery = "/doc/{*fn}?v={version}";

        public const string Resources = "/resources/{resourceName}";

        public const string Projection = "/projection";

        public const string Network = "/network";

        public const string NetworkGeographic = "/network/geographic";

        public const string Nodes = "/network/nodes";

        public const string Links = "/network/links";

        public const string Node = "/network/nodes/{nodeId}";

        public const string Link = "/network/link/{linkId}";

        public const string Catchments = "/network/catchments";

        public const string Catchment = "/network/catchments/{catchmentId}";

        public const string InputSets = "/inputSets";

        public const string InputSet = "/inputSets/{inputSetName}";

        public const string RunInputSet = "/inputSets/{inputSetName}/{action}";

        public const string DataSources = "/dataSources";

        public const string DataSourceGroup = DataSources+"/{dataSourceGroup}";

        public const string DataGroupItem = DataSourceGroup + "/{inputSet}";

        public const string DataGroupItemDetails = DataGroupItem + "/{item}";

        public const string DataGroupMultipleItemDetails = DataSourceGroup + "/" + MatchAll + "/{name}";

        public const string ScenarioTablesIndex = "/tables";

        public const string ScenarioTables = "/tables/{table}";

        public const string Configuration = "/configuration/{element}";

        public const string MatchAll = "__all__";

        public const string NETWORK_ELEMENT_FU_DELIMITER = "@@";

        public static bool TryExtractFunctionalUnit(string networkElement, out string newNetworkElement,
            out string functionalUnit)
        {
            if (networkElement.Contains(NETWORK_ELEMENT_FU_DELIMITER))
            {
                var split = networkElement.Split(new string[] {UriTemplates.NETWORK_ELEMENT_FU_DELIMITER},
                    StringSplitOptions.None);
                newNetworkElement = split[0];
                functionalUnit = split[1];
                return true;
            }

            newNetworkElement = networkElement;
            functionalUnit = null;
            return false;
        }
    }
}
