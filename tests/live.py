"""Helpers shared by the live tests (conftest.py, test_functions_evaluate.py) and the model
generator (fixtures/build_evaluate_model.py). Imports nothing from pytest."""
import os
import socket

import requests

# Must match TransientFunctionEvaluator.TRANSIENT_NAME.
TRANSIENT_NAME = "$f_veneer_evaluate"

STRAY_SCRIPT = '''
fm = scenario.Network.FunctionManager
n = 0
for t in list(fm.Functions) + list(fm.Variables):
    n += sum(1 for x in t.ReferencingFunctions if str(x.Name) == "%s")
result = str(n)
''' % TRANSIENT_NAME


def port_in_use(port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(1.0)
        return s.connect_ex(("127.0.0.1", port)) == 0


def veneer_cmd(exe: str) -> str:
    """The VeneerCmd command for manage.start, with -d when VENEER_SOURCE_DIR is set
    (manage.start has no option for it, but places veneer_exe verbatim at the head of the
    command line)."""
    source_dir = os.environ.get("VENEER_SOURCE_DIR")
    return f'"{exe}" -d "{source_dir}"' if source_dir else f'"{exe}"'


def make_item(id_, expression, date="2000-01-02", **extra):
    """One /functions/evaluate item; extra keys (context, bindings) are passed through."""
    return {"id": id_, "date": date, "expression": expression, **extra}


def evaluate(base_url, items, timeout=120):
    """POST /functions/evaluate; returns (status, parsed JSON body or None)."""
    r = requests.post(f"{base_url}/functions/evaluate", json={"items": items}, timeout=timeout)
    try:
        body = r.json()
    except ValueError:
        body = None
    return r.status_code, body


def stray_count(base_url) -> int:
    """How many ReferencingFunctions entries still hold the endpoint's transient function.
    Runs through /ironpython, which does not take Veneer's run lock and does not sweep."""
    r = requests.post(f"{base_url}/ironpython", json={"Script": STRAY_SCRIPT}, timeout=60)
    r.raise_for_status()
    body = r.json()
    assert not body.get("Exception"), body["Exception"]
    return int(body["Response"]["Value"])
