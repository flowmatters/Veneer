# Server & lifecycle

Endpoints for inspecting the server, checking liveness, switching scenarios, and shutting
down. See [overview.md](overview.md) for the conventions referenced here.

---

## `GET /`

Returns the server status: protocol version, Source version, the loaded project/scenario,
process info, and loaded plugins. This is the first call most clients make — both to confirm
connectivity and to read `Version` (the [protocol version](overview.md#protocol-version)).

- **Response**: [`VeneerStatus`](schemas.md#veneerstatus) (`200 OK`, always)
- **veneer-py**: `v.status()`, `v.scenario_info()`, `v.source_version()`

**Example**

```http
GET / HTTP/1.1
Accept: application/json
```

```json
{
  "Version": 20260519,
  "SourceVersion": "5.16.0.5740",
  "ProjectFile": "Murray.rsproj",
  "ProjectFullFilename": "C:\\models\\Murray.rsproj",
  "Scenario": "Baseline",
  "Projection": { "Projection": "UTM", "Zone": 55, "Hemisphere": "South" },
  "PID": 18244,
  "HostExe": "C:\\Program Files\\eWater\\Source 5.16\\RiverSystem.exe",
  "User": "joelr",
  "PluginsLoaded": [
    "C:\\...\\FlowMatters.Source.Veneer.dll"
  ]
}
```

---

## `GET /ping`

Cheap liveness check. Always returns a JSON string and never throws — on an internal error it
returns a diagnostic string rather than a non-2xx status.

- **Response**: JSON string — `"pong"` on success, or
  `"Ping failed: {ExceptionType}: {Message}\n{StackTrace}"` on error (`200 OK`)
- **veneer-py**: (used internally for connection checks)

```http
GET /ping HTTP/1.1
```

```json
"pong"
```

---

## `POST /scenario/{scenario}`

Selects the active scenario. The `{scenario}` segment may be either a **numeric index** into
the project's scenarios or a **scenario name**. Updates both the request-scoped and the static
shared scenario, so the change is visible to all subsequent requests.

- **Request body**: none
- **Response**: none (`200 OK` on success)
- **`400 Bad Request`**: thrown (`InvalidOperationException`) if Veneer is running inside the
  Source **GUI** — you cannot switch scenarios out from under the UI. This works in the
  headless `VeneerCmd` host.
- A name that matches no scenario raises an error (the lookup uses `First(...)`).
- **veneer-py**: `v.select_scenario(scenario)`

```http
POST /scenario/Baseline HTTP/1.1
```

```http
POST /scenario/0 HTTP/1.1
```

---

## `POST /shutdown`

Stops the server by terminating the host process.

- **Request body**: none
- **Behaviour**:
  - Headless (`VeneerCmd`): calls `Environment.Exit(0)` — the process ends and the request may
    not receive a clean response.
  - In the Source **GUI**: shutdown is **not supported**; the call logs a warning and throws
    (`"Shutdown not supported"`).
- **veneer-py**: `v.shutdown()`

```http
POST /shutdown HTTP/1.1
```

> Because the process exits, treat a dropped/empty response as success when running headless.
