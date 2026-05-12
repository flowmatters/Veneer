# Schematic SVG Endpoint — Design

## Background

Hydrograph dashboards include a `DynamicSvgWidgetComponent` that fetches an SVG document, sanitises it through DOMPurify, and substitutes `$tag$` placeholders against a row of a backing table on every render. See `C:/Dropbox/tmp/dynamic-svg-authoring.md` for the widget's runtime contract.

Veneer already exposes the Source node-link network as GeoJSON at `/network` (`GeoJSONNetwork`, `GeoJSONFeature`), but that response can't be styled in a dashboard. This design adds an SVG-rendered view of the network using the schematic coordinate system, with `$tag$` hooks so dashboards can drive colour, width, and opacity of every link and node from table data.

## Goals

- Render the whole node-link network (no catchments) as a single self-contained SVG document, in the model's schematic coordinate space.
- Expose per-element `$tag$` substitution points so a dashboard can restyle every link and styleable node independently from a single table row.
- Provide a companion JSON sidecar mapping element names to the sanitised tag names so dashboard authors can build the styling table reliably.
- Replace selected node-type icons with simple geometric SVG shapes (filled with `$tag$` colours); fall back to the existing PNG resources for the rest.
- Keep the SVG-building logic isolated from WCF/Source plumbing so it is unit-testable.

## Non-goals

- Subsetting the network via query parameters (e.g. "between node A and node B"). The endpoint shape leaves room for this; the implementation does not include it.
- A geographic-coordinate variant. `/network` already serves geographic data; this endpoint is schematic-only.
- Restyling the bitmap icons themselves. Authors who want fully restylable nodes can request the corresponding type be added to the SVG icon library.
- Per-node icon size overrides. One global icon size, computed from the network's bounding box.
- Catchments. Catchments live in a different coordinate system from the schematic and are excluded.

## Endpoints

Two new endpoints, declared on `ISourceService`:

### `GET /network/schematic.svg`

Returns the SVG document. Content-type `image/svg+xml`.

Errors:

| Condition | Status | Body |
|---|---|---|
| No active scenario | 404 | `{ "error": "no scenario loaded" }` |
| Active scenario but no schematic configuration | 404 | `{ "error": "scenario has no schematic; use /network for geographic coordinates" }` |
| All nodes at the same schematic point | 200 | Document still emitted with a synthesised minimum 100×100 viewBox so the SVG is valid. |

### `GET /network/schematic.svg/tags`

Returns the sidecar JSON. The sidecar is the source of truth for the sanitised, collision-resolved `tag_name` used in every `$tag$`. All other element metadata (model type, URL, geometry, etc.) is already available from `/network` — the sidecar deliberately stays narrow.

```json
{
  "viewBox": [minX, minY, width, height],
  "nodes": [
    {
      "name": "Burrendong Dam",
      "tag_name": "burrendong",
      "tags": ["fill", "stroke", "opacity", "label"],
      "icon_kind": "svg",
      "icon_shape": "triangle",
      "hg_value": "node:burrendong"
    },
    {
      "name": "Some Other Node",
      "tag_name": "some_other_node",
      "tags": ["opacity", "label"],
      "icon_kind": "png",
      "hg_value": "node:some_other_node"
    }
  ],
  "links": [
    {
      "name": "Link #5",
      "tag_name": "link_5",
      "tags": ["stroke", "stroke_width", "opacity", "label"],
      "hg_value": "link:link_5"
    }
  ]
}
```

`name` is the join key against `/network` (matches the `properties.name` field that endpoint already emits).

## Tag scheme

Every styleable element gets per-element `$tag$` placeholders. The widget binds one table row to the whole document, so the styling table has one column per `(element, attribute)` pair.

### Tag name construction

Tags follow the pattern `<kind>_<tag_name>_<attribute>`:

- `kind` is `link` or `node`.
- `tag_name` is the element's `Name` after sanitisation and collision resolution.
- `attribute` is one of the per-kind attributes below.

### Sanitisation rule

Element names are sanitised so they fit the widget's tag regex (`[A-Za-z_-][A-Za-z0-9_-]*`) and survive normalisation as table column names:

1. Lowercase.
2. Replace any run of characters outside `[A-Za-z0-9]` with a single `_`.
3. Collapse repeated `_`.
4. Trim leading and trailing `_`.
5. If the result is empty (name was all punctuation), fall back to `link_<index>` / `node_<index>`.

Examples: `"Storage @ Site #3"` → `storage_site_3`, `"Link  -- 5"` → `link_5`.

### Collision resolution

If two elements (within the same kind) sanitise to the same `tag_name`, append `_2`, `_3`, ... in iteration order of `network.nodes` / `network.links`. The sidecar surfaces the final, de-collided names so consumers never have to replicate this logic.

### Attributes per kind

