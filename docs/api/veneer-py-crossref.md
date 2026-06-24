# veneer-py cross-reference

[veneer-py](https://github.com/flowmatters/veneer-py) is the reference Python client. This
page maps the HTTP API to its methods so you can read either side. It is based on the
`veneer/general.py`, `veneer/bulk.py` and `veneer/server_side.py` modules.

## Low-level helpers (`general.py`)

The `Veneer` client builds its base URL as
`"{protocol}://{host}:{port}{prefix}"` (or, in mirror mode, `"{protocol}://{prefix}"` with
`protocol='file'`). All endpoint calls go through these:

| Helper | HTTP | Notes |
|--------|------|-------|
| `retrieve_json(url, allow_status=None)` | GET | Parses JSON; rewrites `INF`/`-INF` → `Infinity`. In file mode appends `.json`. |
| `retrieve_csv(url, **kwargs)` | GET | Sends `Accept: text/csv`; parses with `read_veneer_csv`. |
| `update_json(url, data, run_async=False)` | PUT | `Content-Type: application/json`. |
| `post_json(url, data=None, run_async=False)` | POST | `Content-Type: application/json`; returns `(status, body)`. |
| `send_json(url, data, method, run_async=False)` | PUT/POST | Generic JSON send. |
| `send(url, method, payload=None, headers={}, run_async=False)` | any | Low-level; on `302` returns the `Location`. |

In **file/mirror mode** (`live=False`), `data_ext='.json'` and `img_ext='.png'` are appended
to every path; in **live mode** no extension is added. That single switch is what makes a
[static mirror](conventions-static-mirror.md) a drop-in substitute for HTTP.

## Endpoint → method map

| Verb | Path | veneer-py |
|------|------|-----------|
| GET | `/` | `status()`, `scenario_info()`, `source_version()` |
| GET | `/ping` | (internal connection check) |
| POST | `/shutdown` | `shutdown()` |
| POST | `/scenario/{scenario}` | `select_scenario(scenario)` |
| GET | `/network` | `network()` |
| GET | `/network/geographic` | `retrieve_json('/network/geographic')` (and mirror) |
| GET | `/network/schematic.svg` (+`/tags`) | (mirror capture; UI tooling) |
| GET | `/network/nodes/{id}`, `/network/link/{id}` | (via the `network()` feature collection) |
| GET | `/runs` | `retrieve_runs()` |
| POST | `/runs` | `run_model(params, start, end, run_async=False, name=None)` |
| GET | `/runs/status` | `run_status()` |
| POST | `/runs/cancel` | `cancel_run()` |
| GET | `/runs/{runId}` | `retrieve_run(run='latest')` |
| DELETE | `/runs/{runId}` | `drop_run(run='latest')` |
| GET | time series (`…/variable/{v}`) | `retrieve_time_series_url(url)` |
| GET | aggregated / multi series | `retrieve_multiple_time_series(run, criteria, timestep)` |
| GET | `/tables/{table}` | `model_table(table='fus')` (CSV) |
| GET | `/functions` | `functions()` |
| PUT | `/functions/{name}` | `update_function(name, value)` |
| GET | `/variables` | `variables()` |
| GET | `/variables/{name}` | `variable(name)`, `function(name)` |
| GET | `/variables/{name}/TimeSeries` | `variable_time_series(name)` |
| PUT | `/variables/{name}/TimeSeries` | `update_variable_time_series(name, ts)` |
| GET | `/variables/{name}/Piecewise` | `variable_piecewise(name)` |
| PUT | `/variables/{name}/Piecewise` | `update_variable_piecewise(name, values)` |
| GET | `/inputSets` | `input_sets()` |
| POST | `/inputSets` | `create_input_set(input_set)` |
| PUT | `/inputSets/{name}` | `update_input_set(name, input_set)` |
| DELETE | `/inputSets/{name}` | (no dedicated helper yet) |
| POST | `/inputSets/{name}/run` | `apply_input_set(name)` |
| PUT | `/recorders` | `configure_recording(enable=[], disable=[])` |
| GET | `/dataSources` | `data_sources()` |
| GET | `/dataSources/{group}` | `data_source(name)` |
| POST | `/dataSources` | `create_data_source(name, data, units, precision)` |
| DELETE | `/dataSources/{group}` | `delete_data_source(group)` |
| GET | `/dataSources/{group}/{inputSet}[/{item}]` | `data_source_item(source, name, input_set)` |
| POST | `/ironpython` | `run_server_side_script(script)` |
| POST | `/custom/{action}` | (used by addon-specific helpers) |

## Server-side scripting (`server_side.py`)

`run_server_side_script(script)` posts `{ "Script": script }` to `/ironpython`, raises if the
response `Exception` is non-null, raises *"Script disabled"* on `403`, and otherwise returns
the (simplified) `Response['Value']`.

The `VeneerIronPython` helper (often reached as `v.model`) generates scripts for you:

- `get(query)` / `set(query, value)` — read/write Source object graph via an accessor
  mini-language: `scenario.Network.Nodes.*Name` (`.*` iterates a collection),
  `...Where(lambda n: n.Name in [...]).*Model` (filter then project).
- `add_to_list(query, value)`, `get_data_sources(query)`, `get_functions(query)`.

All of these compile to a script, POST it to `/ironpython`, and unwrap the
[`IronPythonResponse`](schemas.md#ironpythonresponse). See [scripting.md](scripting.md).

## Bulk mirror (`bulk.py`)

`VeneerRetriever(destination=..., ...).retrieve_all()` walks the GET endpoints and writes each
response to `destination/<url-path>.<ext>`. Reading back is just a `Veneer` client constructed
with `protocol='file'`. Full details and the path-mapping table are in
[conventions-static-mirror.md](conventions-static-mirror.md).
