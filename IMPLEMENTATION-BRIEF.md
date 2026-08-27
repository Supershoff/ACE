# AC Cloud Mule — implementation-planning brief

## How to use this document

This is the requirements handoff for an LLM or engineering team that will create the implementation plan. Treat every `MUST`, `MUST NOT`, and numbered requirement as binding unless the product owner explicitly revises it. Do not silently replace Asheron's Call terminology with generic MMO terminology.

The complete resolved vocabulary and decision history live in [CONTEXT.md](./CONTEXT.md). The ADRs in `docs/adr/` explain the two least-obvious architectural choices. If a planning assumption conflicts with those sources, stop and surface the conflict.

Before planning code changes, inspect the exact ACE release being targeted. The source review informing this brief used upstream ACE commit `7ae5dad6ad43cd4f1a36fac1e960d364c905fd7b` from 2026-08-13, but support must be declared by ACE release rather than by an indefinite `master` claim.

## Product outcome

AC Cloud Mule is an opt-in, self-hosted ACE extension and companion web application that replaces player mule-character workflows with:

- Account-oriented off-world item storage.
- Accurate AC-style inventory and appraisal presentation.
- Personal sharing, recipient-approved transfers, and an Allegiance Vault.
- A public, item-backed auction marketplace.
- Safe in-game withdrawal and complete custody auditing.
- Operator controls, recovery, asset import, and Discord notifications.

The system preserves native ACE objects and player trust. Convenience must never create duplicate items, split custody authority ambiguously, hide overpayment, or bypass ACE's world-entry validation.

## Non-negotiable architecture

| ID | Requirement |
|---|---|
| ARCH-001 | One Cloud Mule deployment serves exactly one immutable Cloud Shard/server ID. Hosting and authentication infrastructure may be shared by separate deployments, but accounts, links, custody, vaults, currencies, listings, and administrator authority never cross shards. |
| ARCH-002 | ACE is the World Boundary Authority. Only the ACE process may move an item from ordinary world possession into Cloud custody or materialize/deliver it back to the playable world. |
| ARCH-003 | The companion backend is the Cloud Transaction Authority. It owns off-world owners, lots, reservations, offers, sharing, vault moves, bids, listings, and settlements while native biotas stay unloaded. |
| ARCH-004 | Cloud-only transactions MUST NOT write native biota `Container`, `Wielder`, or `Location` properties. The web database identity MUST NOT have native-biota write privileges. |
| ARCH-005 | A first-class Cloud Custody Record is a valid ACE persistence state. Database constraints MUST prohibit simultaneous Cloud custody and world possession. ACE loaders, integrity tools, cleanup tools, backup tooling, and GUID handling must recognize it. |
| ARCH-006 | Boundary transitions use row locking, optimistic versions, idempotency keys, and one transactionally committed Activity Ledger/outbox record. Repeating a request must produce the same result, not another item. |
| ARCH-007 | ACE commits deposits and their durable Custody Outbox events without companion-web availability. The web consumes events idempotently and can rebuild all read/search projections. |
| ARCH-008 | When the ACE world process is down but MariaDB and Cloud services remain healthy, every off-world web operation continues. Deposits are naturally unavailable and withdrawal creation/redemption is blocked. |
| ARCH-009 | If the authoritative database is unavailable, the web becomes read-only. It may serve explicitly safe cached public data but MUST NOT queue ownership mutations for replay. |
| ARCH-010 | Partial off-world stack operations use Cloud Stack Lots. The web records quantity claims without creating or editing native biotas. ACE materializes child stacks and allocates GUIDs at the next required world-boundary operation. |
| ARCH-011 | Ordinary deposited stacks never auto-merge. A derived lot records parent/child lineage and counts as a projected item for quota purposes. Raw Pyreal conversion is the only automatic consolidation/replacement rule. |
| ARCH-012 | The stack is ASP.NET Core/.NET 10 for the backend, bridge, workers, and shared pure-domain contracts; TypeScript/React for the web client; and MariaDB for authoritative state. Search is a rebuildable projection, not a second mandatory authority database. |
| ARCH-013 | The ACE fork, companion services, and web client are AGPL-3.0. Operator configuration, secrets, uploaded DATs, and generated assets are deployment data and are not committed to the project. |

### Authority flow

```text
ACE world inventory
    │  Cloud Custodian deposit (ACE validates and commits)
    ▼
Cloud Custody Record ── Cloud backend transactions ──► owner / lot / reservation changes
    │
    │  Withdrawal Token redemption (ACE locks, validates, materializes, commits)
    ▼
ACE world inventory
```

There is no path from the browser directly to native biota mutation.

## Relevant ACE source seams

Revalidate these paths against the selected supported release before designing changes:

