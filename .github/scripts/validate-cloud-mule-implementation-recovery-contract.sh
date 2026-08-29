#!/usr/bin/env bash

set -euo pipefail

reader=.github/scripts/read-claude-execution-field.sh
finder=.github/scripts/find-cloud-mule-implementation-branch.sh
workflow=.github/workflows/claude.yml
fixture_dir="$(mktemp -d)"
trap 'rm -rf "${fixture_dir}"' EXIT

printf '%s\n' '{"result":"single-object"}' >"${fixture_dir}/object.json"
printf '%s\n' '[{"type":"assistant"},{"result":"array-result"}]' >"${fixture_dir}/array.json"
printf '%s\n' '{"type":"assistant"}' '{"result":"stream-result"}' >"${fixture_dir}/stream.json"

[ "$(bash "${reader}" result "${fixture_dir}/object.json")" = single-object ]
[ "$(bash "${reader}" result "${fixture_dir}/array.json")" = array-result ]
[ "$(bash "${reader}" result "${fixture_dir}/stream.json")" = stream-result ]

grep -Fq '[ "${status}" = diverged ]' "${finder}"
[ "$(grep -Fc 'read-claude-execution-field.sh' "${workflow}")" -ge 4 ]
grep -Fq 'refresh-cloud-mule-implementation-branch.sh' "${workflow}"

echo "Claude implementation recovery contract is valid."
