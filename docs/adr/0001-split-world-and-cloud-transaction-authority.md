---
status: accepted
---

# Split world-boundary and Cloud transaction authority

ACE exclusively validates and executes item transitions into or out of the playable world, while the companion Cloud backend exclusively transacts dedicated off-world ownership, reservation, offer, vault, bid, listing, and settlement records. This keeps native biotas under ACE's world rules but allows the Cloud economy to operate during ordinary ACE world-process restarts without granting the web service write access to native biota tables.

## Considered options

- Making ACE execute every Cloud transfer preserved a single writer but unnecessarily made the entire web economy depend on world-process uptime.
- Letting the web edit native biotas enabled downtime operation but created unsafe cache, privilege, and duplicate-custody risks.

## Consequences

Both authorities must implement one versioned transactional handoff protocol with row locks, optimistic versions, idempotency keys, ledger/outbox commits, and database constraints prohibiting simultaneous Cloud and world custody. If MariaDB is unavailable, Cloud mutations stop rather than queue.
