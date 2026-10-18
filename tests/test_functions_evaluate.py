"""Live tests of POST /functions/evaluate against the generated synthetic model.

Two modes (see conftest.py): the default serves a scenario that has run once (`veneer`); with
VENEER_COLD=1 the scenario has never run in the process (`veneer_cold`). Built-ins ($Now, $Start,
$End) are supplied by Veneer and lookup tables work in both; every other unbound reference is an
item error in both — the endpoint answers the same whether or not the scenario has run.

Deliberately NOT live-tested: "no scenario loaded -> 409" (VeneerCmd always has a scenario).
"""
import json
import time

import pytest
import requests

from tests.live import (USAGE_FUNCTION, USAGE_VALUE, evaluate, make_item, run_script,
                        stray_count)

CONTEXT = "$WhatIf.f_Release"
SWEPT_WARNING = "evaluate swept"
MODELLED = "modelled variable"
TIME_SERIES = "time series variable"
FUNCTION_REFERENCE = "function reference"


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


def assert_item_error(result, *fragments):
    assert "value" not in result, result
    for fragment in fragments:
        assert fragment in result.get("error", ""), result


# ---------------------------------------------------------------- a scenario that has run

def test_literal_only(veneer):
    (r,) = only_result(veneer, make_item("a", "1 + 2 * 3"))
    assert r == {"id": "a", "value": 7}


def test_now_honours_the_items_date(veneer):
    jul, mar = only_result(veneer,
                           make_item("jul", "$Now.Month*100+$Now.Day", "2001-07-01"),
                           make_item("mar", "$Now.Month*100+$Now.Day", "2001-03-15"))
    assert jul == {"id": "jul", "value": 701}
    assert mar == {"id": "mar", "value": 315}


def test_leaf_names_resolve_through_context(veneer):
    # An unbound function reference is an item error, and the error names what the leaf name
    # resolved to: through the context's folder, $f_Const is $WhatIf.f_Const. (The model's
    # lookup table is top-level, so its leaf name IS its full name and cannot show this.)
    (r,) = only_result(veneer, make_item("ctx", "$f_Const + 1", context=CONTEXT))
    assert_item_error(r, "$WhatIf.f_Const", FUNCTION_REFERENCE)
    # Without a context the leaf name does not resolve at all: Source's own parse error.
    (r,) = only_result(veneer, make_item("bare", "$f_Const + 1"))
    assert_item_error(r)
    assert "F_CONST" in r["error"].upper() and FUNCTION_REFERENCE not in r["error"]
    # Bound, the leaf token is substituted before Source sees it.
    (r,) = only_result(veneer, make_item("bound", "$f_Const + 1", context=CONTEXT,
                                         bindings=[{"token": "$f_Const", "value": 10}]))
    assert r == {"id": "bound", "value": 11}


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
    # The unbound references are item errors, but each was parsed — and so registered on the
    # real entity — before the guard refused it; release must still undo every registration.
    results = only_result(veneer,
                          make_item("fn", "$f_Const * 2", context=CONTEXT),
                          make_item("mv", "$v_StorageA_Volume * 0 + 1"),
                          make_item("ts", "$v_Demand + 1"),
                          make_item("lk", "lookup($pw_Curve, 15)"),
                          make_item("dates", "$Now.Day + $Start.Year"),
                          make_item("parse", "$f_Const + * 2", context=CONTEXT),
                          make_item("eval", "1 / 0", context=CONTEXT))
    by_id = {r["id"]: r for r in results}
    assert_item_error(by_id["fn"], "$WhatIf.f_Const", FUNCTION_REFERENCE)
    assert_item_error(by_id["mv"], "$v_StorageA_Volume", MODELLED)
    assert_item_error(by_id["ts"], "$v_Demand", TIME_SERIES)
    assert by_id["lk"]["value"] == 12.5
    assert by_id["dates"]["value"] == 2 + 2000
    assert "error" in by_id["parse"] and "error" in by_id["eval"]
    assert stray_count(veneer.base_url) == 0
    # Release alone cleaned up: the batch's SweepStrays backstop had nothing to remove.
    time.sleep(0.2)  # let the log tee catch up
    assert SWEPT_WARNING not in log_text(veneer)


