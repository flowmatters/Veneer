"""Live tests of POST /functions/evaluate against the generated synthetic model.

The `veneer` fixture (conftest.py) runs the scenario once before these tests, so references to
functions and variables read the state that run left behind (e.g. $WhatIf.f_Const = 10).

Deliberately NOT live-tested: "no scenario loaded -> 409" (VeneerCmd always has a scenario).
"""
import json
import time

import pytest
import requests

from tests.conftest import cold_probe, evaluate, stray_count

CONTEXT = "$WhatIf.f_Release"


def item(id, expression, date="2000-01-02", **extra):
    return {"id": id, "date": date, "expression": expression, **extra}


def only_result(veneer, *items):
    status, body = evaluate(veneer[0], list(items))
    assert status == 200, body
    return body["results"]


def test_literal_only(veneer):
    (r,) = only_result(veneer, item("a", "1 + 2 * 3"))
    assert r == {"id": "a", "value": 7}


def test_now_honours_the_items_date(veneer):
    jul, mar = only_result(veneer, item("jul", "$Now.Month*100+$Now.Day", "2001-07-01"),
                           item("mar", "$Now.Month*100+$Now.Day", "2001-03-15"))
    assert jul == {"id": "jul", "value": 701}
    assert mar == {"id": "mar", "value": 315}


@pytest.mark.xfail(strict=True, reason=(
    "endpoint defect: on a scenario that has never run, $Now/$Start/$End and lookup() "
    "evaluate to 0 and modelled variables throw, because FunctionManager.Reset(scenario, "
    "start, end, timeStep, ctx) has not been called; calling it first gives the right values"))
def test_now_honours_the_items_date_before_any_run(veneer):
    status, body = cold_probe["now"]
    assert status == 200, body
    assert body["results"] == [{"id": "jul", "value": 701}]


def test_leaf_names_resolve_through_context(veneer):
    (r,) = only_result(veneer, item("ctx", "$f_Const + 1", context=CONTEXT))
    assert r == {"id": "ctx", "value": 11}
    (r,) = only_result(veneer, item("bare", "$f_Const + 1"))
    assert r["id"] == "bare" and "value" not in r
    assert "F_CONST" in r["error"].upper()


def test_bindings_replace_whole_tokens(veneer):
    # A `$` token surviving the wire (DataContractJsonSerializer drops `$`-named members) IS the test.
    (r,) = only_result(veneer, item("b", "$f_Const + 1", bindings=[{"token": "$f_Const", "value": 2.5}]))
    assert r == {"id": "b", "value": 3.5}


def test_non_finite_result_is_an_item_error(veneer):
    def reject(name):
        raise ValueError("non-JSON constant " + name)

    r = requests.post(f"{veneer[0]}/functions/evaluate", timeout=60,
                      json={"items": [item("div", "1/0"), item("nan", "0/0")]})
    assert r.status_code == 200, r.text
    body = json.loads(r.text, parse_constant=reject)
    assert body["results"] == [{"id": "div", "error": "non-finite result"},
                               {"id": "nan", "error": "non-finite result"}]


def test_lookup_with_a_literal_input(veneer):
    (r,) = only_result(veneer, item("lk", "lookup($pw_Curve, 5)", context="$WhatIf.f_Lookup"))
    assert r == {"id": "lk", "value": 2.5}


def test_per_item_errors_keep_the_batch_200(veneer):
    results = only_result(veneer, item("ok", "2 + 2"), item("syntax", "1 + * 2"),
                          item("unknown", "frobnicate(1)"))
    assert [r["id"] for r in results] == ["ok", "syntax", "unknown"]
    assert results[0] == {"id": "ok", "value": 4}
    assert results[1]["error"] and "value" not in results[1]
    assert results[2]["error"] and "value" not in results[2]


def test_leak_free_after_a_mixed_batch(veneer):
    results = only_result(veneer,
                          item("fn", "$f_Const * 2", context=CONTEXT),
                          item("mv", "$v_StorageA_Volume * 0 + 1"),
                          item("ts", "$v_Demand + 1"),
                          item("lk", "lookup($pw_Curve, 15)"),
                          item("dates", "$Now.Day + $Start.Year"),
                          item("parse", "$f_Const + * 2", context=CONTEXT),
                          item("eval", "$f_Const / 0", context=CONTEXT))
    by_id = {r["id"]: r for r in results}
    assert by_id["fn"]["value"] == 20
    assert by_id["mv"]["value"] == 1
    assert by_id["lk"]["value"] == 12.5
    assert by_id["dates"]["value"] == 2 + 2000
    assert "error" in by_id["parse"] and "error" in by_id["eval"]
    assert stray_count(veneer[0]) == 0


def test_cancelled_request_leaves_no_strays(veneer):
    base_url = veneer[0]
    items = [item(f"i{n}", "$f_Const + $v_StorageA_Volume * 0 + lookup($pw_Curve, 5)", context=CONTEXT)
             for n in range(2000)]
    with pytest.raises(requests.exceptions.Timeout):
        requests.post(f"{base_url}/functions/evaluate", json={"items": items}, timeout=(5, 0.05))
    # The abandoned batch holds the run lock until it finishes, so this one waits for it.
    status, _ = evaluate(base_url, [item("after", "1")])
    assert status == 200
    deadline = time.monotonic() + 30
    while (n := stray_count(base_url)) != 0 and time.monotonic() < deadline:
        time.sleep(0.5)
    assert n == 0


def test_items_cap_is_400(veneer):
    status, body = evaluate(veneer[0], [item(f"i{n}", "1") for n in range(2001)])
    assert status == 400
    assert "too many items" in body["Message"]


def test_malformed_date_is_400(veneer):
    status, body = evaluate(veneer[0], [item("d", "1", date="01/07/2001")])
    assert status == 400
    assert "yyyy-MM-dd" in body["Message"]


def test_get_root_reports_the_protocol(veneer):
    root = requests.get(f"{veneer[0]}/", timeout=30).json()
    assert root["Version"] >= 20261018
