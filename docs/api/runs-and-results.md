# Runs & results

This is the heart of the API: triggering simulations, listing/inspecting/deleting runs,
monitoring or cancelling the active run, and extracting recorded time series (raw, aggregated,
or tabulated).

There is a single, process-wide **run lock** — only one simulation can run at a time. The
active run's state is shared across all clients.

---

## `POST /runs` — trigger a run

Runs the current scenario. The body is a flat JSON object of run parameters.

- **Request body**: [`RunParameters`](schemas.md#runparameters) — a flat key/value object.
  Common keys: `Start`, `End` (dates), `InputSet` (name), `ForecastLength`. Keys are passed
  through to Source, so the accepted set depends on the model/version. An empty object `{}`
  runs with the scenario's configured dates.
- **Success**: **`302 Found`** with a `Location` response header pointing at the new run
  (e.g. `Location: .../runs/5`). The body is empty. Follow the header (or call
  `GET /runs/{n}`) to fetch results.
- **`409 Conflict`**: a run is already in progress. Body is a
  [`SimulationFault`](schemas.md#simulationfault) — *"A simulation is already running. Cancel
  the current run before starting a new one."*
- **`500 Internal Server Error`**: the run failed. Body is a `SimulationFault`. Two cases:
  - The run threw — the fault carries the exception `Message` and `StackTrace`.
  - The run completed without producing a result (Source aborted internally, e.g. an input
    file could not be loaded). The fault carries an explanatory `Message` and a `Log` array of
    the diagnostic messages captured from Source during the run.
- **veneer-py**: `v.run_model(params=None, start=None, end=None, run_async=False, name=None)`

> **The run is synchronous.** The HTTP request does not return until the simulation finishes;
> the `302` is sent *after* completion. To watch progress, poll `GET /runs/status` from a
> second client/connection (state is process-wide). veneer-py's `run_async=True` makes the
> *client* not block, but the server still runs the simulation to completion before
> responding.

> **Run logs.** Log messages emitted during a run are captured and attached to the resulting
> run summary's `RunLog` — but only for runs triggered through Veneer. Runs created in the
> Source GUI return a placeholder log.

**Example**

```http
POST /runs HTTP/1.1
Content-Type: application/json

{ "Start": "1990-01-01", "End": "2000-12-31", "InputSet": "Baseline" }
```

```http
HTTP/1.1 302 Found
Location: http://localhost:9876/runs/5
```

---

## `GET /runs` — list runs

All runs currently held in the result manager.

- **Response**: array of [`RunLink`](schemas.md#runlink) (`200 OK`; empty array if none)
- **veneer-py**: `v.retrieve_runs()`

```json
[
  {
    "RunName": "Run 1",
    "RunUrl": "/runs/1",
    "DateRun": "06/13/2026 09:14:02",
    "Scenario": "Baseline",
    "Status": "RanToCompletion",
    "StartDate": "1990-01-01",
    "EndDate": "2000-12-31",
    "TimeStep": "Daily"
  }
]
```

Follow `RunUrl` to get the full summary.

---

## `GET /runs/{runId}` — run summary

Full summary for one run, including the list of recorded time series and the run log.

- **`{runId}`**: the numeric run number, or the literal `latest` for the most recent run.
- **Response**: [`RunSummary`](schemas.md#runsummary) (`200 OK`). If the run isn't found the
  body is `null`.
- **veneer-py**: `v.retrieve_run(run='latest')`

```json
{
  "DateRun": "2026-06-13T09:14:02",
  "Name": "Run 1",
  "Scenario": "Baseline",
  "Number": 1,
  "Status": "RanToCompletion",
  "StartDate": "1990-01-01",
  "EndDate": "2000-12-31",
  "TimeStep": "Daily",
  "RunLog": ["Run started", "..."],
  "Results": [
    {
      "RunNumber": 1,
      "TimeSeriesName": "Downstream Flow",
      "TimeSeriesUrl": "/runs/1/location/Gauge%20A/element/Downstream%20Flow/variable/Flow",
      "NetworkElement": "Gauge A",
      "RecordingElement": "Downstream Flow",
      "RecordingVariable": "Flow",
      "FunctionalUnit": null
    }
  ]
}
```

Each entry in `Results` gives you a ready-to-use `TimeSeriesUrl` for the time series endpoint
below.

---

## `DELETE /runs/{runId}` — delete a run

Removes a run from memory. Idempotent (`200 OK` even if nothing matched).

- **`{runId}`**: numeric run number, `latest`, or `all` (deletes every run).
- **Response**: none (`200 OK`)
- **veneer-py**: `v.drop_run(run='latest')`
- *Implementation note*: on newer Source versions a run cannot be deleted individually if it
  shares a job with others — the whole containing job is removed.

```http
DELETE /runs/all HTTP/1.1
```

---

## `POST /runs/cancel` — cancel the active run

Requests cancellation of the currently running simulation.

- **Request body**: none
- **`200 OK`**: cancellation request sent.
- **`404 Not Found`**: there is no active run.
- **`400 Bad Request`**: there is an invoker but it isn't currently running.
- **`500 Internal Server Error`**: cancellation threw — body is a
  [`SimulationFault`](schemas.md#simulationfault).
- **veneer-py**: `v.cancel_run()`

> The path is `/runs/cancel` (fixed), not `/runs/{id}/cancel`. It always targets the single
> active run.

---

## `GET /runs/status` — active run status

Progress of the active run. Safe to poll while a `POST /runs` is in flight on another
connection.

- **Response**: [`RunStatus`](schemas.md#runstatus) (`200 OK`, always)
- **veneer-py**: `v.run_status()`

When idle:

```json
{ "IsRunning": false, "CanCancel": false }
```

While running:

```json
{
  "IsRunning": true,
  "CanCancel": true,
  "Scenario": "Baseline",
  "StartDate": "1990-01-01",
  "EndDate": "2000-12-31",
  "CurrentDate": "1994-07-02 00:00:00",
  "PercentComplete": 42.0
}
```

---

## Time series

Recorded outputs are addressed by a four-part path built from a run id plus a network element,
recording element and variable. The base template (`UriTemplates.Recordable`) is:

```
/runs/{runId}/location/{networkElement}/element/{recordingElement}/variable/{variable}
```

You normally don't build this by hand — take `TimeSeriesUrl` from a run summary's `Results`.
Each of `{networkElement}`, `{recordingElement}` and `{variable}` accepts the `__all__`
wildcard (returns a [`MultipleTimeSeries`](schemas.md#multipletimeseries)); `{networkElement}`
also accepts a `@@`-delimited functional unit (see
[overview.md](overview.md#addressing-network-elements)).

### `GET …/variable/{variable}` — raw / windowed series

```
GET /runs/{runId}/location/{ne}/element/{re}/variable/{v}?from={from}&to={to}&precision={p}&timestep={agg}&aggfn={fn}
```

| Query param | Meaning | Default |
|-------------|---------|---------|
| `from` | start [partial date](overview.md#partial-dates-query-parameters) | series start |
| `to` | end partial date | series end |
| `precision` | decimal places to round to | full precision |
| `timestep` | aggregation period (`annual`/`month`/`day`) | none (raw) |
| `aggfn` | aggregation function when `timestep` is set (`sum`, else averaged) | `sum` |

- **Response**: a [`TimeSeriesResponse`](schemas.md#time-series-responses) — a single
  [`SimpleTimeSeries`](schemas.md#simpletimeseries) (with an `Events` array of `{Date, Value}`)
  for one match, or a [`MultipleTimeSeries`](schemas.md#multipletimeseries) when the path
  matches several series (e.g. via `__all__`).
- **`404 Not Found`** with a `null` body when nothing matches.
- **veneer-py**: `v.retrieve_time_series_url(url)` /
  `v.retrieve_multiple_time_series(run, criteria, timestep)`

```json
{
  "Name": "Downstream Flow",
  "Units": "m^3/s",
  "TimeStep": "Daily",
  "StartDate": "1990-01-01",
  "EndDate": "2000-12-31",
  "NoDataValue": -9999.0,
  "Min": 0.0, "Max": 812.4, "Mean": 14.2, "Sum": 56123.5,
  "Events": [
    { "Date": "1990-01-01", "Value": 12.3 },
    { "Date": "1990-01-02", "Value": 11.8 }
  ]
}
```

### `GET …/variable/{variable}/aggregated/{aggregation}` — aggregated series

Same as above, but `{aggregation}` (`annual`/`month`/`day`) is a **path** segment and the
aggregation function is fixed to **`sum`** (you cannot override it with `aggfn` here). Accepts
`from`, `to`, `precision` query params.

- **Response**: `TimeSeriesResponse` (as above)
- **veneer-py**: used by `v.retrieve_multiple_time_series(..., timestep='monthly')` and by the
  mirror retriever for `/aggregated/monthly` and `/aggregated/annual`.

```http
GET /runs/1/location/Gauge%20A/element/Downstream%20Flow/variable/Flow/aggregated/month?precision=3 HTTP/1.1
```

### `GET …/variable/{variable}/tabulated/{functions}` — summary table

Applies one or more summary functions across the matching series and returns a table.

- **`{functions}`**: comma-separated function names (e.g. `min,max,mean,sum`), or `__all__`
  for every available function.
- **Response**: a serialised `DataTable` (`200 OK`). With wildcards in the path, the table
  gains columns identifying each matched series.
- No date/precision query params.

```http
GET /runs/1/location/__all__/element/Downstream%20Flow/variable/Flow/tabulated/min,max,mean HTTP/1.1
```

---

## CSV

Time series (and tables) can be requested as CSV by sending `Accept: text/csv`. veneer-py's
bulk retriever uses this to store compact mirrors. The CSV layout is the date column plus one
value column per series.