# Everything evaluate could conceivably write: every function's Value and each of its usage
# targets' current value, every variable's Value / SingleValue, each lookup table's interpolator
# (identity and size, on its own "L|" line), and the function manager's run time step (set only
# by FunctionManager.Reset). One line per entry.
STATE_SCRIPT = '''
import System
from System.Reflection import BindingFlags
from System.Runtime.CompilerServices import RuntimeHelpers
fm = scenario.Network.FunctionManager
out = []
def read(getter):
    try:
        return repr(getter())
    except Exception as e:
        return "ERR " + str(e)
for f in fm.Functions:
    out.append("F|%s|%s" % (f.FullName, read(lambda: f.Value)))
    for i in range(f.Usages.Count):
        u = f.Usages[i]
        out.append("U|%s|%d|%s" % (f.FullName, i, read(lambda: u.ReflectedItem.itemValue)))
for v in fm.Variables:
    parts = []
    for name in ("Value", "SingleValue"):
        try:
            p = v.GetType().GetProperty(name)
        except Exception:
            p = None
        if p is not None:
            parts.append("%s=%s" % (name, read(lambda: p.GetValue(v))))
    out.append("V|%s|%s" % (v.FullName, ";".join(parts)))
    if v.GetType().Name == "LinearVariable":
        out.append("L|%s|%s|%s" % (v.FullName, read(lambda: RuntimeHelpers.GetHashCode(v.LinearFunction)),
                                   read(lambda: v.LinearFunction.Count)))
rts = fm.GetType().GetProperty("RunTimeStep", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
out.append("RTS|%s" % read(lambda: rts.GetValue(fm)))
result = "\\n".join(out)
'''


def model_state(base_url):
    return run_script(base_url, STATE_SCRIPT).split("\n")


def assert_usage_target_untouched(state):
    """The once-per-run usage target still holds its own value: FunctionManager.Reset (never to
    be called) would have written 0 there."""
    usage_lines = [line for line in state if line.startswith(f"U|{USAGE_FUNCTION}|")]
    assert usage_lines, ("the model has no usage of " + USAGE_FUNCTION
                         + "; regenerate it with tests/fixtures/build_evaluate_model.py")
    assert any(float(line.split("|")[3]) == USAGE_VALUE for line in usage_lines), usage_lines


def run_state(base_url):
    runs = requests.get(f"{base_url}/runs", timeout=60)
    latest = requests.get(f"{base_url}/runs/latest", timeout=300)
    assert runs.status_code == 200 and latest.status_code == 200, (runs.text, latest.text)
    return runs.json(), latest.json()


def test_evaluate_changes_nothing_in_the_model(veneer):
    """Before/after a mixed batch (values and error items alike): nothing in the function
    manager, no usage target, no run and no run result has changed. In particular
    FunctionManager.Reset was not called: it would have written 0 into the usage target."""
    base_url = veneer.base_url
    before, runs_before = model_state(base_url), run_state(base_url)
    assert_usage_target_untouched(before)

    results = only_result(veneer,
                          make_item("lit", "1 + 1"),
                          make_item("dates", "$Now.Day + $Start.Year * 0 + $End.Day * 0"),
                          make_item("lk", "lookup($pw_Curve, 5)"),
                          make_item("bound", "$f_Const * 2 + $v_StorageA_Volume + $v_Demand",
                                    context=CONTEXT,
                                    bindings=[{"token": "$f_Const", "value": 1},
                                              {"token": "$v_StorageA_Volume", "value": 2},
                                              {"token": "$v_Demand", "value": 3}]),
                          make_item("fn", "$f_Const + 1", context=CONTEXT),
                          make_item("mv", "$v_StorageA_Volume + 1"),
                          make_item("ts", "$v_Demand + 1"),
                          make_item("parse", "1 + * 2"),
                          make_item("nan", "0 / 0"),
                          *builtin_items(base_url))
    by_id = {r["id"]: r for r in results}
    assert_builtin_results(by_id)
    assert by_id["lit"]["value"] == 2 and by_id["lk"]["value"] == 2.5
    assert by_id["bound"]["value"] == 7
    for key in ("fn", "mv", "ts", "parse", "nan"):
        assert "error" in by_id[key], by_id[key]

    after, runs_after = model_state(base_url), run_state(base_url)
    changed = sorted(set(before) ^ set(after))
    assert before == after, "model state changed:\n" + "\n".join(changed)
    assert runs_before == runs_after


