# JSON schemas

Reference for every JSON payload shape used by the API. Field names are the JSON names as
serialised (WCF `DataMember`), which are PascalCase except where noted (GeoJSON and schematic
tags use lowercase / snake_case). Types are given as JSON types with the C# type in
parentheses where helpful.

Each type lists its source file. Unless stated otherwise, fields map 1:1 to `[DataMember]`
properties.

> **Reminder:** time series values can be the literal tokens `INF`/`-INF`; gaps use
> `NoDataValue`; dates are strings. See [overview.md](overview.md#json-serialisation-notes).

---

## Status & lifecycle

### VeneerStatus
`ExchangeObjects/VeneerStatus.cs` · response of `GET /`

| Field | Type | Notes |
|-------|------|-------|
| `Version` | number (int) | Protocol version, e.g. `20260519` (`PROTOCOL_VERSION`) |
| `SourceVersion` | string | Source product version |
| `ProjectFile` | string | Project file name |
| `ProjectFullFilename` | string \| null | Absolute path, or `null` if unsaved |
| `Scenario` | string | Active scenario name |
| `Projection` | [ProjectionInfo](#projectioninfo) | Scenario projection |
| `PID` | number (int) | Host process id |
| `HostExe` | string | Host executable path |
| `User` | string | OS user name |
| `PluginsLoaded` | string[] | Paths of active plugins |

---

## Runs & results

### RunParameters
`ExchangeObjects/RunParameters.cs` · request body of `POST /runs`

A **flat, open key/value object** (implemented via `ISerializable`, not fixed `DataMember`s).
Whatever keys you send become run parameters passed to Source. There is no fixed schema; the
accepted keys depend on the model and Source version. Commonly used keys:

| Key | Type | Meaning |
|-----|------|---------|
| `Start` | string | Run start date |
| `End` | string | Run end date |
| `InputSet` | string | Input set to apply |
| `ForecastLength` | number (int) | Forecast horizon (where applicable) |

```json
{ "Start": "1990-01-01", "End": "2000-12-31", "InputSet": "Baseline" }
```

An empty object `{}` runs with the scenario's configured settings.

### RunLink
`ExchangeObjects/RunLink.cs` · element of the `GET /runs` array

| Field | Type | Notes |
|-------|------|-------|
| `RunName` | string | |
| `RunUrl` | string | e.g. `/runs/1` — follow for the summary |
| `DateRun` | string | Invariant-culture timestamp |
| `Scenario` | string | |
| `Status` | string | Run result indicator (e.g. `RanToCompletion`) |
| `StartDate` | string \| null | Configured start |
| `EndDate` | string \| null | Configured end |
| `TimeStep` | string \| null | Configured time step |

### RunSummary
`ExchangeObjects/RunSummary.cs` · response of `GET /runs/{runId}`

| Field | Type | Notes |
|-------|------|-------|
| `DateRun` | string | |
| `Name` | string | |
| `Scenario` | string | |
| `Number` | number (int) | Run number |
| `Status` | string | Run result indicator |
| `StartDate` / `EndDate` / `TimeStep` | string | Configuration metadata |
| `RunLog` | string[] | Captured log (placeholder for non-Veneer runs) |
| `Results` | [TimeSeriesLink](#timeserieslink)[] | Recorded outputs |

### TimeSeriesLink
`ExchangeObjects/TimeSeriesLink.cs` · element of `RunSummary.Results`

| Field | Type | Notes |
|-------|------|-------|
| `RunNumber` | number (int) | |
| `TimeSeriesName` | string | |
| `TimeSeriesUrl` | string | Ready-to-use time series URL |
| `NetworkElement` | string | |
| `RecordingElement` | string | |
| `RecordingVariable` | string | |
| `FunctionalUnit` | string \| null | |

### RunStatus
`ExchangeObjects/RunStatus.cs` · response of `GET /runs/status`

| Field | Type | Notes |
|-------|------|-------|
| `IsRunning` | boolean | |
| `CanCancel` | boolean | |
| `Scenario` | string | Only when running |
| `StartDate` / `EndDate` | string | Config dates (`yyyy-MM-dd`), only when running |
| `CurrentDate` | string | Live sim date (`yyyy-MM-dd HH:mm:ss`), only when running |
| `PercentComplete` | number | 0–100, only when running |

### SimulationFault
`ExchangeObjects/SimulationFault.cs` · fault body of `POST /runs` (409/500) and `POST /runs/cancel` (500)

| Field | Type | Notes |
|-------|------|-------|
| `Message` | string | |
| `StackTrace` | string | |

---

## Time series responses

The time series GET endpoints return a `TimeSeriesResponse` whose concrete type depends on the
request. All inherit the common metadata `TimeSeriesReponseMeta`.

### TimeSeriesReponseMeta (base)
`ExchangeObjects/TimeSeriesResponse.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | |
| `Units` | string | |
| `StartDate` / `EndDate` | string | |
| `TimeStep` | string | e.g. `Daily` |
| `NoDataValue` | number | Sentinel for gaps |
| `Min` / `Max` / `Mean` / `Sum` | number | Summary stats |

### SimpleTimeSeries
`ExchangeObjects/SimpleTimeSeries.cs` — extends the meta above with explicit dated events.
Returned for a single-match time series GET; also the request/response body for
`/variables/{name}/TimeSeries`.

| Field | Type | Notes |
|-------|------|-------|
| *(all meta fields)* | | |
| `Events` | [TimeSeriesEvent](#timeseriesevent)[] | Dated values |

### TimeSeriesEvent
`ExchangeObjects/TimeSeriesEvent.cs`

| Field | Type |
|-------|------|
| `Date` | string |
| `Value` | number |

### SlimTimeSeries
`ExchangeObjects/SlimTimeSeries.cs` — a compact form: values only (no per-event dates), plus
locator metadata. Used in bulk/`__all__` retrieval.

| Field | Type | Notes |
|-------|------|-------|
| *(all meta fields)* | | |
| `Values` | number[] | Values in series order |
| `RunNumber` | number (int) | |
| `SingleURL` | string | URL of the individual series |
| `NetworkElement` / `RecordingElement` / `RecordingVariable` / `FunctionalUnit` | string | Locators |

### MultipleTimeSeries
`ExchangeObjects/MultipleTimeSeries.cs` — wrapper returned when a request matches several
series (e.g. via `__all__`).

| Field | Type | Notes |
|-------|------|-------|
| `TimeSeries` | TimeSeriesReponseMeta[] | Each element is a `SlimTimeSeries`/full-summary entry |

---

## Functions & variables

### FunctionValue
`ExchangeObjects/FunctionValue.cs` · `GET /functions` (array), request of `PUT /functions/{name}`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | Without `$` |
| `FullName` | string | With `$` |
| `Expression` | string | The only field applied on `PUT` |
| `Units` | string | |
| `InitialValue` | number | |

### VariableSummary
`VariableSummary.cs` · `GET /variables` (array), `GET /variables/{name}`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | Without `$` |
| `FullName` | string | With `$` |
| `VariableType` | string | e.g. `TimeSeriesVariable`, `LinearVariable` |
| `ID` | number (int) | |
| `VeneerSupported` | boolean | Whether the API can read/write this type |
| `VeneerDebugInfo` | string \| null | |
| `TimeSeries` | string \| null | URL to the variable's `/TimeSeries`, if applicable |
| `PiecewiseFunction` | string \| null | URL to the variable's `/Piecewise`, if applicable |
| `TimeSeriesDataSources` | object (map string→string) | Input set → data source URL |

### SimplePiecewise
`ExchangeObjects/SimplePiecewise.cs` · `GET`/`PUT /variables/{name}/Piecewise`

| Field | Type | Notes |
|-------|------|-------|
| `XName` | string | X-axis label |
| `YName` | string | Y-axis label |
| `Entries` | number[][] | Array of `[x, y]` pairs |

---

## Input sets & recorders

### InputSetSummary
`ExchangeObjects/InputSetSummary.cs` · `GET /inputSets` (array), request of `POST /inputSets` and `PUT /inputSets/{name}`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | |
| `URL` | string | e.g. `/inputSets/Baseline` |
| `HierarchicalName` | string | |
| `Configuration` | string[] | Instruction strings the set applies |
| `ReloadOnRun` | boolean | |
| `Filename` | string \| null | Backing file, if any |

### RecordingInstructions
`ExchangeObjects/RecordingInstructions.cs` · request of `PUT /recorders`

| Field | Type | Notes |
|-------|------|-------|
| `RecordNone` | string[] | Selectors to stop recording |
| `RecordAll` | string[] | Selectors to start recording |

Selectors use the network-element grammar (`location/.../element/.../variable/...`, with
`__all__` wildcards and `@@` functional-unit suffixes).

---

## Data sources

### SimpleDataGroupItem
`ExchangeObjects/DataSources/SimpleDataGroupItem.cs` · `GET /dataSources` (array),
`GET/POST/PUT /dataSources/{group}`

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Resource URL |
| `Name` | string | |
| `FullName` | string | |
| `Path` | string | |
| `ReloadMatchesOnName` | boolean | (newer Source versions) |
| `Items` | [SimpleDataItem](#simpledataitem)[] | |

### SimpleDataItem
`ExchangeObjects/DataSources/SimpleDataItem.cs` · element of `SimpleDataGroupItem.Items`; response of `GET /dataSources/{group}/{inputSet}`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | |
| `InputSets` | string[] | Input sets this item applies to |
| `Details` | [SimpleDataDetails](#simpledatadetails)[] | |
| `DetailsAsCSV` | string | Inline data (CSV) for create/update |
| `UnitsForNewTS` | string | Units when creating a new series from CSV |
| `ReloadOnRun` | boolean | |
| `Filename` | string \| null | |
| `FilenameIsRelative` | boolean | |
| `UseName` | boolean | |

### SimpleDataDetails
`ExchangeObjects/DataSources/SimpleDataDetails.cs` · element of `SimpleDataItem.Details`; response of `GET /dataSources/{group}/{inputSet}/{item}` and `GET /dataSources/{group}/__all__/{name}` (array)

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string | |
| `TimeSeries` | TimeSeriesReponseMeta | The actual series (a `SimpleTimeSeries` or slim form) |

---

## Tables, configuration & projection

### ModelTableIndex
`ExchangeObjects/ModelTableIndex.cs` · response of `GET /tables`

| Field | Type | Notes |
|-------|------|-------|
| `Tables` | ModelTableIndexItem[] | |

**ModelTableIndexItem**

| Field | Type |
|-------|------|
| `Name` | string |
| `Url` | string |

> `GET /tables/{table}` returns a serialised **`DataTable`** (rows/columns), not one of these
> DTOs. Request `text/csv` for a friendlier form.

### ProjectionInfo
`ExchangeObjects/ProjectionInfo.cs` · field of `VeneerStatus`; request of `PUT /projection`

| Field | Type | Notes |
|-------|------|-------|
| `Projection` | string | Projection name (e.g. `UTM`) |
| `Zone` | number (int) | |
| `Hemisphere` | string | `North` or `South` |

---

## GeoJSON types

These follow the GeoJSON spec, so field names are **lowercase**.

### GeoJSONNetwork
`ExchangeObjects/GeoJSONNetwork.cs` · response of `GET /network` and `GET /network/geographic`

| Field | Type | Notes |
|-------|------|-------|
| `type` | string | Always `"FeatureCollection"` |
| `features` | [GeoJSONFeature](#geojsonfeature)[] | Nodes, links, catchments |

### GeoJSONFeature
`ExchangeObjects/GeoJSONFeature.cs` · response of `GET /network/nodes/{id}` and `GET /network/link/{id}`; element of `features`

| Field | Type | Notes |
|-------|------|-------|
| `type` | string | Always `"Feature"` |
| `id` | string | Resource URL of the element |
| `geometry` | GeoJSONGeometry | `type` + `coordinates` |
| `properties` | object | See below |

**geometry.type** ∈ `Point`, `LineString`, `Polygon`, `MultiPoint`, `MultiLineString`,
`MultiPolygon`. `coordinates` nests accordingly (`[lon,lat]`, `[[lon,lat],...]`, …).

**properties** (a flat, open object; common keys):

| Key | Type | Applies to |
|-----|------|-----------|
| `name` | string | all |
| `feature_type` | string | `node` / `link` / `lateral_link` / `conveyance_link` / `catchment` |
| `icon` | string | nodes — `/resources/...` URL |
| `elevation` | number | nodes |
| `schematic_location` | number[] | nodes — `[x, y]` |
| `from_node` / `to_node` | string | links — node URLs |
| `left_node` / `right_node` | string | lateral/conveyance links — node URLs |
| `link` | string \| null | associated link URL |
| `areaInSquareMeters` | number | catchments |

### SchematicTagMap
`ExchangeObjects/SchematicTagMap.cs` · response of `GET /network/schematic.svg/tags`

| Field | Type | Notes |
|-------|------|-------|
| `viewBox` | number[] | SVG view box |
| `nodes` | SchematicNodeTag[] | |
| `links` | SchematicLinkTag[] | |

**SchematicNodeTag**: `name`, `tag_name`, `tags` (string[]), `icon_kind` (`svg`/`png`),
`icon_shape` (optional), `hg_value`.
**SchematicLinkTag**: `name`, `tag_name`, `tags` (string[]), `hg_value`.

---

## Scripting

### IronPythonScript
`RemoteScripting/IronPythonScript.cs` · request of `POST /ironpython`

| Field | Type | Notes |
|-------|------|-------|
| `Script` | string | IronPython source |
| `Debug` | boolean | Enable debug behaviour |

### IronPythonResponse
`RemoteScripting/IronPythonResponse.cs` · response of `POST /ironpython` and `POST /custom/{action}`

| Field | Type | Notes |
|-------|------|-------|
| `StandardOut` | string | Captured stdout |
| `StandardError` | string | Captured stderr |
| `Response` | [VeneerResponse](#veneerresponse) \| null | Script result (polymorphic) |
| `Exception` | [SimpleException](#simpleexception) \| null | Present when the script threw |

### SimpleException
`ExchangeObjects/SimpleException.cs`

| Field | Type | Notes |
|-------|------|-------|
| `ExceptionType` | string | Full type name |
| `Message` | string | |
| `StackTrace` | string | .NET stack trace |
| `PythonStackTrace` | string | Python stack trace |
| `InnerException` | SimpleException \| null | Recursive |

### VeneerResponse
`ExchangeObjects/VeneerResponse.cs` — polymorphic base for the `Response` field above. WCF
emits a type discriminator; the known subtypes are:

| Subtype | Shape |
|---------|-------|
| `StringResponse` | `{ "Value": string }` |
| `NumericResponse` | `{ "Value": number }` |
| `BooleanResponse` | `{ "Value": boolean }` |
| `ListResponse` | `{ "Value": VeneerResponse[] }` |
| `DictResponse` | `{ "Entries": [ { "Key": any, "Value": any } ] }` |
| `SimpleTimeSeries` / `SlimTimeSeries` / `MultipleTimeSeries` | see [time series](#time-series-responses) |
| `SimplePiecewise` | see [SimplePiecewise](#simplepiecewise) |
| `GeoJSONCoverage` | a GeoJSON `FeatureCollection` |

A client should branch on which fields are present (`Value` vs `Entries`) or on the WCF type
hint.
