# Data sources

Data sources are Source's container for input data — typically time series organised into
**groups**, each containing **items**, each of which has **details** (the actual series, often
per input set). The API exposes the group hierarchy and full CRUD on groups.

## Name encoding

Data-source group and item names frequently contain slashes and percent signs. The server
decodes `%25`→`%` and `%2F`→`/` in the `{dataSourceGroup}` segment. veneer-py escapes `/` as
`%2F` when building these URLs (and `%252F` in "double-escape" mode for nested paths). When
building URLs yourself, URL-encode each segment and remember the server will undo `%2F`/`%25`.

The hierarchy of GET endpoints:

```
/dataSources                                   → all groups (summaries)
/dataSources/{group}                           → one group
/dataSources/{group}/{inputSet}                → one item within a group
/dataSources/{group}/{inputSet}/{item}         → one detail of an item
/dataSources/{group}/__all__/{name}            → matching details across all items in a group
```

---

## `GET /dataSources`

All data-source groups (summary form — items present but details not expanded).

- **Response**: array of [`SimpleDataGroupItem`](schemas.md#simpledatagroupitem) (`200 OK`)
- **veneer-py**: `v.data_sources()`

```json
[
  {
    "id": "/dataSources/Climate",
    "Name": "Climate",
    "FullName": "Climate",
    "Path": "Climate",
    "Items": [
      { "Name": "Rainfall", "InputSets": ["Baseline"] }
    ]
  }
]
```

---

## `GET /dataSources/{dataSourceGroup}`

One group, with its items.

- **Response**: [`SimpleDataGroupItem`](schemas.md#simpledatagroupitem) (`200 OK`)
- **`404 Not Found`** (with `null` body) if the group doesn't exist.
- **veneer-py**: `v.data_source(name)` — note veneer-py requests the *data* (CSV) form for a
  named group, expanding the actual time series.

```http
GET /dataSources/Climate HTTP/1.1
```

---

## `POST /dataSources`

Create a data source group (or replace one with the same name).

- **Request body**: [`SimpleDataGroupItem`](schemas.md#simpledatagroupitem). Items can carry
  inline data via `DetailsAsCSV` plus `UnitsForNewTS`, or load a file, named by the item's
  `Filename` (or, if it has none, by the group's `Name`), with `ReloadOnRun`.
- **Name**: the group is named as Source keeps it — `DataManager.RestrictName(Name)`: `.`, `@`,
  `>`, `,` and `\` become `_`, the name is cut to 200 characters and trimmed. (With its
  interface open, Source renames every data source it shows that way.) A blank `Name` is a
  `400`.
- **Replacing**: a group already of that name (or, from an older Veneer, of `Name` exactly as
  given) is **replaced**. Usages (assignments) of its columns move to the columns of the same
  name in the new group; usages of a column the new group lacks are dropped, with a warning in
  Veneer's log.
- **Response**: the group created, as the summary `GET /dataSources/{dataSourceGroup}` returns
  ([`SimpleDataGroupItem`](schemas.md#simpledatagroupitem)): its final `Name`, `id`, `FullName`
  and `Items`.
- **Errors**: a `SimulationFault` JSON body (`Message`, `StackTrace`) — `400` for an invalid
  request, `500` if the group could not be created (a file that will not load, say). A failed
  request leaves no group behind.
- **veneer-py**: `v.create_data_source(name, data, units, precision)`
- **Protocol**: `Filename` on create, the restricted name, the response body and the faults are
  **added in protocol `20261027`**. Before that, a file item loaded from the group's `Name` (so
  the name *was* the path), names were kept as given (and renamed by Source's interface), and
  the response was empty. A client must check `Version` before sending a `Name` that is not the
  file: an older server would try to load `Name` as the file.

In the Source GUI, the change to the model is made on Source's UI thread (from protocol
`20261027`; before that, a POST failed in the Source 6.x GUI).

```http
POST /dataSources HTTP/1.1
Content-Type: application/json

{
  "Name": "Climate",
  "Items": [
    {
      "Name": "Rainfall",
      "InputSets": ["Baseline"],
      "UnitsForNewTS": "mm/d",
      "DetailsAsCSV": "Date,Rainfall\n1990-01-01,2.4\n1990-01-02,0.0\n"
    }
  ]
}
```

A file-based group, named apart from its file:

```http
POST /dataSources HTTP/1.1
Content-Type: application/json

{
  "Name": "inflow_series",
  "Items": [
    {
      "Name": "Item for inflow_series",
      "InputSets": ["Default Input Set"],
      "Filename": "C:\\inputs\\inflow_series.csv",
      "ReloadOnRun": true
    }
  ]
}
```

```json
{ "Name": "inflow_series", "id": "/dataSources/inflow_series", "FullName": "/inflow_series",
  "Path": "/", "Items": [ { "Name": "Item for inflow_series", "Filename": "C:\\inputs\\inflow_series.csv",
  "ReloadOnRun": true, "Details": [ ... ] } ] }
```

---

## `PUT /dataSources/{dataSourceGroup}`

Update (create or replace) the named group. The server forces the group's `Name` to the URL
segment, then applies the same create-or-replace logic as `POST /dataSources`.

- **Request body**: [`SimpleDataGroupItem`](schemas.md#simpledatagroupitem)
- **Response**: as for `POST /dataSources` — the group created (from protocol `20261027`;
  none before).

```http
PUT /dataSources/Climate HTTP/1.1
Content-Type: application/json

{ "Items": [ { "Name": "Rainfall", "InputSets": ["Baseline"], "UnitsForNewTS": "mm/d",
              "DetailsAsCSV": "Date,Rainfall\n1990-01-01,3.0\n" } ] }
```

---

## `DELETE /dataSources/{dataSourceGroup}`

Delete a group.

- **Response**: none (`200 OK` on success)
- **`404 Not Found`** if the group doesn't exist.
- **Errors**: a `SimulationFault` JSON body, `500` (from protocol `20261027`).
- **veneer-py**: `v.delete_data_source(group)`

```http
DELETE /dataSources/Climate HTTP/1.1
```

---

## `GET /dataSources/{dataSourceGroup}/{inputSet}`

One item within a group (matched by input set).

- **Response**: [`SimpleDataItem`](schemas.md#simpledataitem) (`200 OK`)
- **`404 Not Found`** if the group or the item is missing.
- **veneer-py**: `v.data_source_item(source, name, input_set)`

```http
GET /dataSources/Climate/Baseline HTTP/1.1
```

---

## `GET /dataSources/{dataSourceGroup}/__all__/{name}`

All details named `{name}` across every item in the group. `{name}` is matched first exactly
(URL-safe comparison) and then as a **regular expression**, so you can pull a set of related
series in one call. Matched detail names are prefixed with their item name
(`{itemName}/{detailName}`) and expanded.

- **Response**: array of [`SimpleDataDetails`](schemas.md#simpledatadetails) (`200 OK`)
- **`404 Not Found`** (with `null` body) if nothing matches.

```http
GET /dataSources/Climate/__all__/Rainfall.* HTTP/1.1
```

---

## `GET /dataSources/{dataSourceGroup}/{inputSet}/{item}`

One specific detail (the actual series/value) of an item.

- **Response**: [`SimpleDataDetails`](schemas.md#simpledatadetails) (`200 OK`)
- **`404 Not Found`** if the group, item, or detail is missing.

```http
GET /dataSources/Climate/Baseline/Rainfall HTTP/1.1
```

```json
{
  "Name": "Rainfall",
  "TimeSeries": {
    "Name": "Rainfall",
    "Units": "mm/d",
    "TimeStep": "Daily",
    "StartDate": "1990-01-01",
    "EndDate": "2000-12-31",
    "Events": [ { "Date": "1990-01-01", "Value": 2.4 } ]
  }
}
```
