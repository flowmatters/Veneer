using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer;
using Microsoft.CSharp.RuntimeBinder;
using RiverSystem;
using RiverSystem.Forms.SchematicBuilder;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.Veneer.Formatting
{
    internal class SchematicSvgResult
    {
        public string Svg { get; set; }
        public SchematicTagMap Sidecar { get; set; }
    }

    internal static class SchematicSvgBuilder
    {
        // Defaults are applied via a <style> block with class selectors. When the widget
        // substitutes a $tag$ into inline style="...", the inline value wins. When the tag
        // doesn't resolve (static render, or no row bound), the literal "$tag$" stays inside
        // style="...", browsers drop it as an invalid CSS value, and the class-rule default
        // applies. That's how unbound documents still render as a grey skeleton.
        private const string DefaultLinkStroke = "#888888";
        private const string DefaultLinkStrokeWidth = "2";
        private const string DefaultOpacity = "1";
        private const string DefaultNodeFill = "#cccccc";
        private const string DefaultNodeStroke = "#333333";

        // Icon size is computed as bbox-diagonal / IconSizeDivisor. Higher = smaller icons.
        private const double IconSizeDivisor = 240.0;

        public static SchematicSvgResult Build(Network network, SchematicNetworkConfigurationPersistent schematic, string resourceBaseUrl)
        {
            // resourceBaseUrl is the scheme+authority of the Veneer host (e.g. "http://localhost:9876"),
            // used to make PNG-fallback <image href="..."> absolute. The widget inlines the SVG into the
            // dashboard's DOM, so relative URLs would resolve against the dashboard's origin and miss.
            // Same-document <use href="#veneer-icon-..."> fragment references are unaffected.

            // 1. Resolve schematic coordinates for every node.
            var nodes = network.nodes.Cast<Node>().ToList();
            var locations = nodes
                .Select(n => SchematicLocationForNode(n, schematic))
                .ToList();

            // 2. Compute bounding box.
            var bbox = ComputeBoundingBox(locations);

            // 3. Sanitise names.
            var nodeTagNames = SchematicNameSanitiser.SaniseAndDeCollide(
                nodes.Select(n => n.Name).ToList(), "node");
            var links = network.links.Cast<Link>().ToList();
            var linkTagNames = SchematicNameSanitiser.SaniseAndDeCollide(
                links.Select(l => l.Name).ToList(), "link");

            // 4. Emit SVG and sidecar in lockstep.
            var sb = new StringBuilder();
            var sidecar = new SchematicTagMap
            {
                ViewBox = new[] { bbox.MinX, bbox.MinY, bbox.Width, bbox.Height }
            };

            EmitSvgHeader(sb, bbox);
            EmitDefs(sb);
            EmitStyle(sb);
            sidecar.Links = EmitLinks(sb, nodes, links, locations, linkTagNames);
            sidecar.Nodes = EmitNodes(sb, nodes, locations, nodeTagNames, bbox.IconSize, resourceBaseUrl);
            EmitSvgFooter(sb);

            return new SchematicSvgResult { Svg = sb.ToString(), Sidecar = sidecar };
        }

        // -- coordinate helpers ----------------------------------------------------------------

        private class BBox
        {
            public double MinX, MinY, Width, Height, IconSize;
        }

        internal static double[] ComputeViewBoxForTesting(IEnumerable<PointF> points)
        {
            var bbox = ComputeBoundingBox(points.ToList());
            return new[] { bbox.MinX, bbox.MinY, bbox.Width, bbox.Height, bbox.IconSize };
        }

        private static BBox ComputeBoundingBox(List<PointF> sourceLocations)
        {
            // Source schematic Y already grows downward (same as SVG), so coordinates pass through
            // unchanged. Flipping previously made the diagram appear upside-down relative to
            // Source's schematic editor view.
            if (sourceLocations.Count == 0 ||
                (sourceLocations.All(p => Math.Abs(p.X - sourceLocations[0].X) < 1e-9 &&
                                          Math.Abs(p.Y - sourceLocations[0].Y) < 1e-9)))
            {
                // Degenerate: single point or empty — use a 100x100 centred viewBox.
                var cx = sourceLocations.Count > 0 ? sourceLocations[0].X : 0.0;
                var cy = sourceLocations.Count > 0 ? sourceLocations[0].Y : 0.0;
                return new BBox
                {
                    MinX = cx - 50, MinY = cy - 50, Width = 100, Height = 100,
                    IconSize = 100.0 / IconSizeDivisor
                };
            }

            var minX = sourceLocations.Min(p => (double)p.X);
            var maxX = sourceLocations.Max(p => (double)p.X);
            var minY = sourceLocations.Min(p => (double)p.Y);
            var maxY = sourceLocations.Max(p => (double)p.Y);

            var rawWidth = maxX - minX;
            var rawHeight = maxY - minY;
            // Pad 5% of max dimension on each side
            var pad = 0.05 * Math.Max(rawWidth, rawHeight);
            var padded = new BBox
            {
                MinX = minX - pad,
                MinY = minY - pad,
                Width = rawWidth + 2 * pad,
                Height = rawHeight + 2 * pad,
            };
            var diag = Math.Sqrt(rawWidth * rawWidth + rawHeight * rawHeight);
            padded.IconSize = diag / IconSizeDivisor;
            return padded;
        }

        private static PointF SchematicLocationForNode(Node n, SchematicNetworkConfigurationPersistent schematic)
        {
            return schematic.ExistingFeatureShapeProperties
                .Where(shape => shape.Feature == n)
                .Select(shape => shape.Location)
                .FirstOrDefault();
        }

        // -- SVG emission ---------------------------------------------------------------------

        private static void EmitSvgHeader(StringBuilder sb, BBox bbox)
        {
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"");
            sb.Append(F(bbox.MinX)); sb.Append(' ');
            sb.Append(F(bbox.MinY)); sb.Append(' ');
            sb.Append(F(bbox.Width)); sb.Append(' ');
            sb.Append(F(bbox.Height));
            sb.Append("\">");
        }

        private static void EmitDefs(StringBuilder sb)
        {
            sb.Append("<defs>");
            foreach (var symbolMarkup in NodeIconLibrary.GetAllSymbolMarkup())
                sb.Append(symbolMarkup);
            sb.Append("</defs>");
        }

        private static void EmitStyle(StringBuilder sb)
        {
            sb.Append("<style>")
              .Append(".veneer-links line{stroke:").Append(DefaultLinkStroke)
              .Append(";stroke-width:").Append(DefaultLinkStrokeWidth)
              .Append(";opacity:").Append(DefaultOpacity).Append(";}")
              .Append(".veneer-nodes use{fill:").Append(DefaultNodeFill)
              .Append(";stroke:").Append(DefaultNodeStroke)
              .Append(";opacity:").Append(DefaultOpacity).Append(";}")
              .Append(".veneer-nodes image{opacity:").Append(DefaultOpacity).Append(";}")
              .Append("[data-hg-value]{cursor:pointer;}")
              .Append(".hg-selected{stroke:#1a73e8;stroke-width:3;}")
              .Append("</style>");
        }

        private static SchematicLinkTag[] EmitLinks(
            StringBuilder sb,
            List<Node> nodes,
            List<Link> links,
            List<PointF> locations,
            List<string> linkTagNames)
        {
            var nodeIndex = new Dictionary<Node, int>();
            for (int i = 0; i < nodes.Count; i++) nodeIndex[nodes[i]] = i;

            var sidecarLinks = new List<SchematicLinkTag>(links.Count);
            sb.Append("<g class=\"veneer-links\">");
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                var tag = linkTagNames[i];
                int fromIdx, toIdx;
                if (!nodeIndex.TryGetValue((Node)link.UpstreamNode, out fromIdx) ||
                    !nodeIndex.TryGetValue((Node)link.DownstreamNode, out toIdx))
                {
                    // Defensive: link to a node not in network.nodes — skip.
                    continue;
                }
                var fromLoc = locations[fromIdx];
                var toLoc = locations[toIdx];

                sb.Append("<line x1=\"").Append(F(fromLoc.X))
                  .Append("\" y1=\"").Append(F(fromLoc.Y))
                  .Append("\" x2=\"").Append(F(toLoc.X))
                  .Append("\" y2=\"").Append(F(toLoc.Y))
                  .Append("\" style=\"stroke:$link_").Append(tag).Append("_stroke$")
                  .Append(";stroke-width:$link_").Append(tag).Append("_stroke_width$")
                  .Append(";opacity:$link_").Append(tag).Append("_opacity$\"")
                  .Append(" data-hg-value=\"link:").Append(tag).Append("\"")
                  .Append("><title>$link_").Append(tag).Append("_label$</title></line>");

                sidecarLinks.Add(new SchematicLinkTag
                {
                    Name = link.Name,
                    TagName = tag,
                    Tags = new[] { "stroke", "stroke_width", "opacity", "label" },
                    HgValue = "link:" + tag,
                });
            }
            sb.Append("</g>");
            return sidecarLinks.ToArray();
        }

        private static SchematicNodeTag[] EmitNodes(
            StringBuilder sb,
            List<Node> nodes,
            List<PointF> locations,
            List<string> nodeTagNames,
            double iconSize,
            string resourceBaseUrl)
        {
            var sidecarNodes = new List<SchematicNodeTag>(nodes.Count);
            sb.Append("<g class=\"veneer-nodes\">");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var tag = nodeTagNames[i];
                var loc = locations[i];

                var modelTypeName = NodeModelTypeName(n);
                var shape = NodeIconLibrary.GetShapeFor(modelTypeName);

                var x = loc.X - iconSize / 2.0;
                var y = loc.Y - iconSize / 2.0;
                var modelTypeAttr = modelTypeName != null ? HtmlEscape(modelTypeName) : "";

                if (shape != null)
                {
                    sb.Append("<use href=\"#veneer-icon-").Append(shape).Append("\"")
                      .Append(" x=\"").Append(F(x))
                      .Append("\" y=\"").Append(F(y))
                      .Append("\" width=\"").Append(F(iconSize))
                      .Append("\" height=\"").Append(F(iconSize)).Append("\"")
                      .Append(" style=\"fill:$node_").Append(tag).Append("_fill$")
                      .Append(";stroke:$node_").Append(tag).Append("_stroke$")
                      .Append(";opacity:$node_").Append(tag).Append("_opacity$\"")
                      .Append(" data-hg-value=\"node:").Append(tag).Append("\"")
                      .Append(" data-veneer-model-type=\"").Append(modelTypeAttr).Append("\"")
                      .Append("><title>$node_").Append(tag).Append("_label$</title></use>");

                    sidecarNodes.Add(new SchematicNodeTag
                    {
                        Name = n.Name,
                        TagName = tag,
                        Tags = new[] { "fill", "stroke", "opacity", "label" },
                        IconKind = "svg",
                        IconShape = shape,
                        HgValue = "node:" + tag,
                    });
                }
                else
                {
                    var iconRes = WebUtility.UrlEncode(ResourceNameForNode(n));
                    sb.Append("<image href=\"").Append(resourceBaseUrl).Append("/resources/").Append(iconRes).Append("\"")
                      .Append(" x=\"").Append(F(x))
                      .Append("\" y=\"").Append(F(y))
                      .Append("\" width=\"").Append(F(iconSize))
                      .Append("\" height=\"").Append(F(iconSize)).Append("\"")
                      .Append(" style=\"opacity:$node_").Append(tag).Append("_opacity$\"")
                      .Append(" data-hg-value=\"node:").Append(tag).Append("\"")
                      .Append(" data-veneer-model-type=\"").Append(modelTypeAttr).Append("\"")
                      .Append("><title>$node_").Append(tag).Append("_label$</title></image>");

                    sidecarNodes.Add(new SchematicNodeTag
                    {
                        Name = n.Name,
                        TagName = tag,
                        Tags = new[] { "opacity", "label" },
                        IconKind = "png",
                        IconShape = null,
                        HgValue = "node:" + tag,
                    });
                }
            }
            sb.Append("</g>");
            return sidecarNodes.ToArray();
        }

        private static void EmitSvgFooter(StringBuilder sb) { sb.Append("</svg>"); }

        // -- type-name extraction (mirrors GeoJSONFeature.ResourceName) ----------------------

        private static string NodeModelTypeName(Node n)
        {
            var model = RetrieveNodeModel(n);
            return model != null ? model.GetType().Name : null;
        }

        private static string ResourceNameForNode(Node n)
        {
            object o = (object)RetrieveNodeModel(n) ?? n.FlowPartitioning;
            return o.GetType().Name;
        }

        private static NodeModel RetrieveNodeModel(dynamic n)
        {
            try { return n.NodeModel; }
            catch (RuntimeBinderException) { return n.NodeModels[0]; }
        }

        // -- formatting helpers --------------------------------------------------------------

        private static string F(double v) { return v.ToString("G6", CultureInfo.InvariantCulture); }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
