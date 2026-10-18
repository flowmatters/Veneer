"""Helpers shared by the live tests (conftest.py, test_functions_evaluate.py) and the model
generator (fixtures/build_evaluate_model.py). Imports nothing from pytest."""
import os
import socket

import requests

# Must match TransientFunctionEvaluator.TRANSIENT_NAME.
TRANSIENT_NAME = "$f_veneer_evaluate"

# The generated model's once-per-run function usage: USAGE_FUNCTION drives USAGE_PARAMETER on the
# storage node USAGE_NODE, whose own (saved) value is the distinctive USAGE_VALUE. A run writes the
# function's value there and restores USAGE_VALUE at its end; FunctionManager.Reset — which
# evaluate must never call — would write the parameter type's default (0) through the usage.
# (Every function also drives a per-timestep parameter: see fixtures/build_evaluate_model.py.)
USAGE_NODE = "StorageA"
USAGE_PARAMETER = "InitialStorage"
USAGE_FUNCTION = "$WhatIf.f_Const"
USAGE_VALUE = 123.4

STRAY_SCRIPT = '''
fm = scenario.Network.FunctionManager
n = 0
for t in list(fm.Functions) + list(fm.Variables):
    n += sum(1 for x in t.ReferencingFunctions if str(x.Name) == "%s")
result = str(n)
''' % TRANSIENT_NAME


def port_in_use(port: int) -> bool:
    """True when anything accepts connections on the port, over IPv4 or IPv6 loopback."""
    for family, host in ((socket.AF_INET, "127.0.0.1"), (socket.AF_INET6, "::1")):
        try:
            with socket.socket(family, socket.SOCK_STREAM) as s:
                s.settimeout(1.0)
                if s.connect_ex((host, port)) == 0:
                    return True
        except OSError:
            pass  # no IPv6 on this machine
    return False


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


def run_script(base_url, script, timeout=60):
    """Run an IronPython script through /ironpython (which does not take Veneer's run lock and
    does not sweep) and return the Value of its `result`."""
    r = requests.post(f"{base_url}/ironpython", json={"Script": script}, timeout=timeout)
    r.raise_for_status()
    body = r.json()
    assert not body.get("Exception"), body["Exception"]
    return body["Response"]["Value"]


def stray_count(base_url) -> int:
    """How many ReferencingFunctions entries still hold the endpoint's transient function."""
    return int(run_script(base_url, STRAY_SCRIPT))