| Concern | Existing source seam |
|---|---|
| Native vendor sale and partial-success behavior | `Source/ACE.Server/WorldObjects/Player_Commerce.cs` (`HandleActionSellItem`, `VerifySellItems`) |
| Vendor resale/destruction behavior that Cloud Custodian must bypass | `Source/ACE.Server/WorldObjects/Vendor.cs` (`ProcessItemsForPurchase`) |
| Player-to-player trade eligibility | `Source/ACE.Server/WorldObjects/Player_Trade.cs`, `Player_Inventory.cs` |
| Recursive Attuned checks | `Source/ACE.Server/WorldObjects/Container.cs`, `WorldObject.cs` |
| `Attuned` property number 114 | `Source/ACE.Entity/Enum/Properties/PropertyInt.cs` |
| Inventory enchantment/lifespan heartbeat | `Source/ACE.Server/WorldObjects/Container_Tick.cs` and `Managers/EnchantmentManager.cs` |
| Full appraisal profile construction | `Source/ACE.Server/Network/Structure/AppraiseInfo.cs` and related profile types |
| Icon, overlay, underlay, clothing/palette/shade behavior | `WorldObject_Properties.cs`, `WorldObject_Networking.cs`, `Clothing.cs`, and `ACE.DatLoader` clothing/texture types |
| ACE password verification and legacy migration | `Source/ACE.Database/Models/Auth/AccountExtensions.cs` |
| Allegiance monarch/tree behavior | `Source/ACE.Server/Managers/AllegianceManager.cs`, `WorldObjects/Player_Allegiance.cs` |

Do not merely call an existing path safe because it has similar behavior. The Cloud Custodian and custody boundary require dedicated transaction handling; ordinary ACE vendor code destroys or temporarily resells purchased items.

## Core custody state model

```text
WORLD_POSSESSED
    └─ valid Custodian sale row ─► CLOUD_AVAILABLE

CLOUD_AVAILABLE
    ├─ withdrawal request ───────► WITHDRAWAL_RESERVED
    ├─ listing publication ─────► LISTING_RESERVED
    ├─ transfer offer ──────────► OFFER_RESERVED
    ├─ bid authorization ───────► BID_ESCROW_RESERVED
    ├─ immediate cloud transfer ► CLOUD_AVAILABLE (new owner)
    └─ partial action ──────────► CLOUD_STACK_LOT(s)

WITHDRAWAL_RESERVED
    ├─ successful ACE redemption ► WORLD_POSSESSED
    ├─ validation/capacity failure► WITHDRAWAL_RESERVED
    └─ cancel/expiry ─────────────► CLOUD_AVAILABLE

Other reservations end only through their owning workflow. One quantity may have at most one
exclusive reservation at a time.
```

Reservation and ownership transitions MUST be transactional with their Activity Ledger event and Notification/Outbox intent. Webhook delivery is asynchronous and never participates in the ownership transaction.

## Functional requirements

### Authentication, identity, and account linking

| ID | Requirement |
|---|---|
| AUTH-001 | Login uses the private ACE account name. Public identity uses a Display Character selected from current characters across the Main/Linked group. Account names never appear publicly. |
| AUTH-002 | A private ACE Auth Bridge reuses ACE's password verifier and returns a short-lived grant. The Cloud backend never stores passwords, logs them, or implements password-hash verification. The bridge remains available during world restarts. |
| AUTH-003 | The default Display Character is the current character with the highest `total_Logins`. If the selected character is deleted **or renamed**, select the remaining current character with the highest `total_Logins`. Audit records retain immutable IDs and name snapshots. |
| AUTH-004 | Only Main Account credentials can manage the unified Cloud Inventory. Linked credentials may deposit in game but their web login shows only that they are linked; it cannot view or mutate Main assets. |
| AUTH-005 | Linking transfers all existing Cloud ownership from the source account to the Main Account, and all future source deposits route to Main. Unlinking never restores prior ownership; future deposits go to the newly independent account. |
| AUTH-006 | The source account must be standalone: not linked, not a Main with children, and free of active reservations, listings, bids, settlements, tokens, offers, or other in-flight obligations. Link trees/group merges are forbidden. |
| AUTH-007 | Linking uses a prominent red destructive warning, exact Main Account name typing, source-password re-entry, and an approximately 10-second delayed accept control. |
| AUTH-008 | Linking revokes all personal Sharing Grants to or from the source account. Main Account grants remain. Character-specific allegiance eligibility is unchanged. |
| AUTH-009 | Linking is blocked if it would create seller/bidder self-dealing in an active auction or violate another immutable ownership constraint. |
| AUTH-010 | No Cloud-specific TOTP is in scope. Rare compromise recovery is a manually audited administrator process. |

### Cloud Custodian deposits

