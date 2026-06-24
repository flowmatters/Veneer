# Input sets & recorders

**Input sets** are named collections of configuration changes that can be applied to a model
before a run (Source's "Input Sets" feature). **Recorders** control which results are captured
during a run.

---

## `GET /inputSets`

All input sets.

- **Response**: array of [`InputSetSummary`](schemas.md#inputsetsummary) (`200 OK`)
- **veneer-py**: `v.input_sets()`

```json
[
  {
    "Name": "Baseline",
    "URL": "/inputSets/Baseline",
    "HierarchicalName": "Baseline",
    "Configuration": [
      "$Demand.Expression = $BaseDemand * 1.0"
    ],
    "ReloadOnRun": false,
    "Filename": null
  }
]
```

`Configuration` is the list of instruction strings the input set applies.

---

## `POST /inputSets`

Create a new input set.

- **Request body**: [`InputSetSummary`](schemas.md#inputsetsummary) — `Name` and
  `Configuration` are the key fields.
- **Response**: none (`200 OK` on success)
- **veneer-py**: `v.create_input_set(input_set)`

```http
POST /inputSets HTTP/1.1
Content-Type: application/json

{
  "Name": "DryClimate",
  "Configuration": ["$RainfallScale.Expression = 0.8"],
  "ReloadOnRun": false
}
```

---

## `PUT /inputSets/{inputSetName}`

Update an existing input set's configuration instructions.

- **Request body**: [`InputSetSummary`](schemas.md#inputsetsummary) — `Configuration` is
  applied to the named set.
- **Response**: none (`200 OK`)
- **veneer-py**: `v.update_input_set(name, input_set)`

```http
PUT /inputSets/DryClimate HTTP/1.1
Content-Type: application/json

{ "Name": "DryClimate", "Configuration": ["$RainfallScale.Expression = 0.7"] }
```

---

## `DELETE /inputSets/{inputSetName}`

Delete an input set and its associated configuration from the model.

- **Request body**: none
- **Response**: none (`200 OK`)
- **veneer-py**: no dedicated helper yet — call `v.session.delete(url)` with the input set's `URL`.

```http
DELETE /inputSets/DryClimate HTTP/1.1
```

> `{inputSetName}` is the URL-safe form of the input set name (as returned in the `URL` field of
> `GET /inputSets`). Deleting an input set that does not exist is a no-op (`200 OK`).

---

## `POST /inputSets/{inputSetName}/{action}`

Perform an action on an input set. The only supported action is **`run`**, which applies the
input set to the model.

- **Request body**: none
- **Response**: none (`200 OK` when `action == run`)
- **`400 Bad Request`**: any `{action}` other than `run`
  (*"Cannot perform action {action} on input sets"*).
- **veneer-py**: `v.apply_input_set(name)` (posts to `/inputSets/{name}/run`)

```http
POST /inputSets/DryClimate/run HTTP/1.1
```

> Applying an input set changes the model state but does not trigger a simulation — follow
> with `POST /runs` to run.

---

## `PUT /recorders`

Configure which results are recorded during runs. You supply two lists of element selectors:
ones to **stop** recording and ones to **start** recording.

- **Request body**: [`RecordingInstructions`](schemas.md#recordinginstructions) with
  `RecordNone` (turn off) and `RecordAll` (turn on) arrays. Selectors use the same network
  element grammar as time series, including the `@@` functional-unit suffix.
- **Response**: none (`200 OK`)
- **veneer-py**: `v.configure_recording(enable=[...], disable=[...])`

```http
PUT /recorders HTTP/1.1
Content-Type: application/json

{
  "RecordNone": ["location/__all__/element/__all__/variable/__all__"],
  "RecordAll": ["location/Gauge A/element/Downstream Flow/variable/Flow"]
}
```

> A common pattern is to first disable everything (`RecordNone` with an `__all__` selector),
> then enable only the specific results you want in `RecordAll`. This keeps run outputs (and
> the resulting time series list) small. The exact selector strings veneer-py builds are in
> its `configure_recording` helper — see
> [veneer-py-crossref.md](veneer-py-crossref.md).
