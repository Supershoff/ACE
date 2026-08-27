---
status: accepted
---

# Defer native materialization for partial Cloud stacks

Partial stack actions create Cloud Stack Lots—quantity claims backed by one stackable native biota—in dedicated Cloud tables. The web may transfer and reserve lots while ACE is offline, but only ACE materializes child biotas and allocates their GUIDs when a world-boundary operation requires them.

## Considered options

- Requiring ACE to split every stack immediately would make partial web actions unavailable during restarts.
- Giving the web native-biota and GUID-allocation privileges would violate the custody boundary and introduce cross-process allocation risk.
- Disallowing partial stack actions would make withdrawals, offers, vault moves, listings, and currency escrow unnecessarily clumsy.

## Consequences

Lot quantities must always sum exactly to their backing stack, preserve lineage, participate in exclusive reservations, and count as projected materialized items for quotas. Withdrawal materialization must preserve the original GUID for the remainder where possible and be atomic and idempotent.
