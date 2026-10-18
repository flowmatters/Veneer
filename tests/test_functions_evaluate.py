"""Live tests of POST /functions/evaluate against the generated synthetic model.

The `veneer` fixture (conftest.py) runs the scenario once before these tests, so references to
functions and variables read the state that run left behind (e.g. $WhatIf.f_Const = 10).

Deliberately NOT live-tested: "no scenario loaded -> 409" (VeneerCmd always has a scenario).
"""
import json
import time

import pytest
import requests

from tests.conftest import cold_probe
from tests.live import evaluate, make_item, stray_count

CONTEXT = "$WhatIf.f_Release"
SWEPT_WARNING = "evaluate swept"


def only_result(veneer, *items):
    status, body = evaluate(veneer.base_url, list(items))
    assert status == 200, body
    return body["results"]


def log_text(veneer) -> str:
    with open(veneer.log_path, encoding="utf-8", errors="replace") as f:
        return f.read()


def wait_for_log(veneer, needle, timeout=30.0) -> bool:
    deadline = time.monotonic() + timeout
    while needle not in log_text(veneer):
        if time.monotonic() > deadline:
            return False
        time.sleep(0.05)
    return True


def test_literal_only(veneer):
    (r,) = only_result(veneer, make_item("a", "1 + 2 * 3"))
    assert r == {"id": "a", "value": 7}


def test_now_honours_the_items_date(veneer):
    jul, mar = only_result(veneer,
                           make_item("jul", "$Now.Month*100+$Now.Day", "2001-07-01"),
                           make_item("mar", "$Now.Month*100+$Now.Day", "2001-03-15"))
    assert jul == {"id": "jul", "value": 701}
    assert mar == {"id": "mar", "value": 315}


@pytest.mark.xfail(strict=True, raises=AssertionError, reason=(
    "observed: on a scenario that has been loaded but never run, $Now.Month*100+$Now.Day "
    "evaluates to 0, not 701 (manual probes also saw 0 for $Start.Year and for lookup() with a "
    "literal); suspected cause: the function manager's per-run state is only set up by a run "
    "(calling FunctionManager.Reset first gave the expected values); under investigation"))
def test_now_honours_the_items_date_before_any_run(veneer):
    status, body = cold_probe["now"]
    assert status == 200, body
    assert body["results"] == [{"id": "jul", "value": 701}]


def test_leaf_names_resolve_through_context(veneer):
    (r,) = only_result(veneer, make_item("ctx", "$f_Const + 1", context=CONTEXT))
    assert r == {"id": "ctx", "value": 11}
    (r,) = only_result(veneer, make_item("bare", "$f_Const + 1"))
    assert r["id"] == "bare" and "value" not in r
    assert "F_CONST" in r["error"].upper()


def test_bindings_replace_whole_tokens(veneer):
    # A `$` token surviving the wire (DataContractJsonSerializer drops `$`-named members) IS
    # the test.
    (r,) = only_result(veneer, make_item("b", "$f_Const + 1",
                                         bindings=[{"token": "$f_Const", "value": 2.5}]))
    assert r == {"id": "b", "value": 3.5}


def test_non_finite_result_is_an_item_error(veneer):
    def reject(name):
        raise ValueError("non-JSON constant " + name)

    r = requests.post(f"{veneer.base_url}/functions/evaluate", timeout=60,
                      json={"items": [make_item("div", "1/0"), make_item("nan", "0/0")]})
    assert r.status_code == 200, r.text
    body = json.loads(r.text, parse_constant=reject)
    assert body["results"] == [{"id": "div", "error": "non-finite result"},
                               {"id": "nan", "error": "non-finite result"}]


def test_lookup_with_a_literal_input(veneer):
    (r,) = only_result(veneer,
                       make_item("lk", "lookup($pw_Curve, 5)", context="$WhatIf.f_Lookup"))
    assert r == {"id": "lk", "value": 2.5}


def test_per_item_errors_keep_the_batch_200(veneer):
    results = only_result(veneer, make_item("ok", "2 + 2"), make_item("syntax", "1 + * 2"),
                          make_item("unknown", "frobnicate(1)"))
    assert [r["id"] for r in results] == ["ok", "syntax", "unknown"]
    assert results[0] == {"id": "ok", "value": 4}
    assert results[1]["error"] and "value" not in results[1]
    assert results[2]["error"] and "value" not in results[2]


def test_leak_free_after_a_mixed_batch(veneer):
    results = only_result(veneer,
                          make_item("fn", "$f_Const * 2", context=CONTEXT),
                          make_item("mv", "$v_StorageA_Volume * 0 + 1"),
                          make_item("ts", "$v_Demand + 1"),
                          make_item("lk", "lookup($pw_Curve, 15)"),
                          make_item("dates", "$Now.Day + $Start.Year"),
                          make_item("parse", "$f_Const + * 2", context=CONTEXT),
                          make_item("eval", "$f_Const / 0", context=CONTEXT))
    by_id = {r["id"]: r for r in results}
    assert by_id["fn"]["value"] == 20
    assert by_id["mv"]["value"] == 1
    # A time series variable reads its current in-Source value, not the value at the item's
    # date; after the fixture's warm-up run that is 0 (observed).
    assert by_id["ts"]["value"] == 1
    assert by_id["lk"]["value"] == 12.5
    assert by_id["dates"]["value"] == 2 + 2000
    assert "error" in by_id["parse"] and "error" in by_id["eval"]
    assert stray_count(veneer.base_url) == 0
    # Release alone cleaned up: the batch's SweepStrays backstop had nothing to remove.
    time.sleep(0.2)  # let the log tee catch up
    assert SWEPT_WARNING not in log_text(veneer)


def test_cancelled_request_leaves_no_strays(veneer):
    base_url = veneer.base_url
    expression = "$f_Const + $v_StorageA_Volume * 0 + lookup($pw_Curve, 5)"
    items = [make_item(f"i{n}", expression, context=CONTEXT) for n in range(2000)]
    started = time.monotonic()
    with pytest.raises(requests.exceptions.Timeout):
        requests.post(f"{base_url}/functions/evaluate", json={"items": items}, timeout=(5, 0.05))
    # Do NOT follow up with another evaluate: its own finally runs SweepStrays and would hide
    # the very leak this test looks for. Wait until the abandoned batch has been received,
    # then for Veneer's run lock: the batch holds it from just after logging "Evaluating"
    # until its own release and sweep are done, and GET /runs/status takes the same lock
    # without sweeping.
    assert wait_for_log(veneer, "Evaluating 2000 expression(s)")
    time.sleep(1)
    status = requests.get(f"{base_url}/runs/status", timeout=300)
    assert status.status_code == 200, status.text
    print(f"abandoned 2000-item batch finished within {time.monotonic() - started:.2f}s")
    deadline = time.monotonic() + 30
    while (n := stray_count(base_url)) != 0 and time.monotonic() < deadline:
        time.sleep(0.5)
    assert n == 0


def test_items_cap_is_400(veneer):
    status, body = evaluate(veneer.base_url, [make_item(f"i{n}", "1") for n in range(2001)])
    assert status == 400
    assert "too many items" in body["Message"]


def test_malformed_date_is_400(veneer):
    status, body = evaluate(veneer.base_url, [make_item("d", "1", date="01/07/2001")])
    assert status == 400
    assert "yyyy-MM-dd" in body["Message"]


def test_get_root_reports_the_protocol(veneer):
    root = requests.get(f"{veneer.base_url}/", timeout=30).json()
    assert root["Version"] >= 20261018
