# Tables, configuration, projection & files

A grab-bag of supporting endpoints: model tables, enumerating configuration values, setting
the map projection, and serving project files and embedded resources.

---

## `GET /tables`

Index of the model tables Veneer can tabulate (each is a named, addressable table).

- **Response**: [`ModelTableIndex`](schemas.md#modeltableindex) (`200 OK`)
- **veneer-py**: model tables are fetched directly via `v.model_table(table)`

```json
{
  "Tables": [
    { "Name": "fus", "Url": "/tables/fus" },
    { "Name": "nodes", "Url": "/tables/nodes" }
  ]
}
```

---

## `GET /tables/{table}`

One model table.

- **Response**: a serialised `DataTable` (`200 OK`). Request `Accept: text/csv` for CSV
  (which is what veneer-py and the mirror retriever use).
- **`404 Not Found`** (with `null` body) if `{table}` is not a known table name.
- **veneer-py**: `v.model_table(table='fus')`

```http
GET /tables/fus HTTP/1.1
Accept: text/csv
```

---

## `GET /configuration/{element}`

Enumerate distinct configuration values. Used to populate pickers for building time series /
recorder URLs.

- **`{element}`** (case-insensitive):
  - `networkelement` → distinct network element names
  - `recordingelement` → distinct recording element names
  - anything else → empty array
- **Response**: `string[]` (`200 OK`)
- **veneer-py**: used internally when building recording instructions

```http
GET /configuration/networkelement HTTP/1.1
```

```json
["Gauge A", "Storage 1", "Inflow"]
```

---

## `PUT /projection`

Assign the scenario's map projection (used when reprojecting the network to geographic
coordinates).

- **Request body**: [`ProjectionInfo`](schemas.md#projectioninfo) — `Projection`, `Zone`,
  `Hemisphere`.
- **Response**: none (`200 OK`)
- **veneer-py**: set via server-side scripting / direct `update_json('/projection', ...)`

```http
PUT /projection HTTP/1.1
Content-Type: application/json

{ "Projection": "UTM", "Zone": 55, "Hemisphere": "South" }
```

---

## `GET /doc/{*fn}`

Serve a file from the project directory. `{*fn}` is a catch-all path relative to the project's
file directory (so it can contain slashes).

- **Response**: file bytes; `Content-Type` is inferred from the file extension (`200 OK`).
- There is also a `GET /doc/{*fn}?v={version}` form; the `v` query parameter is accepted but
  **ignored** (it delegates to the same handler) — useful only as a cache-buster.
- A missing/unreadable file surfaces as a server error (no special 404 body).
- **veneer-py**: used for retrieving project-relative documents/resources.

```http
GET /doc/reports/summary.html HTTP/1.1
```

---

## `GET /resources/{resourceName}`

Serve an embedded resource (node/link icons) as a PNG. This is what GeoJSON feature
`properties.icon` URLs and the schematic SVG point at.

- **Response**: PNG bytes, `Content-Type: image/png` (`200 OK`)
- A missing resource surfaces as a server error.
- **veneer-py**: icons are captured to the mirror as `.png` files.

```http
GET /resources/InflowNodeModel HTTP/1.1
```
