# Veneer menu ordering

## Problem

Menus declared in a `.veneer` file appear in Source's menu bar in **alphabetical
order**, not in the order they appear in the file. A user specifying two menus
saw the second one first.

`RequiredMenus` (`ReportingMenu.cs:236-270`) builds the list of top-level menus,
and `InitialiseRequiredMenus` (line 226) creates them in that order via
`MainMenuStrip.Items.Add`, which appends. That list therefore *is* the menu-bar
order. It has three defects:

1. **Line 261 — `addonMenus.OrderBy(m => m)`.** An explicit alphabetical sort.
   This is the reported bug.
2. **Line 242 — `HashSet<string>`.** Even with the sort removed, a `HashSet`
   carries no insertion-order guarantee.
3. **Lines 255-258 — `Reporting` is emitted first** whenever it has content, and
   `HasMenuContent` (line 272) reports content whenever *any* `.htm*` file sits
   in the project directory. So a stray HTML file pins `Reporting` to the front
   even when no addon names it.

Ordering *within* a menu is already correct and is not the problem: line 89
filters the `config.addons` array, which preserves file order, and
`FindOrCreateNestedMenu` (line 157) appends submenus on first appearance. Only
the top-level bar is affected.

### Line reference convention

Line numbers cite `ReportingMenu.cs` as it stands at commit `23990b5`. The
addon-launch-modes work renames the class to `VeneerMenu` and the file to
`VeneerMenu.cs`, and lands first (see Sequencing). This spec is written against
the post-rename name; the line references are to the pre-rename file because
that is what is readable today.

## Scope

In scope: top-level menu order; restricting HTML report items to the `Reporting`
menu; addons before reports inside `Reporting`; logo placement; blank menu path
segments; and `ClearMenu` removing the menus Veneer actually created.

Out of scope: the REST API. No endpoint references `VeneerAddon` — it appears
only in `ProjectLoadListener`, `VeneerMenu`, `WebServerStatusControl` and its own
definition file. Consequently **no `PROTOCOL_VERSION` bump** and **no
`docs/api/` change**. `docs/veneer-file-format.md` does require updating.

Also out of scope, and deliberately so: making menu name matching
case-insensitive, and populating addons under a menu name that collides with one
of Source's own. Both are discussed below.

## Ordering rules

### Top-level bar

One rule: **first appearance in `addons`, then `Reporting` if it has not already
appeared.**

| `addons` menus | `.htm*` present | Resulting bar |
|---|---|---|
| `Modelling`, `Analysis` | no | `Modelling`, `Analysis` |
| `Modelling`, `Analysis` | yes | `Modelling`, `Analysis`, `Reporting` |
| `Modelling`, `Reporting`, `Analysis` | either | `Modelling`, `Reporting`, `Analysis` |
| `Analysis`, `Modelling`, `Analysis` | no | `Analysis`, `Modelling` |
| none | either | `Reporting` |

Menus the file never mentions cannot claim a position within file order, so
`Reporting` is appended last when it exists only to hold discovered HTML reports.
When an addon *does* name it, it takes its file position like any other menu.

`Reporting` is always created when nothing else is, so that the logo item and any
HTML reports have a home. This preserves today's behaviour for a project with no
`.veneer` file at all.

Row 4 is unchanged behaviour stated explicitly: duplicate menu names collapse,
and the first appearance sets the position.

Row 2 is worth contrasting with the case where there are no HTML reports *and* no
addon names `Reporting`: there, `Reporting` is not created. That is also today's
behaviour (`HasMenuContent` false, `hasOtherMenus` true) and is unchanged.

### Within a menu

**Addons in file order, then discovered HTML reports.** Anything the file
specifies precedes anything auto-discovered, mirroring the top-level rule.

This reverses the current arrangement, where the HTML block (lines 76-83) runs
before the addon block (lines 90-118).

### HTML reports belong to `Reporting` only

`PopulateReportMenu` enumerates the project directory's `.htm*` files
unconditionally, whatever `mnu` it was called with, so **every** Veneer top-level
menu receives the same report list. A file specifying `Modelling` and `Analysis`
gets the reports duplicated into both, above the addons.

The HTML enumeration becomes conditional on `mnu == MenuLayout.DEFAULT_MENU`.

This is a fix rather than a change of specified behaviour:
`docs/veneer-file-format.md:85` already states that HTML files are "automatically
added to the `Reporting` menu". The code is being brought into line with its
documentation.

### Logo placement

The Veneer logo item goes at the bottom of the **last** menu in the bar,
replacing the `requiredMenus[0] == mnu` test at line 132.

## What ordering cannot control

