# Session handoff - 2026-11-18

## State

POC built and working: Veto.Gantt 1.0.7 as an XAF 26.1 Blazor list editor. The E2E gate
(`dotnet run --project XafGantt.E2ETests`) passes with 30 checks. Nothing is committed yet; the
repo is local only (`git init`, no remote).

Plan and code were each reviewed by an in-session reviewer (Gemini and Codex out of quota); all
findings from both passes are applied. Results: `docs/veto-findings.md`.

## Waiting on the owner

1. Commit the initial state, and whether to create a GitHub repo (private) for it.
2. Fork G-HK/Veto to fix V1 (mouse click does not select a draggable bar) and possibly V2
   (host-controlled changes). Fork from the 1.0.7 release commit, not `dev` (net11).
3. A ContextBoard project for XafGantt, so the findings become cards.

## Not covered yet

See "Not covered" in `docs/veto-findings.md`: Dutch culture, timed tasks, nested list view, classic
themes, tooltips and export menu by eye, Detail View edit and return, concurrent delete.
