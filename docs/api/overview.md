# Overview & conventions

This page covers everything that applies across the whole API: the base URL, how requests
and responses are encoded, the error model, CORS, and the cross-cutting naming rules you
need to address network elements and time series.

## Base URL and hosting

Veneer hosts a Kestrel + CoreWCF HTTP server inside the Source process (or inside the
headless `VeneerCmd.exe`).

- **Default**: `http://localhost:9876`
- The port is configurable (Veneer status panel in Source, or `--port` for `VeneerCmd`).
  When started from a `.veneer` file or `VENEER_PORT` environment variable, that value wins.
- **Prefix**: there is no path prefix by default; endpoints are rooted at `/`.
- **HTTPS**: the server can be configured with SSL. When enabled, use `https://` — the
  endpoint paths are identical.
- There is **no authentication**. Anyone who can reach the port has full access, including
  the ability to run arbitrary IronPython in-process (when scripting is enabled). Bind to
  `localhost` or firewall the port accordingly.

There is exactly **one model loaded at a time**. All shared state (the active scenario, the
run lock, the recorded run logs) is static and process-wide, so two clients talk to the same
model. There is no per-client session.

## Content types

- **Responses** are JSON (`application/json`) unless the endpoint returns a binary stream.
  The binary endpoints are:
  - `GET /network/schematic.svg` → `image/svg+xml`
  - `GET /resources/{resourceName}` → `image/png`
  - `GET /doc/{*fn}` → content-type inferred from the file extension
- `GET /tables/{table}` and time series can be requested as CSV by sending
  `Accept: text/csv` (veneer-py uses this for tables and bulk time series).
- **Request bodies** (POST/PUT) are JSON. Send `Content-Type: application/json`.

### JSON serialisation notes

The server serialises via WCF `DataContract`/`DataMember`. A few consequences worth knowing:

- **Property names are PascalCase** as declared in the `[DataContract]` types (e.g. `Name`,
  `StartDate`, `Events`). The GeoJSON and schematic-tag payloads are the exception — they use
  lowercase / snake_case names (`type`, `features`, `feature_type`, `tag_name`) to match the
  GeoJSON spec and the schematic tooling.
- **Infinities**: time series values that are infinite are emitted as the JSON tokens `INF`
  and `-INF` (not valid JSON numbers). veneer-py rewrites these to `Infinity` before parsing;
  a strict JSON parser will choke on them, so handle this if you write your own client.
- **`NoDataValue`**: time series carry a `NoDataValue` (often a large sentinel). Gaps are
  represented with that value rather than `null`.
- **Dates** in payloads are strings (see below), not JSON dates.

## Date and number conventions

### Partial dates (query parameters)

The time series query parameters `from` and `to` accept **partial dates**:

| You send | `from` resolves to | `to` resolves to |
|----------|--------------------|------------------|
| `2023` | 1 Jan 2023 | 31 Dec 2023 |
| `2023-06` | 1 Jun 2023 | 30 Jun 2023 |
| `2023-06-15` | 15 Jun 2023 | 15 Jun 2023 |

`from` rounds to the **start** of the period; `to` rounds to the **end**. Omitting a bound
means "from the start of the series" / "to the end of the series".

### Dates in bodies and responses

Dates inside JSON payloads (`StartDate`, `EndDate`, `DateRun`, time series event `Date`,
etc.) are strings. Run-status dates use `yyyy-MM-dd`, and the live simulation date uses
`yyyy-MM-dd HH:mm:ss`. Time series event dates use the model's date formatting.

### `precision`

The `precision` query parameter on time series requests is an integer number of decimal
places to round values to. Omit it for full precision.

## Addressing network elements

Several endpoints (time series, recorders) take a **network element** name. Two special
conventions apply:

### `@@` — functional unit delimiter

A network element name may carry a functional unit (FU) suffix, delimited by `@@`:

```
SubCatchment1@@Forest
```

The server splits on `@@` (`UriTemplates.NETWORK_ELEMENT_FU_DELIMITER`) into the element name
and the functional unit. Use this to target a specific FU within a catchment.

### `__all__` — match-all wildcard