| Element | Attributes |
|---|---|
| Link | `stroke`, `stroke_width`, `opacity`, `label` |
| Node (SVG-restylable) | `fill`, `stroke`, `opacity`, `label` |
| Node (PNG fallback) | `opacity`, `label` |

`label` populates a `<title>` element so authors can use it for tooltips without changing visual layout.

### Click selection

Every link and every node (including PNG-fallback nodes) carries `data-hg-value="link:<tag_name>"` or `data-hg-value="node:<tag_name>"`. The `link:` / `node:` prefix lets a single dashboard option distinguish a clicked link from a clicked node when both could share a `tag_name`. The sidecar surfaces these values as the `hg_value` field.

A small `<style>` block at the top of the document gives clicked elements a default highlight (`stroke-width: 3; stroke: #1a73e8`). Dashboard CSS can override this.

### Defaults

The root `<svg>` declares document-wide defaults via `data-default-*` so every element renders sensibly when no row is selected or when a column is null:

```xml
data-default-link-stroke="#888888"
data-default-link-stroke-width="2"
data-default-link-opacity="1"
data-default-node-fill="#cccccc"
data-default-node-stroke="#333333"
data-default-node-opacity="1"
```

The widget's `data-default-<name>` lookup is keyed by tag name, so `$link_5_stroke$` falls back to the value of `data-default-link-5-stroke` — but we don't emit per-element defaults, so the global ones above are what actually apply via the widget's fallback chain. (Equivalent to: dashboard table missing → grey skeleton diagram.)

## Coordinates and sizing

### Source

Schematic coordinates are read via the same path `GeoJSONFeature` uses: `ScriptHelpers.GetSchematic(scenario)` returns a `SchematicNetworkConfigurationPersistent`, and `shape.Location` (a `PointF`) holds each node's position. Links use a straight line between their upstream and downstream nodes' schematic points (matching `GeoJSONFeature.cs:117`).

### viewBox

1. Compute the bounding box of all node schematic locations.
2. Pad by 5% of `max(width, height)` on each side.
3. Source schematic Y grows upward; SVG y grows downward. Negate Y at emit time. The viewBox therefore uses `-maxYsource` as `minY` and the bbox height as `height`.
4. Degenerate case (zero width or zero height after considering all nodes — e.g. a single-node network): centre the point in a 100×100 viewBox so the document is still valid.

### Icon size

Single global size: `iconSize = diag / 80` where `diag = sqrt(width² + height²)` of the unpadded bbox. Every node icon (SVG or PNG) is centred on its node location.

## SVG document structure

```xml
<svg xmlns="http://www.w3.org/2000/svg"
     viewBox="<minX> <minY> <width> <height>"
     data-default-link-stroke="#888888"
     data-default-link-stroke-width="2"
     data-default-link-opacity="1"
     data-default-node-fill="#cccccc"
     data-default-node-stroke="#333333"
     data-default-node-opacity="1">
  <defs>
    <symbol id="veneer-icon-circle"    viewBox="-1 -1 2 2">…</symbol>
    <symbol id="veneer-icon-triangle"  viewBox="-1 -1 2 2">…</symbol>
    <symbol id="veneer-icon-diamond"   viewBox="-1 -1 2 2">…</symbol>
    <symbol id="veneer-icon-hexagon"   viewBox="-1 -1 2 2">…</symbol>
    <symbol id="veneer-icon-plus"      viewBox="-1 -1 2 2">…</symbol>
    <symbol id="veneer-icon-trapezoid" viewBox="-1 -1 2 2">…</symbol>
  </defs>
  <style>
    [data-hg-value] { cursor: pointer; }
    .hg-selected { stroke: #1a73e8; stroke-width: 3; }
  </style>

  <g class="veneer-links">
    <line x1="…" y1="…" x2="…" y2="…"
          stroke="$link_<tag>_stroke$"
          stroke-width="$link_<tag>_stroke_width$"
          opacity="$link_<tag>_opacity$"
          data-hg-value="link:<tag>">
      <title>$link_<tag>_label$</title>
    </line>
    <!-- one per link -->
  </g>

  <g class="veneer-nodes">
    <!-- SVG-restylable node: -->
    <use href="#veneer-icon-<shape>"
         x="…" y="…" width="…" height="…"
         fill="$node_<tag>_fill$"
         stroke="$node_<tag>_stroke$"
         opacity="$node_<tag>_opacity$"
         data-hg-value="node:<tag>">
      <title>$node_<tag>_label$</title>
    </use>

    <!-- PNG-fallback node: -->
    <image href="/resources/<IconName>"
           x="…" y="…" width="…" height="…"
           opacity="$node_<tag>_opacity$"
           data-hg-value="node:<tag>">
      <title>$node_<tag>_label$</title>
    </image>
  </g>
</svg>
```

Links are emitted before nodes so nodes paint on top.

The `<symbol>` definitions in `<defs>` are emitted unconditionally (small, < 1 KB total) so the document is self-contained even if which shapes are needed changes between scenarios.

