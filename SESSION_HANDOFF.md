# Session handoff - 2026-11-18

## State

POC built and working: Veto.Gantt as an XAF 26.1 Blazor list editor. The E2E gate
(`dotnet run --project XafGantt.E2ETests`) passes with 33 checks. Repo: github.com/MBrekhof/XafGantt
(private). Open work is on the ContextBoard project XafGantt.

Veto is forked to github.com/MBrekhof/Veto, cloned at `C:\Projects\Veto`. The click-selection fix
(VETO-001) lives on `fix/click-selects-draggable-bar` (based on the 1.0.7 release) and this POC runs
on a package built from it (`local-packages/`, see CLAUDE.md for the rebuild steps).

Plan, POC code and the Veto fix were each reviewed by an in-session reviewer (Gemini and Codex were
out of quota); all findings are applied. Results: `docs/veto-findings.md`.

## Next

- Upstream PR for the click fix: https://github.com/G-HK/Veto/pull/28 (open).
- When upstream releases a Veto.Gantt that contains the click fix: go back to the nuget.org package
  and delete `local-packages/` and `nuget.config`.
- The Backlog cards: VETO-002..006 (upstream candidates, most unverified) and GANTT-001..007
  (what the POC did not cover).
