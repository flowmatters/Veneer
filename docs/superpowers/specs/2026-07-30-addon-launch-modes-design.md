# Addon launch modes

## Problem

`.veneer` addon entries register items in Source's Tools menu. Today the only
supported action is launching a program by path:

```json
{ "name": "Nightly report", "type": "exe", "path": "tools/report.bat" }
```

`ReportingMenu.LaunchExeAddon` (`ReportingMenu.cs:176-198`) resolves the path
relative to the project directory, special-cases `.bat` by delegating to
`cmd.exe /C`, injects `VENEER_PORT`, and starts the process.

This forces every non-trivial addon to be a committed `.bat` file. Three
capabilities are missing:

1. Passing **arguments** to the program.
2. Running a **series of commands** without a wrapper script on disk.
3. Setting **environment variables** for the launched program.

Writing a generated `.bat` to a temporary folder is not an acceptable
implementation of (2): in locked-down deployments the temp directory may be
blocked by policy or antivirus.

## Solution

Extend the addon schema with optional `args`, `env` and `workingDirectory`
fields, and add a `script` addon type that executes a list of command lines in a
single `cmd.exe` session fed over redirected stdin. Nothing is written to disk.

### Scope

In scope: the addon schema, process launching, output capture, and the
extraction and rename that make the above tractable.

Out of scope: the REST API. `VeneerAddon` is referenced only by
`ProjectLoadListener`, `ReportingMenu` and its own definition file — no endpoint
exposes it. Consequently **no `PROTOCOL_VERSION` bump** and **no `docs/api/`
change** are required.

## Schema

`VeneerAddon` (`Addons/VeneerConfiguration.cs:79-91`) gains four optional
fields. Existing `.veneer` files continue to load and run unchanged: omitting
all four reproduces today's behaviour, with one deliberate exception — the
`workingDirectory` default, described below and listed under Risks.

```csharp
public string[] args { get; set; }
public Dictionary<string,string> env { get; set; }
public string workingDirectory { get; set; }
public string[] script { get; set; }
```

### `type: "exe"` — program with arguments

```json
{
  "name": "Calibrate",
  "type": "exe",
  "path": "tools/calib.exe",
  "args": ["--port", "%VENEER_PORT%", "--out", "results 2026"],
  "env": { "RUN_LABEL": "nightly" },
  "workingDirectory": "tools"
}
```

`args` is an array only. There is deliberately no single-string command-line
form: `type: "script"` with one line already serves as the verbatim
command-line escape hatch, so a string form would duplicate it while adding a
*third* quoting path — and, unlike the two below, one where the shell rather
than Veneer decides how the string splits into arguments.

`workingDirectory` is resolved relative to the project directory and defaults
to the project directory when absent. (Today's code sets no working directory
at all, so the child inherits Source's — an incidental behaviour change, and the
project directory is the useful default.)

### `type: "script"` — inline command series

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

`path` is unused for this type.

### Validation

An addon entry is invalid if it specifies both `path` and `script`, or is
`type: "script"` with `script` absent or empty. An invalid entry produces a
disabled menu item with an explanatory tooltip plus a logged error — the same
treatment scenario-filtered addons already get at `ReportingMenu.cs:112-116`,
rather than the item vanishing silently.

Validation errors are **logged at most once each**, deduplicated by message in a
static set that is cleared when the project changes. This matters because
`VeneerConfiguration.Load` is called from four places
(`ReportingMenu.cs:85,239,287` and `ProjectLoadListener.cs:192`) and the config
is therefore re-read every time the menu is opened. Without deduplication a
single malformed entry would log on every drop-down.

## Environment and variable expansion

A single effective environment is built per launch and used for two purposes —
passed to the child process, and used to expand `%VAR%` references:

```
process environment
  + VENEER_PORT, VENEER_PROJECT_DIR, VENEER_PROJECT_FILE   (injected by Veneer)
  + addon.env                                              (wins on conflict)
  = effective environment
```

`VENEER_PORT` is injected today. `VENEER_PROJECT_DIR`
(`Scenario.Project.FileDirectory`) and `VENEER_PROJECT_FILE` (the full
`.rsproj` path) are new. `VENEER_SCENARIO` was considered and rejected as not
currently needed.

