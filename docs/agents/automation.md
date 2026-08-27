# AC Cloud Mule automation

The repository automation advances one dependency-ready implementation issue at a time. It intentionally serializes work to reduce merge conflicts and make custody-invariant failures attributable.

## Pipeline

1. `cloud-mule-orchestrator.yml` selects the lowest-numbered open `ready-for-agent` issue whose `## Dependencies` issues are closed and for which no Claude pull request is open.
2. `claude.yml` implements the issue from `master`, follows Red → Green → Refactor, and opens a draft pull request.
3. `cloud-mule-ci.yml` runs repository policy checks, cross-platform .NET builds, Cloud-specific tests when present, and web checks when a client workspace exists.
4. `request-codex-review.yml` requests one `@codex review` for the exact PR head only after CI succeeds.
5. `codex-review-response.yml` routes Codex P0/P1 findings back to Claude on the same branch. Each correction produces a new SHA, CI run, and Codex review. Three unsuccessful repair cycles stop with `automation:needs-attention`.
6. A clean current-SHA Codex review auto-merges an ordinary issue only when `CLOUD_MULE_AUTOMATION_ENABLED=true`. An `automation:human-gate` issue instead becomes `automation:ready-for-user-testing` and remains unmerged.
7. After an automatic merge, the orchestrator is dispatched again. A scheduled reconciliation also recovers missed dispatches.

## Required one-time setup

- Merge the bootstrap pull request containing these workflows into the default branch. GitHub only dispatches issue-comment and scheduled workflows that exist on the default branch.
- Grant `Supershoff/ACE` access to the existing organization Actions secret `CLAUDE_CODE_OAUTH_TOKEN`.
- In Codex Cloud, connect GitHub, enable Code Review for `Supershoff/ACE`, and enable automatic review if desired. The workflow still requests review per tested SHA.
- Set the repository variable `CLOUD_MULE_AUTOMATION_ENABLED` to `true` only after a successful manual smoke run. Missing or any other value is the emergency stop.

## State labels

- `automation:queued` — selected for the next Claude dispatch.
- `automation:in-progress` — implementation or correction is active.
- `automation:retry` — safe transient retry is allowed.
- `automation:needs-attention` — the loop stopped; do not advance automatically.
- `automation:human-gate` — never auto-merge this issue.
- `automation:ready-for-user-testing` — CI and Codex review passed; human acceptance is next.

## Human gates

The private fidelity corpus and later phase acceptance issues are human gates: #24, #28, #34, #39, #47, #53, and #59. Private DAT files, extracted art, captures, credentials, secrets, and absolute operator paths never enter GitHub.

## Emergency stop and recovery

Set `CLOUD_MULE_AUTOMATION_ENABLED=false` to stop scheduled selection and automatic merge. Existing runs may finish their current bounded job but may not start a new issue. Apply `automation:needs-attention` to any issue that must remain stopped.

After resolving a transient problem, remove `automation:needs-attention`, add `automation:retry`, and dispatch the orchestrator manually. Never bypass failed custody, security, CI, or Codex gates merely to advance the queue.
