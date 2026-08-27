# AC Cloud Mule automation

The repository automation advances one dependency-ready implementation issue at a time. It intentionally serializes work to reduce merge conflicts and make custody-invariant failures attributable.

## Pipeline

1. `cloud-mule-orchestrator.yml` selects the lowest-numbered open `ready-for-agent` issue whose `## Dependencies` issues are closed and for which no Claude pull request or queued/in-progress pre-PR implementation exists.
2. `claude.yml` implements the issue from `master`, follows Red → Green → Refactor, and opens a draft pull request.
3. `cloud-mule-ci.yml` runs repository policy checks, cross-platform .NET builds, Cloud-specific tests when present, and web checks when a client workspace exists.
4. After all CI jobs pass, `cloud-mule-ci.yml` explicitly dispatches `claude-review.yml` with the tested branch and SHA. The reviewer runs independently and read-only using `awesome-skills/code-review-skill` at a pinned commit. This explicit handoff avoids GitHub's recursive `workflow_run` suppression after automated repairs.
5. The same workflow routes Claude P0/P1 findings into a bounded Claude repair on the existing PR. Each correction produces a new SHA, CI run, and fresh independent review. Three unsuccessful repair cycles stop with `automation:needs-attention`.
6. A clean current-SHA Claude review auto-merges an ordinary issue only when `CLOUD_MULE_AUTOMATION_ENABLED=true`. An `automation:human-gate` issue instead becomes `automation:ready-for-user-testing` and remains unmerged.
7. After an automatic merge, the orchestrator is dispatched again. `cloud-mule-reconcile.yml` also reconstructs the correct next action every ten minutes from durable PR SHA, CI run, review marker, issue label, and repair-limit state. A missed or rejected event therefore delays progress instead of stopping it.

## Required one-time setup

- Merge the bootstrap pull request containing these workflows into the default branch. GitHub only dispatches issue-comment and scheduled workflows that exist on the default branch.
- Grant `Supershoff/ACE` access to the existing organization Actions secret `CLAUDE_CODE_OAUTH_TOKEN`.
- Grant the Claude workflow its existing organization secret. The workflow checks out the public review skill at its pinned commit without requiring another credential.
- Set the repository variable `CLOUD_MULE_AUTOMATION_ENABLED` to `true` only after a successful manual smoke run. Missing or any other value is the emergency stop.

## State labels

- `automation:queued` — selected for the next Claude dispatch.
- `automation:in-progress` — implementation or correction is active.
- `automation:retry` — safe transient retry is allowed.
- `automation:needs-attention` — bounded recovery was exhausted or a real human decision is required. Transient handoff failures with a viable PR are automatically returned to `automation:in-progress`.
- `automation:human-gate` — never auto-merge this issue.
- `automation:ready-for-user-testing` — CI and Claude review passed; human acceptance is next.

## Human gates

The private fidelity corpus and later phase acceptance issues are human gates: #24, #28, #34, #39, #47, #53, and #59. Private DAT files, extracted art, captures, credentials, secrets, and absolute operator paths never enter GitHub.

## Emergency stop and recovery

Set `CLOUD_MULE_AUTOMATION_ENABLED=false` to stop scheduled selection and automatic merge. Existing runs may finish their current bounded job but may not start a new issue. Apply `automation:needs-attention` to any issue that must remain stopped.

The reconciler automatically recovers transient missed dispatches and stale labels. After three failed CI or review-repair cycles, resolve the substantive blocker, remove `automation:needs-attention`, add `automation:retry`, and dispatch the reconciler or orchestrator. Never bypass failed custody, security, CI, or Claude-review gates merely to advance the queue.
