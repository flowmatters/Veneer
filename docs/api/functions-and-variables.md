# Functions & variables

Source models parameterise behaviour with **functions** (named expressions, prefixed with `$`)
and **variables** (the inputs those functions read — modelled values, time series, or
piecewise-linear relationships). These endpoints list and edit them.

> **The `$` prefix.** Function and variable full names in Source begin with `$`. The API
> hides this: in URLs you pass the name **without** the leading `$`, and the server prepends
> it internally. So to address `$Demand` you call `/variables/Demand`.

---

## `GET /functions`

All functions in the scenario.

- **Response**: array of [`FunctionValue`](schemas.md#functionvalue) (`200 OK`)
- **veneer-py**: `v.functions()`

```json
[
  {
    "Name": "Demand",
    "FullName": "$Demand",
    "Expression": "$BaseDemand * 1.1",
    "Units": "ML/d",
    "InitialValue": 0.0,
    "TimeOfEvaluation": "StartOfTimeStep"
  }
]
```

---

## `PUT /functions/{functionName}`

Set a function's expression. `{functionName}` is matched first against `Name` then `FullName`
(with `$` prepended).

- **Request body**: [`FunctionValue`](schemas.md#functionvalue) — only `Expression` is
  applied; the other fields are ignored on write.
- **Response**: none (`200 OK`).
- If no function matches, the server logs a warning (listing available functions) and does
  **not** error — the call still returns `200`. Read back with `GET /functions` to confirm.
- **veneer-py**: `v.update_function(name, value)`

```http
PUT /functions/Demand HTTP/1.1
Content-Type: application/json

{ "Expression": "$BaseDemand * 1.2" }
```

---

## `POST /functions/evaluate`

Evaluate a batch of independent expressions in-process, at a chosen date, with chosen
reference tokens replaced by literal values — without adding anything to the scenario's
function manager. This is the basis for "what-if" evaluation: a batch is just many items, so
evaluating a function across a range of dates or binding values is one request. Field names
on this endpoint's request/response bodies are **lowercase** (`token`, `id`, `items`,
`results`, …) — the exception, alongside GeoJSON, to the PascalCase convention described in
[overview.md](overview.md#json-serialisation-notes).

- **Request body**: [`EvaluateRequest`](schemas.md#evaluaterequest) — a list of
  [`EvaluateItem`](schemas.md#evaluateitem), each with an `id`, a `date`, an `expression`, an
  optional `context`, and optional `bindings`. Up to 2000 items per request.
- **Response**: [`EvaluateResponse`](schemas.md#evaluateresponse) (`200 OK`) — one
  [`EvaluateResultItem`](schemas.md#evaluateresultitem) per request item, **in request order**.
  Duplicate `id`s are accepted; tell them apart by position, not by `id`.
- **Bindings are a list, not an object.** `bindings` is `[{token, value}, ...]` rather than a
  `token → value` map, because the server's JSON serialiser silently drops any object member
  whose name isn't a legal XML name — which every `$`-prefixed token is. Substitution is
  textual: each `token` is replaced by its `value` (an invariant-culture numeric literal,
  parenthesised if negative) wherever it appears as a **whole token**, matched
  case-insensitively, after `//` and `/* ... */` comments are stripped from the expression
  (these are assumed to be Source's own function-language comment forms). What happens to a
  reference you do **not** bind is described below. Every `token` must start with `$` and every `value` must be finite; otherwise that item gets
  an error (`binding token must start with '$': ...` / `binding $X: non-finite value`), never
  a request-level failure. A null or empty `token` in a binding is silently dropped rather
  than erroring. `token` and `value` are themselves required fields of each binding object —
  if either is missing, deserialisation of the whole request fails and the framework answers
  its own `400 Bad Request`, not a `SimulationFault`.
- **Built-ins are supplied by Veneer.** For every built-in token you do not bind, Veneer
  substitutes a literal before parsing, computed as Source computes it during a run:
  `$Now.*` (`Day`, `Month`, `Year`, `Hour`, `DayOfYear`, `DaysInMonth`) from the item's
  `date`; `$Start.*` / `$End.*` from the start / end date of the scenario's current run
  configuration; `$ActiveInputSet` as the index of the configuration's selected input set
  (a comparison such as `$ActiveInputSet == "Wet"` is left for Source to rewrite, as it does
  for a run); and each pattern variable as its pattern's value at the item's `date` (by full
  name, and by leaf name relative to `context`). These do not depend on the scenario having
  run, and a binding you supply always takes precedence. Names resolve as Source resolves
  them: functions before variables, and each by **full name before leaf name** — so a leaf
  name in `context`'s folder means the built-in only when no function answers to it and no
  variable has that full name (otherwise it means that function or variable, which is then
  subject to the rule below).
- **Unbound references are item errors.** Source keeps no value for these at an arbitrary
  date, so instead of evaluating against whatever is in memory (a function's *last* evaluated
  value, a time series variable's last-played value, current model state — or 0 on a scenario
  that has not run), the item fails with a message naming the reference (`{name}` is its full
  name). Bind the reference to evaluate it:
  - `{name} is a function reference: Source holds only its last evaluated value, not its value at the item's date; bind it`
  - `{name} is a modelled variable: it has no value outside a run; bind it`
  - `{name} is a context variable: it only has a value at a function usage during a run; bind it`
  - `{name} is a time series variable: Veneer cannot read it at the item's date; bind it`
  - `could not bind built-in {name}` — a built-in Veneer could not compute (e.g. the run
    configuration could not be read, or a pattern value is not finite).
  - `{name} is a {type}, which Veneer cannot evaluate outside a run; bind it` — any other
    variable type.

  When several references are unbound, the messages are joined with `"; "`.
- **Lookup tables work on any scenario**, including one loaded but never run: a piecewise
  linear variable whose in-memory interpolator has not been built yet (Source builds it at the
  start of a run) is built on demand from its stored points — the one change evaluate makes,
  to a derived cache that is not saved and that the next run rebuilds identically. Bilinear
  tables need nothing.
- **Known limit: plugin custom functions.** A custom function supplied by a plugin that reads
  run state itself (for example through `ICustomFunctionContext.CurrentTimeStep`) sees
  whatever the last run left behind, or nothing on a scenario that has not run; Veneer can
  neither supply that state nor detect that the function uses it.
- **Apart from that cache, nothing in the model is reset or changed.** Evaluate never calls Source's per-run reset of
  the function manager, because that reset also writes default values into the model
  parameters that functions drive.
- **`context`** (optional) is the `Name` or `FullName` of an existing function, as returned by
  `GET /functions` — **not** a folder path. The expression is evaluated as if placed inside
  that function's own folder, so unqualified (leaf) names used in `expression` resolve
  relative to it, the same as a sibling function would. Omitted or empty → the expression has
  no containing folder, so only full names (and other top-level functions) resolve. Matching
  is case-insensitive; an unresolvable or ambiguous `context` is an error on that item
  (`context function not found: ...` / `ambiguous context: ...`), not a request-level failure.
  A `FullName` match takes precedence over a leaf `Name` match. A `context` that names a
  top-level function (one with no parent folder itself) has no effect — the transient ends
  up with no containing folder either way, same as omitting `context`.
- **`date`** is `yyyy-MM-dd`. The time step used to evaluate (e.g. to convert a rate to a
  per-step amount) comes from the scenario's **current run configuration** (`Daily`, with a
  logged warning, if that configuration cannot be read), not from the request.
- **Results**: exactly one of `value`/`error` per item. A result that is NaN or ±Infinity is
  reported as the item error `"non-finite result"` (JSON cannot carry them; the serialiser
  would emit bare `NaN`/`INF` tokens — see
  [overview.md#json-serialisation-notes](overview.md#json-serialisation-notes)). Any other
  parse or evaluation problem is also an item error; a leading prefix naming the internal
  transient function used to evaluate it is stripped from the message first.
- **`400 Bad Request`**: a request-level problem, as a
  [`SimulationFault`](schemas.md#simulationfault). Only the **first** problem found is
  reported — validation stops there rather than collecting every issue. A missing or `null`
  `items` array is **not** an error: it is treated as empty, so the response is `200 OK` with
  `"results": []`. Exact messages (`{index}` is the item's zero-based position in `items`):
  - `too many items: {count} > 2000` — more than `MAX_ITEMS` items.
  - `item[{index}] must not be null`
  - `item[{index}] needs an id and an expression`
  - `item {id}: date must be yyyy-MM-dd ({reason})` — `{reason}` is `missing`,
    `not yyyy-MM-dd`, or `not a calendar date`.
- **`409 Conflict`**: also a `SimulationFault`. Either no scenario is loaded
  (`"No scenario is loaded."`), or a run has been claimed or is in progress via Veneer
  (`"A simulation is running; evaluate after it finishes."`). A run started from the Source
  GUI rather than through Veneer is not detected and does not trigger this.
- The batch holds the server's single run lock for its whole duration, so a concurrent
  `POST /runs`, `GET /runs/status`, or `POST /runs/cancel` waits until the batch finishes.
- Not gated by `AllowScript` — only `POST /ironpython` answers `403` when scripting is
  disabled; evaluate runs no script and is always available.
- **Added in protocol `20261018`** (see [`GET /`](server-and-lifecycle.md)).
- **veneer-py**: no dedicated helper yet.

```http
POST /functions/evaluate HTTP/1.1
Content-Type: application/json

{
  "items": [
    {
      "id": "1",
      "date": "2000-01-01",
      "expression": "$Storages.StorageA.f_Inflow * 2",
      "bindings": [ { "token": "$Storages.StorageA.f_Inflow", "value": 12.5 } ]
    },
    {
      "id": "2",
      "date": "2000-01-01",
      "expression": "1 / $v_Rain",
      "bindings": [ { "token": "$v_Rain", "value": 0.0 } ]
    }
  ]
}
```

```json
{
  "results": [
    { "id": "1", "value": 25.0 },
    { "id": "2", "error": "non-finite result" }
  ]
}
```

---

## `GET /variables`

All variables (the inputs to functions).

- **Response**: array of [`VariableSummary`](schemas.md#variablesummary) (`200 OK`)
- **veneer-py**: `v.variables()`

```json
[
  {
    "Name": "BaseDemand",
    "FullName": "$BaseDemand",
    "VariableType": "TimeSeriesVariable",
    "ID": 12,
    "VeneerSupported": true,
    "VeneerDebugInfo": null,
    "TimeSeries": "/variables/BaseDemand/TimeSeries",
    "PiecewiseFunction": null,
    "TimeSeriesDataSources": { "Baseline": "/dataSources/Demands/Baseline/BaseDemand" },
    "TimeOfEvaluation": null,
    "DateRange": null
  }
]
```

`TimeSeries` / `PiecewiseFunction` are URLs to the relevant sub-resource when the variable is
of that kind (otherwise `null`). `VeneerSupported` flags variable types the API can read/write;
`TimeSeriesDataSources` maps each input set to the data-source URL feeding the variable.
`TimeOfEvaluation` and `DateRange` are populated for variable kinds that carry them (modelled
variables) and `null` otherwise.

---

## `GET /variables/{variableName}`

A single variable.

- **Response**: [`VariableSummary`](schemas.md#variablesummary) (`200 OK`; `null` body if not
  found).
- **veneer-py**: `v.variable(name)` (also `v.function(name)`, which reads the same resource)

```http
GET /variables/BaseDemand HTTP/1.1
```

---

## `GET /variables/{variableName}/TimeSeries`

The variable's input time series (for time-series-typed variables).

- **Response**: [`SimpleTimeSeries`](schemas.md#simpletimeseries) (`200 OK`)
- **veneer-py**: `v.variable_time_series(name)`

```json
{
  "Name": "BaseDemand",
  "Units": "ML/d",
  "TimeStep": "Daily",
  "StartDate": "1990-01-01",
  "EndDate": "2000-12-31",
  "NoDataValue": -9999.0,
  "Events": [ { "Date": "1990-01-01", "Value": 5.0 } ]
}
```

---

## `PUT /variables/{variableName}/TimeSeries`

Replace the variable's input time series.

- **Request body**: [`SimpleTimeSeries`](schemas.md#simpletimeseries) — the `Events` array
  (`{Date, Value}` pairs) is the payload that matters; metadata like `Units`/`TimeStep` should
  match the series.
- **Response**: none (`200 OK`)
- **veneer-py**: `v.update_variable_time_series(name, ts)`

```http
PUT /variables/BaseDemand/TimeSeries HTTP/1.1
Content-Type: application/json

{
  "TimeStep": "Daily",
  "StartDate": "1990-01-01",
  "Events": [
    { "Date": "1990-01-01", "Value": 5.0 },
    { "Date": "1990-01-02", "Value": 5.5 }
  ]
}
```

---

## `GET /variables/{variableName}/Piecewise`

The variable's piecewise-linear relationship (for piecewise-typed variables).

- **Response**: [`SimplePiecewise`](schemas.md#simplepiecewise) (`200 OK`)
- **veneer-py**: `v.variable_piecewise(name)`

```json
{
  "XName": "Storage Volume",
  "YName": "Surface Area",
  "Entries": [ [0.0, 0.0], [1000.0, 50.0], [5000.0, 180.0] ]
}
```

---

## `PUT /variables/{variableName}/Piecewise`

Replace the variable's piecewise-linear relationship.

- **Request body**: [`SimplePiecewise`](schemas.md#simplepiecewise) — `Entries` is an array of
  `[x, y]` pairs; `XName`/`YName` label the axes.
- **Response**: none (`200 OK`)
- **veneer-py**: `v.update_variable_piecewise(name, values)`

```http
PUT /variables/StorageRelationship/Piecewise HTTP/1.1
Content-Type: application/json

{
  "XName": "Storage Volume",
  "YName": "Surface Area",
  "Entries": [ [0.0, 0.0], [1000.0, 50.0], [5000.0, 180.0] ]
}
```
