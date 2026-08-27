# Issue tracker: GitHub

Issues and implementation plans for this repository live in GitHub Issues on `Supershoff/ACE`. Use the `gh` CLI for issue operations and pass `--repo Supershoff/ACE` when the current Git remote does not identify that repository.

## Conventions

- **Create an issue**: `gh issue create --repo Supershoff/ACE --title "..." --body-file <path>`.
- **Read an issue**: `gh issue view <number> --repo Supershoff/ACE --comments` and fetch its labels.
- **List issues**: `gh issue list --repo Supershoff/ACE --state open --json number,title,body,labels,comments`, with appropriate label and state filters.
- **Comment on an issue**: `gh issue comment <number> --repo Supershoff/ACE --body-file <path>`.
- **Apply or remove labels**: `gh issue edit <number> --repo Supershoff/ACE --add-label "..."` or `--remove-label "..."`.
- **Close an issue**: `gh issue close <number> --repo Supershoff/ACE --comment "..."`.

When an engineering skill says to publish to the issue tracker, create a GitHub issue in `Supershoff/ACE`. When it says to fetch a relevant ticket, read that issue and its comments and labels.

## Active implementation plan

AC Cloud Mule version-one work is tracked by [GitHub issue #60](https://github.com/Supershoff/ACE/issues/60) and its ten ordered milestones. Before starting an implementation issue:

1. Confirm every dependency listed in the issue is closed and the preceding phase gate passed.
2. Implement one focused issue per pull request using its Red → Green → Refactor instructions.
3. Link the pull request to the issue and include its automated and required manual acceptance evidence.
4. Do not post or commit private DAT files, extracted client art, captures, credentials, secrets, or absolute operator paths.
