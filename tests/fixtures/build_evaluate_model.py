"""Generate the synthetic evaluate test model. Run once; the operator reviews the saved
.rsproj in Source before it is committed. Nothing here derives from any real project.

    VENEER_CMD_EXE=<path to FlowMatters.Source.VeneerCmd.exe> VENEER_TEST_PORT=9890 \
        python tests/fixtures/build_evaluate_model.py [output.rsproj]

The output defaults to tests/fixtures/evaluate-model.rsproj. Set VENEER_SOURCE_DIR to the
Source binaries folder when VeneerCmd cannot find Source on its own (it is passed as
VeneerCmd's -d option).
"""
import os
import sys

import pandas as pd
import requests
import veneer
from veneer import manage

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
from tests.live import (USAGE_FUNCTION, USAGE_NODE, USAGE_PARAMETER,  # noqa: E402  (needs the path above)
                        USAGE_VALUE, port_in_use, run_script, veneer_cmd)

PORT = int(os.environ.get("VENEER_TEST_PORT", "9890"))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "evaluate-model.rsproj")

# Inflow A -> StorageA -> Gauge 1 -> Inflow B -> Inflow C -> Inflow D. The three downstream
# inflow nodes exist only to give functions per-timestep usages (see USAGES).
NODES = [("Inflow A", "inflow"), ("StorageA", "storage"), ("Gauge 1", "gauge"),
         ("Inflow B", "inflow"), ("Inflow C", "inflow"), ("Inflow D", "inflow")]
LINKS = [("Inflow A", "StorageA", "Link A"), ("StorageA", "Gauge 1", "Link B"),
         ("Gauge 1", "Inflow B", "Link C"), ("Inflow B", "Inflow C", "Link D"),
         ("Inflow C", "Inflow D", "Link E")]
CURVE = pd.DataFrame({"Lookup": [0.0, 10.0, 20.0], "Result": [0.0, 5.0, 20.0]})
FUNCTIONS = [
    ("$f_Const", "10"),
    ("$f_Release", "IF($Now.Day = 1, $v_StorageA_Volume * 0.5, $f_Const + 1)"),
    ("$f_Lagged", "$f_Lagged + 1"),
    ("$f_Lookup", "lookup($pw_Curve, $f_Const)"),
    ("$f_Demand2", "$v_Demand * 2"),
]
# Source evaluates a function during a run only when something uses it, so every function drives a
# model input (node, parameter); otherwise its recorded series is all zeros. These are per-timestep
# inflows, so each function's result unit is a flow (Source refuses a run whose usages have
# non-commensurate units). f_Const drives USAGE_NODE's USAGE_PARAMETER instead (see tests/live.py),
# a volume set once per run; it is also evaluated at the start of every time step, so it too has a
# recorded series. (A second usage, per-timestep, would need a per-timestep VOLUME input: one
# function has one result unit.)
USAGES = [
    ("$WhatIf.f_Demand2", "Inflow A", "Flow"),
    ("$WhatIf.f_Release", "Inflow B", "Flow"),
    ("$WhatIf.f_Lagged", "Inflow C", "Flow"),
    ("$WhatIf.f_Lookup", "Inflow D", "Flow"),
]
# What each function's recorded series must look like after the run: "varies", or the constant.
EXPECTED_SERIES = {"$WhatIf.f_Const": 10.0, "$WhatIf.f_Lookup": 5.0, "$WhatIf.f_Release": "varies",
                   "$WhatIf.f_Lagged": "varies", "$WhatIf.f_Demand2": "varies"}
# Every function usage: one "<function>|<target type>|<member>|<target's current value>" line each.
USAGES_SCRIPT = '''
fm = scenario.Network.FunctionManager
out = []
for f in fm.Functions:
    for u in f.Usages:
        ri = u.ReflectedItem
        member = getattr(ri.Member, "Name", None) or str(ri.Member)
        out.append("%s|%s|%s|%r" % (f.FullName, ri.PermanentTarget.GetType().Name, member, ri.itemValue))
result = "\\n".join(sorted(out))
'''
VARIABLE_TYPES = {"$v_StorageA_Volume": "ModelledVariable", "$v_Demand": "TimeSeriesVariable",
                  "$pw_Curve": "LinearVariable"}


def require(ok, what, got) -> None:
    if not ok:
        raise RuntimeError(f"{what} failed: {got!r}")


def script_value(result):
    """The Value of a veneer-py run_script result, or None."""
    response = result.get("Response") if isinstance(result, dict) else None
    return response.get("Value") if isinstance(response, dict) else None


