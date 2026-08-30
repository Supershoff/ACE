#!/usr/bin/env bash

set -euo pipefail

reader=.github/scripts/read-claude-execution-field.sh
finder=.github/scripts/find-cloud-mule-implementation-branch.sh
attempt_counter=.github/scripts/count-cloud-mule-implementation-attempts.sh
workflow=.github/workflows/claude.yml
ci_workflow=.github/workflows/cloud-mule-ci.yml
fixture_dir="$(mktemp -d)"
trap 'rm -rf "${fixture_dir}"' EXIT

printf '%s\n' '{"result":"single-object"}' >"${fixture_dir}/object.json"
printf '%s\n' '[{"type":"assistant"},{"result":"array-result"}]' >"${fixture_dir}/array.json"
printf '%s\n' '{"type":"assistant"}' '{"result":"stream-result"}' >"${fixture_dir}/stream.json"

[ "$(bash "${reader}" result "${fixture_dir}/object.json")" = single-object ]
[ "$(bash "${reader}" result "${fixture_dir}/array.json")" = array-result ]
[ "$(bash "${reader}" result "${fixture_dir}/stream.json")" = stream-result ]

cat >"${fixture_dir}/comments.json" <<'JSON'
[[
  {"body":"<!-- claude-implementation-substantive-failure:29:old-1 -->"},
  {"body":"<!-- claude-implementation-substantive-failure:29:old-2 -->"},
  {"body":"<!-- claude-implementation-reset:29:node-enabled -->"},
  {"body":"<!-- claude-implementation-substantive-failure:29:new-1 -->"},
  {"body":"<!-- claude-implementation-transient:29:new-transient -->"}
]]
JSON
[ "$(bash "${attempt_counter}" 29 substantive-failure <"${fixture_dir}/comments.json")" = 1 ]
[ "$(bash "${attempt_counter}" 29 transient <"${fixture_dir}/comments.json")" = 1 ]

grep -Fq '[ "${status}" = diverged ]' "${finder}"
[ "$(grep -Fc 'read-claude-execution-field.sh' "${workflow}")" -ge 4 ]
grep -Fq 'refresh-cloud-mule-implementation-branch.sh' "${workflow}"
grep -Fq 'count-cloud-mule-implementation-attempts.sh' "${workflow}"
grep -Fq 'elif [ "${POLICY_RESULT}" = failure ]' "${ci_workflow}"
grep -Fq 'leaving recovery to the reconciler' "${ci_workflow}"

for claude_workflow in \
  .github/workflows/claude.yml \
  .github/workflows/claude-ci-repair.yml \
  .github/workflows/claude-review.yml; do
  grep -Fq 'Bash(node:*)' "${claude_workflow}"
  grep -Fq 'Bash(npm:*)' "${claude_workflow}"
done

echo "Claude implementation recovery contract is valid."
