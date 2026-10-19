"""Generate the synthetic recorder-sets test model. Run once; the operator reviews the saved
.rsproj in Source before it is committed. Nothing here derives from any real project.

    VENEER_CMD_EXE=<path to FlowMatters.Source.VeneerCmd.exe> VENEER_TEST_PORT=9892 \
        python tests/fixtures/build_recorder_sets_model.py [output.rsproj]

The output defaults to tests/fixtures/recorder-sets-model.rsproj. Set VENEER_SOURCE_DIR to the
Source binaries folder when VeneerCmd cannot find Source on its own (it is passed as
VeneerCmd's -d option). VeneerCmd is started with -x, so no registered plugins load.

The model: Inflow X -> Inflow Y -> Gauge Z. The named recorder set SET_X records item X (Inflow
X's downstream flow) and is the only set selected; the Current Recorder Tree records item Y
(Inflow Y's downstream flow). A run with the saved selection therefore records X only: the
selected named set overrides the tree.

After saving, the script reopens the saved model in a second VeneerCmd and checks it, then
checks GET /recorderSets and the SelectedRecorderSets run parameter against it (protocol
20261019 or later). Those runs are never saved.
"""
import os
import shutil
import sys
import tempfile

import requests
import veneer
from veneer import manage

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
from tests.live import port_in_use, run_script, veneer_cmd  # noqa: E402  (needs the path above)

PORT = int(os.environ.get("VENEER_TEST_PORT", "9892"))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "recorder-sets-model.rsproj")

NODES = [("Inflow X", "inflow"), ("Inflow Y", "inflow"), ("Gauge Z", "gauge")]
LINKS = [("Inflow X", "Inflow Y", "Link XY"), ("Inflow Y", "Gauge Z", "Link YZ")]
FLOWS = {"Inflow X": 5.0, "Inflow Y": 7.0}

TREE = "Current Recorder Tree"
SET_X = "SetX"
ITEM_X = "Inflow X"
ITEM_Y = "Inflow Y"


def recorder_path(node):
    return f"location/{node}/element/Downstream Flow/variable/Downstream Flow"


CLEAR_TREE_SCRIPT = '''
import clr
clr.AddReference('TIME')
from TIME.ScenarioManagement import ProjectViewRow, RecordingStates
rows = scenario.Project.ProjectViewTable.SelectTable(ProjectViewRow.RecorderFields.Scenario, scenario)
rows.SetRecorderState(RecordingStates.RecordNone)
result = 'cleared'
'''

# Every recordable item in the Current Recorder Tree whose state is not RecordNone, one
# "<network element>|<element>|<state>" line each.
TREE_SCRIPT = '''
import clr
clr.AddReference('TIME')
from TIME.ScenarioManagement import ProjectViewRow
rows = scenario.Project.ProjectViewTable.SelectTable(ProjectViewRow.RecorderFields.Scenario, scenario)
out = []
for row in rows:
    if row is None or row.ElementRecorder is None: continue
    for item in row.ElementRecorder.RecordableItems:
        if str(item.RecordingStatus) != 'RecordNone':
            out.append('%s|%s|%s' % (row.NetworkElementName, row.ElementName, item.RecordingStatus))
result = '\\n'.join(sorted(out))
'''

# Creates SET_X from the tree's recorder strings while the tree records item X alone.
CREATE_SET_SCRIPT = '''
import System
from RiverSystem.ScenarioSets.RecorderSets import RecorderSetCreator, RecorderSet, RecorderSetManager
line = RecorderSetCreator.GenerateRecorderStrings(scenario).strip()
mgr = scenario.GetPluginModel[RecorderSetManager]()
rs = RecorderSet('%s')
rs.Configuration.Instructions = line
mgr.Sets.Add(rs)
parsed = rs.Parse(scenario, System.Array[str]([line]))
result = '%%s\\n%%s' %% (line, ','.join(str(getattr(p, 'Status', '')) for p in parsed))
''' % SET_X

# Selects SET_X alone, in place.
SELECT_SET_SCRIPT = '''
from RiverSystem.ScenarioSets.RecorderSets import RecorderSetManager
mgr = scenario.GetPluginModel[RecorderSetManager]()
selected = scenario.CurrentConfiguration.SelectedRecorderSets
selected.Clear()
selected.Add([s for s in mgr.Sets if s.Name == '%s'][0])
result = ','.join(s.Name for s in selected)
''' % SET_X