def reference_veneer_assembly_in_scripts() -> None:
    """veneer-py's model-building scripts import FlowMatters.Source.Veneer...ScriptHelpers and
    System.Text.RegularExpressions. The script engine only preloads assemblies Source's
    AssemblyManager knows about, and a VeneerCmd run beside (not inside) the Source folder
    loads Veneer outside it, so add both references explicitly. Harmless where the assemblies
    are already visible."""
    original = veneer.server_side.VeneerIronPython._init_script
    references = ("import clr\n"
                  "clr.AddReference('FlowMatters.Source.Veneer')\n"
                  "clr.AddReference('System.Text.RegularExpressions')\n")

    def init_script(self, namespace=None):
        return references + original(self, namespace)

    veneer.server_side.VeneerIronPython._init_script = init_script


def build(v, base_url) -> None:
    for name, kind in NODES:
        created = v.model.node.create(name, veneer.server_side.NODE_TYPES[kind])
        require(script_value(created) == name, f"creating node {name}", created)
    for from_node, to_node, name in LINKS:
        created = v.model.link.create(from_node, to_node, name=name)
        require(script_value(created) == name, f"creating link {name}", created)

    created = v.model.node.storages.create_modelled_variable(
        "Storage Volume", variable_name="v_StorageA_Volume", nodes=["StorageA"])
    require(isinstance(created, dict) and created.get("created") == ["$v_StorageA_Volume"]
            and not created.get("failed"), "creating $v_StorageA_Volume", created)

    ts = pd.DataFrame({"demand": [1.0 + i % 3 for i in range(31)]},
                      index=pd.date_range("2000-01-01", periods=31, freq="D"))
    created = v.create_data_source("WhatIfDemand", data=ts, units="ML/d")
    require(isinstance(created, tuple) and created[0] == 200,
            "creating data source WhatIfDemand", created)
    # veneer-py does not add the '$' here, and a variable named without it cannot be
    # referenced.
    created = v.model.functions.create_time_series_variables(
        ["$v_Demand"], "WhatIfDemand", ["demand"])
    require(isinstance(created, dict) and created.get("created") == ["$v_Demand"],
            "creating $v_Demand", created)
    v.model.functions.create_piecewise_variable("pw_Curve", table=CURVE)  # returns None
    curve = requests.get(f"{base_url}/variables/pw_Curve/Piecewise", timeout=60).json()
    require(curve.get("Entries") == CURVE.values.tolist(), "filling $pw_Curve", curve)

    for name, expression in FUNCTIONS:
        created = v.model.functions.create_functions([name], expression, params=[()],
                                                     function_path="WhatIf")
        require(isinstance(created, dict) and not created.get("failed"),
                f"creating {name}", created)

    # One function usage on a numeric model parameter holding a distinctive value, so that the
    # no-mutation test would see a write to a usage target (see tests/live.py).
    # A usage on a once-per-run parameter needs the function evaluated at 'Run Initialisation'
    # (Source refuses the run otherwise); keep 'Start of TimeStep' so it is evaluated every step.
    v.model.functions.set_options(
        "EvaluationTimes", ["TimeOfEvaluation.InitialiseRun | TimeOfEvaluation.StartOfTimeStep"],
        fromList=True, functions=[USAGE_FUNCTION])
    times = v.model.functions.get_options("EvaluationTimes", functions=[USAGE_FUNCTION])
    require(len(times) == 1 and "InitialiseRun" in str(times[0])
            and "StartOfTimeStep" in str(times[0]), f"{USAGE_FUNCTION}'s evaluation times", times)
    set_result_unit(v, USAGE_FUNCTION, "cubicMetres")
    storages = v.model.node.storages
    storages.set_param_values(USAGE_PARAMETER, USAGE_VALUE, nodes=[USAGE_NODE])
    value = storages.get_param_values(USAGE_PARAMETER, nodes=[USAGE_NODE])
    require(value == [USAGE_VALUE], f"setting {USAGE_NODE}.{USAGE_PARAMETER}", value)
    applied = storages.apply_function(USAGE_PARAMETER, USAGE_FUNCTION, nodes=[USAGE_NODE])
    require(isinstance(applied, dict) and applied.get("success") == 1,
            f"applying {USAGE_FUNCTION} to {USAGE_NODE}.{USAGE_PARAMETER}", applied)
    for function, node, parameter in USAGES:
        set_result_unit(v, function, "cubicMetresPerSecond")
        applied = v.model.node.apply_function(parameter, function, nodes=[node])
        require(isinstance(applied, dict) and applied.get("success") == 1,
                f"applying {function} to {node}.{parameter}", applied)
    check_usages(base_url, "after applying them")

    # create_modelled_variable reports "created" even for a name it skipped, so confirm the
    # final state independently.
    variables = {x["FullName"]: x.get("VariableType")
                 for x in requests.get(f"{base_url}/variables", timeout=60).json()}
    for name, kind in VARIABLE_TYPES.items():
        require(variables.get(name) == kind, f"checking {name}", variables.get(name))
    functions = {f["FullName"]: f["Expression"]
                 for f in requests.get(f"{base_url}/functions", timeout=60).json()}
    for name, expression in FUNCTIONS:
        full_name = "$WhatIf." + name[1:]
        require(functions.get(full_name) == expression, f"checking {full_name}",
                functions.get(full_name))


