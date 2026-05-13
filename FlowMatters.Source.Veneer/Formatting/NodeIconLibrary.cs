using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FlowMatters.Source.Veneer.Formatting
{
    /// <summary>
    /// Maps NodeModel short type names to SVG shape ids, and loads the embedded shape definitions
    /// on first use.
    /// </summary>
    internal static class NodeIconLibrary
    {
        // NodeModel short type name → shape id. Unmapped types fall back to PNG via /resources/.
        private static readonly Dictionary<string, string> TypeToShape =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Names verified against actual NodeModel.GetType().Name in a live scenario.
                { "InjectedFlow",               "plus" },      // inflow
                { "ConfluenceNodeModel",        "circle" },
                { "GaugeNodeModel",             "trapezoid" },
                { "StorageNodeModel",           "triangle" },
                { "ExtractionNodeModel",        "diamond" },   // supply point
                { "MinimumFlowConstraintModel", "hexagon" },   // minimum flow
                { "RiverConstraintNodeModel",   "hexagon" },   // maximum flow constraint
            };

        private static readonly string[] AllShapes =
            { "circle", "triangle", "diamond", "hexagon", "plus", "trapezoid" };

        // Per-shape default fill colour, used as a static-render fallback (CSS) and as the per-element
        // data-default-node_<tag>_fill that the widget falls back to when no row is bound.
        // Shapes not listed here use the generic node-fill default from SchematicSvgBuilder.
        private static readonly Dictionary<string, string> ShapeDefaultFill =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "triangle",  "#1f77b4" },   // storage — blue
                { "plus",      "#1f77b4" },   // inflow — blue
                { "trapezoid", "#d62728" },   // gauge — red
                { "diamond",   "#2ca02c" },   // extraction — green
                { "hexagon",   "#ff7f0e" },   // min/max flow — orange
                // circle (confluence) intentionally absent → use generic grey default.
            };

        /// <summary>Returns the default fill colour for a shape id, or null if it has no per-shape override.</summary>
        public static string GetDefaultFillFor(string shapeId)
        {
            string colour;
            return ShapeDefaultFill.TryGetValue(shapeId ?? "", out colour) ? colour : null;
        }

        /// <summary>Returns all (shape, colour) pairs that have a per-shape override.</summary>
        public static IEnumerable<KeyValuePair<string, string>> AllShapeDefaultFills()
        {
            return ShapeDefaultFill;
        }

        private static readonly Lazy<Dictionary<string, string>> SymbolMarkup =
            new Lazy<Dictionary<string, string>>(LoadAll);

        /// <summary>Returns the shape id for a NodeModel short type name, or null if unmapped.</summary>
        public static string GetShapeFor(string nodeModelTypeName)
        {
            string shape;
            return TypeToShape.TryGetValue(nodeModelTypeName ?? "", out shape) ? shape : null;
        }

        /// <summary>Returns the full SVG markup (the &lt;symbol&gt;…&lt;/symbol&gt;) for a shape id.</summary>
        public static string GetSymbolMarkup(string shapeId)
        {
            return SymbolMarkup.Value[shapeId];
        }

        /// <summary>Returns markup for all shapes (used to populate &lt;defs&gt;).</summary>
        public static IEnumerable<string> GetAllSymbolMarkup()
        {
            foreach (var s in AllShapes) yield return SymbolMarkup.Value[s];
        }

        private static Dictionary<string, string> LoadAll()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var asm = typeof(NodeIconLibrary).Assembly;
            foreach (var shape in AllShapes)
            {
                var resourceName = "FlowMatters.Source.Veneer.Resources.NodeIcons." + shape + ".svg";
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing embedded SVG resource: " + resourceName);
                    using (var reader = new StreamReader(stream))
                    {
                        result[shape] = reader.ReadToEnd().Trim();
                    }
                }
            }
            return result;
        }
    }
}
