using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlowMatters.Source.WebServer
{
    class UriTemplates
    {
        public const string Recordable = "location/{networkElement}/element/{recordingElement}/variable/{variable}";

        public const string TimeSeries = "/runs/{runId}/"+Recordable;

        public const string AggregatedTimeSeries =
            "/runs/{runId}/location/{networkElement}/element/{recordingElement}/variable/{variable}/aggregated/{aggregation}";

        public const string RunResults = "/runs/{runId}";
        public const string Runs = "/runs";

        public const string Files = "/doc/{*fn}";

        public const string FilesD = "/doc/{dir}/{fn}";

        public const string FilesDD = "/doc/{dir1}/{dir2}/{fn}";

        public const string FilesQuery = "/doc/{*fn}?v={version}";

        public const string Resources = "/resources/{resourceName}";

        public const string Network = "/network";

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

        public const string MatchAll = "__all__";
    }
}
