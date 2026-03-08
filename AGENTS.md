# Agent Entry Point

Start with [Docs/Project_Index.md](Docs/Project_Index.md). It is the canonical documentation map and entry point for this repo.

Required operating order:
- Obey [Docs/CODEX_CONTRACT.txt](Docs/CODEX_CONTRACT.txt) as mandatory policy.
- Use [Docs/Architecture_Guardrails.md](Docs/Architecture_Guardrails.md) for subsystem boundaries and ownership.
- When working from an explicit task, use [Docs/CODEX_TASK_TEMPLATE.txt](Docs/CODEX_TASK_TEMPLATE.txt).
- Read the relevant `Docs/Subsystems/*.md` files before editing subsystem code or subsystem docs.
- Treat [Docs/Code_Snapshot.md](Docs/Code_Snapshot.md) as orientation only; it is non-canonical.

Working rules:
- Prefer subsystem-local edits over widening central file responsibilities.
- Do not widen ownership boundaries unless the task explicitly requires it.
- Keep documentation aligned with the final repo state, including [Docs/RepoStatus.md](Docs/RepoStatus.md) when docs or behavior change.