def input_set_name(base_url) -> str:
    input_sets = requests.get(f"{base_url}/inputSets", timeout=60).json()
    assert len(input_sets) == 1, input_sets  # the generator makes exactly one
    return input_sets[0]["Name"]


# The pattern variable (month m -> 10 * m) and $ActiveInputSet (the model's one input set is
# selected: index 0).
def builtin_items(base_url):
    name = input_set_name(base_url)
    return [
        make_item("pat_leaf", "$pat_Season", "2001-07-01", context="$WhatIf.f_Pattern"),
        make_item("pat_full", "$WhatIf.pat_Season + 1", "2001-03-15"),
        make_item("pat_bare", "$pat_Season", "2001-07-01"),
        make_item("ais", "$ActiveInputSet + 1"),
        make_item("ais_is", f'$ActiveInputSet == "{name}"'),
        make_item("ais_if", f'IF($ActiveInputSet == "{name}", 5, 6)'),
    ]


def assert_builtin_results(by_id):
    assert by_id["pat_leaf"]["value"] == 70, by_id["pat_leaf"]
    assert by_id["pat_full"]["value"] == 31, by_id["pat_full"]
    # Without a context the leaf name does not resolve: Source's own parse error.
    assert_item_error(by_id["pat_bare"])
    assert "PAT_SEASON" in by_id["pat_bare"]["error"].upper()
    assert by_id["ais"]["value"] == 1, by_id["ais"]
    assert by_id["ais_is"]["value"] == 1, by_id["ais_is"]
    assert by_id["ais_if"]["value"] == 5, by_id["ais_if"]


def test_pattern_and_active_input_set(veneer):
    results = only_result(veneer, *builtin_items(veneer.base_url))
    assert_builtin_results({r["id"]: r for r in results})


def test_cancelled_request_leaves_no_strays(veneer):
    base_url = veneer.base_url
    # Every item is an error now (unbound references), but each is still parsed and registered
    # before the guard refuses it, so the release path is exercised exactly as before.
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


# ------------------------------------------- a scenario loaded but never run (VENEER_COLD=1)
# The generated model's run configuration covers 2000-01-01 .. 2000-01-31.

COLD_BATCH = [
    make_item("now", "$Now.Month*100+$Now.Day", "2001-07-01"),
    make_item("start", "$Start.Year*10000+$Start.Month*100+$Start.Day"),
    make_item("end", "$End.Day"),
    make_item("lookup", "lookup($pw_Curve, 5)", context="$WhatIf.f_Lookup"),
    make_item("mv", "$v_StorageA_Volume + 1"),
    make_item("ts", "$v_Demand + 1"),
    make_item("fn", "$f_Const + 1", context=CONTEXT),
    make_item("mv_bound", "$v_StorageA_Volume + 1",
              bindings=[{"token": "$v_StorageA_Volume", "value": 4}]),
    make_item("ts_bound", "$v_Demand + 1", bindings=[{"token": "$v_Demand", "value": 2}]),
    make_item("fn_bound", "$f_Const + 1", context=CONTEXT,
              bindings=[{"token": "$f_Const", "value": 2.5}]),
]

