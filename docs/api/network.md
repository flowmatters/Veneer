# Network

The network endpoints expose the model's nodes, links and catchments as
[GeoJSON](https://geojson.org/), plus a rendered schematic (SVG) and its tag metadata. See
[schemas.md](schemas.md#geojson-types) for the GeoJSON payload shapes.

Two coordinate spaces are available:

- **`/network`** — schematic coordinates (the layout you see in Source's schematic editor).
- **`/network/geographic`** — real-world coordinates (longitude/latitude), reprojected from
  the scenario's projection.

---

## `GET /network`

The whole network as a GeoJSON `FeatureCollection`, in **schematic** coordinates.

- **Response**: [`GeoJSONNetwork`](schemas.md#geojsonnetwork) (`200 OK`)
- **veneer-py**: `v.network()`

Features include nodes (points), links (line strings) and catchments (polygons). Each
feature's `properties.feature_type` is one of `node`, `link`, `lateral_link`,
`conveyance_link`, or `catchment`. Link features carry `from_node`/`to_node` (and
`left_node`/`right_node` for lateral/conveyance links); node features carry `icon`,
`elevation` and `schematic_location`; catchments carry `areaInSquareMeters`. The feature `id`
is the resource URL for that element (e.g. `/network/nodes/3`).

**Example (abridged)**

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "id": "/network/nodes/3",
      "geometry": { "type": "Point", "coordinates": [120.5, -33.2] },
      "properties": {
        "name": "Inflow",
        "feature_type": "node",
        "icon": "/resources/InflowNodeModel",
        "elevation": 210.0,
        "schematic_location": [120.5, -33.2]
      }
    },
    {
      "type": "Feature",
      "id": "/network/link/7",
      "geometry": { "type": "LineString", "coordinates": [[120.5, -33.2], [121.0, -33.4]] },
      "properties": {
        "name": "link for Storage",
        "feature_type": "link",
        "from_node": "/network/nodes/3",
        "to_node": "/network/nodes/5"
      }
    }
  ]
}
```

---

## `GET /network/geographic`

Identical structure to `GET /network`, but geometry coordinates are reprojected to
**geographic** (longitude/latitude) using the scenario's projection.

- **Response**: [`GeoJSONNetwork`](schemas.md#geojsonnetwork) (`200 OK`)
- **veneer-py**: retrieved via the file mirror / direct `retrieve_json('/network/geographic')`

> Use this when you have no schematic, or when you need lon/lat. If the scenario has a
> schematic but you want map coordinates, this is the endpoint you want.

---

## `GET /network/schematic.svg`

A rendered SVG of the schematic, with node icons resolved against `/resources/...`.

- **Response**: SVG XML, `Content-Type: image/svg+xml` (`200 OK`)
- **`404 Not Found`** with a JSON `{"error": "..."}` body when:
  - no scenario is loaded → `{"error":"no scenario loaded"}`
  - the scenario has no schematic →
    `{"error":"scenario has no schematic; use /network for geographic coordinates"}`

```http
GET /network/schematic.svg HTTP/1.1
```

---

## `GET /network/schematic.svg/tags`

The "sidecar" metadata for the schematic SVG: the SVG `viewBox` plus, for each node and link,
the element name, the SVG tag name(s) used to address it in the rendered SVG, and any
hydrologic-grouping value. Use this to map SVG elements back to model elements (e.g. to make
the SVG interactive).

- **Response**: [`SchematicTagMap`](schemas.md#schematictagmap) (`200 OK`)
- **`404 Not Found`** with a `null` body under the same conditions as `schematic.svg` (no
  scenario / no schematic).

```json
{
  "viewBox": [0, 0, 1024, 768],
  "nodes": [
    { "name": "Inflow", "tag_name": "node-3", "tags": ["node-3"],
      "icon_kind": "svg", "hg_value": "" }
  ],
  "links": [
    { "name": "link for Storage", "tag_name": "link-7", "tags": ["link-7"], "hg_value": "" }
  ]
}
```

---

## `GET /network/nodes/{nodeId}`

A single node as a GeoJSON feature. `{nodeId}` is the **integer index** of the node (the same
integer that appears in the feature `id` from `/network`).

- **Response**: [`GeoJSONFeature`](schemas.md#geojsonfeature) (`200 OK`)
- On a non-integer or out-of-range id the implementation logs an error and returns a `null`
  body (effectively "not found").
- **veneer-py**: accessed via the network feature collection rather than a dedicated method.

```http
GET /network/nodes/3 HTTP/1.1
```

---

## `GET /network/link/{linkId}`

A single link as a GeoJSON feature. `{linkId}` is the **integer index** of the link.

- **Response**: [`GeoJSONFeature`](schemas.md#geojsonfeature) (`200 OK`)
- Same `null`-on-bad-id behaviour as nodes.

```http
GET /network/link/7 HTTP/1.1
```

> Note the path is `/network/link/{id}` (singular `link`), not `/network/links/{id}`.

---

<a name="not-implemented"></a>
## Declared but not implemented

`UriTemplates.cs` declares several network templates that are **not wired to any
`ISourceService` method** and therefore are **not live endpoints**. Do not rely on them:

- `/network/nodes` (list) and `/network/links` (list)
- `/network/lateral_link/{linkId}`
- `/network/conveyance_link/{linkId}`
- `/network/catchments` and `/network/catchments/{catchmentId}`

Lateral and conveyance links *do* appear as features within `GET /network` (with the
appropriate `feature_type`), but there is no per-id endpoint for them. To enumerate nodes,
links or catchments, read `GET /network` and filter by `properties.feature_type`.
