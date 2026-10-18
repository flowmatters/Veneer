"""Generate the synthetic evaluate test model. Run once; the operator reviews the saved
.rsproj in Source before it is committed. Nothing here derives from any real project.

    VENEER_CMD_EXE=<path to FlowMatters.Source.VeneerCmd.exe> VENEER_TEST_PORT=9890 \
        python tests/fixtures/build_evaluate_model.py

Set VENEER_SOURCE_DIR to the Source binaries folder when VeneerCmd cannot find Source on its
own (it is passed as VeneerCmd's -d option).
"""
import os, socket, sys
import pandas as pd
import requests
import veneer
from veneer import manage

PORT = int(os.environ.get("VENEER_TEST_PORT", "9890"))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "evaluate-model.rsproj")


def port_in_use(port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(1.0)
        return s.connect_ex(("127.0.0.1", port)) == 0


def veneer_cmd() -> str:
    """The VeneerCmd command for manage.start, with -d when VENEER_SOURCE_DIR is set
    (manage.start has no option for it, but places veneer_exe verbatim at the head of the
    command line)."""
    exe = f'"{os.environ["VENEER_CMD_EXE"]}"'
    source_dir = os.environ.get("VENEER_SOURCE_DIR")
    return f'{exe} -d "{source_dir}"' if source_dir else exe


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


def main() -> None:
    reference_veneer_assembly_in_scripts()
    if port_in_use(PORT):
        sys.exit(f"port {PORT} is already in use; refusing to start VeneerCmd there")
    # No project file: VeneerCmd creates an empty project and scenario.
    procs, ports, _logs = manage.start(project_fn=None, ports=PORT, script=True,
                                       veneer_exe=veneer_cmd(), quiet=True,
                                       return_log_paths=True)
    try:
        if ports[0] != PORT:
            raise RuntimeError(f"VeneerCmd bound port {ports[0]}, not {PORT}")
        v = veneer.Veneer(port=ports[0])
        v.model.node.create("Inflow A", veneer.server_side.NODE_TYPES["inflow"])
        v.model.node.create("StorageA", veneer.server_side.NODE_TYPES["storage"])
        v.model.node.create("Gauge 1", veneer.server_side.NODE_TYPES["gauge"])
        v.model.link.create("Inflow A", "StorageA", name="Link A")
        v.model.link.create("StorageA", "Gauge 1", name="Link B")
        v.model.node.storages.create_modelled_variable(
            "Storage Volume", variable_name="v_StorageA_Volume", nodes=["StorageA"])
        ts = pd.DataFrame({"demand": [1.0 + i % 3 for i in range(31)]},
                          index=pd.date_range("2000-01-01", periods=31, freq="D"))
        v.create_data_source("WhatIfDemand", data=ts, units="ML/d")
        # veneer-py does not add the '$' here, and a variable named without it cannot be referenced.
        v.model.functions.create_time_series_variables(["$v_Demand"], "WhatIfDemand", ["demand"])
        v.model.functions.create_piecewise_variable("pw_Curve",
            table=pd.DataFrame({"Lookup": [0.0, 10.0, 20.0], "Result": [0.0, 5.0, 20.0]}))
        for name, expression in [
                ("$f_Const", "10"),
                ("$f_Release", "IF($Now.Day = 1, $v_StorageA_Volume * 0.5, $f_Const + 1)"),
                ("$f_Lagged", "$f_Lagged + 1"),
                ("$f_Lookup", "lookup($pw_Curve, $f_Const)"),
                ("$f_Demand2", "$v_Demand * 2")]:
            created = v.model.functions.create_functions([name], expression, params=[()],
                                                         function_path="WhatIf")
            if not isinstance(created, dict) or created.get("failed"):
                raise RuntimeError(f"could not create {name}: {created}")
        # A new project's run period defaults to the month before today; set it to the
        # period the time series covers so the saved model runs as-is.
        v.model._safe_run(
            "import System\n"
            "scenario.CurrentConfiguration.StartDate = System.DateTime(2000, 1, 1)\n"
            "scenario.CurrentConfiguration.EndDate = System.DateTime(2000, 1, 31)\n")
        requests.put(f"http://localhost:{ports[0]}/recorders", json={"RecordNone": [], "RecordAll": [
            "location/Functions/element/Functions/variable/Functions"]}).raise_for_status()
        try:
            v.run_model(start="01/01/2000", end="31/01/2000")
        except Exception as e:
            print("run failed (continuing):", e, file=sys.stderr)
        v.model.save(OUT)
        print("saved", OUT)
    finally:
        manage.kill_all_now(procs)


if __name__ == "__main__":
    main()
