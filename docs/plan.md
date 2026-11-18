# XafGantt POC plan

Goal: see how the open-source `Veto.Gantt` Blazor component (MIT, github.com/G-HK/Veto) behaves as a
list editor inside a DevExpress XAF Blazor application, and collect the bugs and rough edges we hit.

XAF Blazor has no Gantt list editor (dxdocs "List Editors", 113189: Grid, TreeList, Scheduler, Chart,
PivotGrid only). The integration pattern is the documented one: "How to: Use a Custom Component to
Implement List Editor (Blazor)", dxdocs 403258.

## Stack (same as the sibling POCs, e.g. XafReportScheduler)

- XAF 26.1 (the version the installed `dx.xaf` template produces; all DX packages on one version),
  Blazor Server, .NET 10, EF Core, SQL Server LocalDB catalog `XafGantt`, password security.
- `Veto.Gantt` 1.0.7 from nuget.org, pinned. Its nuspec targets net10.0 and depends on Veto.Core,
  Buttons, Feedback, Icon, Menu, TextBox and `Microsoft.AspNetCore.Components.Web >= 10.0.14`.
- No fork yet. Upstream `dev` has moved to net11.0, so a later fork used as a project reference must
  branch from the 1.0.7 release commit `2744786487a400601583492610c1211993f9eefb`.

## Solution layout

```
XafGantt.slnx / .sln
XafGantt.Module/            domain + IGanttTask; no Veto reference
XafGantt.Blazor.Server/     Veto.Gantt reference, list editor, wrapper component
XafGantt.E2ETests/          console app, C# Playwright (phase gate)
docs/plan.md, docs/veto-findings.md, README.md, CLAUDE.md
```

Scaffold: `dotnet new dx.xaf -n XafGantt -orm EFCore -db SqlServer -p Blazor -s Password -m Default`.

## Module

`IGanttTask` (the editor works against this, so any entity can opt in):

```csharp
public interface IGanttTask {
    string GanttId { get; }             // unique, stable; the entity key as string
    string GanttName { get; }
    DateTime GanttStart { get; set; }   // inclusive
    DateTime GanttEnd { get; set; }     // inclusive day when time-of-day is 00:00
    string GanttParentId { get; }       // null for a root
    double? GanttProgress { get; }      // 0..1
    string GanttCategory { get; }
    bool GanttIsMilestone { get; }
    IEnumerable<string> GanttDependsOn { get; }
}
```

`ProjectTask : BaseObject, IGanttTask` (EF Core, all properties virtual, collections initialised):
`Name`, `StartDate`, `EndDate`, `Progress` (0..100 int), `Category` (string), `IsMilestone`,
`Parent` / `Children` (self reference), `Predecessors` / `Successors` (self-referencing many-to-many,
explicit `UsingEntity` config), `Description`. Validation rule: `EndDate >= StartDate`.

Seeding (ModuleUpdater, only when the table is empty): about 15 tasks relative to today in two
projects, three levels deep, with dependencies, one milestone, mixed progress and categories.
Users: `Admin` (full) and `Viewer` (read-only on ProjectTask), both with an empty password in DEBUG.

## Blazor.Server

1. `GanttListView.razor`: thin wrapper that renders `<VetoGantt>`; parameters `Items`,
   `SelectedId`, `Editable`, `ColorScheme`, `OnItemChanged`, `OnItemSelected`, `OnOpen`.
   `PersistKey` stays null: with a key the chart keeps per-user overrides in the browser and loads
   pglite from the jsdelivr CDN; the ObjectSpace is our store.
   `View = Schedule` so the whole plan opens; `ItemContextMenu` offers "Open".
