# Domain docs

This repository uses a single domain context for ACE and AC Cloud Mule.

## Before exploring or changing code

- Read `CONTEXT.md` at the repository root.
- Read each applicable accepted decision under `docs/adr/`.
- Read `IMPLEMENTATION-BRIEF.md` when planning or implementing AC Cloud Mule work.

If one of these files is absent from a worktree or branch, proceed with the sources that are present rather than inventing replacement requirements.

## Use the glossary vocabulary

Use domain terms exactly as defined in `CONTEXT.md` in issue titles, implementation plans, code, tests, and reviews. Avoid synonyms that the glossary explicitly rejects.

If a needed concept is missing, determine whether the proposed language is unnecessary or represents a genuine domain gap. Surface genuine gaps for a product decision before implementation.

## Flag conflicts

If proposed work contradicts `CONTEXT.md`, `IMPLEMENTATION-BRIEF.md`, or an accepted ADR, identify the conflict explicitly rather than silently overriding the documented decision.