`VENEER_PORT` is the **configured** port, not a promise that the server is
listening. `WebServerStatusControl.Port` is set independently of `Running`, so
the value is available with the server stopped, and addons that need a live API
must handle connection failure themselves. The existing behaviour of opening the
panel when `Control == null` (`ReportingMenu.cs:178-181`) is retained: it is
where addon output is routed, so having it open is consistent rather than
incidental.

### Expansion is Veneer's job, not the shell's

In `script` mode `cmd` expands `%VAR%` itself. In `exe` mode there is no shell,
so an unexpanded `%VENEER_PORT%` would reach the child as a **literal string**.
Veneer therefore expands `%VAR%` in `path`, `args` and `workingDirectory`
before launching, so a given `%VAR%` means the same thing in both modes.

`Environment.ExpandEnvironmentVariables` must **not** be used for this: it
resolves against the current process environment only, so variables declared in
the addon's own `env` block would fail to expand. Expansion is implemented
against the merged dictionary above.

Unknown variables are left as-is rather than replaced with empty string, so a
typo is visible in the log rather than silently producing a truncated argument.

### `env` values are expanded too

Values inside the addon's own `env` block are expanded as well, against the
process environment plus the injected variables. Without this,
`"env": { "OUT": "%VENEER_PROJECT_DIR%\\out" }` would reach the child literally
and then be expanded by `cmd` at use time in `script` mode but stay literal in
`exe` mode — reintroducing exactly the asymmetry this section exists to remove.

`env` values are **not** expanded against each other. One `env` entry cannot
reference another, because that would make the result depend on JSON key order.
Attempting it leaves the reference intact, consistent with the unknown-variable
rule above.

## Argument quoting

A shared `QuoteArgument` helper handles Windows command-line quoting (wrap in
double quotes when the argument contains whitespace or a quote; escape embedded
quotes and trailing backslashes).

`ProcessStartInfo.ArgumentList` is deliberately **not** used, even on `master`
where it exists. It is absent on .NET Framework 4.8, so relying on it would
make `master` and `legacy_ci` structurally different in the exact code most
likely to harbour edge-case bugs. One implementation, ported verbatim.

This also fixes a live bug: `ReportingMenu.cs:188` builds `"/C " + fullPath`
unquoted, so any `.bat` addon under a path containing a space is broken today.

### Two distinct quoting paths

`QuoteArgument` alone is **not** sufficient. With `UseShellExecute = false` a
`.bat` cannot be the `FileName`, so the existing `cmd /C` shim must survive and
must now also carry `args` — and `cmd`'s `/C` quote handling is not
`CreateProcess`'s. Verified empirically:

```
cmd /C  "<path>" "<arg>"      ->  'C:\Users\...\dir' is not recognized     FAIL
cmd /C ""<path>" "<arg>""     ->  ARG1=[arg with space]                    OK
```

So there are two composition rules, and the plan must implement both:

| Target | `FileName` | `Arguments` | Quoting |
|---|---|---|---|
| `.exe` | the resolved path | `QuoteArgument` per arg, space-joined | args: `QuoteArgument` |
| `.bat` / `.cmd` | `cmd.exe` | `/D /V:OFF /C "` + **unconditionally quoted** path + space-joined quoted args + `"` | path: always quoted; args: `QuoteArgumentForCmd` |

The `.bat` form adds a pair of outer quotes, and also quotes more aggressively
inside them — see the next subsection. The two forms differ in three respects,
not just the wrapper: the outer quoting, the path rule, and the argument rule.

**The `.bat` path is quoted unconditionally**, even when it contains no
whitespace. Two reasons: the doubled-outer-quote form is only stable while three
or more quotes are present, and quoting conditionally would make the composed
line silently change shape depending on whether the path happened to contain a
space — so a bug would appear only for some users' directory layouts. This means
the path and the arguments use *different* quoting functions on this path.

### The `cmd` path needs a wider quoting trigger

The two paths cannot share one quoting rule. `QuoteArgument`'s trigger —
whitespace or an embedded quote — is correct for `CreateProcess`, but on the
`cmd /C` path the argument sits inside a shell command line where
metacharacters are live. An argument containing `&`, `|`, `<`, `>`, `^`, `(` or
`)` has no whitespace, so it would go unquoted and be interpreted by `cmd`.
Verified against the composition rule above:

```
args: ["a&b"]  ->  ARG1=[a]   + 'b' is not recognized ...   tail EXECUTED
args: ["a|b"]  ->  ARG1=[]    + 'b' is not recognized ...   argument LOST
args: ["a^b"]  ->  ARG1=[ab]                                caret EATEN
```