def require(ok, what, got) -> None:
    if not ok:
        raise RuntimeError(f"{what} failed: {got!r}")


def script_value(result):
    """The Value of a veneer-py run_script result, or None."""
    response = result.get("Response") if isinstance(result, dict) else None
    return response.get("Value") if isinstance(response, dict) else None


def reference_veneer_assembly_in_scripts() -> None:
    """As in build_evaluate_model.py: veneer-py's model-building scripts need these references
    when VeneerCmd runs beside (not inside) the Source folder."""
    original = veneer.server_side.VeneerIronPython._init_script
    references = ("import clr\n"
                  "clr.AddReference('FlowMatters.Source.Veneer')\n"
                  "clr.AddReference('System.Text.RegularExpressions')\n")

    def init_script(self, namespace=None):
        return references + original(self, namespace)

    veneer.server_side.VeneerIronPython._init_script = init_script


def tree(base_url):
    value = run_script(base_url, TREE_SCRIPT)
    return [line for line in value.split("\n") if line]


def put_recorders(base_url, record_all) -> None:
    requests.put(f"{base_url}/recorders", timeout=60,
                 json={"RecordNone": [], "RecordAll": record_all}).raise_for_status()


def build(v, base_url) -> None:
    for name, kind in NODES:
        created = v.model.node.create(name, veneer.server_side.NODE_TYPES[kind])
        require(script_value(created) == name, f"creating node {name}", created)
    for from_node, to_node, name in LINKS:
        created = v.model.link.create(from_node, to_node, name=name)
        require(script_value(created) == name, f"creating link {name}", created)
    for node, flow in FLOWS.items():
        v.model.node.set_param_values("Flow", flow, nodes=[node])
    flows = v.model.node.get_param_values("Flow", nodes=list(FLOWS))
    require(flows == list(FLOWS.values()), "setting the inflows", flows)

    # A new project's run period defaults to the month before today.
    v.model._safe_run(
        "import System\n"
        "scenario.CurrentConfiguration.StartDate = System.DateTime(2000, 1, 1)\n"
        "scenario.CurrentConfiguration.EndDate = System.DateTime(2000, 1, 10)\n")

    # SET_X: generated from the tree while the tree records item X alone.
    run_script(base_url, CLEAR_TREE_SCRIPT)
    put_recorders(base_url, [recorder_path(ITEM_X)])
    on = tree(base_url)
    require(len(on) == 1 and on[0].startswith(ITEM_X + "|"), "the tree recording X alone", on)
    line, status = run_script(base_url, CREATE_SET_SCRIPT).split("\n")
    print(f"{SET_X} instructions: {line}")
    require(line.count("\n") == 0 and ITEM_X in line, f"{SET_X}'s instructions", line)
    require(status == "Complete", f"parsing {SET_X}'s instructions", status)

    # The tree: item Y alone. The selection: SET_X alone.
    run_script(base_url, CLEAR_TREE_SCRIPT)
    selected = run_script(base_url, SELECT_SET_SCRIPT)
    require(selected == SET_X, f"selecting {SET_X}", selected)
    put_recorders(base_url, [recorder_path(ITEM_Y)])
    on = tree(base_url)
    require(len(on) == 1 and on[0].startswith(ITEM_Y + "|"), "the tree recording Y alone", on)


def start(project_fn, exe):
    if port_in_use(PORT):
        sys.exit(f"port {PORT} is already in use; refusing to start VeneerCmd there")
    procs, ports, _logs = manage.start(project_fn=project_fn, ports=PORT, script=True,
                                       veneer_exe=veneer_cmd(exe) + " -x",
                                       quiet=True, return_log_paths=True)
    print("started VeneerCmd pid(s):", [p.pid for p in procs])
    if ports[0] != PORT:
        manage.kill_all_now(procs)
        raise RuntimeError(f"VeneerCmd bound port {ports[0]}, not {PORT}")
    return procs


