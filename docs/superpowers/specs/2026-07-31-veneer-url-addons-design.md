# URL addons

## Problem

`.veneer` addon entries can launch a program (`type: "exe"`) or an inline command
series (`type: "script"`). There is no way to point a menu item at a **link** —
a project's wiki, a help page, a modelling guide.

Investigating turned up a second problem that makes this the same piece of work.

### `Process.Start(url)` does not open a browser on `master`

`Process.Start(string)` takes its `UseShellExecute` value from the
`ProcessStartInfo` default, and .NET Core flipped that default from `true` to
`false`. Verified on the .NET runtime available here:

```
[System.Diagnostics.ProcessStartInfo]::new("https://example.com").UseShellExecute
-> False
```

On `net8.0-windows` that means `Process.Start("https://…")` does not hand the
string to the shell. It tries to execute it as a file and throws
`Win32Exception`. Two call sites do exactly this:

| Site | What it opens |
|---|---|
| `VeneerMenu.cs:266` (`Launch`) | the HTML report items |
| `VeneerMenu.cs:160` | the Veneer logo click |

Both work on `legacy_ci`, where .NET Framework 4.8 still defaults
`UseShellExecute` to `true`. Both are WinForms `Click` handlers, so on `master`
the throw surfaces as an unhandled-exception dialog in Source rather than a
logged failure — the hazard `AddonLauncher`'s own comments exist to avoid.

**This has not been observed in a running Source.** It is inferred from the
verified runtime default plus the code. Task-level manual verification should
confirm it, but the fix is correct either way: setting `UseShellExecute`
explicitly is right on both branches.

### A third bug, in the same method

`Launch` builds its URL from `SourceRESTfulService.DEFAULT_PORT`
(`VeneerMenu.cs:264`) — the compile-time constant `9876`. Not `Control.Port`,
and not `WebServerStatusControl.DefaultPort`, which both `options.defaultPort`
and the `VENEER_PORT` environment variable feed. So HTML report links point at
9876 no matter where the server is listening.

## Scope

In scope: the `url` addon type; its validation; the shared shell-open helper; the
two broken call sites; the hard-coded report port; docs and a sample.

Out of scope: the REST API. `VeneerAddon` appears in no endpoint — a repo-wide
search hits only `Addons/VeneerConfiguration.cs` and `Tests/AddonValidationTests.cs`.
**No `PROTOCOL_VERSION` bump and no `docs/api/` change.**

## Schema

`VeneerAddon` gains one field:

```csharp
public string url { get; set; }
```

```json
{
  "name": "Model wiki",
  "type": "url",
  "url": "https://wiki.example.org/catchment",
  "menu": "Help"
}
```

`path`, `script`, `args` and `workingDirectory` are unused for this type. `env`
**is** honoured, because it feeds variable expansion.

### Validation

`VeneerAddon.Validate` gains four rules. As with the existing rules, a failure
renders a **disabled menu item with an explanatory tooltip** at menu-build time
rather than failing on click.

| Condition | Message |
|---|---|
| `url` alongside `path` or `script` | specifies `url` together with `path` or `script`; they are mutually exclusive |
| `url` present but `type` is not `url` | specifies `url` but type is not `url` |
| `type: "url"` with no `url` | is type `url` but has no `url` |
| `url` without an allowed scheme | has a `url` that is not `http://`, `https://` or `mailto:` |

The existing rule "has neither `path` nor `script`; there is nothing to run" is
widened to mention `url`, so a valid URL addon no longer trips it.

The second rule is not redundant. Without it, `{"type": "exe", "url": "..."}`
passes validation, dispatches to `LaunchExe`, and fails inside
`Path.Combine(dir, null)` — caught and logged by `AddonLauncher`, but as an
opaque `ArgumentNullException` rather than a schema error.

Rule order matters: the mutual-exclusion checks run before the type checks, so
an entry that is wrong in two ways reports the structural problem first.

## The scheme allowlist

`http://`, `https://`, `mailto:`. Nothing else.

`file://` was considered and **rejected**. It was originally included for the
"PDF on a network share" case, but it also admits `file://server/share/tool.exe`,
and dropping it means `type: "url"` cannot launch a local program by any
spelling. A network-share document needs an `http(s)` URL or an `exe` addon.

This is a predictability boundary, not a security boundary. A `.veneer` file can
already run arbitrary programs via `exe` and `script`, so the allowlist grants
nothing and withholds nothing at the trust level. What it buys is that a typo'd
or malformed `url` fails visibly instead of silently launching something.

### Matching is a literal prefix test, not `Uri` parsing

`AddonUrl.HasAllowedScheme` compares the **trimmed** string against each allowed
prefix, case-insensitively. `Uri`-based validation was rejected on evidence:

```
Uri.TryCreate("http://localhost:%VENEER_PORT%/doc/x", Absolute)  -> False
Uri.TryCreate(@"C:\Windows\System32\cmd.exe",         Absolute)  -> True, Scheme = "file"
```

