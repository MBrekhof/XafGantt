# Veto.Gantt 1.0.7 in XAF Blazor: findings

Measured 2026-11-18 on XAF 26.1, .NET 10, Blazor Server, Chromium (Playwright), SQL Server LocalDB.
Evidence is the E2E gate (`XafGantt.E2ETests`) unless a line says otherwise. Plan reviewed by an
in-session reviewer (Gemini and Codex were out of quota).

## Verdict

It works. The chart renders inside the XAF shell in the Fluent light and dark themes, drag and
keyboard rescheduling commit through the ObjectSpace, validation and security are respected, and
500 tasks render in under a second. Two Veto issues needed work: V1 is fixed in our fork, V2 has a
workaround in the wrapper.

## Answers to the POC questions

| # | Question | Answer |
|---|---|---|
| 1 | Builds in an XAF 26.1 net10 app? | Yes, no Veto warnings. Package targets net10.0. |
| 2 | Styling clash with DevExpress? | None seen in Fluent light and dark. Veto keeps its own indigo accent; it does not follow the DX accent colour. Classic themes not tried. |
| 3 | Drag over the Blazor Server circuit? | Works: move, resize, keyboard. A commit is in the database about 0.2 s after the key press (500 tasks). |
| 4 | Change made elsewhere shows up? | Yes after XAF Refresh, once the wrapper clears Veto's overrides (V2). Edit in a Detail View and return: not tested. |
| 5 | Layering inside the XAF shell? | Context menu and XAF's error toast display correctly. Tooltips and the export menu not checked by eye. |
| 6 | 0 and 500 tasks? | 0: "No tasks match these filters." 500: chart rendered 0.9 s after navigation (2.4 s first time), 4,200 DOM nodes, no virtualisation. Scratch measurement, not in the gate. |
| 7 | Read-only for a user without write permission? | Yes, but only because the editor checks it itself (X2). |
| 8 | Dutch culture? | Not tested. |

## Veto issues

**V1. A mouse click does not select a draggable bar.** Bug in 1.0.7, **fixed in the fork**
(`github.com/MBrekhof/Veto`, branch `fix/click-selects-draggable-bar`): the pointer is now captured
once it has moved 3 px instead of on the press. Veto's 114 Gantt specs pass with it, plus two new
ones; this POC runs on that build (`local-packages/Veto.Gantt.1.0.8-xafgantt.2.nupkg`) and its gate
asserts the click. Not offered upstream yet. The original finding: With `Editable="true"`, clicking a leaf
bar leaves the selection where it was; clicking a summary bar, or Enter on a focused bar, selects.
Reproduced in the gate and on Veto's own demo site (`/docs/components/gantt`, the editable chart),
so it is not an XAF interaction. Likely cause, read in `veto-gantt-drag.js`: pointer-down captures
the pointer on the grid, so the click never reaches the bar's button. Consequence here: XAF's
Delete and the like cannot be aimed at a task with the mouse; right-click, then Open, still works.

**V2. The chart keeps a dragged bar's dates as its own override, with no way for the host to accept
or reject one change.** Design gap. `_overrides` is filled on every drag, also without `PersistKey`,
and laid over `Items` on every render; only `ResetAsync()` clears it (all rows at once). A host that
stores the change itself must call `ResetAsync()` after every `OnItemChanged`, or a rejected drag
stays where it was dropped and later data changes are masked. The wrapper does this.

**V3. Bar labels on bars with 0 % progress are hard to read**, in light and in dark (white on a pale
bar). Seen in the screenshots; contrast ratio not measured.

**V4. Summary rows are drawn from the parent's own dates and never roll up their children**, and
summary rows and milestones cannot be dragged. Design. The mapper works around the first by giving
a parent the span of its descendants (display only).

**V5. A drag reports whole days only**, also for tasks with a time of day. Design. The editor keeps
the stored time of day.

**V6. `PersistKey` loads pglite from the jsdelivr CDN by default** (`PersistenceModuleUrl`). Design.
Not used here; do not set it in an intranet app without changing that URL.

**V7. Upstream `dev` now targets net11.0.** A fork used as a project reference from a net10 XAF app
must branch from the 1.0.7 release commit `2744786487a400601583492610c1211993f9eefb`.

Read in the source, not reproduced:

- The task names in the left panel are not clickable (only bars select), and there is no
  click-to-deselect.
- A task dated 1 January 0001 (saved without dates) makes the chart throw while building its
  timeline, and the Day scale draws one column per day, so a typo year builds tens of thousands of
  columns. The mapper leaves tasks outside today plus or minus ten years off the chart; the gate
  checks that an undated task no longer breaks the view, but was not run without that guard.
- Rows in a parent cycle have no root and are dropped silently. The mapper makes cycle members
  roots, and a Save rule forbids creating a cycle.

## XAF-side lessons (what the integration needed)

**X1. Two stylesheet links in `_Host.cshtml`**: `_content/Veto.Core/VetoStyles.css` and the app's own
`XafGantt.Blazor.Server.styles.css`. The second is commented out in the XAF template, and without it
the Veto packages' scoped CSS is missing.

**X2. The List View does not tell the editor that the user may not write.** `ListEditor.AllowEdit`
carried only the model setting; for the read-only user every bar was still draggable until the
editor asked `IRequestSecurityStrategy.CanWrite(type, objectSpace)` itself.
`DataManipulationRight.CanEdit(type, "", null, ...)` returned true for that user.

**X3. `SelectionType.Full`**, with zero or one selected object. `FocusedObject` alone deactivates
Delete and Open.

**X4. Dark mode**: XAF's `IsDark` is internal. The editor derives it from `IThemeService` (Fluent
mode, or "dark" in a classic theme's URL) and passes `ColorScheme` explicitly.

**X5. The chart needs an explicit height** (`calc(100vh - 15rem)` here); otherwise the page scrolls.

**X6. Commit per drop**: `OnItemChanged` sets the dates and calls `CommitChanges()`; on any exception
`Rollback(false)` and XAF's `IExceptionHandlerService` shows the message. Validation runs on commit
in a List View as well.

**X7. Self references in EF Core on SQL Server** (parent/children and a many-to-many of the task with
itself) need `ClientSetNull` / `ClientCascade`; both created and worked.

**X8. Dependencies cost one query per task**: the mapper reads `Predecessors` on every task, which
lazy-loads per row (500 tasks, 500 queries; still 0.9 s on LocalDB). Eager-load before using this
against a remote database.

## Not covered

Dutch culture; tasks with a time of day; nested list view (a `Children` tab on the Detail View);
classic DevExpress themes; tooltips and export menu by eye; edit in a Detail View and return;
a task deleted in another session while the chart is open; data access modes other than Client.