# Per-run state that evaluate must leave alone on a never-run scenario: the built-in date
# variables' recorded values (still 0) and the function manager's run time step (still unset:
# FunctionManager.Reset was never called); and the lookup table evaluate built on demand.
COLD_STATE_SCRIPT = '''
from System.Reflection import BindingFlags
fm = scenario.Network.FunctionManager
dates = [v for v in fm.Variables if v.GetType().Name in ("NowVariable", "FixedDateVariable")]
curve = [v for v in fm.Variables if v.FullName == "$pw_Curve"][0]
rts = fm.GetType().GetProperty("RunTimeStep", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
result = "%d|%r|%s|%d" % (len(dates), max(abs(v.Value) for v in dates),
                          rts.GetValue(fm) is None, curve.LinearFunction.Count)
'''


def test_cold_now_honours_the_items_date(veneer_cold):
    jul, mar = only_result(veneer_cold,
                           make_item("jul", "$Now.Month*100+$Now.Day", "2001-07-01"),
                           make_item("mar", "$Now.Month*100+$Now.Day", "2001-03-15"))
    assert jul == {"id": "jul", "value": 701}
    assert mar == {"id": "mar", "value": 315}


def test_cold_batch(veneer_cold):
    batch = COLD_BATCH + builtin_items(veneer_cold.base_url)
    first = only_result(veneer_cold, *batch)
    by_id = {r["id"]: r for r in first}
    assert_builtin_results(by_id)
    assert by_id["now"] == {"id": "now", "value": 701}
    assert by_id["start"] == {"id": "start", "value": 20000101}
    assert by_id["end"] == {"id": "end", "value": 31}
    assert by_id["lookup"] == {"id": "lookup", "value": 2.5}
    assert_item_error(by_id["mv"], "$v_StorageA_Volume", MODELLED)
    assert_item_error(by_id["ts"], "$v_Demand", TIME_SERIES)
    assert_item_error(by_id["fn"], "$WhatIf.f_Const", FUNCTION_REFERENCE)
    assert by_id["mv_bound"] == {"id": "mv_bound", "value": 5}
    assert by_id["ts_bound"] == {"id": "ts_bound", "value": 3}
    assert by_id["fn_bound"] == {"id": "fn_bound", "value": 3.5}
    # The lookup table is now built; the same batch answers identically.
    assert only_result(veneer_cold, *batch) == first
    assert stray_count(veneer_cold.base_url) == 0


def test_cold_evaluate_leaves_the_per_run_state_alone(veneer_cold):
    results = only_result(veneer_cold,
                          make_item("dates", "$Now.Day + $Start.Year + $End.Day"),
                          make_item("lookup", "lookup($pw_Curve, 5)"))
    assert [r.get("value") for r in results] == [2 + 2000 + 31, 2.5]
    count, max_value, unset, curve_points = run_script(veneer_cold.base_url,
                                                       COLD_STATE_SCRIPT).split("|")
    assert int(count) == 18  # Now, Start and End x Day, Month, Year, Hour, DayOfYear, DaysInMonth
    assert float(max_value) == 0, "a built-in date variable recorded a value"
    assert unset == "True", "FunctionManager.Reset has run"
    assert int(curve_points) > 0, "the lookup table was not built"


def test_cold_evaluate_changes_nothing_else_in_the_model(veneer_cold):
    """The warm no-mutation check on a never-run scenario, less the lookup tables (the one
    thing evaluate may build there) and the run results (there are none)."""
    base_url = veneer_cold.base_url

    def state():
        return [line for line in model_state(base_url) if not line.startswith("L|")]

    def runs():
        r = requests.get(f"{base_url}/runs", timeout=60)
        assert r.status_code == 200, r.text
        return r.json()

    before, runs_before = state(), runs()
    assert_usage_target_untouched(before)
    results = only_result(veneer_cold, *COLD_BATCH, *builtin_items(base_url),
                          make_item("parse", "1 + * 2"), make_item("nan", "0 / 0"))
    assert any("value" in r for r in results) and any("error" in r for r in results)
    after = state()
    changed = sorted(set(before) ^ set(after))
    assert before == after, "model state changed:\n" + "\n".join(changed)
    assert runs() == runs_before