The first would reject the single most useful URL form, because the variable
sits in the port position and the string is not a well-formed URI until after
expansion. The second means any `Uri`-based allowlist has to special-case a
bare Windows path being classified as `file` — a prefix test never sees it as a
scheme at all.

Trimming matters: a JSON value with an incidental leading space would otherwise
fail the prefix test for no reason a user could see.

### The scheme must be written literally

Validation runs before expansion, so `"url": "%HELP_URL%"` is **rejected**.
`"url": "https://%HOST%/help"` is fine — the variable may appear anywhere after
the scheme.

Validating after expansion instead would allow the fully-variable form, but it
moves the failure from menu-build time to click time and gives up the
disabled-item-with-tooltip treatment every other invalid addon gets. Not worth
it for a form nobody has asked for.

### No post-expansion re-check

None of the allowed prefixes contains a `%`, and expansion only replaces `%NAME%`
occurrences, so expansion **cannot** remove or alter the scheme that validation
already accepted. A second check after expansion would be redundant rather than
defence in depth, and is deliberately omitted.

## Launch path

Click → expand `url` through `AddonEnvironment` → `ShellLink.TryOpen`.

```
VeneerMenu  case "url"  ->  AddonLauncher.LaunchUrl(addon, context, log)
                              AddonEnvironment.BuildEffective + Expand
                              ShellLink.TryOpen
```

### The panel is not force-opened

`LaunchAddon` calls `WebServerStatusControl.Launch()` when `Control` is null, to
obtain `Control.Port`. For a wiki link that side effect is unwanted, so
`AddonContext.Port` instead comes from `Control?.Port` falling back to
`WebServerStatusControl.DefaultPort`. This matches what `Launch` already does for
HTML reports, which reads a static rather than opening the panel.

That has a consequence: `ControlAddonLog` dereferences its control, so with the
panel closed there is no usable sink. A second implementation, `SourceAddonLog`,
writes only to `TIME.Management.Log`, and `VeneerMenu` picks between them.
Errors already went to Source's log via `ControlAddonLog`, so nothing is lost
when the panel is shut — only the `Debug`-level chatter, which has nowhere to go
anyway.

### `ShellLink`

```csharp
internal static class ShellLink
{
    public static bool TryOpen(string url, out string error);
}
```

The only place in the codebase that sets `UseShellExecute = true`. It returns a
bool rather than throwing because **every** caller is a WinForms `Click`
handler, where an escaping exception becomes a dialog in Source.

`Process.Start(ProcessStartInfo)` may return null when an existing handler
process absorbs the request, so the result must not be dereferenced.

## Structure

| File | Responsibility |
|---|---|
| `Addons/AddonUrl.cs` | **New.** The scheme allowlist as a pure static. No RiverSystem, TIME or WinForms dependency. |
| `DomainActions/ShellLink.cs` | **New.** The single `UseShellExecute = true` call, guarded. |
| `Addons/VeneerConfiguration.cs` | `url` field; four validation rules; widened "nothing to run" rule. |
| `DomainActions/AddonLauncher.cs` | `LaunchUrl` entry point. |
| `VeneerMenu.cs` | `case "url"`; `SourceAddonLog`; the two call sites; the report port. |

`AddonUrl` goes in `Addons/` rather than `DomainActions/` for a concrete reason:
`VeneerAddon.Validate` needs the scheme rule, and `DomainActions` already depends
on `Addons` (`AddonLauncher` uses `VeneerAddon`). Placing it in `DomainActions`
would point that dependency both ways.

`LaunchUrl` is a **separate entry point**, not a branch inside
`AddonLauncher.Launch`. `Launch` opens with a hard `ProjectDirectory` guard and
builds a full child-process environment; a URL needs neither, and a wiki link
routed through it would fail with "no project directory is available".

## The two existing call sites

| Site | Before | After |
|---|---|---|
| `VeneerMenu.cs:266` (`Launch`) | `Process.Start(url)` | `ShellLink.TryOpen`, failure logged |
| `VeneerMenu.cs:160` (logo) | `Process.Start("http://www.flowmatters.com.au")` | same |

Failures log through `TIME.Management.Log.WriteError` directly rather than
`LogOnce`. `LogOnce` de-duplicates by message and is cleared only on project
change, which is right for menu-build-time spam and wrong for a click: a user
clicking a broken link twice should see two failures, not one.

### The report port

`Launch` starts using `Control?.Port ?? WebServerStatusControl.DefaultPort`
instead of the `SourceRESTfulService.DEFAULT_PORT` constant.

This is a **behaviour change for users who are not adopting this feature**:
report links begin honouring the configured port. It is included because the
method is being rewritten anyway and leaving a known-wrong port immediately above
the line being fixed would be indefensible. Listed under Risks.