| ID | Requirement |
|---|---|
| DEP-001 | Cloud Custodian is a shared zero-payout Vendor NPC. Players open the native vendor UI, fill the sell pane, and submit its contents. It never creates payout currency or ordinary vendor resale inventory. |
| DEP-002 | Rows use familiar vendor partial success: validate and commit each independently. Valid rows deposit even when other rows fail. Rejected rows remain with the player and report the exact item-specific reason. |
| DEP-003 | A row is eligible only when it is in ordinary inventory and legal under ACE player-to-player trade rules. Equipped items must first be moved to inventory. |
| DEP-004 | Reject all containers in the first release, even empty ones; all Attuned or Sticky items (`PropertyInt.Attuned` 114 at value 1 or higher); active pet devices; character-bound or otherwise unsafe stateful objects; finite-lifespan items; active cooldown/attachment state; and anything already reserved/traded. |
| DEP-005 | Runtime item enchantments are accepted and frozen: preserve the persisted remaining duration while off-world, then resume ACE heartbeat processing after withdrawal. Permanent built-in spells remain ordinary static properties. |
| DEP-006 | Raw Pyreals convert at exactly 287,500 Pyreals per MMD (`Trade Note (250,000)`). Combine deposits with an exact account-level Pyreal Remainder, create as many MMDs as possible, preserve the remainder without rounding, and allow raw withdrawal of that remainder. |
| DEP-007 | Default Custodian locations are every mansion and Marketplace. Admins independently toggle the mansion set and Marketplace location. They may add/remove custom full ACE position strings such as `0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309`. |
| DEP-008 | Admin location/config changes hot-apply when ACE is running and persist while it is down. A disabled Custodian must reject a stale open-window commit rather than accept against old configuration. |
| DEP-009 | There is no bulk import now or later. Existing mule items enter through normal Custodian vendor deposits. |

### Inventory, stack quantities, and quotas

| ID | Requirement |
|---|---|
| INV-001 | Each non-stack Cloud Item has one original native biota/GUID and one Cloud Custody Record. Each stack custody record has one backing native biota and one or more exactly summed Cloud Stack Lots when partial operations occur. |
| INV-002 | Partial withdrawal, Transfer Offer, listing, escrow, and Allegiance Vault moves are supported. Selecting one stack reveals an inline quantity control defaulted to full; multi-select defaults to all selected quantities. |
| INV-003 | For a logical split, the original GUID remains associated with the remainder where possible. ACE materialization produces new child GUIDs through native allocation and logs complete lineage. |
| INV-004 | Storage is unlimited by default. Admins may enable shard-wide personal and Allegiance Vault quotas measured as native biotas plus projected materialized lots. One Pyreal Remainder does not count. |
| INV-005 | Lowering a quota never deletes or forcibly transfers assets. An over-limit owner enters reduce-only mode: count-reducing actions work; deposits and new incoming offers, purchases, or vault takes do not. |
| INV-006 | Quotas are checked when a new obligation is created/accepted, but never break an already-binding auction, confirmed Buy It Now, accepted offer, or Vault Absorption. Such settlement may exceed the new quota and then leaves the recipient reduce-only. |

### Withdrawal

| ID | Requirement |
|---|---|
| WDR-001 | A web selection creates a high-entropy, single-use Withdrawal Token and exclusive reservation. The command shown/copied to the user is valid for 15 minutes. |
| WDR-002 | Any character currently in the owner's Main/Linked group may redeem. Audit the owner, acting ACE account, and exact receiving character. Unlinking immediately prevents a former member from redeeming. |
| WDR-003 | A multi-item/quantity redemption is all-or-none. Cancel, expiry, or success ends the reservation. Capacity or safe-state failure delivers nothing but leaves the token retryable until expiry. |
| WDR-004 | Redemption requires a living, fully loaded, non-combat character who is not trading, portaling, recalling, logging out, or performing another transfer. Revalidate the token and every item under transaction lock. |
| WDR-005 | ACE performs complete native receive validation: inventory slots, native stack merges, burden, uniqueness and other rules. Ordinary inventory only. Return exact actionable in-game failures. |
| WDR-006 | Allowed by default in Marketplace and any landblock containing player housing/SlumLord. Custom locations are admin-named landblocks in `0x123E` format. `withdraw anywhere` is an audited shard-wide bypass and defaults off. Custodian positions are unrelated. |
| WDR-007 | Allegiance Vault items cannot be withdrawn. A member must first take them into personal Cloud Inventory. |
| WDR-008 | If ACE is down, block token creation and redemption. Existing reservations remain governed by their clocks unless Global Cloud Maintenance pauses them. If only the web is down, already-issued tokens remain redeemable from ACE's local authority state. |

### Marketplace currency and listing terms