**Menus reused from Source.** `FindOrCreateReportMenu` (line 52) matches by
`Text` against the existing menu bar, so a `.veneer` menu named `Tools` binds to
Source's own `Tools` menu wherever Source put it. File order governs only the
menus Veneer creates, which are appended after Source's own. This is inherent to
matching by name and is documented rather than fixed.

**Case.** `Analysis` and `analysis` are distinct menus today, because
`item.Text == mnu` is an ordinal comparison. First-appearance de-duplication uses
the same ordinal comparison, so this behaviour is unchanged: two menus, in file
order. Folding them together would be a separate and arguable change, and doing
it here would mean picking which casing wins.

## Structure

### `Addons/MenuLayout.cs` (new)

```csharp
public static class MenuLayout
{
    public const string DEFAULT_MENU = "Reporting";

    public static string[]     SplitMenuPath(string menuPath);
    public static string       TopLevelMenu(string menuPath);
    public static List<string> TopLevelMenus(VeneerAddon[] addons, bool hasHtmlReports);
}
```

`SplitMenuPath` and `GetTopLevelMenu` (renamed `TopLevelMenu`) move here from the
menu class, and `DEFAULT_MENU` moves with them so there is a single definition.
`TopLevelMenus` implements the rule table above.

`VeneerMenu` retains the I/O — loading the config and probing the project
directory for `.htm*` files — and passes primitives in. That is what makes the
ordering reachable from tests without a loaded scenario, and it is the same
boundary, drawn for the same reason, as `AddonLauncher` in the addon-launch-modes
spec.

The class must not depend on RiverSystem or TIME types. `VeneerAddon` is already
a plain POCO, so this is achievable. Note that `Addons/VeneerConfiguration.cs`
carries `using RiverSystem` for its `RiverSystemScenario` overloads;
`MenuLayout` goes in a separate file precisely so it does not inherit that.

`hasHtmlReports` is a required argument rather than something `TopLevelMenus`
derives, because the rule table depends on it: with no HTML reports and no addon
naming `Reporting`, the menu is not created.

### `HasMenuContent` disappears

`HasMenuContent` (line 272) did two jobs — "are there HTML files" and "does any
addon target this menu". The first becomes the `hasHtmlReports` argument; the
second becomes a check inside `TopLevelMenus`. It is only ever called with
`DEFAULT_MENU`, so nothing else needs the general form.

### Two fields on `VeneerMenu`

- **`_menuLayout`** — the ordered `List<string>` returned by `TopLevelMenus`,
  computed once in `InitialiseRequiredMenus`.
- **`_createdMenus`** — a `List<ToolStripMenuItem>` of the items Veneer actually
  added to `MainMenuStrip`, appended in `FindOrCreateReportMenu`'s create branch.

`ClearMenu` removes the `_createdMenus` references and clears both fields.

The logo test reads `_menuLayout` rather than recomputing. `PopulateReportMenu`
falls back to recomputing when `_menuLayout` is empty. This fallback is
defensive rather than reachable — `DropDownOpening` can only fire on a menu that
exists, which implies `InitialiseRequiredMenus` ran — but `_menuLayout` is
instance state on a singleton that `ClearMenu` empties, so the method should not
assume it is populated.

### Why `_createdMenus` holds references, not names

`ClearMenu` currently locates items to remove by **matching `Text`** across the
whole of Source's menu bar (lines 218-222). Combined with the name-collision
case above, a `.veneer` file containing `"menu": "Tools"` makes Veneer delete
*Source's own* `Tools` menu on the next scenario change. Holding
`ToolStripMenuItem` references means Veneer can only remove what it created.

A second, milder consequence of the same collision is left alone deliberately:
`DropDownOpening` is wired only in the create branch (line 58), so addons
targeting an existing Source menu are never populated. That silence is the
*safe* outcome, because `PopulateReportMenu` opens with `DropDownItems.Clear()`
(line 69), which on a Source-owned menu would wipe Source's own items. Making
such addons work requires not clearing a menu Veneer does not own, which is its
own piece of work. Documented as unsupported.

### Blank menu path segments

`SplitMenuPath` applies `RemoveEmptyEntries` **before** `Trim`, so
whitespace-only segments survive as empty strings. Verified:

```
"   "               -> [""]
"Modelling| |Sub"   -> ["Modelling", "", "Sub"]
"|"                 -> []
"Modelling|"        -> ["Modelling"]
""                  -> caught by the IsNullOrEmpty guard -> ["Reporting"]
```

So `"menu": "   "` produces a blank-titled top-level menu, and
`"menu": "Modelling| |Sub"` a blank-titled submenu between two real ones. The
`IsNullOrEmpty` guard at line 143 does not catch the first because the string is
not empty, only whitespace.

