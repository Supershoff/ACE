#!/usr/bin/env bash

set -euo pipefail

issue_number="${1:?issue number is required}"
preferred_branch="${2:-}"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
default_branch="${CLOUD_MULE_BASE_BRANCH:-master}"
candidates="$(mktemp)"
trap 'rm -f "${candidates}"' EXIT

validate_branch() {
  local name="$1"
  local branch_json sha commit message comparison status ahead date

  [ -n "${name}" ] || return 1
  [ "${name}" != "${default_branch}" ] || return 1
  branch_json="$(gh api "repos/${repository}/git/ref/heads/${name}" 2>/dev/null)" || return 1
  sha="$(jq -r .object.sha <<<"${branch_json}")"
  commit="$(gh api "repos/${repository}/commits/${sha}" 2>/dev/null)" || return 1
  message="$(jq -r .commit.message <<<"${commit}")"
  grep -Eq "^(Fixes|Closes|Resolves) #${issue_number}([^0-9]|$)" <<<"${message}" || return 1

  comparison="$(gh api "repos/${repository}/compare/${default_branch}...${name}" 2>/dev/null)" || return 1
  status="$(jq -r .status <<<"${comparison}")"
  ahead="$(jq -r .ahead_by <<<"${comparison}")"
  # A valid implementation can become "diverged" when master advances while a
  # long Claude run is still working. Preserve that durable work; the handoff
  # refreshes it onto current master before opening or updating the PR.
  { [ "${status}" = ahead ] || [ "${status}" = diverged ]; } && [ "${ahead}" -gt 0 ] || return 1

  date="$(jq -r .commit.committer.date <<<"${commit}")"
  printf '%s\t%s\t%s\n' "${date}" "${name}" "${sha}" >>"${candidates}"
}

# Prefer the action's declared branch when it is valid, but never trust the
# output without checking issue ownership and ancestry.
if validate_branch "${preferred_branch}"; then
  printf '%s\n' "${preferred_branch}"
  exit 0
fi

# Claude can create and push a correctly owned branch itself without returning
# branch_name or while using a nonstandard prefix. Search every repository
# branch and recover only tips that canonically close this issue and contain
# commits ahead of their merge-base with the current base branch.
while read -r encoded; do
  branch="$(base64 -d <<<"${encoded}")"
  validate_branch "$(jq -r .name <<<"${branch}")" || true
done < <(gh api --paginate "repos/${repository}/branches?per_page=100" --jq '.[] | @base64')

sort -r "${candidates}" | head -n 1 | cut -f 2