## Icon library

Six embedded SVG resources under `FlowMatters.Source.Veneer/Resources/NodeIcons/`:

- `circle.svg`
- `triangle.svg`
- `diamond.svg`
- `hexagon.svg`
- `plus.svg` (fat plus)
- `trapezoid.svg` (isosceles)

Each is authored in a `-1 -1 2 2` viewBox as a single path or primitive with **no baked-in fill or stroke**. Fill and stroke come from the `<use>` element's attributes (which carry the `$tag$` placeholders), so each shape can pick up per-node colour without re-emitting geometry. The files are loaded once at assembly startup and cached.

### Node-type → shape mapping

`NodeIconLibrary` is a static dictionary keyed by `NodeModel` short type name:

| NodeModel type | Shape |
|---|---|
| `InflowNodeModel` | plus |
| `ConfluenceNodeModel` | circle |
| `GaugeNodeModel` (and stream-gauge variants) | trapezoid |
| `StorageNodeModel` | triangle |
| `SupplyPointNodeModel` (and water-user supply variants) | diamond |
| `MinimumFlowRequirementNodeModel` | hexagon |
| `MaximumFlowConstraintNodeModel` | hexagon |
| anything else | `null` → PNG fallback |

The exact RiverSystem type names are verified during implementation against the loaded assembly; "variants" above is shorthand for "any NodeModel whose short type name matches a related pattern". The mapping is a single small dictionary so it is easy to extend later.

### PNG fallback

For nodes whose type isn't in the table, emit `<image href="/resources/{ResourceName(n)}" .../>` using the same `ResourceName` helper as `GeoJSONFeature.cs:73`. The widget fetches the SVG from the Veneer host, so the relative `/resources/...` URL resolves against the same origin. CORS is already configured globally.

PNG-fallback nodes still participate in `$opacity$` and `$label$` (and `data-hg-value` click selection), but not `$fill$` or `$stroke$` — those tags are simply absent from the document and from the element's `tags` array in the sidecar.

## Code layout

New files under `FlowMatters.Source.Veneer/`:

- `Formatting/SchematicSvgBuilder.cs` — pure function `(Network, SchematicNetworkConfigurationPersistent) → SchematicSvgResult` where `SchematicSvgResult` contains the SVG string and the sidecar DTO. The builder has no Source or WCF dependencies beyond what it's handed; it consumes `Network` and the schematic configuration and emits strings. Unit-testable against synthesised fixtures.
- `Formatting/NodeIconLibrary.cs` — type-name → shape lookup and lazy embedded-resource loader for the six SVG snippets.
- `ExchangeObjects/SchematicTagMap.cs` — DTOs for the sidecar JSON (`SchematicTagMap`, `SchematicNodeTag`, `SchematicLinkTag`).
- `Resources/NodeIcons/*.svg` — six shape files, marked as embedded resources in the csproj.

Modified files:

- `ISourceService.cs` — declare the two new `[WebGet]` methods.
- `SourceService.cs` — implement them: pull `_sharedScenario.Network`, fetch the schematic, return 404 with explanatory JSON if absent, otherwise hand off to `SchematicSvgBuilder`. Bump `PROTOCOL_VERSION` in `VeneerStatus.cs` (per project memory rule for REST API surface changes).
- `UriTemplates.cs` — add `SchematicSvg = "/network/schematic.svg"` and `SchematicSvgTags = "/network/schematic.svg/tags"`.

## Testing

- **Unit tests for `SchematicSvgBuilder`** against in-memory `Network` fixtures: empty network, single-node network (degenerate bbox), two-node-one-link network, collision case (two links with same sanitised name), mixed SVG/PNG node types.
- **Sanitisation tests** for the name-mangler covering: spaces, punctuation, leading digits/punctuation, all-punctuation names, Unicode.
- **Sidecar/SVG consistency test** — for every element in the sidecar, assert its `tag_name` appears in the SVG body in every `$tag$` substitution point the `tags` array advertises. Catches drift between the two outputs.
- **DOMPurify round-trip smoke test** — emit a sample SVG, run it through DOMPurify in a Node script (one-off, manually run from `veneer-py` repo or similar), verify nothing critical is stripped. Not automated; documented in the implementation plan as a manual gate.
- **Endpoint integration** via the existing `SourceService` test harness: 404 paths (no scenario, no schematic) and 200 path with a small fixture.

## Out of scope (forward references)

- Network subsetting via query parameters (`?between=<nodeA>&<nodeB>` or similar). Nothing in the endpoint shape precludes adding this later — `SchematicSvgBuilder` takes a `Network`, so the subset could be computed in `SourceService` and a derived sub-network handed in.
- Geographic-coordinate variant. Would be a sibling endpoint, not a parameter.
- Custom icon-size or per-node-importance scaling.
- Restylable bitmap icons (tinting via filters).
- Catchments. Different coordinate system.
