#!/usr/bin/env bash

set -euo pipefail

issue_number="${1:?issue number is required}"
source_branch="${2:?source branch is required}"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

if [[ "${source_branch}" == claude/* || "${source_branch}" == "cloud-mule/issue-${issue_number}-"* ]]; then
  printf '%s\n' "${source_branch}"
  exit 0
fi

sha="$(gh api "repos/${repository}/git/ref/heads/${source_branch}" --jq .object.sha)"
target="claude/issue-${issue_number}-recovered-${sha:0:8}"

existing_sha=''
if existing_ref="$(gh api "repos/${repository}/git/ref/heads/${target}" 2>/dev/null)"; then
  existing_sha="$(jq -r .object.sha <<<"${existing_ref}")"
fi
if [ -n "${existing_sha}" ]; then
  [ "${existing_sha}" = "${sha}" ] || {
    echo "Recovery branch ${target} already exists at a different commit." >&2
    exit 1
  }
else
  gh api --method POST "repos/${repository}/git/refs" \
    -f ref="refs/heads/${target}" -f sha="${sha}" >/dev/null
fi

printf '%s\n' "${target}"