2. `GanttListViewModel : ComponentModelBase` with the same properties.
3. `GanttListEditor : ListEditor, IComponentContentHolder, IControlOrderProvider, IComplexListEditor`,
   `[ListEditor(typeof(IGanttTask), false)]` (not the default editor; confirm the two-argument
   attribute in the installed source before relying on it).
   - `AssignDataSourceToControl`/`Refresh`/`BindingList_ListChanged`: map `IGanttTask` objects to
     `VetoGanttItem` records and keep a `Dictionary<string, IGanttTask>` by id.
   - Mapping: `GanttEnd` at midnight is an inclusive whole day (so `End = date`, no `EndTime`);
     the `VetoGanttItem(DateTime, DateTime)` constructor is NOT used because it treats a midnight
     end as exclusive. A parent id or dependency id that is not in the current list is dropped.
     End before start is clamped to start (and noted in findings).
   - Selection: `OnItemSelected` sets the selected object and raises `OnSelectionChanged`;
     `SelectionType.SingleObject`... (the value XAF offers for single selection; confirm in source).
   - Open: context-menu "Open" raises `OnProcessSelectedItem` (opens the DetailView).
   - Drag (`OnItemChanged`): look up the object, shift `GanttStart`/`GanttEnd` by the reported day
     difference keeping the time of day, `ObjectSpace.CommitChanges()`. On a validation or security
     exception: `ObjectSpace.Rollback()`/reload, refresh the chart to the stored dates, show the
     message via the XAF error handling service. Commit happens per drop (no Save button).
   - `Editable` = the current user may write `ProjectTask` (XAF security check for the type), so
     `Viewer` gets a read-only chart.
   - `BreakLinksToControls` unsubscribes and clears the data.
4. `DataAccessModeHelper.RegisterEditorSupportedModes(typeof(GanttListEditor), Client)` in the
   Blazor module.
5. Model: a second view `ProjectTask_ListView_Gantt` with `EditorType = GanttListEditor` and its own
   navigation item "Tasks (Gantt)"; the generated grid `ProjectTask_ListView` stays as "Tasks".
6. Host page: `<link rel="stylesheet" href="_content/Veto.Core/VetoStyles.css" />`.
7. Theme: `ColorScheme` is set explicitly to Light or Dark from the active XAF theme (how XAF 26.1
   exposes light/dark is to be verified in dxdocs or the installed source; if there is no clean
   signal, the finding is recorded and the chart stays Light).

## Changes after the plan review (2026-11-18, in-session reviewer; Gemini and Codex out of quota)

These override the text above where they differ. All ten were checked against the sources.

1. `SelectionType.Full` (there is no `SingleObject`; `FocusedObject` alone deactivates Open/Delete).
   `GetSelectedObjects()` returns zero or one object; the `FocusedObject` setter pushes `SelectedId`.
2. VetoGantt keeps its own date overrides after a drag even with `PersistKey` null
   (`_overrides`, cleared only by `ResetAsync()`). The wrapper holds an `@ref` and calls
   `ResetAsync()` after every `OnItemChanged` round trip, success or failure, so the chart always
   shows what the ObjectSpace holds.
3. A change is applied by `Edit` kind from `change.Start`/`change.End` (plus the stored time of
   day), not by `change.Days`: `Moved` sets both, `StartChanged` start only, `EndChanged` end only.
   The chart clamps a resize at start = end, so the validation-rejection path is exercised with a
   second rule instead: a task cannot start in a weekend.
4. Context-menu "Open" first selects the right-clicked item, raises `OnSelectionChanged`, then
   `OnProcessSelectedItem`. Menu entries are `VetoMenuItem`s.
5. Veto treats every item with a child as a summary row: not draggable, and drawn from its own
   dates (no roll-up). Milestones are not draggable either. The editor therefore maps a parent's
   bar to the min start / max end of its descendants (display only; stored parent dates are not
   touched). Only leaf, non-milestone tasks move.
6. `View` and `Scale` are two-way bound to the component model (a constant would undo the user's
   switch on every re-render); `SelectedId` is written back in `OnItemSelected`.