| ID | Requirement |
|---|---|
| MKT-001 | Marketplace Units are valuation only, never an owned wallet balance. Payment and escrow always consist of actual Cloud Items or Cloud Stack Lots. |
| MKT-002 | Admins maintain a Currency Catalog of eligible exact WCIDs. Only economically interchangeable classes should be cataloged. MMD begins enabled at 1 Unit, but sellers are never required to accept it. Sellers need not own an exemplar. |
| MKT-003 | Each seller maintains default accepted WCIDs and positive integer Unit values. A listing may deselect currencies and captures an immutable Currency Terms Snapshot at publication. Later seller edits or catalog disablement do not alter active listings. |
| MKT-004 | A listing is an immutable item/quantity bundle with title, tags, public Opening Price, optional Buy It Now, Currency Terms Snapshot, and Auction Duration. Every field freezes at publication. |
| MKT-005 | Allowed durations are admin-configured and default to 1, 3, and 7 days. Publication stores the exact end timestamp. Hard Close permits last-second bidding and never extends automatically. |
| MKT-006 | Sellers may enter any positive integer price even when accepted currency denominations cannot compose it. Do not snap, normalize, or reject prices using denomination GCD. There is no hidden reserve. |
| MKT-007 | Publication immediately creates a Listing Reservation. Before the first accepted bid, the seller may cancel and republish. After a bid, only an audited administrator action may cancel. Unsold close releases the bundle. |

### Bidding, Buy It Now, and settlement

| ID | Requirement |
|---|---|
| MKT-101 | A bidder explicitly authorizes specific non-stackable currency items and quantities from stackables. Only this Authorized Payment Mix may be reserved or spent. |
| MKT-102 | Currency rows are drag-to-order spending preference. Among exact combinations, consume higher-priority WCIDs first; use deterministic GUID ordering within one interchangeable WCID. Preview projected tender and explain contextually that proxy competition may change it. |
| MKT-103 | Proxy bidding has a logical one-Unit minimum increment. The public price becomes the smallest amount above the competing price, at or above Opening Price, within the leader's maximum, and exactly payable from that bidder's authorized escrow. A denomination-required larger jump must be disclosed before confirmation. |
| MKT-104 | Accepted bids cannot be cancelled. The current leader may reduce the private maximum to the current public price, never below it. Release only newly excess escrow atomically. Maximum increases and payment-priority changes must preserve exact coverage of all binding obligations. |
| MKT-105 | Equal maximum bids favor the earliest server-committed accepted maximum. Use database/authority commit order, never browser time. Maximums remain private. |
| MKT-106 | Outbid escrow releases immediately. Winning settlement transfers only the actual winning tender and releases unused authorized escrow. One asset/quantity cannot back multiple active obligations. |
| MKT-107 | Buy It Now remains after bids until used or reached by the current price. Entering a max at or above it opens a distinct purchase confirmation rather than silently bidding. |
| MKT-108 | Prefer exact Buy It Now tender. If none is available, permit the smallest authorized tender above the asking price only after showing its composition, exact excess, and a clear no-change warning. The bidder may revise currency selection/order first. Seller receives the full tender. Record/display asking price and actual payment. |
| MKT-109 | Buy It Now is the only intentional overpayment path. Proxy bidding and normal close always settle at exactly payable public prices and never spend above them. |
| MKT-110 | The Main/Linked ownership group may not bid on or buy its own listing. Linking is blocked if it would create an active seller/bidder conflict. |
| MKT-111 | Close commits an immutable winner and final price. Bundle and authorized tender transfer in one transaction or neither transfers. A transient post-close failure enters Settlement Pending with reservations intact and retries idempotently; admins may retry but not choose another outcome. |
| MKT-112 | First release has no listing fee, buyer fee, seller commission, or automatic tax. |
| MKT-113 | Active bidder identities are anonymous to all non-admins, including the seller. Show public prices/timestamps and the viewer's own status. After successful settlement, publish buyer and seller Display Characters; maximums stay private. |

### Public Marketplace, retention, and operating states

| ID | Requirement |
|---|---|
| MKT-201 | Active and completed listing URLs are publicly searchable/shareable without login. Public pages may show seller Display Character, item/bundle/appraisal data, currencies, prices, times, and anonymized bid history. Never expose ACE account names, private inventories, maximum bids, credentials, tokens, or private ledger activity. |
| MKT-202 | Successful sales remain public indefinitely as price history. Unsold, seller-cancelled, and admin-cancelled listings leave public search and direct access after 30 days. Admin-cancelled pages show a neutral public status, not the private reason. Ledger records remain forever. |
| MKT-203 | Enabled permits all Marketplace actions. Disabled blocks only **new listings**; existing auctions may bid, use Buy It Now, close, and settle. |
| MKT-204 | Maintenance Frozen blocks all Marketplace mutations and clock progress. Resume shifts end times by the exact frozen duration. No Marketplace state cancels auctions or releases reservations. There is no separate Draining state. |

### Transfer Offers and sharing

