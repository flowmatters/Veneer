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
    "InitialValue": 0.0
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
    "TimeSeriesDataSources": { "Baseline": "/dataSources/Demands/Baseline/BaseDemand" }
  }
]
```

`TimeSeries` / `PiecewiseFunction` are URLs to the relevant sub-resource when the variable is
of that kind (otherwise `null`). `VeneerSupported` flags variable types the API can read/write;
`TimeSeriesDataSources` maps each input set to the data-source URL feeding the variable.

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
