#!/usr/bin/env bash

set -euo pipefail

branch="${1:?implementation branch is required}"
default_branch="${CLOUD_MULE_BASE_BRANCH:-master}"

git fetch origin "${default_branch}" "${branch}"

if git merge-base --is-ancestor "origin/${default_branch}" "origin/${branch}"; then
  printf '%s\n' "${branch}"
  exit 0
fi

git switch --detach "origin/${branch}"
git config user.name github-actions
git config user.email github-actions@github.com

if ! git merge --no-edit "origin/${default_branch}"; then
  git merge --abort || true
  echo "Implementation branch ${branch} conflicts with current ${default_branch}; durable work was preserved." >&2
  exit 2
fi

git push origin "HEAD:refs/heads/${branch}"
printf '%s\n' "${branch}"