This is a correctness bug and a mild injection vector — a `.veneer` file could
run a command its author did not write. Quoting on the metacharacters fixes all
cases, with the argument arriving intact.

`QuoteArgumentForCmd` therefore quotes when the argument contains whitespace, a
quote, **or any of `& | < > ^ ( )`**. It is a thin wrapper over
`QuoteArgument` with a widened trigger, not a separate implementation.

This distinction is easy to miss because every whitespace-based test passes
while metacharacters ship broken, so the metacharacter cases are called out
explicitly in the Testing table.

### `%` and `!` are deliberately **not** in the trigger set

Neither can be protected by quoting, so adding them would achieve nothing.
Verified: `cmd /C "<bat> "%MYVAR%""` yields `ARG1=[EXPANDED]` — double quotes do
not stop percent expansion.

The design is nevertheless safe on `%`, by a property worth stating explicitly:
**cmd's environment *is* the merged effective environment Veneer expanded
against.** So anything cmd could expand, Veneer has already expanded, and what
survives Veneer's pass is exactly what cmd also leaves alone — `%NOSUCHVAR%`
arrives literal on both paths, honouring the unknown-variable rule. Lone and
doubled percents pass through untouched (`50%` → `50%`, `a%%b` → `a%%b`; there is
no batch-style collapsing in a `/C` context). The two-mode invariant holds.

The residual case is cmd's *synthesised* pseudo-variables — `%CD%`, `%RANDOM%`,
`%DATE%`, `%TIME%`, `%ERRORLEVEL%` — which are not in the environment block and
so resolve on the `.bat` path while staying literal on the `.exe` path. This is
a documented asymmetry, not a defect to fix.

`!` is inert by default but **is** eaten under delayed expansion, which is
machine-configurable via
`HKLM\SOFTWARE\Microsoft\Command Processor\DelayedExpansion`. Quoting does not
help there either; the fix is a launch flag, below.

### Hardening the cmd invocation

Every `cmd.exe` launch — both the `.bat` shim and the script session — uses
`/D /V:OFF`:

- **`/V:OFF`** makes `!` immune to a machine-wide `DelayedExpansion` registry
  setting, restoring literal `!` regardless of local policy.
- **`/D`** skips `AutoRun` under
  `HKLM|HKCU\SOFTWARE\Microsoft\Command Processor`, which otherwise executes an
  arbitrary site-configured command at every `cmd` startup and could `cd` or set
  variables under the script's feet.

Both are unset on the development machine, but a feature whose motivating
context is locked-down, policy-managed deployments must not depend on that. (The
filter rules are already resilient to AutoRun *output*: anything it prints
precedes the prompt line and is dropped by the drop-until-sentinel rule.)

## Script execution

One `cmd.exe` process per launch, with `UseShellExecute = false`,
`CreateNoWindow = true`, and stdin, stdout and stderr all redirected.

Lines are written to stdin in order, each followed by an injected failure guard,
giving stop-on-first-failure entirely inside `cmd` with no round-tripping
between .NET and the shell:

```
@echo off
echo ##VENEER:<nonce>:1
<line 1>
if errorlevel 1 exit %errorlevel%
echo ##VENEER:<nonce>:2
<line 2>
if errorlevel 1 exit %errorlevel%
...
exit 0
```

`<nonce>` is a GUID generated per launch, formatted with the default
`Guid.ToString()` ("D"). It exists so that a marker can never be confused with
program output: a script whose own output happened to contain `##VENEER:7` would
otherwise be silently swallowed *and* would corrupt step attribution. With a
per-launch nonce the collision is not merely unlikely but unreachable, which
removes the need to specify collision behaviour at all.

The "D" format is required, not incidental: the nonce is interpolated into the
regex `^echo ##VENEER:<nonce>:\d+$`, and the `"B"` or `"P"` formats would inject
braces or parentheses as regex metacharacters. Alternatively, escape it.

Stop-on-first-failure is fixed behaviour, not configurable. Per-line
`%errorlevel%` expansion is correct here because each line is parsed as it is
read, after the previous line has run — verified: a line exiting 7 terminates
the session with code 7 and skips the remainder.

### What the captured stream actually contains

The output needs filtering, and the shape was established empirically rather
than assumed. Feeding the above to `cmd.exe` with redirected stdin produces:

