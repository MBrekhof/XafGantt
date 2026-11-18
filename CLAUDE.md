# CLAUDE.md

POC: Veto.Gantt (github.com/G-HK/Veto, MIT) as an XAF Blazor list editor. See `README.md` for what
it does and how to run it, `docs/veto-findings.md` for results, `docs/plan.md` for the plan.

## Build / run / E2E

```bash
dotnet build XafGantt.sln
dotnet run --no-build --project XafGantt.Blazor.Server -- --updateDatabase --silent   # once, creates + seeds
dotnet run --no-build --project XafGantt.Blazor.Server                                # https://localhost:5001
dotnet run --project XafGantt.E2ETests                                                # gate, app on :5110, exit 0/1
```

Login `Admin` or `Viewer` (read-only), empty password, DEBUG builds only.

## Non-negotiables

- EF Core only, never XPO. All DevExpress packages on 26.1.
- `XafGantt.Module` must not reference Veto: the editor works against `IGanttTask`.
- `Veto.Gantt` is `1.0.8-xafgantt.3`, built from the fork `C:\Projects\Veto`
  (github.com/MBrekhof/Veto, branch `fix/click-selects-draggable-bar`, based on the 1.0.7 release
  commit because upstream `dev` is net11.0) and committed in `local-packages/`; `nuget.config` adds
  that folder as a source. To rebuild it after a change in the fork: set `<Version>` in
  `src/Components/Veto.Gantt/Veto.Gantt.csproj` to the next `1.0.8-xafgantt.N`, run
  `dotnet pack src/Components/Veto.Gantt/Veto.Gantt.csproj -c Release -o C:\Projects\XAFGantt\local-packages`,
  revert the version change in the fork, and bump the reference in `XafGantt.Blazor.Server.csproj`.
- Changes to Veto itself are made in the fork's own checkout, not from here.
- Verify DevExpress behaviour in dxdocs or the installed source, and Veto behaviour in its source;
  never from memory.
- The wrapper must call `ResetAsync()` after every `OnItemChanged` (findings V2). Do not remove it.
- Upstream issues and PRs to G-HK/Veto need the owner's explicit go, each one.

## Task state

No board project yet; open items are in `docs/veto-findings.md` ("Not covered") and
`SESSION_HANDOFF.md`.
