# Developing against a static mirror

You don't need a live Veneer instance to develop a client. veneer-py's `VeneerRetriever`
(in [`veneer/bulk.py`](https://github.com/flowmatters/veneer-py/blob/master/veneer/bulk.py))
can walk a live server and write every GET response to a local directory tree, producing a
**static mirror**. A client can then be pointed at that directory and serve the same JSON it
would have fetched over HTTP. This page explains the mapping so you can build or consume a
mirror with any tooling, not just veneer-py.

## The mapping: URL → file

The rule is simple: **the URL path becomes the file path, with an extension appended.**

- Strip the leading `/`.
- Append `.json` for JSON responses, `.csv` for CSV, `.png` for images.
- Create intermediate directories as needed.

So a mirror rooted at `./mirror` maps:

| API request | Mirror file |
|-------------|-------------|
| `GET /` | `mirror/.json` (the empty path segment) |
| `GET /runs` | `mirror/runs.json` |
| `GET /runs/1` | `mirror/runs/1.json` |
| `GET /runs/1/location/MyNode/element/Downstream Flow/variable/Flow` | `mirror/runs/1/location/MyNode/element/Downstream Flow/variable/Flow.json` |
| `GET …/variable/Flow/aggregated/monthly` | `mirror/runs/1/location/…/variable/Flow/aggregated/monthly.json` |
| `GET /network` | `mirror/network.json` |
| `GET /network/geographic` | `mirror/network/geographic.json` |
| `GET /network/schematic.svg` | `mirror/network/schematic.svg` (SVG kept as-is) |
| `GET /network/schematic.svg/tags` | `mirror/network/schematic.svg/tags.json` |
| `GET /functions` | `mirror/functions.json` |
| `GET /variables` | `mirror/variables.json` |
| `GET /variables/$Foo/TimeSeries` | `mirror/variables/$Foo/TimeSeries.json` |
| `GET /inputSets` | `mirror/inputSets.json` |
| `GET /dataSources` | `mirror/dataSources.json` |
| `GET /dataSources/MyGroup` | `mirror/dataSources/MyGroup.csv` (data is captured as CSV) |
| `GET /tables/fus` | `mirror/tables/fus.csv` |
| `GET /resources/{name}` | `mirror/resources/{name}.png` (icons captured as PNG) |

### Important consequences

- **Query strings are dropped.** `VeneerRetriever` builds distinct *paths* for the variants
  it wants (e.g. `…/aggregated/monthly`, `…/aggregated/annual`) rather than encoding
  `?from=…&to=…&precision=…` into filenames. A mirror therefore contains the *full* series
  for whatever aggregations were captured — it does not contain date-windowed or
  precision-rounded variants. A client reading a mirror should fetch the whole series and do
  any windowing/rounding itself.
- **Only GETs are mirrored.** Mutating endpoints (POST/PUT/DELETE) and the scripting
  endpoints have no representation in a mirror; they require a live server.
- **`__all__` cross-run series** are captured under the literal `__all__` run id when the
  retriever is configured to do so (`retrieve_slim_ts`), producing
  `mirror/runs/__all__/location/…/aggregated/monthly.json` and similar.

## What gets captured

`VeneerRetriever.retrieve_all()` walks (subject to its flags):

- `GET /` (status)
- `GET /runs`, then each `GET /runs/{n}` (run summaries)
- Each time series URL listed in a run summary's `Results`, optionally as daily JSON/CSV and
  as `/aggregated/monthly` and `/aggregated/annual`
- `GET /functions`, `GET /variables` (+ each variable's `TimeSeries`/`Piecewise` when present)
- `GET /inputSets`
- `GET /dataSources` (+ per-group data, as CSV, when enabled)
- `GET /network`, `GET /network/geographic`
- `GET /network/schematic.svg` and `/tags` (optional)
- `GET /tables/{name}` (as CSV)
- Node icons via `GET /resources/{…}` (as PNG)

Retrieval is controlled by flags such as `retrieve_daily`, `retrieve_monthly`,
`retrieve_annual`, `retrieve_slim_ts`, `retrieve_single_runs`, `retrieve_data_sources`,
`retrieve_ts_json`, `retrieve_ts_csv`. If you build your own mirror, the only hard rule is the
path-mapping above; capture whatever subset your client needs.

## Reading a mirror back (veneer-py)

The veneer-py `Veneer` client has a `file` protocol mode. When constructed with
`protocol='file'`, it reads `prefix + url + '.json'` (or `.png`) from disk instead of issuing
HTTP requests — the rest of the API is identical.

```python
from veneer.bulk import VeneerRetriever
from veneer.general import Veneer

# 1. Capture a live server to disk
VeneerRetriever(destination='./mirror', host='localhost', port=9876,
                retrieve_daily=True, retrieve_monthly=True,
                retrieve_annual=True).retrieve_all(clean=True)

# 2. Later, work entirely offline
v = Veneer.from_url('file://./mirror', live=False)
#   equivalently: Veneer(protocol='file', prefix='./mirror', port=None, host=None, live=False)

v.network()                      # reads ./mirror/network.json
v.retrieve_run(1)                # reads ./mirror/runs/1.json
v.retrieve_runs()                # reads ./mirror/runs.json
```

In file mode, `live=False` causes the client to append `.json`/`.png` to every path; in HTTP
(`live`) mode no extension is appended. That single switch is the whole difference between a
live client and a mirror client — which is why a correctly laid-out mirror is a drop-in
substitute for a live server for all GET traffic.

## Building a mirror without veneer-py

If you're writing a client in another language and want a local fixture set:

1. For each GET you care about, request it from a live server.
2. Save the response body to `<root>/<path-without-leading-slash>.<ext>` where `<ext>` is
   `json` for JSON, `csv` for CSV (`Accept: text/csv`), `png` for icons, and the SVG is saved
   verbatim.
3. To enumerate time series, read each run's summary (`/runs/{n}`) and follow the
   `TimeSeriesUrl` values in `Results`.

Your client then just needs a "read from `<root>/<path>.json`" code path alongside its HTTP
path, exactly as veneer-py does.