def stop(procs) -> None:
    manage.kill_all_now(procs)
    print("killed VeneerCmd pid(s):", [p.pid for p in procs])


def recorder_sets(base_url):
    r = requests.get(f"{base_url}/recorderSets", timeout=60)
    require(r.status_code == 200, "GET /recorderSets", (r.status_code, r.text))
    return r.json()


def run(base_url, body):
    """POST /runs without following the redirect: (status, recorded network elements or the
    response text)."""
    r = requests.post(f"{base_url}/runs", json=body, timeout=600, allow_redirects=False)
    if r.status_code != 302:
        return r.status_code, r.text
    summary = requests.get(f"{base_url}/{r.headers['Location'].lstrip('/')}", timeout=300)
    summary.raise_for_status()
    summary = summary.json()
    require(summary.get("Status") == "RunSuccess", "the run", (summary.get("Status"), summary.get("RunLog")))
    return 302, sorted({result["NetworkElement"] for result in summary["Results"]})


def check(base_url) -> None:
    """The saved model as reopened, then the recorder-set behaviour it exists to reproduce."""
    features = requests.get(f"{base_url}/network", timeout=60).json()["features"]
    nodes = sorted(f["properties"]["name"] for f in features if f["properties"].get("feature_type") == "node")
    links = sorted(f["properties"]["name"] for f in features if f["properties"].get("feature_type") == "link")
    scenario = requests.get(f"{base_url}/", timeout=60).json().get("Scenario")
    print("scenario:", scenario)
    print("nodes:", nodes)
    print("links:", links)
    require(nodes == sorted(n for n, _ in NODES), "the nodes", nodes)
    require(links == sorted(n for _, _, n in LINKS), "the links", links)
    on = tree(base_url)
    print("Current Recorder Tree records:", on)
    require(len(on) == 1 and on[0].startswith(ITEM_Y + "|"), "the tree recording Y alone", on)

    saved = recorder_sets(base_url)
    print("GET /recorderSets:", saved)
    require(saved == {"Sets": [TREE, SET_X], "Selected": [SET_X]}, "the saved recorder sets", saved)

    cases = [
        ({}, (302, [ITEM_X])),                                          # the named set overrides
        ({"SelectedRecorderSets": []}, (302, [ITEM_Y])),                # replace: the tree alone
        ({"SelectedRecorderSets": [TREE, SET_X]}, (302, [ITEM_X, ITEM_Y])),  # add: the union
        ({"SelectedRecorderSets": [SET_X]}, (302, [ITEM_X])),
    ]
    for body, expected in cases:
        got = run(base_url, body)
        print(f"POST /runs {body}: {got}")
        require(got == expected, f"POST /runs {body}", got)
        after = recorder_sets(base_url)
        require(after == saved, f"the selection after POST /runs {body}", after)

    for body in ({"SelectedRecorderSets": ["no such set"]}, {"SelectedRecorderSets": "not-a-list"},
                 {"SelectedRecorderSets": [SET_X, None]}):
        status, text = run(base_url, body)
        print(f"POST /runs {body}: {status} {text}")
        require(status == 400, f"refusing POST /runs {body}", (status, text))
        after = recorder_sets(base_url)
        require(after == saved, f"the selection after refusing POST /runs {body}", after)

    on = tree(base_url)
    require(len(on) == 1 and on[0].startswith(ITEM_Y + "|"), "the tree after the runs", on)


def main() -> None:
    out = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else OUT
    exe = os.environ["VENEER_CMD_EXE"]
    reference_veneer_assembly_in_scripts()
    base_url = f"http://localhost:{PORT}"

    # No project file: VeneerCmd creates an empty project and scenario.
    procs = start(None, exe)
    try:
        v = veneer.Veneer(port=PORT)
        build(v, base_url)
        v.model.save(out)
        print("saved", out)
    finally:
        stop(procs)

    # The checks run on a copy, so nothing they do can reach the saved model.
    work = tempfile.mkdtemp(prefix="recorder-sets-model-")
    try:
        copy = os.path.join(work, os.path.basename(out))
        shutil.copyfile(out, copy)
        procs = start(copy, exe)
        try:
            check(base_url)
        finally:
            stop(procs)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print("checked", out)


if __name__ == "__main__":
    main()