| ID | Requirement |
|---|---|
| XFER-001 | Sending to another player creates a recipient-approved Transfer Offer, not an immediate ownership change. Resolve a current character name once to immutable recipient Main Account ID; later rename/deletion must not redirect it. |
| XFER-002 | Offers reserve the entire item/lot set, expire after seven days, and accept atomically or not at all. Sender may cancel; recipient may decline; cancel/decline/expiry releases reservations. |
| SHARE-001 | A Sharing Grant targets an immutable resolved Main/Linked ownership group through a current character lookup. It survives recipient character rename/deletion and displays the group's current Display Character. |
| SHARE-002 | Personal tiers are only View Only and View & Withdraw. There is no View + Deposit tier and no direct personal deposit permission; inbound assets use Transfer Offers. |
| SHARE-003 | View & Withdraw creates a token redeemable by the grantee's own group and audits the owner, grant, requester, and receiving character. It grants no listing, bidding, settings, account linking, offer creation, or permission-management authority. |
| SHARE-004 | Explicit individual access, including None, overrides guild-derived personal access. Loss of qualifying allegiance membership revokes derived access immediately and invalidates its unredeemed withdrawal tokens. |

### Allegiance Vault

| ID | Requirement |
|---|---|
| VAULT-001 | Membership derives live from ACE's allegiance tree. Do not create a parallel guild roster. Every action names one Acting Character; an alt's membership does not grant unrelated characters access. |
| VAULT-002 | Every current member has equal view, contribute, and take privileges. No rank ACLs or configurable tiers in the first release. |
| VAULT-003 | Personal-to-vault contribution and vault-to-personal take are immediate Cloud ownership transfers. The vault is cloud-only and cannot withdraw, list, bid, use Buy It Now, or create external Transfer Offers. |
| VAULT-004 | The vault follows the monarch. When a monarch joins another allegiance, atomically absorb the entire source vault into the destination vault, archive the empty source, and preserve item provenance plus both vault identities. |
| VAULT-005 | Block deletion of a monarch character while its vault is nonempty. Out-of-band database deletion requires audited administrator recovery; do not guess a successor destination. |

### Activity, notifications, and Discord

| ID | Requirement |
|---|---|
| EVT-001 | One append-only immutable Activity Ledger covers deposits/rejections/conversion, withdrawal attempts/results, ownership, lots, offers, sharing, linking, vault activity, listings, bids, escrow, settlement, notifications, configuration, admin inspection/intervention, maintenance, import, backup, and recovery. No web UI can edit/delete it. |
| EVT-002 | Events include immutable actor/owner IDs, display snapshots, item/GUID/lot identity and relevant snapshot, timestamp, outcome, reason, correlation/idempotency ID, and shard ID. Users see their scope; allegiance members see complete vault history; admins see global history. |
| EVT-003 | A compact in-app Notification Center reports actionable private events such as offers, outbids, sales, settlements, sharing changes, reservation outcomes, and admin actions. Coalesce repetitive events and auto-read when appropriate. No email or granular preference matrix in version one. |
| EVT-004 | Public Discord webhook announces only successful listing publication and completed sale settlement using Display Characters and public listing data. It never announces private deposits, offers, sharing, vault activity, or account names. |
| EVT-005 | A separate admin-only audit webhook summarizes inspections, interventions, forced cancellations, recovery, configuration, Marketplace/global state, and other admin ledger events. Never include passwords, account names unless operationally indispensable, tokens, or unnecessary private item data. |
| EVT-006 | All webhook delivery is asynchronous, bounded/retried, observable, and incapable of blocking or rolling back a domain transaction. |
| EVT-007 | Public and authorized private changes propagate through versioned Live State Streams. Optimistic UI must reconcile to server versions and visibly reverse rejected actions. |

### Inventory presentation, appraisal, and search

