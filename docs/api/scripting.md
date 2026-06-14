# Server-side scripting

These two endpoints are the API's escape hatch: they execute **IronPython** *inside the Source
process*, with full access to the loaded scenario and the Source object model. Anything the
RESTful endpoints can't express, you can do here. This is also how veneer-py implements much of
its higher-level functionality (its `find`/`get`/`set` helpers all compile down to scripts
posted to `/ironpython`).

> **Security.** Scripting runs arbitrary code in-process. It is gated by a server-side
> `AllowScript` switch; when disabled, these endpoints return **`403 Forbidden`**. Since there
> is no authentication, only enable scripting on a server you fully control and that is not
> reachable by untrusted clients.

---

## `POST /ironpython`

Run an IronPython script and return its captured output, result value, and any exception.

- **Request body**: [`IronPythonScript`](schemas.md#ironpythonscript) — `Script` (the source)
  and `Debug` (bool).
- **Response**: [`IronPythonResponse`](schemas.md#ironpythonresponse) (`200 OK`) — carries
  `StandardOut`, `StandardError`, a polymorphic `Response` value, and an `Exception`
  ([`SimpleException`](schemas.md#simpleexception)) when the script threw.
- **`403 Forbidden`** (with `null` body) if scripting is disabled.
- **veneer-py**: `v.run_server_side_script(script)` — raises if `Exception` is non-null, and
  raises a "Script disabled" error on `403`.

### The execution environment

The script runs with the loaded scenario and project handler injected. veneer-py prefixes
scripts with a standard preamble that imports common namespaces and the Source-side helper
class:

```python
import clr
clr.AddReference('System.Core')
import System
import FlowMatters.Source.Veneer.RemoteScripting.ScriptHelpers as H
clr.ImportExtensions(System.Linq)
# ... your statements ...
```

`H` (`ScriptHelpers`) is the bridge that turns Source objects into JSON-serialisable
[`VeneerResponse`](schemas.md#veneerresponse) values. To return a value to the client, assign
it to the script's result (veneer-py's helpers build the script so the final expression becomes
`Response`).

### The response shape

```json
{
  "StandardOut": "hello\n",
  "StandardError": "",
  "Response": { "Value": 42.0 },
  "Exception": null
}
```

On error, `Response` is `null` and `Exception` is populated:

```json
{
  "StandardOut": "",
  "StandardError": "Traceback (most recent call last): ...",
  "Response": null,
  "Exception": {
    "ExceptionType": "System.NullReferenceException",
    "Message": "Object reference not set to an instance of an object.",
    "StackTrace": "...",
    "PythonStackTrace": "...",
    "InnerException": null
  }
}
```

`Response` is **polymorphic** — it can be any `VeneerResponse` subtype:
`StringResponse`/`NumericResponse`/`BooleanResponse` (`{"Value": ...}`), `ListResponse`,
`DictResponse` (`{"Entries":[{"Key":...,"Value":...}]}`), a time series type, a
`GeoJSONCoverage`, etc. See [schemas.md](schemas.md#veneerresponse).

### How veneer-py uses it

`veneer/server_side.py` builds scripts programmatically rather than asking you to write
IronPython by hand. Its `VeneerIronPython` helper exposes:

- `get(query)` / `set(query, value)` — read or assign Source object properties using an
  accessor mini-language (e.g. `scenario.Network.Nodes.*Name`, where `.*` iterates a
  collection and `.Where(lambda n: ...)` filters).
- `add_to_list(query, value)`, `get_data_sources(query)`, `get_functions(query)` — targeted
  helpers.
- `run_script(script)` — submit a raw script; it raises on `Exception`, then returns
  `Response['Value']` (simplified via `simplify_response`).

So `v.model.node_types()` (and the dozens of similar convenience methods) are thin wrappers
that generate a script, POST it here, and unwrap `Response`.

### Example

```http
POST /ironpython HTTP/1.1
Content-Type: application/json

{ "Script": "result = scenario.Network.Nodes.Count", "Debug": false }
```

---

## `POST /custom/{action}`

Invoke a **named, server-registered** custom endpoint. Custom endpoints are pre-registered
IronPython templates (configured at startup / by addons) that take a list of string
parameters, build a script, and run it. This lets an operator expose a curated, named
operation without clients sending arbitrary code.

- **Request body**: `string[]` — the `parameters` passed to the custom endpoint's script
  generator.
- **Response**: [`IronPythonResponse`](schemas.md#ironpythonresponse) (`200 OK`) — same shape
  as `/ironpython`.
- If `{action}` matches no registered endpoint, the response body is `null` (treat as not
  found).
- Subject to the same `AllowScript` gate → **`403 Forbidden`** when scripting is disabled.

```http
POST /custom/rebuildIndexes HTTP/1.1
Content-Type: application/json

["arg1", "arg2"]
```

> Custom endpoints are defined server-side (see the `RemoteScripting/` `CustomEndPoint` type
> and the `Addons/` configuration). They are not discoverable through the API — a client must
> know the action name out of band.
