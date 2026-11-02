"""Helpers shared by the live tests (conftest.py, test_functions_evaluate.py) and the model
generator (fixtures/build_evaluate_model.py). Imports nothing from pytest."""
import getpass
import os
import re
import shutil
import socket
import tempfile
import zipfile

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


# ---- fixture hygiene: a saved .rsproj is a zip, and Source writes the saving user's name into
# its _CreatorMetadata.txt. The fixture is published, so it must carry no user name, machine name
# or local path.

NEUTRAL_NAME = "sift-tests"
# Members Source writes as text; every other member (the Firebird .fdb, the .tsz time series) is
# copied through unchanged.
TEXT_MEMBER_SUFFIXES = (".txt", ".xml", ".json")
# A Windows drive path (C:\... or C:/...), up to the next quote, angle bracket, ';' or whitespace.
LOCAL_PATH = re.compile(r"\b[A-Za-z]:[\\/][^\s\"'<>;]*")
CREATOR_USER = re.compile(r"(User:\s*)[^;\r\n]*")


def local_identities():
    """The names that identify this machine and its user: user name and machine name."""
    names = {os.environ.get("USERNAME"), os.environ.get("USER"), os.environ.get("COMPUTERNAME")}
    try:
        names.add(getpass.getuser())
    except Exception:
        pass
    return sorted(n for n in names if n and n.strip() and n != NEUTRAL_NAME)


def scrub_text(text, identities):
    """`text` with the creator's user replaced by NEUTRAL_NAME, every whole-word occurrence of an
    identity (case-insensitive) replaced by NEUTRAL_NAME, and every local drive path removed."""
    text = CREATOR_USER.sub(lambda m: m.group(1) + NEUTRAL_NAME, text)
    for name in identities:
        text = re.sub(r"(?<![\w-])" + re.escape(name) + r"(?![\w-])", NEUTRAL_NAME, text,
                      flags=re.IGNORECASE)
    return LOCAL_PATH.sub("", text)


def is_text_member(name):
    return name.lower().endswith(TEXT_MEMBER_SUFFIXES)


def identity_hits(text, identities):
    """What in `text` would identify the machine: identities (whole word, any case) and local
    drive paths."""
    hits = [n for n in identities
            if re.search(r"(?<![\w-])" + re.escape(n) + r"(?![\w-])", text, flags=re.IGNORECASE)]
    return hits + LOCAL_PATH.findall(text)


def _decode(data):
    return data.decode("utf-8-sig") if data.startswith(b"\xef\xbb\xbf") else data.decode("utf-8")


def scrub_rsproj(path, identities=None):
    """Rewrite the zip at `path` with every text member scrubbed (scrub_text) and every other
    member's bytes unchanged, in the original order, names, timestamps and compression. Returns
    {text member: changed?}."""
    identities = local_identities() if identities is None else identities
    report = {}
    fd, tmp = tempfile.mkstemp(suffix=".rsproj", dir=os.path.dirname(os.path.abspath(path)))
    os.close(fd)
    try:
        with zipfile.ZipFile(path) as src, zipfile.ZipFile(tmp, "w") as dst:
            for info in src.infolist():
                data = src.read(info)
                if is_text_member(info.filename):
                    bom = data.startswith(b"\xef\xbb\xbf")
                    text = _decode(data)
                    scrubbed = scrub_text(text, identities)
                    report[info.filename] = scrubbed != text
                    data = (b"\xef\xbb\xbf" if bom else b"") + scrubbed.encode("utf-8")
                dst.writestr(info, data, compress_type=info.compress_type)
        shutil.move(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)
    return report


def rsproj_identity_hits(path, identities=None):
    """{member: hits} for every text member of the .rsproj at `path` that would identify the
    machine, plus a _CreatorMetadata.txt whose user is not NEUTRAL_NAME. Empty when clean."""
    identities = local_identities() if identities is None else identities
    found = {}
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if not is_text_member(info.filename):
                continue
            text = _decode(z.read(info))
            hits = identity_hits(text, identities)
            if info.filename == "_CreatorMetadata.txt":
                users = [m.group(0) for m in CREATOR_USER.finditer(text)]
                hits += [u for u in users if u.split(":", 1)[1].strip() != NEUTRAL_NAME]
            if hits:
                found[info.filename] = hits
    return found


def binary_identity_hits(path, identities=None):
    """{member: [identity, ...]} for every NON-text member of the .rsproj at `path` whose bytes
    contain an identity (any case, as UTF-8 or UTF-16LE). Reported, never edited: the members
    are binary (a Firebird database, time series)."""
    identities = local_identities() if identities is None else identities
    found = {}
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if is_text_member(info.filename):
                continue
            data = z.read(info)
            hits = [n for n in identities for enc in ("utf-8", "utf-16-le")
                    if re.search(re.escape(n.encode(enc)), data, flags=re.IGNORECASE)]
            if hits:
                found[info.filename] = sorted(set(hits))
    return found