| ID | Requirement |
|---|---|
| UI-001 | Normalize each item to exactly one Inventory Category, primarily from `PropertyInt.ItemType` flags using a documented deterministic priority and `WeenieType` fallback. Preserve all raw properties for search/appraisal. |
| UI-002 | AC-style views are virtual, automatically sorted, and have no persistent manual slots/gaps. Create/delete category pages automatically. Each Mule Page contains 102 items and is named `[Category] Mule [n]`. |
| UI-003 | Desktop targets a 6-by-17 grid when suitable. Narrow layouts may reflow without changing page membership under the current sort/filter. The spreadsheet view shares filters and deterministic sorting, with stable item identity as the final tie-break. |
| UI-004 | Selecting/right-clicking an item opens Full Cloud Appraisal: a faithful reconstruction of the in-game ID panel's player-facing content, section ordering, font character, colors, borders, spacing, and language. It is always a complete successful appraisal independent of character skill and excludes GM-only raw fields. |
| UI-005 | Icon Reconstruction must derive the accurate static icon from instance properties and active DAT assets, including base Icon DID, ClothingBase resolution, PaletteTemplate, Shade values, IconUnderlay, IconOverlay/secondary behavior where applicable, and static `UiEffects` layers. It must not substitute a generic WCID image. |
| UI-006 | Cache icons by a complete content/composition key. Stack quantity, selection, reservation, and web badges are separate UI layers. Missing references use an explicit neutral fallback and create admin diagnostics rather than silently showing a wrong icon. Icons contain no animation; magical glow is a still blue layer. |
| UI-007 | Visual direction is hybrid: AC-authentic inventory and appraisal surfaces; quieter modern Marketplace, tables, settings, and admin using the same palette, typography, borders, and material language. Avoid generic dashboard cards and avoid gratuitous skeuomorphism outside fidelity surfaces. |
| UI-008 | Mobile is first-class. Preserve accurate icons, hierarchy, appraisal access, and every operation while adapting layout. Keyboard navigation, visible focus, sufficient contrast, screen-reader names, touch targets, and reduced-motion support are release criteria. |
| UI-009 | Follow Progressive Interface throughout: direct manipulation, contextual disclosure, and sensible defaults; no unnecessary wording, checkboxes, dropdowns, or permanently visible advanced controls. Use active consistent verbs and exact actionable errors. |
| SRCH-001 | Normal and property search run against an authorization-scoped prepared index. Safe Regex Search is an advanced mode over indexed candidate data only, never SQL, with non-backtracking execution, time/pattern/input/result/rate limits. Admin can disable regex independently. |

### DAT asset import

| ID | Requirement |
|---|---|
| ASSET-001 | Admin uploads `client_portal.dat` through the web once; `client_highres.dat` is optional. No client art or source DAT is committed or distributed with Cloud Mule. |
| ASSET-002 | Upload is resumable and validates expected format plus checksum. Extraction runs in a background versioned staging area with progress and precise errors. Activate the new manifest atomically only after complete success; keep old assets live on failure. |
| ASSET-003 | Retain the latest uploaded source DAT in protected non-public storage for automatic reprocessing by future releases. Admin may upload changed DATs. Import/activation/reprocessing is audited. |
| ASSET-004 | Build a DID-addressable manifest sufficient for deterministic icon reconstruction and approved interface textures. Generated public derivatives must not expose the source DAT through path traversal, arbitrary range access, or raw download endpoints. |
| ASSET-005 | Include golden reconstruction tests for a curated corpus covering clothing palette/shade variants, underlays, overlays, tailoring, imbues, magical UI effects, stack counts, and missing/corrupt references. |

### Administration, maintenance, deployment, and recovery

| ID | Requirement |
|---|---|
| ADM-001 | Admin means ACE `ace_auth.account.accessLevel == 5`. Revalidate on every sensitive request; session claims alone are insufficient. |
| ADM-002 | Viewing another inventory is logged. Ordinary admin transfer requires a written reason and delayed confirmation and may touch only unreserved assets. Force transfer may atomically unwind uncommitted reservations/listings/offers and refund escrow, but cannot override a closed committed settlement. Notify affected owners with the reason. |
| ADM-003 | Admin controls include Custodian sets/custom positions, named withdrawal landblocks, withdraw-anywhere, Currency Catalog, seller/market diagnostics, quotas/exemptions, Marketplace State, Global Cloud Maintenance, webhooks, DATs/assets, backups, and audited recovery. |
| ADM-004 | Global Cloud Maintenance keeps reads available but blocks every mutation, including deposits and settlements. Pause auction, offer, reservation, and Withdrawal Token clocks; resume by shifting deadlines exactly. Entry/exit require reason, confirmation, ledger event, and admin webhook. Never cancel or unlock automatically. |
| OPS-001 | Publish self-contained Windows x64 services and Docker images/Compose example from one supported release. One Operator Bootstrap validates DB access, creates the Cloud schema/restricted identities, runs migrations, generates secrets, and prints the initial web URL. Routine administration then occurs in the web app. |
| OPS-002 | Refuse mutations when the ACE extension, Auth Bridge, Cloud schema, and backend protocol versions are incompatible. Expose health/version diagnostics. Use versioned forward migrations and declare supported ACE releases. |
| OPS-003 | Provide first-party Coordinated Backup of Cloud schema plus relevant ACE shard/auth state from one consistent point. Include configuration and active asset manifest; optionally include encrypted secrets/source DAT. Dashboard shows last verified backup. |
| OPS-004 | Restore is offline and guarded. Validate shard ID, versions, snapshot correlation, custody constraints, lot sums, reservation references, native biota existence, and GUID invariants before ACE may start. Refuse mismatched snapshots. |

## Auction and offer state machines

### Listing

