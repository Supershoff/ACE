#!/usr/bin/env bash

set -euo pipefail

field="${1:?field name is required}"
execution_file="${2:?Claude execution file is required}"

# claude-code-action versions have emitted a single result object, a JSON
# array of events, and a stream of JSON values. Slurp all three forms, flatten
# one array layer, and return the last populated field from the final event.
jq -s -r --arg field "${field}" '
  [
    .[]
    | if type == "array" then .[] else . end
    | select(type == "object")
    | .[$field]?
    | select(. != null and . != "")
  ]
  | last // ""
' "${execution_file}"