def set_result_unit(v, function, unit) -> None:
    v.model.functions.set_options("ResultUnit", [unit], functions=[function])
    got = v.model.functions.get_options("ResultUnit", functions=[function])
    require(len(got) == 1 and got[0] is not None, f"{function}'s result unit", got)


def check_usages(base_url, when) -> None:
    usages = run_script(base_url, USAGES_SCRIPT).split("\n")
    print(f"function usages ({when}):", *usages, sep="\n  ")
    require(len(usages) == len(USAGES) + 1, f"counting the function usages {when}", usages)
    target = [u for u in usages if u.startswith(USAGE_FUNCTION + "|")
              and u.split("|")[2] == USAGE_PARAMETER]
    require(len(target) == 1 and float(target[0].split("|")[3]) == USAGE_VALUE,
            f"{USAGE_NODE}.{USAGE_PARAMETER} {when}", target)


def check_recorded_series(base_url) -> None:
    """Every function was evaluated during the run: its recorded series is what EXPECTED_SERIES
    says, never all zeros."""
    latest = requests.get(f"{base_url}/runs/latest", timeout=300)
    latest.raise_for_status()
    summary = latest.json()
    require(summary.get("Status") != "RunIncomplete", "the run",
            (summary.get("Status"), summary.get("RunLog"), summary.get("LastStackTrace")))
    results = summary["Results"]
    for name, expected in EXPECTED_SERIES.items():
        # Recorded as "Functions@<folder>@$<leaf>".
        folder, leaf = name[1:].split(".")
        variable = f"Functions@{folder}@${leaf}"
        matches = [r for r in results if r.get("RecordingVariable") == variable]
        require(len(matches) == 1, f"finding the recorded series of {name}",
                [(r.get("NetworkElement"), r.get("RecordingElement"), r.get("RecordingVariable"))
                 for r in results])
        series = requests.get(base_url + matches[0]["TimeSeriesUrl"], timeout=300)
        series.raise_for_status()
        values = [e["Value"] for e in series.json()["Events"]]
        distinct = sorted(set(values))
        print(f"recorded {name}: {len(values)} values, {len(distinct)} distinct, "
              f"min {min(values)}, max {max(values)}, first {values[:4]}")
        require(len(values) == 31, f"the length of {name}'s series", len(values))
        if expected == "varies":
            require(len(distinct) > 1, f"{name}'s series varies", distinct)
        else:
            require(distinct == [expected], f"{name}'s series is {expected}", distinct)


def main() -> None:
    out = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else OUT
    reference_veneer_assembly_in_scripts()
    if port_in_use(PORT):
        sys.exit(f"port {PORT} is already in use; refusing to start VeneerCmd there")
    # No project file: VeneerCmd creates an empty project and scenario.
    procs, ports, _logs = manage.start(project_fn=None, ports=PORT, script=True,
                                       veneer_exe=veneer_cmd(os.environ["VENEER_CMD_EXE"]),
                                       quiet=True, return_log_paths=True)
    try:
        print("started VeneerCmd pid(s):", [p.pid for p in procs])
        if ports[0] != PORT:
            raise RuntimeError(f"VeneerCmd bound port {ports[0]}, not {PORT}")
        base_url = f"http://localhost:{ports[0]}"
        v = veneer.Veneer(port=ports[0])
        build(v, base_url)
        # A new project's run period defaults to the month before today; set it to the
        # period the time series covers so the saved model runs as-is.
        v.model._safe_run(
            "import System\n"
            "scenario.CurrentConfiguration.StartDate = System.DateTime(2000, 1, 1)\n"
            "scenario.CurrentConfiguration.EndDate = System.DateTime(2000, 1, 31)\n")
        requests.put(f"{base_url}/recorders", timeout=60, json={
            "RecordNone": [],
            "RecordAll": ["location/Functions/element/Functions/variable/Functions"],
        }).raise_for_status()
        run = requests.post(f"{base_url}/runs", timeout=600,
                            json={"StartDate": "01/01/2000", "EndDate": "31/01/2000"})
        require(run.status_code == 200, "the run", (run.status_code, run.text))
        check_recorded_series(base_url)
        # The run restores every usage target at its end; confirm before saving.
        check_usages(base_url, "after the run")
        v.model.save(out)
        print("saved", out)
    finally:
        manage.kill_all_now(procs)
        print("killed VeneerCmd pid(s):", [p.pid for p in procs])


if __name__ == "__main__":
    main()