```text
PUBLISHED_NO_BID ── seller cancel ─► CANCELLED_SELLER
        │
        ├─ first bid ──────────────► PUBLISHED_WITH_BID
        ├─ Buy It Now ─────────────► SETTLED
        └─ hard close ─────────────► CLOSED_UNSOLD

PUBLISHED_WITH_BID
        ├─ Buy It Now ─────────────► SETTLED
        └─ hard close ─────────────► SETTLED or SETTLEMENT_PENDING ─► SETTLED

Admin cancellation is an audited exceptional transition from either published state.
Marketplace Maintenance Frozen and Global Cloud Maintenance are orthogonal clock/mutation gates,
not listing states.
```

### Transfer Offer

```text
PENDING_RESERVED
    ├─ recipient accepts ─► ACCEPTED_TRANSFERRED
    ├─ recipient declines ─► DECLINED_RELEASED
    ├─ sender cancels ─────► CANCELLED_RELEASED
    └─ seven-day expiry ───► EXPIRED_RELEASED
```

## Transaction and concurrency rules

1. Use database time and authoritative commit order for deadlines and bid priority.
2. Lock custody/lot rows in deterministic order for multi-item transactions to avoid deadlocks.
3. Persist a version on every mutable aggregate and require expected-version checks on browser commands.
4. Store idempotency keys on every external boundary and retryable worker command.
5. Commit ledger and outbox records in the same database transaction as the state change.
6. Deliver outbox effects at least once; consumers must be idempotent.
7. Enforce exclusive reservation and lot-quantity sums with database constraints where possible and transaction validation everywhere else.
8. Never infer success from a timed-out caller. Requery the idempotency record.
9. Treat Global/Marketplace freezes as transaction preconditions revalidated at commit, not only UI flags.
10. Do not let a stale open Custodian window, stale listing page, or stale permission claim bypass current state.

## Conceptual records

The implementation plan should map these concepts to tables/aggregates without treating the names as a required physical schema:

- CloudShard and protocol/schema version.
- OwnershipGroup, MainAccount, LinkedAccount, DisplayCharacter selection/history.
- CloudCustodyRecord and CloudStackLot with backing/lineage/materialization state.
- Typed exclusive reservations and allocation rows.
- PyrealRemainder.
- SharingGrant and character-derived allegiance authorization cache/projection.
- AllegianceVault and archived absorption lineage.
- CurrencyCatalogEntry and seller currency defaults.
- Listing, immutable CurrencyTermsSnapshot, Bid, BidEscrow allocation, settlement attempt.
- TransferOffer and WithdrawalToken.
- ActivityLedgerEvent, CustodyOutboxEvent, Notification, webhook delivery attempt.
- Custodian configuration, WithdrawalLandblock, Marketplace/Global state intervals.
- AssetImport, AssetManifest, composition cache key, diagnostic.
- BackupManifest and restore validation report.

Do not create a parallel allegiance membership roster as authority. A cache is permitted only when it is versioned/refreshed from ACE and every sensitive action revalidates the current Acting Character.

## Security baseline

- HTTPS at the public edge; secure HttpOnly SameSite cookies; CSRF protection; strict origin policy; session rotation; short-lived Auth Bridge grants.
- Private-service authentication between Cloud backend, Auth Bridge, and ACE boundary endpoints; bind privately and support key rotation. Do not expose these endpoints publicly.
- Passwords, login account names, withdrawal tokens, auth grants, connection strings, and raw webhook secrets must never enter normal logs or public telemetry.
- Authorization is server-side on every object query and command. Search indexes and live streams must be scoped before data leaves the server.
- Rate-limit login, account linking, token creation/redemption, bids, search/regex, public scraping, and admin endpoints proportionally.
- Escape/sanitize seller titles/tags and all user text in web and Discord rendering.
- Use cryptographically secure random Withdrawal Tokens; store a one-way verifier if practical; compare safely; make one-use consumption transactional.
- Keep administrator recovery manual and audited. Do not add TOTP or automated compromise rollback in the first release.

## Required verification strategy

The implementation plan must include automated and manual coverage for at least:

### Custody and concurrency

- Deposit versus logout/trade/move races; web transfer versus withdrawal reservation; double redemption; repeated idempotency keys; process crash at every commit boundary.
- Database constraints proving no native biota is both world-possessed and Cloud-custodied.
- Lot conservation property tests: positive quantities, exact sum to backing stack, no duplicate allocation, correct materialization/GUID lineage.
- Outbox replay/rebuild from empty read models and duplicate/out-of-order delivery handling.

### Eligibility and ACE behavior

- Table-driven rejection corpus for Attuned/Sticky, containers, nested cases, equipped, active pets, finite lifespan, cooldown/attachment, runtime enchantments, trade state, and valid static items.
- Partial-success Custodian batches with simultaneous valid and invalid rows and precise in-game messages.
- Raw Pyreal boundary/property tests around 287,500, large deposits, existing remainders, repeated requests, and raw remainder withdrawal.

### Marketplace

