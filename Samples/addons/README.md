# Addon launch modes

A `.veneer` file sits beside a Source project as `<project>.rsproj.veneer` and
registers entries in Source's menu bar. This directory has worked examples of the
two launch modes.

## `type: "exe"` — run a program

```json
{
  "name": "Calibrate",
  "type": "exe",
  "path": "tools/calib.exe",
  "args": ["--port", "%VENEER_PORT%", "--out", "results 2026"],
  "env": { "RUN_LABEL": "nightly" },
  "workingDirectory": "tools",
  "menu": "Veneer|Calibration"
}
```

| Field | Meaning |
|---|---|
| `path` | program to run, relative to the project directory unless rooted |
| `args` | array of arguments. Veneer quotes each one, so write them as the program should receive them — do not add your own quotes |
| `env` | environment variables for the child process |
| `workingDirectory` | relative to the project directory; defaults to it |

`args` is an array only. There is no single-string command-line form — use
`type: "script"` with one line if you need to hand a raw command line to a shell.

A `.bat` or `.cmd` target still works and now accepts `args` too. It runs via
`cmd.exe /D /V:OFF /C`, which matters for the limitations below.

## `type: "script"` — run several commands

```json
{
  "name": "Nightly run",
  "type": "script",
  "env": { "RUN_LABEL": "nightly" },
  "script": [
    "cd %VENEER_PROJECT_DIR%",
    "python calibrate.py --run %RUN_LABEL%",
    "python report.py"
  ]
}
```

The lines run in **one `cmd.exe` session**, fed over its standard input. Nothing
is written to disk, so a locked-down temp directory cannot block it. Because it is
a single session, `set`, `cd`, `&&` and `%VAR%` all persist from one line to the
next.

Execution **stops at the first line that fails** (non-zero exit). This is not
configurable. The failing line number is reported in the log.

`path` and `script` are mutually exclusive; an entry with both is rejected.

## Injected variables

Available in `path`, `args`, `workingDirectory`, `env` values and script lines:

| Variable | Value |
|---|---|
| `VENEER_PORT` | the configured port. **Not** a promise the server is listening — it is set whether or not the server is running, so handle connection failure yourself |
| `VENEER_PROJECT_DIR` | the project directory |
| `VENEER_PROJECT_FILE` | full path to the `.rsproj` |

Your own `env` entries win over these. They cannot reference each other — one
`env` value referring to another `env` value is left unexpanded, because the
result would depend on JSON key order.

An unknown `%VAR%` is left as literal text rather than blanked, so a typo is
visible in the log instead of silently producing a truncated argument.

## Limitations of `type: "script"`

Feeding commands to `cmd.exe` over stdin is not identical to running a `.bat`
file. Three differences matter:

- **A command that reads stdin will consume the rest of your script.** `pause`,
  an interactive CLI, `more`, `sort` with no input file — any of these read from
  the same pipe the remaining lines arrive on. If such a command then waits for
  input that never comes, the addon never finishes. This is the most likely
  problem in practice.
- **`goto` and labels do not work, and fail silently.** `goto :skip` does not
  jump, nothing is printed, and `:skip` is ignored. There is no diagnostic at all.
- **`for` loops take a single `%`**, not the `%%` used in batch files. The `%%i`
  form silently does nothing.

If you need any of these, ship a `.bat` and use `type: "exe"` — which now takes
`args`, so it is no longer a dead end. Note the escape hatch is itself
shell-dependent: a `.bat` target still routes through `cmd.exe`, so it does not
help in a deployment where `cmd.exe` is blocked outright. There, invoke a real
executable directly.

## Output

No console window appears. Output goes to the **Veneer log** in the Web Server
Monitoring panel, at `Debug` level — lower the Log Level dropdown to see it.
Errors also go to Source's own log, where they survive the panel being closed.

In script mode the log reads as a transcript: each command followed by its
output. The command echo shows the line **before** `%VAR%` expansion, so you will
see `cd %VENEER_PROJECT_DIR%` rather than the resolved path.

## Diagnosing a menu item that does nothing

An entry that is greyed out has a tooltip explaining why, and the same reason is
written to Source's log once. Causes: a scenario filter that does not match the
active scenario, an unrecognised `type`, both `path` and `script` present,
`type: "script"` with no lines, or neither `path` nor `script`.
