#!/usr/bin/env bash

set -euo pipefail

issue_number="${1:?issue number is required}"
attempt_kind="${2:?attempt kind is required}"

case "${attempt_kind}" in
  substantive-failure|transient) ;;
  *) echo "unsupported attempt kind: ${attempt_kind}" >&2; exit 2 ;;
esac

jq -r \
  --arg reset_marker "<!-- claude-implementation-reset:${issue_number}:" \
  --arg attempt_marker "<!-- claude-implementation-${attempt_kind}:${issue_number}:" '
    [.[][]] as $comments
    | ([range(0; $comments | length)
        | select($comments[.].body | contains($reset_marker))]
       | last // -1) as $reset_index
    | [$comments[($reset_index + 1):][]
       | select(.body | contains($attempt_marker))]
    | length
  '