Where a path segment names a network element, recording element, or variable, the literal
token `__all__` (`UriTemplates.MatchAll`) matches **all** values for that segment. For
example, a time series URL with `__all__` for the network element returns every matching
series (as a `MultipleTimeSeries` response). `__all__` is also used in the data-source
"multiple item details" endpoint and as a special `runId`/`functions` value.

### URL encoding

Names frequently contain spaces, slashes and other characters. URL-encode path segments.
Note the data-source endpoints decode `%25`→`%` and `%2F`→`/` server-side, and veneer-py
escapes `/` as `%2F` (optionally double-escaping to `%252F`) when a name contains a slash —
see [data-sources.md](data-sources.md).

## The error model

Veneer does not use a single uniform error envelope. There are three patterns:

1. **Status code + `null`/empty body** — most GET-by-id endpoints. When a resource isn't
   found, the server sets the HTTP status (usually `404 Not Found`) and returns a `null`
   JSON body or no body. Examples: `GET /dataSources/{group}` (404), `GET /tables/{table}`
   (404), `GET /network/nodes/{id}` (returns `null` on a bad id).

2. **`{"error": "..."}` JSON** — a few endpoints write a small JSON error object with an
   `error` string. The clearest example is `GET /network/schematic.svg`, which returns `404`
   with `{"error":"no scenario loaded"}` or
   `{"error":"scenario has no schematic; use /network for geographic coordinates"}`.

3. **`SimulationFault`** — the run endpoints (`POST /runs`, `POST /runs/cancel`) throw a WCF
   `WebFaultException<SimulationFault>`. The body is a JSON
   [`SimulationFault`](schemas.md#simulationfault) (`Message` + `StackTrace`) and the status
   is `409 Conflict` (a run is already in progress) or `500 Internal Server Error` (the run
   threw).

Other notable status codes:

| Status | Where | Meaning |
|--------|-------|---------|
| `302 Found` | `POST /runs` | Run completed; `Location` header points at the new run |
| `403 Forbidden` | `POST /ironpython`, `POST /custom/{action}` | Scripting is disabled on the server |
| `400 Bad Request` | `POST /scenario/{scenario}`, `POST /inputSets/{name}/{action}` | Invalid operation (e.g. setting a scenario while running in the GUI; an action other than `run`) |
| `404 Not Found` | `POST /runs/cancel` | No active run to cancel |

Because the framework also produces its own faults for malformed requests
(deserialisation failures, unknown routes), defensive clients should treat any non-2xx as an
error and read the body opportunistically.

## CORS and OPTIONS

Veneer is designed to be called from browsers, so it implements CORS:

- **`OPTIONS *`** (`GetOptions`) responds to preflight requests. It sets
  `Access-Control-Allow-Methods: GET, PUT, POST, DELETE`,
  `Access-Control-Allow-Headers: Content-Type, Accept`, and
  `Access-Control-Max-Age: 1728000`.
- A CORS message inspector (in `CORS/`) runs on every response: it echoes the request's
  `Origin` back in `Access-Control-Allow-Origin` and adds
  `Access-Control-Request-Method: POST,GET,PUT,DELETE,OPTIONS` and
  `Access-Control-Allow-Headers: X-Requested-With,Content-Type`.

In practice this means any origin is allowed. There is no credentialed-CORS configuration.

## Protocol version

`GET /` returns a [`VeneerStatus`](schemas.md#veneerstatus) whose `Version` field is the
**protocol version** — an integer date stamp (currently `20260519`). Clients can read this to
detect API-surface changes. The constant lives in
[`VeneerStatus.cs`](../../FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs) as
`PROTOCOL_VERSION` and is bumped whenever the REST surface changes.

## The veneer-py client

The reference Python client, [veneer-py](https://github.com/flowmatters/veneer-py), wraps all
of this. Its low-level helpers are:

- `retrieve_json(url)` — GET + parse JSON (rewrites `INF`/`-INF`)
- `retrieve_csv(url)` — GET with `Accept: text/csv`
- `update_json(url, data)` — PUT JSON
- `post_json(url, data)` — POST JSON
- `send_json(url, data, method)` — PUT/POST JSON

Each endpoint page lists the matching high-level method; the full map is in
[veneer-py-crossref.md](veneer-py-crossref.md).
