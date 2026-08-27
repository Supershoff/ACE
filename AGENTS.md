## Agent skills

### Issue tracker

Issues and implementation work are tracked in GitHub Issues on `Supershoff/ACE`. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the canonical `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix` labels. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository. Read `CONTEXT.md` and applicable ADRs under `docs/adr/`. See `docs/agents/domain.md`.

### Code review

Codex code and pull-request reviews must use the installed `code-review-skill`. Review the linked issue and acceptance criteria first, then apply the relevant C#/.NET, TypeScript/React, architecture, security, performance, concurrency, and universal-quality guidance. Validate material findings with focused tests, builds, or static checks where practical, and prioritize actionable correctness, custody-invariant, security, and regression findings over formatting preferences.
