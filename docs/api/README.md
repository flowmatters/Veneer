# Veneer REST API Reference

Veneer is a REST API plugin for [eWater Source](https://ewater.org.au/), a hydrological
modelling system. It hosts an HTTP server inside Source (or inside the headless
`VeneerCmd` executable) and exposes a JSON-based RESTful API for querying and manipulating
a loaded model.

This folder is the **reference documentation for the HTTP API**. It is aimed at people
building clients — whether against a live Veneer instance or against a static, file-based
mirror produced by [veneer-py](https://github.com/flowmatters/veneer-py)'s
`VeneerRetriever`.

> **Source of truth.** Every endpoint described here is declared in
> [`FlowMatters.Source.Veneer/ISourceService.cs`](../../FlowMatters.Source.Veneer/ISourceService.cs)
> (the CoreWCF `[ServiceContract]`). These docs live in the Veneer repository, beside that
> contract, so the two stay in sync. If you change `ISourceService.cs`, update the matching
> page here — and bump `PROTOCOL_VERSION` in
> [`VeneerStatus.cs`](../../FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs)
> whenever the API surface changes.

## How to read these docs

1. Start with **[overview.md](overview.md)** — base URL, JSON conventions, the error model,
   CORS, and the cross-cutting naming rules (`@@`, `__all__`, partial dates, `precision`).
2. If you are working offline against a captured snapshot, read
   **[conventions-static-mirror.md](conventions-static-mirror.md)** — how `VeneerRetriever`
   maps URLs to files and how to point a client at the mirror.
3. Jump to the resource page you need (below). Each documents its endpoints with verb, URI
   template, parameters, request/response schemas, status codes, an example, and the
   equivalent veneer-py call.
4. **[schemas.md](schemas.md)** is the catalogue of every JSON payload shape.
5. **[veneer-py-crossref.md](veneer-py-crossref.md)** maps each endpoint to its veneer-py
   client method.

## Resource pages

| Page | Covers |
|------|--------|
| [server-and-lifecycle.md](server-and-lifecycle.md) | `GET /`, `GET /ping`, `POST /shutdown`, `POST /scenario/{scenario}` |
| [network.md](network.md) | `GET /network`, `/network/geographic`, `schematic.svg` (+`/tags`), nodes & links |
| [runs-and-results.md](runs-and-results.md) | Triggering, listing, deleting runs; run status & cancel; time series, aggregated & tabulated results |
| [functions-and-variables.md](functions-and-variables.md) | `/functions`, `/variables` (incl. `TimeSeries` and `Piecewise`) |
| [input-sets.md](input-sets.md) | `/inputSets` family, applying an input set, `PUT /recorders` |
| [data-sources.md](data-sources.md) | `/dataSources` CRUD, items and details, `__all__` matching |
| [tables-and-config.md](tables-and-config.md) | `/tables`, `/configuration/{element}`, `PUT /projection`, `/doc`, `/resources` |
| [scripting.md](scripting.md) | `POST /ironpython` and `POST /custom/{action}` (server-side IronPython) |

## Quick reference: all live endpoints

The table below is the complete set of endpoints wired into `ISourceService`. (Several URI
templates in `UriTemplates.cs` — `/network/nodes`, `/network/links`,
`/network/lateral_link/{id}`, `/network/conveyance_link/{id}`, `/network/catchments`,
`/network/catchments/{id}` — are **declared but not wired to any method**, so they are not
live. See [network.md](network.md#not-implemented).)

| Verb | Path | Page |
|------|------|------|
| `OPTIONS` | `*` | [overview.md](overview.md#cors-and-options) |
| `GET` | `/` | [server-and-lifecycle](server-and-lifecycle.md) |
| `GET` | `/ping` | [server-and-lifecycle](server-and-lifecycle.md) |
| `POST` | `/shutdown` | [server-and-lifecycle](server-and-lifecycle.md) |
| `POST` | `/scenario/{scenario}` | [server-and-lifecycle](server-and-lifecycle.md) |
| `GET` | `/network` | [network](network.md) |
| `GET` | `/network/geographic` | [network](network.md) |
| `GET` | `/network/schematic.svg` | [network](network.md) |
| `GET` | `/network/schematic.svg/tags` | [network](network.md) |
| `GET` | `/network/nodes/{nodeId}` | [network](network.md) |
| `GET` | `/network/link/{linkId}` | [network](network.md) |
| `GET` | `/runs` | [runs-and-results](runs-and-results.md) |
| `POST` | `/runs` | [runs-and-results](runs-and-results.md) |
| `POST` | `/runs/cancel` | [runs-and-results](runs-and-results.md) |
| `GET` | `/runs/status` | [runs-and-results](runs-and-results.md) |
| `GET` | `/runs/{runId}` | [runs-and-results](runs-and-results.md) |
| `DELETE` | `/runs/{runId}` | [runs-and-results](runs-and-results.md) |
| `GET` | `/runs/{runId}/location/{ne}/element/{re}/variable/{v}` | [runs-and-results](runs-and-results.md) |
| `GET` | `…/variable/{v}/aggregated/{aggregation}` | [runs-and-results](runs-and-results.md) |
| `GET` | `…/variable/{v}/tabulated/{functions}` | [runs-and-results](runs-and-results.md) |
| `GET` | `/functions` | [functions-and-variables](functions-and-variables.md) |
| `PUT` | `/functions/{functionName}` | [functions-and-variables](functions-and-variables.md) |
| `GET` | `/variables` | [functions-and-variables](functions-and-variables.md) |
| `GET` | `/variables/{variableName}` | [functions-and-variables](functions-and-variables.md) |
| `GET` | `/variables/{variableName}/TimeSeries` | [functions-and-variables](functions-and-variables.md) |
| `PUT` | `/variables/{variableName}/TimeSeries` | [functions-and-variables](functions-and-variables.md) |
| `GET` | `/variables/{variableName}/Piecewise` | [functions-and-variables](functions-and-variables.md) |
| `PUT` | `/variables/{variableName}/Piecewise` | [functions-and-variables](functions-and-variables.md) |
| `GET` | `/inputSets` | [input-sets](input-sets.md) |
| `POST` | `/inputSets` | [input-sets](input-sets.md) |
| `PUT` | `/inputSets/{inputSetName}` | [input-sets](input-sets.md) |
| `POST` | `/inputSets/{inputSetName}/{action}` | [input-sets](input-sets.md) |
| `PUT` | `/recorders` | [input-sets](input-sets.md) |
| `GET` | `/dataSources` | [data-sources](data-sources.md) |
| `POST` | `/dataSources` | [data-sources](data-sources.md) |
| `GET` | `/dataSources/{dataSourceGroup}` | [data-sources](data-sources.md) |
| `PUT` | `/dataSources/{dataSourceGroup}` | [data-sources](data-sources.md) |
| `DELETE` | `/dataSources/{dataSourceGroup}` | [data-sources](data-sources.md) |
| `GET` | `/dataSources/{dataSourceGroup}/{inputSet}` | [data-sources](data-sources.md) |
| `GET` | `/dataSources/{dataSourceGroup}/__all__/{name}` | [data-sources](data-sources.md) |
| `GET` | `/dataSources/{dataSourceGroup}/{inputSet}/{item}` | [data-sources](data-sources.md) |
| `GET` | `/tables` | [tables-and-config](tables-and-config.md) |
| `GET` | `/tables/{table}` | [tables-and-config](tables-and-config.md) |
| `GET` | `/configuration/{element}` | [tables-and-config](tables-and-config.md) |
| `PUT` | `/projection` | [tables-and-config](tables-and-config.md) |
| `GET` | `/doc/{*fn}` | [tables-and-config](tables-and-config.md) |
| `GET` | `/resources/{resourceName}` | [tables-and-config](tables-and-config.md) |
| `POST` | `/ironpython` | [scripting](scripting.md) |
| `POST` | `/custom/{action}` | [scripting](scripting.md) |

## A note on OpenAPI

These pages are hand-written prose. A machine-readable OpenAPI 3 description is a possible
follow-up; if added, it should be generated/maintained from `ISourceService.cs` so it can't
drift from the contract.
