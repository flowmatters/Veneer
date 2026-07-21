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
  inline data via `DetailsAsCSV` plus `UnitsForNewTS`, or reference a file via `Filename` /
  `FilenameIsRelative` / `ReloadOnRun`.
- **Response**: none (`200 OK`). If a group with the same name exists it is **replaced**.
- **veneer-py**: `v.create_data_source(name, data, units, precision)`

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

---

## `PUT /dataSources/{dataSourceGroup}`

Update (create or replace) the named group. The server forces the group's `Name` to the URL
segment, then applies the same create-or-replace logic as `POST /dataSources`.

- **Request body**: [`SimpleDataGroupItem`](schemas.md#simpledatagroupitem)
- **Response**: none (`200 OK`)

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
