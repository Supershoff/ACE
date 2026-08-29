#!/usr/bin/env bash
set -euo pipefail

orchestrator=.github/workflows/cloud-mule-orchestrator.yml

dispatch_inputs="$(sed -n '/^  workflow_dispatch:/,/^concurrency:/p' "${orchestrator}")"
if ! grep -Eq '^[[:space:]]+default: false[[:space:]]*$' <<<"${dispatch_inputs}"; then
  echo "cloud-mule-orchestrator workflow_dispatch must run live by default; dry-run is explicit opt-in."
  exit 1
fi

mapfile -t handoffs < <(
  grep -R -n --include='*.yml' 'gh workflow run cloud-mule-orchestrator.yml' .github/workflows
)

if [ "${#handoffs[@]}" -eq 0 ]; then
  echo "No autonomous orchestrator handoff was found."
  exit 1
fi

for handoff in "${handoffs[@]}"; do
  if ! grep -q -- '-f dry_run=false' <<<"${handoff}"; then
    echo "Autonomous orchestrator handoff does not explicitly disable dry-run: ${handoff}"
    exit 1
  fi
done

echo "Autonomous orchestrator dispatch contract is valid."
