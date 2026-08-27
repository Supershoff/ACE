## Agent skills

### Issue tracker

Issues and implementation work are tracked in GitHub Issues on `Supershoff/ACE`. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the canonical `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix` labels. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository. Read `CONTEXT.md` and applicable ADRs under `docs/adr/`. See `docs/agents/domain.md`.

### Code review

Codex code and pull-request reviews must use the installed `code-review-skill`. Review the linked issue and acceptance criteria first, then apply the relevant C#/.NET, TypeScript/React, architecture, security, performance, concurrency, and universal-quality guidance. Validate material findings with focused tests, builds, or static checks where practical, and prioritize actionable correctness, custody-invariant, security, and regression findings over formatting preferences.

## Code Review Rules

### Custody authority and conservation

- Flag any path that lets browser, companion, worker, or administrative code create, destroy, materialize, or mutate native ACE biotas. Safe path: only ACE world-boundary code may move items into/out of Cloud custody or allocate child GUIDs.
- Flag any state or transaction that can permit simultaneous world and Cloud custody, non-positive or non-conserved Cloud Stack Lots, duplicate reservation/allocation, or lost GUID lineage. Safe path: database constraints plus deterministic locked validation and invariant tests.

### Transaction correctness

- Flag state changes that do not commit their Activity Ledger and outbox/notification intent in the same MariaDB transaction. Safe path: one unit of work with an idempotency record and authoritative aggregate version.
- Flag missing commit-time revalidation, nondeterministic lock ordering, caller-timeout success inference, browser-time ordering, or retry paths that can apply twice. Safe path: database time, expected versions, deterministic locks, and replay of the stored idempotent result.
- Flag auction code that invents change, uses a Unit wallet, overpays outside explicitly confirmed Buy It Now, or spends assets outside the Authorized Payment Mix.

### Authorization and privacy

- Flag authorization performed only in UI/projections, stale session claims used for sensitive administration, unscoped search/live-stream output, or allegiance caches treated as authority.
- Flag passwords, hashes, private account names, grants, Withdrawal Tokens, secrets, DAT paths/content, private item data, or webhook credentials entering URLs, logs, telemetry, public events, artifacts, or ordinary error output.

### Verification quality

- Review the linked issue's Red tests and acceptance criteria before the implementation. Flag production behavior added without a test that first demonstrated the missing behavior or regression.
- Prioritize correctness, security, concurrency, data loss, compatibility, and missing-test findings. Leave formatting/import ordering to deterministic tooling.
- Treat unavailable, skipped, flaky, or prerequisite-dependent checks as unverified, never passing. Require exact commands and results in the pull request.

### Safe change shape

- Search adjacent ACE and Cloud code for an existing helper/policy before accepting duplication. Flag stringly typed state, TOCTOU checks, no-op updates, leaky authority abstractions, and unbounded work on public inputs.
- Keep each pull request scoped to one issue. Safe path: preserve unrelated behavior, document unavoidable follow-up explicitly, and do not merge a phase gate without its executable evidence.

### Automation

The AC Cloud Mule automation pipeline and emergency stop are documented in `docs/agents/automation.md`. Automation may auto-merge ordinary dependency-complete issues only after current-SHA CI and a clean Codex review. Issues labeled `automation:human-gate` must stop at `automation:ready-for-user-testing`.