- Property-based exact-change/proxy tests across denominations, priorities, stack quantities, equal max ties, max reduction, escrow release, arbitrary seller prices, and Buy It Now overpayment warnings.
- Concurrent last-second bids ordered by committed server transactions.
- Atomic bundle/tender settlement, Settlement Pending retry, admin cancellation/refund, quota changes after commitment, and every Marketplace/Global state gate.
- Self-dealing attempts across Main/Linked groups and link attempts that would create conflicts.

### Identity, permissions, and vaults

- Character rename/deletion fallback; link/unlink irreversible ownership; standalone source checks; source grant revocation; linked credential denial.
- Sharing Grant override/None behavior, membership loss, token invalidation, and exact acting/receiving audit identity.
- Allegiance join/leave, monarch swear, Vault Absorption, nonempty monarch deletion block, and out-of-band recovery path.

### Presentation and assets

- Golden icon-composition fixtures against known in-game results for all relevant layers and clothing variants.
- Golden appraisal fixtures by item class against ACE AppraiseInfo/client presentation semantics.
- Visual regression at desktop grid and representative mobile widths; keyboard-only and screen-reader smoke tests.
- DAT staging failure, malformed input, interrupted/resumed upload, atomic manifest activation, cache invalidation, and protected-source access tests.

### Operations and recovery

- World down/web up behavior; web down/world up deposit and existing-token behavior; DB down read-only behavior; incompatible version lockout.
- Coordinated backup/restore into an empty environment, mismatch refusal, invariant validation, and read-model rebuild.
- Webhook failure/retry without transaction rollback; redaction tests for public/admin notifications and logs.

## Suggested planning workstreams and dependency order

This is dependency guidance, not permission to weaken vertical acceptance criteria:

1. **Invariants and schema spike:** prove Cloud Custody Record integration with ACE loading/integrity tools, database constraints, GUID behavior, and crash-safe boundary transactions.
2. **Shared domain/contracts:** identifiers, state machines, eligibility results, versioned commands/events, exact payment engine, lot conservation.
3. **ACE extension:** Cloud Custodian vendor path, deposit/conversion, withdrawal redemption, allegiance/character events, monarch deletion guard, local outbox.
4. **Cloud authority and Auth Bridge:** identity/linking, custody transactions, reservations, ledger/outbox, permission checks, downtime behavior.
5. **Asset/appraisal fidelity spike:** DAT pipeline, icon compositor, pure Full Cloud Appraisal model, golden corpus. Prove this early rather than leaving fidelity to UI polish at the end.
6. **Inventory vertical slice:** login, grid/table/search, appraisal, partial lots, withdrawal, live updates, activity/notifications.
7. **Sharing and Allegiance Vault:** offers, grants, acting-character authorization, absorption/recovery.
8. **Marketplace vertical slice:** catalog/defaults, publication, escrow/proxy/BIN, close/settlement, public pages, Discord.
9. **Admin/operations:** configuration, state gates, audits/interventions, imports, backup/restore, packaging, health/version support.
10. **Hardening:** concurrency/fault injection, security review, accessibility/mobile regression, upgrade/rollback rehearsals, operator documentation.

Each workstream should end in executable acceptance tests against a disposable ACE/MariaDB environment. Avoid a plan that builds the entire database, then entire API, then entire UI without complete custody-safe vertical slices.

## Explicit non-goals

- Cross-server inventories, links, transfers, vaults, or marketplace.
- Compatibility claims for unmodified or arbitrary ACE versions.
- Bulk import/migration from mule characters.
- Containers in Cloud custody.
- Finite-lifespan or actively attached/cooldown-bound items.
- Direct web mutation of native biotas.
- Manual backpack-slot arrangement.
- Abstract Unit wallets or manufactured auction change.
- Hidden reserves, bid cancellation, anti-sniping extensions, or marketplace fees in version one.
- Mandatory MMD acceptance.
- Direct Allegiance Vault withdrawal, marketplace use, or external offers.
- Personal View + Deposit permission.
- Parallel guild roster or rank-based vault ACLs.
- Email notifications or a granular notification-settings matrix.
- Cloud-specific TOTP or automated account-compromise adjudication.
- Bundled/distributed client DAT files or extracted source assets.

## Planning decisions still left to engineering

These are implementation choices, not unresolved product behavior. Select them during planning and record additional ADRs only when the trade-off is hard to reverse:

- Physical table/aggregate layout and migration mechanics.
- REST/command/live-stream endpoint shapes and internal serialization.
- Rebuildable search-index library and on-disk/in-DB projection format.
- Exact private-service authentication mechanism and deployment topology.
- Icon compositor/cache implementation and legally usable font-loading/fallback mechanics that meet the visual fidelity requirement.
- Backup destination adapters, encryption implementation, schedule, and retention defaults.
- Observability stack, SLO thresholds, and worker retry schedules.
- Concrete UI component libraries, provided they do not impose a generic dashboard aesthetic.

No material product question is intentionally left open in this handoff.
