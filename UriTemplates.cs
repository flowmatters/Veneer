using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlowMatters.Source.WebServer
{
    class UriTemplates
    {
        public const string TimeSeries =
            "/runs/{runId}/location/{networkElement}/element/{recordingElement}/variable/{variable}";

        public const string AggregatedTimeSeries =
            "/runs/{runId}/location/{networkElement}/element/{recordingElement}/variable/{variable}/aggregated/{aggregation}";

        public const string RunResults = "/runs/{runId}";
        public const string Runs = "/runs";

        public const string Files = "/doc/{fn}";

        public const string Resources = "/resources/{resourceName}";

        public const string Network = "/network";

        public const string Nodes = "/network/nodes";

        public const string Links = "/network/links";

        public const string Node = "/network/nodes/{nodeId}";

        public const string Link = "/network/link/{linkId}";

        public const string Catchments = "/network/catchments";

        public const string Catchment = "/network/catchments/{catchmentId}";

    }
}