```
Microsoft Windows [Version 10.0.26200.8875]      <- banner, on STDOUT
(c) Microsoft Corporation. All rights reserved.
                                                 <- blank
C:\models\catchment>@echo off                    <- prompt, ONCE, on this line
echo ##VENEER:<nonce>:1                          <- input echo (no prompt)
##VENEER:<nonce>:1                               <- marker output
python calibrate.py                              <- input echo
<real output>
if errorlevel 1 exit %errorlevel%                <- input echo
exit 0                                           <- input echo (terminator)
```

Three corrections to naive assumptions, all verified on this machine:

- **`@echo off` is what suppresses the prompt**, not `PROMPT`. Setting `PROMPT`
  to an empty value does *not* work — `cmd` treats empty or undefined `PROMPT`
  as "use the default" and prints the full `C:\models\catchment>` regardless.
- **`/Q` has no effect at all** on a stdin-fed session; output is byte-identical
  with and without it. It is not used.
- **Command echo survives `echo off`.** Every input line is echoed to stdout,
  including silent ones (`set`, `rem`, the `if errorlevel` guards). `@echo off`
  removes the *prompt* from line two onward; it does not remove the *echo*.

The banner goes to stdout and no switch suppresses it for a stdin-fed session.

### Filter rules

Keeping the command echo is not merely convenient, it is the only sound option:
stripping it is **not reliably possible**, because an echoed input line is
byte-identical to a program printing the same text, whereas Veneer's own
injected lines are known constant strings. So the echo is kept and read as a
transcript — command followed by its output — and only Veneer's scaffolding is
stripped.

**These rules apply to `script`-mode launches only.** An `exe` launch produces
no banner and no `>@echo off` sentinel, so applying rule 1 to it — together with
the flush-on-end fallback below — would buffer the addon's entire stdout and
emit it only at exit, destroying streaming output for a long-running addon. In
`exe` mode stdout and stderr pass through to the log unfiltered.

Applied to each captured stdout line of a `script`-mode launch, in order:

| Rule | Action |
|---|---|
| Everything up to and including the line containing `>@echo off` | drop (banner, blank, prompt line) |
| Exactly matches the guard string `if errorlevel 1 exit %errorlevel%` | drop |
| Exactly matches the terminator `exit 0` | drop |
| Matches `^echo ##VENEER:<nonce>:\d+$` | drop (echo of the marker command) |
| Matches `^##VENEER:<nonce>:\d+$` | record as current step, then drop |
| Anything else | keep — user's command echo, or real output |

The recorded step number is what attributes a failure to a specific script line,
so a non-zero exit reports "line 3 (`python report.py`) exited 1" rather than
"the script failed". These rules are pure string handling and are unit-tested.

Three robustness points:

- **Comparisons are made after trimming line endings.** cmd's echo is a
  byte-level replay of the input, so a CRLF-terminated line echoes back with the
  trailing `\r` while an LF-only one does not. `OutputDataReceived` normally
  absorbs this, but an exactly-matches rule must not depend on that.
- **Rule 1 is a drop-until-sentinel state machine, and needs a fallback.** If
  the `>@echo off` sentinel never arrives — a pathological cmd failure — the
  naive implementation discards the entire stream and the failure is invisible.
  If the stream ends without the sentinel, the buffered pre-sentinel lines are
  flushed to the log instead of dropped.
- **The echo is pre-expansion.** The transcript shows `cd %VENEER_PROJECT_DIR%`,
  not the resolved path, because cmd echoes the line as read. This is a real
  limit on the transcript's debugging value and is worth stating so it does not
  surprise whoever writes the tests.

### Documented limitations

`cmd` reading from redirected stdin is not identical to executing a `.bat`
file. Three differences must be documented for addon authors:

- **A command that reads stdin will eat the rest of the script.** `pause`, an
  interactive CLI, `more`, `sort` with no input file — any of these consume the
  remaining script lines *and* the injected guards from the same pipe. This is
  the most likely footgun in practice, more so than the two below, and it fails
  confusingly rather than cleanly.
- `goto` and labels do not work, and fail **silently** — `goto :skip` does not
  jump, no error is printed, and `:skip` is ignored. There is no diagnostic, so
  this must be called out in the author-facing docs.
- `for` loops take single `%` rather than the `%%` used in batch files. The `%%i`
  batch form silently does nothing.

