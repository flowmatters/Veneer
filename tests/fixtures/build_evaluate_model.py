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

NODES = [("Inflow A", "inflow"), ("StorageA", "storage"), ("Gauge 1", "gauge")]
LINKS = [("Inflow A", "StorageA", "Link A"), ("StorageA", "Gauge 1", "Link B")]
CURVE = pd.DataFrame({"Lookup": [0.0, 10.0, 20.0], "Result": [0.0, 5.0, 20.0]})
FUNCTIONS = [
    ("$f_Const", "10"),
    ("$f_Release", "IF($Now.Day = 1, $v_StorageA_Volume * 0.5, $f_Const + 1)"),
    ("$f_Lagged", "$f_Lagged + 1"),
    ("$f_Lookup", "lookup($pw_Curve, $f_Const)"),
    ("$f_Demand2", "$v_Demand * 2"),
]
# The usage target as the function manager sees it: "<usage count>|<first target's value>".
USAGE_SCRIPT = '''
fm = scenario.Network.FunctionManager
f = [x for x in fm.Functions if x.FullName == "%s"][0]
result = "%%d|%%r" %% (f.Usages.Count, f.Usages[0].ReflectedItem.itemValue if f.Usages.Count else None)
''' % USAGE_FUNCTION
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
    storages = v.model.node.storages
    storages.set_param_values(USAGE_PARAMETER, [USAGE_VALUE], nodes=[USAGE_NODE])
    value = storages.get_param_values(USAGE_PARAMETER, nodes=[USAGE_NODE])
    require(value == [USAGE_VALUE], f"setting {USAGE_NODE}.{USAGE_PARAMETER}", value)
    applied = storages.apply_function(USAGE_PARAMETER, USAGE_FUNCTION, nodes=[USAGE_NODE])
    usage = run_script(base_url, USAGE_SCRIPT)
    require(usage == f"1|{USAGE_VALUE!r}", f"applying {USAGE_FUNCTION} to {USAGE_PARAMETER}",
            (usage, applied))

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
        try:
            v.run_model(start="01/01/2000", end="31/01/2000")
        except Exception as e:
            print("run failed (continuing):", e, file=sys.stderr)
        # The run restores the usage target at its end; confirm before saving.
        usage = run_script(base_url, USAGE_SCRIPT)
        require(usage == f"1|{USAGE_VALUE!r}", "the usage target after the run", usage)
        v.model.save(out)
        print("saved", out)
    finally:
        manage.kill_all_now(procs)


if __name__ == "__main__":
    main()
