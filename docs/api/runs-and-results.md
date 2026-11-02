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
  Each key is a property path on the scenario's running configuration, and its value is
  assigned to that property for the run. The common keys are `StartDate` and `EndDate`. The
  accepted set depends on the configuration and the Source version. An empty object `{}` runs
  with the scenario's configured settings.
  - **Values are converted by the property's type.** A date takes a `dd/MM/yyyy` string
    (`"31/12/2000"`). A time step takes a time step name. An input set takes an input set's
    name. Any other value is assigned as sent.
  - **A key that names no property, or a value that does not convert** (`"32/01/2000"`), fails
    the run with a `500`, and the configuration is left as it was. On Source 6.10, for example,
    there is no `InputSet` property, so an `InputSet` key is a `500`.
  - **An input set name that matches no input set is not refused.** Where the property is an
    input set, an unknown name assigns no input set (`null`) for the run, without an error.
  - **Two keys are not configuration properties.** `_RunName` names the resulting run, where
    the Source version supports it. `SelectedRecorderSets` chooses the recorder sets for the run
    (see [Choosing recorder sets for one run](#choosing-recorder-sets-for-one-run)).
- **Parameters apply to this run only.** Any values supplied are applied to the scenario
  configuration for the duration of the run and then **restored to their pre-run values**
  once the run finishes — on success, failure, or cancellation. A subsequent run that omits a
  parameter therefore uses the scenario's own configured value, not the value from a previous
  request, and a run never mutates (or, if the project is later saved, persists) the scenario
  configuration.

  > **Behaviour change (protocol `20260716`).** Earlier versions applied run parameters
  > *permanently* to the scenario configuration, so a value sent on one run persisted to later
  > runs and into the saved project. Runs are now self-contained. To change the scenario's
  > configured dates durably, edit the configuration in the Source GUI.
- **Success**: **`302 Found`** with a `Location` response header pointing at the new run
  (e.g. `Location: .../runs/5`). The body is empty. Follow the header (or call
  `GET /runs/{n}`) to fetch results.
- **`400 Bad Request`**: an invalid `SelectedRecorderSets`. Body is a
  [`SimulationFault`](schemas.md#simulationfault). Nothing is changed and no run starts. See
  [Choosing recorder sets for one run](#choosing-recorder-sets-for-one-run).
  **Added in protocol `20261019`.**
- **`409 Conflict`**: a run is already in progress. Body is a
  [`SimulationFault`](schemas.md#simulationfault) — *"A simulation is already running. Cancel
  the current run before starting a new one."* Also while an earlier run is still stopping
  (*"A previous simulation is still stopping. Try again once it has stopped."*); see
  [When a run is still stopping](#when-a-run-is-still-stopping). The "still stopping" `409` is
  **added in protocol `20261019`.**
- **`500 Internal Server Error`**: the run failed. Body is a `SimulationFault`. Two cases:
  - The run threw — the fault carries the exception `Message` and `StackTrace`, and the `Log`
    captured so far.
  - The run completed without producing a result (Source aborted internally, e.g. an input
    file could not be loaded). The fault carries an explanatory `Message` and a `Log` array of
    the diagnostic messages captured from Source during the run.
- **veneer-py**: `v.run_model(params=None, start=None, end=None, run_async=False, name=None, **kwargs)`.
  `start`/`end` are sent as `StartDate`/`EndDate` in `dd/MM/yyyy`, `name` as `_RunName`, and
  `kwargs` as further keys.

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

{ "StartDate": "01/01/1990", "EndDate": "31/12/2000" }
```

```http
HTTP/1.1 302 Found
Location: http://localhost:9876/runs/5
```

### Choosing recorder sets for one run

**Added in protocol `20261019`.** Earlier versions do not apply the `SelectedRecorderSets` key.
Protocol `20261018`, for example, accepts it without an error and runs with the saved selection.
Check `Version` from [`GET /`](server-and-lifecycle.md) before relying on it.

Source records a run through the recorder sets selected in the scenario's configuration
([`GET /recorderSets`](input-sets.md#get-recordersets) lists them). The **Current Recorder
Tree** is one of those sets, and it is what [`PUT /recorders`](input-sets.md#put-recorders)
configures. When a named set is selected and the tree is not, the named sets govern recording
and the tree's recorders are ignored. When the tree is selected as well, the run records both.

`SelectedRecorderSets` is a JSON array of set names. It is the **whole selection for this run**:

| `SelectedRecorderSets` | The run records |
|------------------------|-----------------|
| *(key omitted)* | Through the saved selection, unchanged |
| `[]` | The Current Recorder Tree only (**replace**): what `PUT /recorders` asked for, and none of the named sets |
| `["Current Recorder Tree"]` | The same as `[]` |
| `["Current Recorder Tree", "SetX"]` | The tree and `SetX` together (**add**) |
| `["SetX"]` | `SetX` only; the tree's recorders are ignored |

- **Add is a union.** With the tree and named sets selected together, the run records
  everything each of them records. This was observed on Source 5.61 and 6.10. To keep the
  user's usual outputs and add your own, send `"Current Recorder Tree"` followed by the names
  `GET /recorderSets` reports in `Selected`.
- **Names.** Each name matches a set's name exactly (case-sensitive). On Source 5.16 and later,
  `"Current Recorder Tree"` is Source's constant name for the tree, and always means the
  scenario's tree, even when a named set has the same name. Source 4.x has no such constant and
  was not run. A repeated name is dropped (the first is kept), and the order is otherwise kept.
- **For this run only.** Veneer copies the selection before the run, replaces it, and restores
  the copy when the run ends: after success, failure and cancel alike. An empty saved selection
  reads back as `["Current Recorder Tree"]` afterwards, which means the same thing.
- **Validated before anything changes.** These are a **`400 Bad Request`** with a
  [`SimulationFault`](schemas.md#simulationfault) body. A `400` leaves the configuration exactly
  as it was, including every other run parameter in the body, and no run starts.

  | Request | `Message` |
  |---------|-----------|
  | Not an array (a string, number, object or `null`) | `SelectedRecorderSets must be a JSON array of recorder set names, not a string.` |
  | An element that is not a string | `SelectedRecorderSets[1] must be a recorder set name (a string), not null.` |
  | A name that matches no set | `SelectedRecorderSets names an unknown recorder set: 'no such set'. Available recorder sets: 'Current Recorder Tree', 'SetX'.` |
  | A name that more than one set has | `SelectedRecorderSets names a recorder set name shared by more than one set: 'SetX'. Available recorder sets: …` |
  | A non-empty list for a scenario with no recorder sets | `SelectedRecorderSets cannot select 'SetX': the scenario has no recorder sets.` |
  | A non-empty list when the configuration has no recorder set selection | `SelectedRecorderSets cannot select 'SetX': the current configuration has no recorder set selection.` |

  The type in the first two messages (`a string`, `a number`, `an object`, `null`) and the index
  follow the request. An unknown-name or shared-name message lists every offending name. `[]` is
  accepted for a scenario with no recorder sets, and changes nothing.
- **Other run parameters.** `SelectedRecorderSets` is validated before any of them is applied,
  so an invalid value is a `400` even when another key is also bad. The others are then applied
  before the selection is changed, so if one of them fails (a `500`, see above), the selection
  has not been changed.

```http
POST /runs HTTP/1.1
Content-Type: application/json

{ "StartDate": "01/01/1990", "EndDate": "31/12/2000", "SelectedRecorderSets": [] }
```

#### When a run is still stopping

Normally the selection is restored before `POST /runs` responds. But a cancelled run waits at
most 5 s for Source to stop. If Source's run is still executing when the request finishes
(after that wait times out, or after an error that left it running), the restore waits until the
run stops. This applies to every run started through Veneer, whether or not it sent
`SelectedRecorderSets`. **Added in protocol `20261019`.** Until the run stops:

- `POST /runs` answers **`409`**: *"A previous simulation is still stopping. Try again once it
  has stopped."*
- `POST /functions/evaluate` answers **`409`**: *"A simulation is running; evaluate after it
  finishes."*
- `GET /runs/status` reports `IsRunning: false`, and `POST /runs/cancel` answers `404`, because
  the run no longer counts as active.
- If Source's run never stops, only restarting the host (VeneerCmd or the Source GUI) recovers.

#### Restore errors

A failure to restore the selection is never thrown. Its message, *"Could not restore the
recorder set selection to its pre-run value: …"*, is added at `Error` to the run's log: the
`RunLog` of the resulting run, or the `Log` of the `500` fault. It is also logged to Source's
log, which the run's log captures too, so the line can appear twice. A restore that waits for a
stopping run (above) finishes after the request, so its errors reach only the console (VeneerCmd)
or the Source GUI's log.

#### While the selection is applied and restored

Both steps are brief. During them `POST /runs` answers `409` (*"A simulation is already
running…"*), while `GET /runs/status` reports `IsRunning: false` and `POST /runs/cancel` answers
`400`.

#### Hosted in the Source GUI

The selection belongs to the project, so the Source GUI sees the run's selection while the run is
in progress. Veneer makes its changes on the GUI thread.

- **Saving the project during a run** saves the run's selection, not the user's.
- **Editing the selection in the GUI during a run** is overwritten when the run ends and the
  selection is restored.
- **The tree's recording states.** During a run with a named set selected and the tree not
  selected, Source temporarily rewrites the tree's recording states. Source restores them itself
  when the run ends, but a project saved during the run saves the temporary states.

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
  "RunLog": ["[Information] Run started", "[Error] timestep 1990-06-01: Data file not found"],
  "LastStackTrace": null,
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
- **`404 Not Found`**: there is no active run. This includes an earlier run that is still
  stopping (see [When a run is still stopping](#when-a-run-is-still-stopping)).
- **`400 Bad Request`**: there is an invoker but it isn't currently running, as during the brief
  steps before and after the simulation itself (for example, applying and restoring
  `SelectedRecorderSets`).
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
GET /runs/{runId}/location/{ne}/element/{re}/variable/{v}?from={from}&to={to}&precision={p}&content={c}&timestep={agg}&aggfn={fn}
```

| Query param | Meaning | Default |
|-------------|---------|---------|
| `from` | start [partial date](overview.md#partial-dates-query-parameters) | series start |
| `to` | end partial date | series end |
| `precision` | decimal places to round to | full precision |
| `content` | response shape — `full` or `headers` (see below) | omitted (legacy shape) |
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

#### The `content` query parameter

Added in protocol `20260727`. It selects the response *shape* independently of how many series
the path happens to match — useful for discovering what a run contains without downloading
values, and for fetching one series in a compact, self-describing form.

| `content` | Response |
|-----------|----------|
| *(omitted)* | Legacy behaviour: `SimpleTimeSeries` for exactly one match, `MultipleTimeSeries` otherwise |
| `full` | Always a [`MultipleTimeSeries`](schemas.md#multipletimeseries) of [`SlimTimeSeries`](schemas.md#slimtimeseries) — *including* for a single match |
| `headers` | Always a `MultipleTimeSeries` of [`TimeSeriesFullSummary`](schemas.md#timeseriesfullsummary) — the same entries **without** the `Values` array |

- Any other value (including an empty one) is a **`400 Bad Request`** with a `null` body; the
  parameter never silently falls back to `full`.
- `content=full` differs from omitting the parameter in exactly one respect: it suppresses the
  single-match `SimpleTimeSeries` branch. That makes a lone series ~4× smaller (bare `double[]`
  instead of one `{Date, Value}` object per timestep) and attributable, since `SimpleTimeSeries`
  carries none of the locator fields (`RecordingVariable`, `SingleURL`, …).
- `content=headers` is the cheap way to enumerate a run: every locator field, the summary stats,
  and the invariant-culture `StartDate`/`EndDate`/`TimeStep` that define the date axis, with no
  values. (`RunSummary.StartDate` on `GET /runs/{n}` is *not* invariant-culture — prefer these.)
- The parameter is honoured identically on the `/aggregated/{aggregation}` route below.

```http
GET /runs/1/location/__all__/element/__all__/variable/__all__?content=headers HTTP/1.1
```

```json
{
  "TimeSeries": [
    {
      "Name": "Downstream Flow",
      "Units": "m^3/s",
      "TimeStep": "Daily",
      "StartDate": "03/01/2026 00:00:00",
      "EndDate": "10/30/2026 00:00:00",
      "NoDataValue": -9999.0,
      "Min": 0.0, "Max": 812.4, "Mean": 14.2, "Sum": 56123.5,
      "RunNumber": 1,
      "SingleURL": "/runs/1/location/Gauge%20A/element/Downstream%20Flow/variable/Flow",
      "NetworkElement": "Gauge A",
      "RecordingElement": "Downstream Flow",
      "RecordingVariable": "Flow",
      "FunctionalUnit": null
    }
  ]
}
```

### `GET …/variable/{variable}/aggregated/{aggregation}` — aggregated series

Same as above, but `{aggregation}` (`annual`/`month`/`day`) is a **path** segment and the
aggregation function is fixed to **`sum`** (you cannot override it with `aggfn` here). Accepts
`from`, `to`, `precision` and `content` query params.

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

> **Not currently served on the CoreWCF (`master`) build.** `Formatting/TimeSeriesResponseFormatter.cs`
> is registered by `ReplyFormatSwitchBehaviour` but is not reached under CoreWCF's
> `WebHttpBinding`: measured against Source 6.10, `Accept: text/csv` on a time series route
> returns `Content-Type: application/json` and a JSON `MultipleTimeSeries` body. Clients should
> not rely on CSV here; use `?content=full` for a compact values response instead.