Authors needing any of these should ship a `.bat` and use `type: "exe"`, which
now accepts `args` and so is no longer a dead end. Note that this escape hatch
is itself shell-dependent: a `.bat` target still routes through the `cmd /C`
shim, so it does not help in a deployment where `cmd.exe` is blocked outright
(see Risks).

## Output and logging

The console window is hidden, so Veneer owns the output entirely.

| Source | Destination | Level |
|---|---|---|
| stdout | Veneer log | `Debug` |
| stderr | Veneer log | `Warning` |
| non-zero exit / launch failure | Veneer log **and** `TIME.Management.Log.WriteError` | `Error` |

Routing script chatter to the Veneer log keeps third-party output out of
Source's model log, and the existing level filter (`_minimumLogLevel`, settable
from `LogLevelCombo`) means verbose output is hidden by default. Failures also
go to the Source log because the Veneer log is in-memory and disappears when
the panel is closed or cleared — an error must survive that and be findable
where users already look.

### Required change to the log path

`WebServerStatusControl` subscribes its `LogBox` sink inside `StartServer`
(`server.LogGenerator += ServerLogEvent`, `WebServerStatusControl.xaml.cs:184`).
Addons can be launched with the server stopped, so the sink must be hoisted to
be live whenever the panel is open, independent of server state.

Thread marshalling needs no new work: `ServerLogEvent` already posts through
`_originalContext` (line 221), which is what `OutputDataReceived` — raised on a
threadpool thread — requires.

## Structure

### `VeneerMenu` (rename)

`ReportingMenu` is renamed to `VeneerMenu`. The existing name is inaccurate:
besides the addon tree the class builds the HTML report items (`NiceName`, line
200), the `/doc/` links (line 209) and the Veneer logo item (lines 134-137).
`AddonMenu` was considered and rejected as narrower than what the class does.

The class is `internal` (line 19), so this is not an API change and is
unreachable from veneer-py or IronPython. Call sites are in-assembly only:
`ProjectLoadListener.cs:165,168,209` and
`WebServerStatusControl.xaml.cs:102,113,114`.

The rename ships as an isolated commit with no behaviour change, ported to
`legacy_ci` before feature work begins. Renaming a file on one branch while the
other keeps the old name is exactly the friction `branch-porting-guide.md`
exists to manage; sequencing it first keeps the feature diff clean and
portable. `CLAUDE.md:44` and `branch-porting-guide.md:207` are updated to
match. Historical documents under `docs/superpowers/specs/` and `plans/` are
left unchanged — they record what was true when written.

### `AddonLauncher` (new)

Process launching moves out of the menu class into
`DomainActions/AddonLauncher.cs`:

```csharp
AddonLauncher.Launch(VeneerAddon addon, AddonContext context, IAddonLog log)
```

`AddonContext` carries the project directory, project file and port.
`IAddonLog` abstracts the log sink.

The motivation is testability as much as size. Environment merging, variable
expansion, quoting and script generation are pure functions, and behind this
boundary they are reachable without a running Source. `VeneerMenu` retains only
menu construction.

### Unknown addon types

The `switch` on `addon.type` (`ReportingMenu.cs:101-107`) has no `default`, so
an unrecognised type silently produces a dead menu item. A `default` case
disables the item and logs, matching the existing treatment of
scenario-filtered addons at lines 112-116.

## Testing

Tests follow the convention **already established in this repository** and go in
`FlowMatters.Source.Veneer/Tests/`, in namespace `FlowMatters.Source.Veneer.Tests`,
as NUnit fixtures using `Assert.That(..., Is.EqualTo(...))`. See the existing
`Tests/SchematicNameSanitiserTests.cs` and `Tests/SchematicSvgBuilderTests.cs`.

No new project is created. NUnit 4.1.0, NUnit3TestAdapter, Microsoft.NET.Test.Sdk
and coverlet.collector are already `PackageReference`s in
`FlowMatters.Source.Veneer.csproj` (lines 109-119), and tests run against the
main project with `dotnet test`.

An earlier draft of this spec asserted that the repository had no C# test
infrastructure and proposed adding a separate test project. That was incorrect —
the convention above already exists, and following it is both less work and less
divergence.