The fix is to drop empty entries *after* trimming, and fall back to
`DEFAULT_MENU` when nothing survives. In scope because both defects land in the
layout this spec otherwise defines.

## Testing

Tests go in `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs`, as an NUnit
fixture in namespace `FlowMatters.Source.Veneer.Tests` using
`Assert.That(..., Is.EqualTo(...))` — the convention established by
`Tests/SchematicNameSanitiserTests.cs` and `Tests/SchematicSvgBuilderTests.cs`.
No new project: NUnit, NUnit3TestAdapter and Microsoft.NET.Test.Sdk are already
`PackageReference`s in `FlowMatters.Source.Veneer.csproj`.

| Area | Cases |
|---|---|
| `TopLevelMenus` | every row of the rule table; `Reporting` at its file position when an addon names it; appended last when only HTML reports exist; omitted when neither applies; sole menu when `addons` is null or empty; duplicates collapse to first appearance; `Analysis`/`analysis` remain distinct |
| `SplitMenuPath` | whitespace-only path; blank middle segment dropped; `"\|"`; trailing `"\|"`; null; empty; arbitrary nesting depth preserved |
| `TopLevelMenu` | null, empty, whitespace, single segment, multi-segment |

The whitespace rows are load-bearing: every well-formed case passes against the
current implementation, and only the whitespace ones expose the
`RemoveEmptyEntries`-before-`Trim` ordering.

Menu construction, `_createdMenus` removal and logo placement require a live
Source and remain manual:

1. Two menus in a deliberately non-alphabetical order — confirm bar order
   matches the file.
2. Add an `.htm` file — confirm `Reporting` appears last, and that reports sit
   below the addons within it, and are absent from the other menus.
3. Switch scenarios — confirm no menu leaks and the order survives.
4. A `.veneer` file naming `Tools` — confirm Source's `Tools` menu survives a
   scenario change.

## Documentation

`docs/veneer-file-format.md` needs four edits:

- **Line 66** — `"Models"` is described as creating a menu "next to
  `Reporting`", which no longer describes the position.
- **Line 70** — state that bar order follows the order menus first appear in the
  file, and that `Reporting` is appended last when no addon names it.
- **Line 149** — the logo moves from the *first* top-level menu to the *last*.
- **New note** — naming an existing Source menu is unsupported.

Line 85 needs no change: it already describes HTML reports as belonging to the
`Reporting` menu.

## Sequencing

This lands as the third commit, after the `ReportingMenu` → `VeneerMenu` rename
and after the addon-launch-modes feature.

It touches `RequiredMenus`, `ClearMenu`, `PopulateReportMenu` and
`SplitMenuPath`. The addon-launch-modes work modifies none of those — it changes
the `addon.type` switch and extracts `LaunchExeAddon` into `AddonLauncher` — so
the two do not collide beyond sharing a file.

It ports to `legacy_ci` independently. `MenuLayout` is plain C# with no CoreWCF
or framework-version surface, and the `VeneerMenu` edits are WinForms, identical
on both branches.

## Decisions taken

| Decision | Chosen | Rejected |
|---|---|---|
| `Reporting` position | its file position when named, otherwise appended last | always first; always last |
| Within a menu | addons in file order, then HTML reports | reports first (today's arrangement) |
| HTML report scope | `Reporting` only | every top-level menu (today's behaviour) |
| Logo placement | bottom of the last menu | bottom of the first (today); pinned to `Reporting` |
| Structure | pure `MenuLayout` + tracked created menus | patch `RequiredMenus` in place; a menu model and renderer |
| `_createdMenus` contents | `ToolStripMenuItem` references | menu name strings (removes Source's own menus) |
| Case sensitivity | unchanged, ordinal | fold differing casings together |
| Source menu name collision | documented as unsupported | wire `DropDownOpening` onto a Source-owned menu |
| Sequencing | after the rename and addon-launch-modes | land first, independently; fold into that spec |

## Risks

**Existing projects will see their menu bar reorder.** Any project whose
`.veneer` file is not already in alphabetical order gets a different layout, and
projects with HTML reports lose the leading `Reporting` menu position. This is
the intended fix, but it is a visible change for every affected user, not only
the one who reported it.

**HTML reports disappear from non-`Reporting` menus.** A project that had come to
rely on reports being reachable from whichever menu was open will now find them
only under `Reporting`. The duplication was almost certainly unintended — the
documentation never described it — but it has presumably been in place for some
time.

**The logo moves.** It attaches to the last menu rather than the first, so users
of multi-menu projects will see it relocate. Cosmetic, and it remains
non-configurable.