## Testing

New fixture `Tests/AddonUrlTests.cs`, NUnit, `Assert.That(..., Is.EqualTo(...))`,
matching `Tests/SchematicNameSanitiserTests.cs` and the `Addon*Tests.cs` fixtures.

| Area | Cases |
|---|---|
| Accepted | `http://`, `https://`, `mailto:`; mixed case (`HTTPS://`, `MailTo:`); leading and trailing whitespace; `%VAR%` appearing after the scheme |
| Rejected | `file://`; `C:\Windows\System32\cmd.exe`; `\\server\share\tool.exe`; `ms-msdt:/id`; bare `wiki.example.org`; `https:/` (one slash); `httpsx://`; a fully-variable `%HELP_URL%`; null; empty; whitespace-only |

Added to the existing `Tests/AddonValidationTests.cs`: each of the four new
rules; a valid URL addon returning null; and confirmation that the widened
"nothing to run" rule no longer fires for a URL addon while still firing for an
entry with none of the three.

The `C:\…\cmd.exe` and `%HELP_URL%` rows are load-bearing — they are the two
cases that distinguish this implementation from the `Uri`-based one, and both
pass trivially under a naive "does it look like a URL" check.

`ShellLink.TryOpen` cannot be unit-tested without launching a browser, so it is
verified manually:

1. A `type: "url"` addon opens the wiki page in the default browser.
2. A `mailto:` addon opens the mail client.
3. A `file://` addon is **greyed out** with a tooltip, not launched.
4. An HTML report item opens, on the **configured** port rather than 9876.
5. The Veneer logo item opens flowmatters.com.au.
6. A URL addon works with the Veneer panel closed, and does not open it.

Checks 4 and 5 are the regression tests for the `UseShellExecute` bug; they
should fail on `master` before this work.

## Documentation

- `docs/veneer-file-format.md` — `url` in the addon field table; a
  `type: "url"` section covering the allowlist, the literal-scheme rule and
  `%VAR%` expansion; a note that `file://` is deliberately excluded.
- `Samples/addons/` — a `link-menu.rsproj.veneer`, following
  `exe-with-args.rsproj.veneer` and `inline-script.rsproj.veneer`, and a line in
  that directory's README.

## Porting to `legacy_ci`

The `UseShellExecute = true` assignment is a **no-op** on that branch — .NET
Framework 4.8 already defaults it to `true`, which is exactly why the two call
sites work there and not on `master`. Setting it explicitly is still correct and
keeps the branches identical.

`AddonUrl.cs` and `ShellLink.cs` need `<Compile Include>` entries in the
non-SDK-style csproj, as does `Tests/AddonUrlTests.cs`.

Note that branch does not currently build in this environment — pristine
`legacy_ci` fails with a pre-existing `MC1000` WPF markup-compiler error,
unrelated to any of this. Plan for verification by inspection plus a C# 7.3
`csc` compile check of the new files, as was done for the menu-ordering port.

## Decisions taken

| Decision | Chosen | Rejected |
|---|---|---|
| Schema shape | `url` field with `type: "url"` | overload `path`; infer from `url` with no type |
| Allowed schemes | `http://`, `https://`, `mailto:` | adding `file://`; http/https only; no restriction |
| Scheme matching | literal case-insensitive prefix on the trimmed string | `Uri.TryCreate` + `Scheme` check |
| When validated | before expansion, at menu-build time | after expansion, at click time |
| Post-expansion re-check | none — provably redundant | re-check as defence in depth |
| Variable expansion | yes, via existing `AddonEnvironment` | none; expansion only when the panel is open |
| Port source | `Control?.Port ?? DefaultPort` | force the panel open to read `Control.Port` |
| Launch entry point | separate `AddonLauncher.LaunchUrl` | a branch inside `AddonLauncher.Launch` |
| `AddonUrl` location | `Addons/` | `DomainActions/`, which inverts an existing dependency |
| Existing broken sites | fixed, sharing `ShellLink` | left for separate work |
| Report port | fixed to the configured port | left hard-coded at 9876 |

## Risks

**Report links change port.** Anyone running Veneer on a non-default port has
had broken report links; they now work. Anyone who had come to depend on the
links always pointing at 9876 — for instance with a second tool listening there
— would see a change. Judged very unlikely and strictly a fix, but it is a
behaviour change for users who never asked for URL addons.

**The `UseShellExecute` diagnosis is inferred, not observed.** The runtime
default was verified directly; the resulting failure in Source was not. If
report and logo links turn out to work on `master` today, the diagnosis is wrong
somewhere and manual checks 4 and 5 will say so. The change remains correct
regardless, but the framing in this document would need revisiting.

**`mailto:` depends on a registered handler.** On a machine with no mail client,
the shell raises an error that surfaces as a logged addon failure. That is the
designed behaviour, not a defect, but it will look like a Veneer bug to the user
who hits it.