| Area | Cases |
|---|---|
| `QuoteArgument` | spaces, embedded quotes, trailing backslashes, empty string |
| `QuoteArgumentForCmd` | **each of `& \| < > ^ ( )` quoted** even with no whitespace; otherwise agrees with `QuoteArgument` |
| Command-line composition | `.exe` form; `.bat` form's doubled outer quotes; spaced path with and without args; **`.bat` with a metacharacter argument** |
| Environment merge | injection, `addon.env` overriding an injected var, null `env` |
| Variable expansion | `path`/`args`/`workingDirectory`; `env` values expanded; `env` entries not cross-referencing; unknown vars left intact; **expansion is single-pass** (a substituted value containing `%X%` is not re-expanded) |
| Script generation | `@echo off` first, guard after every line, nonce marker before every line, `exit 0` terminator, `/D /V:OFF` on the invocation |
| Output filtering | banner + prompt line dropped; guard echoes dropped; `exit 0` terminator dropped; marker echo and marker output dropped; step number recorded; user command echo and real output both kept; CRLF and LF input both match the exact-match rules; stream ending without the sentinel flushes rather than discards; **`exe`-mode output passes through unfiltered** |
| Schema validation | both `path` and `script` present; `type: "script"` with absent or empty `script`; error logged once across repeated `Load` calls |

The metacharacter rows are load-bearing. Every whitespace-based case passes
against the narrow quoting trigger while `&`, `|` and `^` ship broken, so
without them the suite gives false confidence.

The repository's other test assets are complementary, not alternatives: the
top-level `tests/` directory holds Python tests for `compile_all.py`, and the
framework in `veneer-testing.md` drives a live Veneer over HTTP. Neither can
cover quoting and expansion edge cases at reasonable cost.

The pure functions behind the `AddonLauncher` boundary must not depend on
RiverSystem or TIME types, so that these tests exercise them without needing a
loaded scenario. `VeneerAddon` is already a plain POCO, which makes this
achievable — but it is a constraint on the design of `AddonContext`, which must
carry primitives (project directory, project file, port) rather than a
`RiverSystemScenario`.

End-to-end verification (a real addon launching, output appearing in the Veneer
log, a failing line halting the script) remains manual, with worked examples
added under `Samples/`.

## Decisions taken

| Decision | Chosen | Rejected |
|---|---|---|
| Inline script execution | `cmd.exe` via redirected stdin | line-by-line `Process.Start` (no shared state); `cmd /C` joined with `&&` (~8191 char limit, quoting); `powershell -EncodedCommand` |
| Failure handling | stop on first failure, fixed | run every line; per-addon flag |
| Console window | hidden | visible; per-addon flag |
| Output destination | Veneer log, errors also to Source log | either alone; both for everything |
| Schema shape | extend `exe`, add `script` | three types; one unified type |
| `args` form | `string[]` only | `string \| string[]` |
| Injected variables | `VENEER_PORT`, `VENEER_PROJECT_DIR`, `VENEER_PROJECT_FILE` | `VENEER_SCENARIO` |
| Menu class name | `VeneerMenu`, renamed in its own commit first | `AddonMenu`; rename later; don't rename |
| Prompt suppression | `@echo off` as the first injected line | empty `PROMPT` (does not work — reverts to default); `/Q` (inert on a stdin-fed session) |
| Command echo | kept, and read as a transcript | stripping it — not reliably possible, since an echoed line is byte-identical to a program printing the same text |
| Step markers | `##VENEER:<per-launch GUID>:<n>` | a bare `##VENEER:<n>` prefix, which program output could collide with |

## Risks

**Shell invocation may itself be blocked.** Avoiding the temp file removes one
obstacle, but some EDR products alert on `cmd.exe` spawned by a non-shell parent
such as `Source.exe`. Deployments that hit this can use `type: "exe"` with
`args` **and a non-`.bat` target** — that path calls `CreateProcess` directly and
never invokes a shell. This is why the `exe` improvements matter independently of
the `script` type and are not merely a convenience.

The qualification matters: a `.bat` target still routes through the `cmd /C`
shim, so in a `cmd`-blocked deployment both the `script` type *and* the `.bat`
escape hatch offered under Documented limitations are unavailable. Such a
deployment must invoke a real executable directly. Nothing in this design can
work around a blanket block on `cmd.exe`.

**`workingDirectory` default is a behaviour change.** Existing `exe` addons
currently inherit Source's working directory; they will now default to the
project directory. This is the more useful default and matches how `path` is
already resolved, but it could affect an addon relying on the old behaviour.
