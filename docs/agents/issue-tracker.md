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