7. `Editable` comes from `DataManipulationRight.CanEdit` for the list view's object type, checked
   in `Setup`/on data assignment. Object-level denials surface at commit and are rolled back.
8. `ProjectTask` implements `IGanttTask` explicitly, so no extra columns or view members appear.
9. `TestId="gantt"` on the chart: bars are `gantt-bar-{id}` with `data-editable`; the pointer test
   waits for `data-draggable="true"` on the root, grabs within 6 px of the right edge to resize,
   and measures a header cell for the day width.
10. `Scales` restricted to Day, Week, Month.

Also: catch every exception in the change handler, use `ObjectSpace.Rollback(false)`.

Added cases: rejected drag returns the bar; drag, then change the row elsewhere, then XAF Refresh
shows the stored dates; two consecutive drags accumulate; parent and milestone bars are not
draggable; a task with a time of day keeps it through a move; click-to-select on an editable bar
(unverified whether the drag script swallows the click).

Unverified until tried: which stylesheets beyond `VetoStyles.css` are needed, whether Veto.Core
needs service registration, self-referencing many-to-many in XAF EF Core, `_content` assets when
the E2E starts the built exe.

## What the POC must answer (each gets a line in docs/veto-findings.md)

1. Does the package restore and build inside an XAF 26.1 net10 Blazor Server app without warnings?
2. Do the Veto stylesheet and tokens leak into or clash with the DevExpress theme, in a light and a
   dark XAF theme?
3. Do drag-move and drag-resize work over the Blazor Server circuit, and do they survive a re-render
   after commit (no bar jumping back, no lost selection)?
4. Does a change made in the grid view or a DetailView show up in the Gantt after returning?
5. Do tooltips, the export menu and the context menu layer correctly inside the XAF shell
   (z-index against the XAF header, navigation and popups)?
6. Behaviour with 0 tasks, and with about 500 tasks (render time, drag latency).
7. Is the chart read-only for a user without write permission?
8. Culture: Dutch browser/server culture for dates in headers, tooltips and CSV export.

## Cases the editor must handle (test or code for each)

- empty data source; null data source after `BreakLinksToControls`
- task whose parent or predecessor is filtered out of the list
- end date before start date in stored data
- milestone (start = end)
- drag by a user without write permission (chart not editable; a forced change is rejected)
- drag that breaks the validation rule (resize end before start): rejected, chart restored
- two drags in a row on the same bar; drag immediately after a refresh
- object deleted in another view while the Gantt is open (id lookup misses: ignore and refresh)
- selection of a task, then Delete via the XAF toolbar
- nested Gantt (a `Children` collection on the DetailView) is out of scope: the editor is only
  assigned to the root view

## E2E gate (XafGantt.E2ETests, console app, exit 0/1)

Starts the built app on port 5110 after checking the port is free, health-checks it, and stops it.

1. Login as Admin, open "Tasks (Gantt)": the chart root and at least 10 bars are present.
2. Screenshot in the default light theme and after switching to a dark XAF theme.
3. Keyboard move: focus a leaf bar, press ArrowRight once, then assert through the "Tasks" grid (or
   a direct DbContext read) that start and end both moved by one day.
4. Pointer drag-resize of a bar end by two day columns; assert the stored end date.
5. Context menu "Open" shows the DetailView of that task.
6. Login as Viewer: bars present, ArrowRight changes nothing in the database.

## Out of scope

Auto-scheduling of successors, critical path, resources, WinForms, a reusable NuGet module,
nested list views, server/instant-feedback data access modes.

## Order of work

1. Scaffold, build, run once (database created).
2. Module: interface, entity, DbContext config, seeding, roles.
3. Add Veto.Gantt, stylesheet link; build (answers question 1).
4. Wrapper component, component model, list editor, model view + navigation.
5. Manual smoke with screenshots, then the E2E project.
6. Findings file, README, CLAUDE.md, handoff.
