# AC Cloud Mule

AC Cloud Mule is an account-oriented storage and exchange system for ACE emulator servers. It removes stored items from ordinary play while preserving them as authoritative ACE objects that can later be transferred safely.

## Language

**Cloud Inventory**:
A collection of ACE items held outside character inventories while remaining owned by an account, another player, or a guild.
_Avoid_: Mule, database inventory

**Cloud Item**:
An ACE item placed in **Cloud Inventory** and therefore unavailable to ordinary in-game interaction until transferred or withdrawn.
_Avoid_: Web item, copied item

**Item Custody**:
The exclusive persisted possession state of a native ACE biota: either ordinary ACE world possession or one **Cloud Custody Record**, never both.
_Avoid_: Database access, copied ownership

**World Boundary Authority**:
ACE's exclusive authority to validate and execute transitions between ordinary world possession and Cloud custody.
_Avoid_: Web withdrawal, direct biota mutation

**Cloud Transaction Authority**:
The companion backend's exclusive authority to transact owners, reservations, offers, vault activity, bids, listings, and settlements while items remain in Cloud custody.
_Avoid_: World inventory authority, biota editor

**Cloud Custodian**:
A shared zero-payout vendor NPC whose native sell window deposits its submitted contents into the selling account's **Cloud Inventory**.
_Avoid_: Personal vendor, drag-to-give NPC

**Custodian Location**:
An administrator-controlled world position at which a **Cloud Custodian** is available.
_Avoid_: Vendor account, personal location

**Cloud-eligible Item**:
An inventory item that ACE permits in player-to-player trade and that is neither a container nor otherwise unsafe or stateful to transfer.
_Avoid_: Any non-Attuned item

**Frozen Enchantment**:
A temporary item enchantment whose persisted remaining duration is preserved without ticking while its native biota is in Cloud custody and resumes when the item returns to ACE inventory processing.
_Avoid_: Removed buff, offline enchantment scheduler

**Raw Pyreal Deposit**:
Pyreal currency submitted through a **Cloud Custodian's** sell window and exchanged for MMDs at a rate of 287,500 Pyreals per MMD.
_Avoid_: Marketplace units

**Pyreal Remainder**:
An account's exact unconverted Raw Pyreal balance below the 287,500-Pyreal threshold for creating the next MMD.
_Avoid_: Dust, rounded balance

**Main Account**:
The ACE account that owns a player's unified Cloud Inventory and controls any accounts linked to it.
_Avoid_: Username, display character

**Linked Account**:
An independently authenticated ACE account whose Cloud assets and future deposits are assigned to a **Main Account**.
_Avoid_: Shared account, merged login

**Display Character**:
A character identity selected from the Main or Linked Accounts to represent the player publicly without exposing an ACE account name.
_Avoid_: Username, account name

**ACE-backed Login**:
A Cloud Mule sign-in whose credentials are verified exclusively by a private ACE authentication endpoint before a web session is created.
_Avoid_: Direct auth-database login

**ACE Auth Bridge**:
A small independently hosted private service that reuses ACE's password verifier and issues short-lived Cloud Mule authentication grants without depending on the ACE world process.
_Avoid_: Web password verifier, public auth endpoint

**Withdrawal Token**:
A high-entropy, single-use authorization that lets an eligible character receive a selected set of Cloud Items within 15 minutes.
_Avoid_: Withdrawal command, item ID

**Withdrawal Reservation**:
An exclusive, temporary hold on every Cloud Item selected for one pending Withdrawal Token.
_Avoid_: Pending ownership transfer

**Withdrawal Landblock**:
A user-named landblock, stored in `0x123E` format, in which Withdrawal Tokens may be redeemed.
_Avoid_: Coordinate radius, Custodian Location

**Marketplace Unit**:
A listing-specific measure used to compare the seller's accepted currencies without becoming a separately owned currency.
_Avoid_: Coin, wallet balance

**Bid Escrow**:
An exclusive reservation of actual accepted-currency Cloud Items or stack quantities that backs a bidder's maximum bid.
_Avoid_: Unit balance, account debit

**Exactly Payable Bid**:
A bid price that can be composed without change from whole accepted-currency items or whole quantities split from stackable items in Bid Escrow.
_Avoid_: Rounded bid, overpayment

**Proxy Increment**:
The smallest exactly payable price at least one Unit above the competing maximum or opening price and no greater than the leading bidder's maximum.
_Avoid_: Fixed denomination jump, percentage increment

**Authorized Payment Mix**:
The bidder-approved set of specific currency items and stack quantities from which the marketplace may construct payable bids up to a stated maximum.
_Avoid_: Available balance, automatic payment source

**Hard Close**:
An auction ending at its published timestamp without extension for late bidding activity.
_Avoid_: Soft close, anti-sniping extension

**Auction Duration**:
One of the administrator-configured listing lengths offered to sellers, defaulting to 1, 3, or 7 days and converted into an exact end timestamp at publication.
_Avoid_: Arbitrary end date, editable closing time

**Opening Price**:
The public, immutable minimum Unit price at which an auction may receive its first exactly payable bid.
_Avoid_: Hidden reserve, secret minimum

**Buy It Now**:
An optional listing price that ends an auction immediately when the buyer confirms either exact payment or a disclosed overpayment with no change.
_Avoid_: Maximum bid, reserve price

**Buy It Now Overpayment**:
The buyer's explicit, warned choice to tender more Units than the advertised Buy It Now price when their authorized physical currencies cannot compose it exactly.
_Avoid_: Hidden rounding, manufactured change

**Bid Priority**:
The deterministic ordering of accepted maximum bids, with earlier committed bids winning equal-maximum ties.
_Avoid_: Browser timestamp, random tie-break

**Binding Bid Floor**:
The current public auction price below which the leading bidder may not reduce their private maximum or supporting escrow.
_Avoid_: Bid cancellation, price rollback

**Currency Terms Snapshot**:
The immutable set of accepted currency weenie classes and integer Unit values captured when a listing is published.
_Avoid_: Seller's current currency settings

**Currency Catalog**:
The administrator-approved set of economically interchangeable weenie classes that sellers may include in Currency Terms Snapshots.
_Avoid_: Seller inventory, currency balance

**Listing Reservation**:
An exclusive hold on the exact Cloud Item or immutable item bundle offered by a published marketplace listing.
_Avoid_: Listing copy, pending sale

**Settlement Pending**:
A closed auction with an immutable winner and price whose reserved assets await an idempotent, atomic Cloud transaction.
_Avoid_: Failed auction, completed sale

**Transfer Offer**:
A time-limited, revocable proposal to transfer a reserved set of Cloud Items to another Main Account upon recipient acceptance.
_Avoid_: Direct gift, mailed item

**Sharing Grant**:
An owner's permission assignment to another resolved Main Account group for access to the owner's personal Cloud Inventory.
_Avoid_: Character permission, account-password sharing

**Allegiance Vault**:
A cloud-only, free-to-a-good-home repository whose current ACE allegiance members may freely contribute to and take from.
_Avoid_: Guild bank, shared personal inventory

**Acting Character**:
The specific current character whose ACE allegiance membership authorizes an Allegiance Vault operation by a logged-in Main Account.
_Avoid_: Display Character, account-wide guild identity

**Vault Absorption**:
The automatic transfer of every item from a former monarch's Allegiance Vault into the new monarch's Allegiance Vault when the former monarch joins that allegiance.
_Avoid_: Governance handoff, frozen vault

**Activity Ledger**:
The append-only history of every Cloud asset, reservation, permission, identity, marketplace, vault, and administrative state change.
_Avoid_: Deposit log, editable history

**Marketplace State**:
The administrator-controlled Enabled, Disabled, or Maintenance Frozen operating mode governing marketplace activity and auction clocks.
_Avoid_: Marketplace cancellation

**Global Cloud Maintenance**:
An administrator-controlled read-only safety state that pauses every Cloud mutation and all expiry clocks without cancelling or unlocking assets.
_Avoid_: Marketplace Disabled, outage mode

**Safe Regex Search**:
An advanced indexed-search mode constrained by non-backtracking execution, time, pattern, input, result, and request-rate limits.
_Avoid_: Raw SQL search, unrestricted regex

**Inventory Category**:
A single normalized display grouping derived primarily from ACE ItemType flags with deterministic priority and a WeenieType fallback.
_Avoid_: Object class, raw WeenieType

**Mule Page**:
A deterministic 102-item virtual page within one Inventory Category, presented as a 6-by-17 AC-style grid on desktop and reflowed responsively without changing page membership.
_Avoid_: Physical container, persistent slot map

**Full Cloud Appraisal**:
A character-independent, visually faithful reconstruction of the in-game ID panel containing all player-facing information ACE would reveal after a successful complete appraisal.
_Avoid_: Raw database dump, skill-gated appraisal

**Icon Reconstruction**:
The deterministic static composition of an item's in-game inventory image from its instance properties and corresponding DAT assets, including resolved base icon, clothing palette and shade, underlay, overlay, and still UI-effect layers.
_Avoid_: WCID thumbnail, approximate icon

**Asset Import**:
An administrator-initiated, versioned extraction of web-ready AC icons and approved interface textures from an uploaded client DAT file.
_Avoid_: Bundled client assets, manual server deployment

**Cloud Custody Record**:
The exclusive first-class record that keeps one native ACE biota out of world possession and identifies either its single Cloud owner or the quantity lots backed by a stackable biota.
_Avoid_: Serialized item copy, hidden mule container

**Cloud Stack Lot**:
An independently owned or reserved quantity claim against one stackable biota in Cloud custody, materialized as a separate native ACE biota only when a world-boundary operation requires it.
_Avoid_: Copied stack, web-created biota

**Storage Quota**:
An optional server-wide personal or Allegiance Vault limit measured by native biotas plus projected biotas for independently materializable Cloud Stack Lots and disabled by default.
_Avoid_: Slot count, Pyreal balance limit

**Cloud Shard**:
One isolated ACE world and its Cloud Mule economy, identified by an immutable server ID even when infrastructure or authentication services are shared with other worlds.
_Avoid_: Cross-server marketplace, global inventory

**Progressive Interface**:
The product-wide rule that common actions stay visually simple while advanced capability appears through direct manipulation, contextual controls, and sensible defaults rather than persistent form clutter.
_Avoid_: Settings wall, checkbox-driven workflow

**Public Marketplace**:
The unauthenticated, read-only catalog of active and completed listings exposed through searchable, shareable URLs.
_Avoid_: Public inventory, anonymous trading

**Public Listing Retention**:
The visibility policy that keeps successful sales public indefinitely and removes unsold or cancelled listing pages from public access after 30 days without deleting their ledger history.
_Avoid_: Ledger deletion, permanent stale listing

**Notification Center**:
The authenticated user's compact in-app inbox for private actionable events, presented through an unread badge and contextual destinations.
_Avoid_: Email subsystem, per-event settings matrix

**Custody Outbox**:
ACE's durable ordered record of locally committed Cloud Mule mutations, used to rebuild or catch up companion-web read models without making deposits depend on web availability.
_Avoid_: Best-effort webhook, web-owned deposit

**Operator Bootstrap**:
The one-time command that validates database access, creates the dedicated Cloud schema and restricted identities, runs migrations, generates secrets, and reports the initial web URL.
_Avoid_: Recurring command-line administration, manual SQL setup

**Coordinated Backup**:
A verified point-in-time backup that captures the Cloud schema and relevant ACE shard/auth state together so custody records cannot be restored against mismatched native biotas.
_Avoid_: Cloud-only dump, unverified restore

**Companion Stack**:
The .NET 10 Cloud backend, independent ACE Auth Bridge, background workers, TypeScript/React client, and MariaDB Cloud schema shipped alongside the ACE fork.
_Avoid_: Unrelated backend runtime, mandatory search cluster

**Live State Stream**:
The versioned server event channel that keeps public Marketplace and authorized private Cloud views synchronized across tabs and devices.
_Avoid_: Poll-only UI, client-owned state

## Relationships

- A **Cloud Inventory** contains zero or more **Cloud Items**
- Every **Cloud Item** belongs to exactly one current owner
- Every **Cloud Stack Lot** belongs to exactly one current owner and is backed by exactly one stackable native biota under a Cloud Custody Record
- ACE is the **World Boundary Authority**: only ACE may create Cloud custody from an in-world item or restore a Cloud Item to the playable world
- The companion backend is the **Cloud Transaction Authority** and may mutate only dedicated Cloud ownership, reservation, and transaction records while their native biotas remain unloaded and unchanged
- Cloud-only transfers never write native biota Container, Wielder, or Location properties
- Partial withdrawal, offer, listing, escrow, or Allegiance Vault actions create or transfer **Cloud Stack Lots** in the Cloud schema without splitting the backing native biota
- ACE materializes any required native child stacks at withdrawal, uses its normal GUID allocation, preserves the original GUID for the remainder where possible, and records parent-child lineage
- A transactional handoff with row locking, optimistic version checks, idempotency keys, and database constraints serializes deposit and withdrawal against simultaneous Cloud operations
- ACE commits Custodian deposits, Raw Pyreal conversion, Activity Ledger entries, and corresponding **Custody Outbox** events without requiring the companion web service to be online
- The web application consumes the Custody Outbox idempotently and can rebuild its searchable read models after an outage
- The companion backend uses a narrowly privileged database identity that can transact the Cloud schema but cannot modify native ACE biota tables
- While the ACE game process is offline but its database is healthy, all cloud-only inventory and Marketplace activity continues; withdrawal creation and redemption are unavailable
- If the shared database is unavailable, the web application permits cached browsing where safe but becomes read-only and never queues ownership mutations
- The **Companion Stack** shares versioned contracts and pure custody-domain rules with ACE without loading or coupling the web process to live ACE world objects
- MariaDB remains authoritative; search uses a rebuildable indexed read model and introduces no second mandatory database in the first release
- Public Marketplace changes and authorized private inventory, reservation, bid, listing, offer, and notification changes propagate through a **Live State Stream**
- Every streamed entity carries an authoritative version; optimistic UI is limited to suitable actions and must reconcile or visibly reverse when the committed result differs
- Every Cloud Inventory, account link, reservation, offer, listing, currency rule, Allegiance Vault, and ledger event belongs to exactly one **Cloud Shard**
- Each Cloud Mule deployment serves exactly one **Cloud Shard**; an operator may host separate deployments for several worlds on shared infrastructure, but transferable state and administrator authority never cross their server-ID boundaries
- Releases include self-contained Windows x64 services plus Docker images and a Compose example from the same supported version
- **Operator Bootstrap** is the only required setup command; after it succeeds, routine configuration and DAT imports occur in the administrator web interface
- First-party backup tooling creates a **Coordinated Backup**, and the admin dashboard reports the last verified backup status
- Restore is an offline guarded operator workflow that validates custody invariants and refuses mismatched Cloud and ACE snapshots before the world process may start
- Configuration and the active asset manifest are included in backup scope; secrets and retained source DAT files are optional encrypted inclusions
- Opening any **Cloud Custodian** uses ACE's familiar vendor interface; the player fills the sell pane and submits it as a zero-payout Cloud deposit
- The sell-pane contents, including the stack objects and quantities represented by the native client transaction, define the requested deposit batch
- A Custodian sale never creates a Pyreal payout or ordinary vendor resale inventory
- Custodian sell batches use familiar vendor partial-success behavior: each submitted row is validated and committed independently
- Eligible rows enter Cloud custody even when other rows fail; every rejected item remains with the player and reports its exact reason
- A **Cloud Custodian** occupies exactly one enabled **Custodian Location**
- A **Cloud-eligible Item** must be legal under ACE's player-to-player trade rules
- Containers are not **Cloud-eligible Items** in the first version, even when empty
- Items with finite lifespans, active cooldowns, summoned attachments, or other time-dependent runtime ownership state are not Cloud-eligible
- Runtime item enchantments are allowed as **Frozen Enchantments**; deposit preserves their native registry state and withdrawal resumes ACE heartbeat processing from the same remaining duration
- Permanent built-in item spells and other static properties are unaffected by the runtime-state rule
- Equipped items must be moved into ordinary inventory before they can become **Cloud Items**
- A rejected deposit identifies the exact reason to the player in game
- A **Raw Pyreal Deposit** produces one MMD for every 287,500 Pyreals accepted
- A **Raw Pyreal Deposit** is combined with the account's existing **Pyreal Remainder** before conversion
- A **Pyreal Remainder** is preserved without rounding or loss and may be withdrawn as raw Pyreals
- A **Main Account** owns all Cloud assets transferred from each of its **Linked Accounts**
- Deposits made by a **Linked Account** enter its **Main Account's** Cloud Inventory
- Linking transfers ownership of the linked account's existing Cloud Inventory to the **Main Account**
- Linking requires a prominent destructive-action warning and a deliberately delayed confirmation control
- Unlinking a **Linked Account** does not restore or reassign assets previously transferred to the **Main Account**
- After unlinking, future deposits belong to the newly independent account
- Only a standalone ACE account with no existing parent or child link relationship may be added as a **Linked Account**
- Linked-account trees and whole-group merges are prohibited
- A source account must have no pending transaction, reservation, listing, bid, settlement, Withdrawal Token, or Transfer Offer before linking
- Linking revokes every incoming and outgoing personal **Sharing Grant** associated with the source account while leaving the destination Main Account's grants unchanged
- Linking does not alter character-specific Allegiance Vault eligibility
- Only **Main Account** credentials grant web-management access to the unified Cloud Inventory
- A **Linked Account** may deposit in game but its credentials cannot view, withdraw, list, transfer, bid with, or administer the **Main Account's** assets
- Login uses the **Main Account's** ACE account name while public activity uses its selected **Display Character**
- The default **Display Character** is the current character with the highest `total_Logins` across the Main and Linked Accounts
- If the selected **Display Character** is deleted or renamed, the system selects the remaining current character with the highest `total_Logins`
- An **ACE-backed Login** never gives Cloud Mule direct password-hash verification or write responsibility
- The **ACE Auth Bridge** remains available independently of ACE world restarts, so both existing sessions and new logins can continue using the web application during game maintenance
- A **Withdrawal Token** may be redeemed by any character currently belonging to the Main Account or one of its Linked Accounts
- A **Withdrawal Token** cannot be redeemed by an unrelated account and expires after 15 minutes or its first successful use
- An already-issued Withdrawal Token remains redeemable during a web outage because ACE can validate its local reservation and redemption rules
- Withdrawal history identifies both the owning **Main Account** and the exact receiving character and ACE account
- A **Withdrawal Reservation** prevents its Cloud Items from being listed, transferred, modified, or included in another withdrawal
- A **Withdrawal Reservation** ends on successful redemption, explicit cancellation, or expiry of its **Withdrawal Token**
- A multi-item withdrawal delivers every reserved item or none of them
- Insufficient recipient capacity leaves the reservation active and retryable until expiry
- Withdrawal redemption requires an alive, fully loaded, non-combat player who is not trading, portaling, recalling, or performing another inventory transfer
- Redemption must occur at an allowed location and pass ACE's complete native receive checks, including slots, stack merges, burden, uniqueness, and other inventory rules
- Any failed redemption check delivers nothing, preserves the reservation until expiry, and reports actionable reasons in game
- Custom withdrawal authorization uses administrator-managed **Withdrawal Landblocks**, not position radii
- Custodian Locations and **Withdrawal Landblocks** are independent settings
- A **Marketplace Unit** is a valuation layer only and is never issued as an independent asset
- Every bid is backed by **Bid Escrow** already present in the bidder's Cloud Inventory
- Winning **Bid Escrow** transfers its actual currency items or stack quantities to the seller; losing escrow is released
- Proxy bidding uses a one-Unit minimum **Proxy Increment** and advances to the smallest **Exactly Payable Bid** above the competing price and within the bidder's maximum
- A proxy price may visibly jump by more than one Unit only when the bidder's authorized physical currency denominations cannot pay an intermediate value exactly
- The bidder must confirm a disclosed price jump caused by indivisible currency denominations
- **Bid Escrow** contains only assets included in the bidder's **Authorized Payment Mix**
- Bidders drag accepted-currency rows into spending-priority order; when several exact tenders exist, settlement consumes higher-priority WCIDs first
- Within one interchangeable WCID, deterministic GUID ordering selects the specific authorized items or stack quantities spent
- The bid interface previews the currently projected tender and explains contextually that proxy competition may change its final composition
- Winning settlement transfers an exactly payable subset and releases all unused escrow
- Auctions use a **Hard Close** and accept valid bids until the published end timestamp
- Sellers choose an **Auction Duration** from administrator-configured options, and publication stores the listing's exact immutable end timestamp
- Each auction has a public immutable **Opening Price** and no hidden reserve; if no exactly payable bid meets it, the listing closes unsold and releases its Listing Reservation
- Sellers may publish any positive integer Opening Price or Buy It Now price regardless of whether the Currency Terms Snapshot can theoretically compose that exact amount
- The listing form does not normalize, snap, or reject prices based on currency-denomination divisibility
- **Buy It Now** remains available after bidding begins until purchased or reached by the current auction price
- Entering a maximum bid at or above **Buy It Now** opens a distinct purchase confirmation instead of silently converting the bid
- Buy It Now prefers exact payment; when unavailable, the buyer may confirm the smallest authorized tender above the advertised price after seeing the exact excess and a no-change warning
- The bidder may revise their authorized currencies or drag priority order before confirming the tender
- A Buy It Now seller receives the full actual tender; the completed listing and Activity Ledger preserve both advertised price and actual payment
- **Buy It Now Overpayment** is the only overpayment path; proxy bids and normal auction settlement remain exactly payable and never spend above the displayed winning price
- Successful **Buy It Now** settlement releases all competing Bid Escrow
- A Main Account and all of its Linked Accounts are prohibited from bidding on or buying that ownership group's own listing
- An account link is blocked while it would create a seller-bidder identity conflict in an active auction
- **Bid Priority** is assigned by the server's committed transaction order
- Maximum bids remain private while the current price and winning status are visible
- An accepted bid cannot be cancelled
- The current leading bidder may reduce their maximum no lower than the **Binding Bid Floor**, without lowering the public price or changing the current winner through that reduction
- Reducing a maximum atomically releases only the escrow no longer required to exactly cover the reduced commitment
- A bidder may increase a maximum or revise currency priority only when the resulting Authorized Payment Mix still exactly covers every binding obligation
- Active-auction bid history anonymizes bidders from all non-admin viewers, including the seller
- Successful settlement reveals buyer and seller Display Characters in the completed listing and sold webhook, while maximum bids remain private
- The **Public Marketplace** exposes listing details, seller Display Character, accepted currencies, current prices, and anonymized bid history without requiring login
- Public listing pages never expose ACE account names, private inventories, maximum bids, credentials, or private ledger activity
- Bidding, buying, selling, and every personal Cloud Mule action require **ACE-backed Login**
- The **Notification Center** reports Transfer Offers, outbids, sales, settlements, sharing changes, reservation outcomes, and administrative actions affecting the user's assets
- Repetitive events are coalesced, and visiting an event's destination may mark its notification read automatically
- The first version sends no email and exposes no granular notification-preference controls
- Successful sale pages remain public indefinitely as price history
- Unsold, seller-cancelled, and administrator-cancelled listing pages leave public search and direct access after 30 days
- An administrator-cancelled page shows only a neutral public status; its detailed reason remains private and available through the Activity Ledger to authorized viewers
- Every listing has a **Currency Terms Snapshot** derived from the seller's defaults and optional per-listing deselections
- Changes to seller defaults do not alter published listings, and published currency terms cannot be edited
- Every published listing field is immutable, including bundle, title, tags, prices, currency terms, duration, and end time
- A seller corrects an unbid listing by cancelling and republishing it; after the first accepted bid only an audited administrator intervention can stop it
- Existing listings retain an administrator-disabled currency unless an administrator explicitly cancels them with an audited reason
- A listing requires at least one accepted currency, but MMD acceptance is optional
- Sellers choose accepted currency WCIDs from the **Currency Catalog** without needing to own an example item
- Currency eligibility matches exact WCID, so administrators should catalog only classes whose instances are economically interchangeable
- Publishing a listing immediately creates a **Listing Reservation** that prevents withdrawal, transfer, modification, currency use, or duplicate listing
- A seller may cancel and release a **Listing Reservation** only before the first accepted bid
- Bundle membership is immutable after publication, and an unsold listing releases its reservation at close
- If a transient service or database failure prevents the authoritative Cloud transfer after close, the auction enters **Settlement Pending** with both sides' reservations intact
- A **Settlement Pending** auction retries idempotently and cannot change its winner or final price
- Marketplace settlement transfers the complete listing bundle and the authorized winning tender atomically or transfers nothing; normal auctions tender exact payment while confirmed Buy It Now may include disclosed overpayment
- The initial marketplace charges no listing fee, buyer fee, or seller commission
- Sending items to another player creates a **Transfer Offer** rather than changing ownership immediately
- A **Transfer Offer** resolves a current character name to an immutable recipient account ID, expires after seven days, and transfers all offered items or none
- The sender may cancel before acceptance, while rejection, cancellation, or expiry releases the reserved items
- Immediate personal-to-**Allegiance Vault** contributions do not use **Transfer Offers**
- A **Sharing Grant** is addressed through a current character but stored against the resolved immutable Main Account ID
- A **Sharing Grant** applies to the grantee's current Main and Linked Accounts and survives character deletion or rename
- Personal **Sharing Grants** have only two access levels: View Only and View & Withdraw
- Personal inbound transfers always use **Transfer Offers**; there is no personal deposit permission
- View & Withdraw permits Withdrawal Tokens for the grantee's own Main/Linked account group but does not permit marketplace, bidding, account, settings, transfer-offer, or permission actions
- An explicit individual **Sharing Grant**, including None, overrides guild-derived personal-inventory access
- Loss of qualifying guild membership immediately revokes derived access and invalidates unredeemed Withdrawal Tokens created through it
- **Allegiance Vault** membership and rank come exclusively from ACE rather than a separate Cloud Mule roster
- Access to an **Allegiance Vault** is evaluated through an eligible **Acting Character**, so membership on one character does not grant unrelated alts access
- An active **Allegiance Vault** grants every current member equal view, contribute, and take privileges
- Contributing transfers a Cloud Item immediately from personal ownership to the **Allegiance Vault**
- Taking transfers a Cloud Item immediately from the **Allegiance Vault** to the member's personal Cloud Inventory
- An **Allegiance Vault** cannot create Withdrawal Tokens, marketplace listings, bids, or external Transfer Offers
- To use a vault item in game or on the Marketplace, a member must first take it into personal Cloud Inventory
- When a monarch joins another allegiance, **Vault Absorption** moves every item from the former vault into the new allegiance's vault and archives the emptied source vault
- **Vault Absorption** preserves each item's provenance and the source and destination vault identities in history
- ACE blocks deletion of a monarch character while that monarch's **Allegiance Vault** is nonempty
- An out-of-band monarch deletion leaves the vault available only for audited administrator recovery
- The **Activity Ledger** records immutable actor and owner IDs, display-name snapshots, item identity or relevant snapshots, timestamps, outcomes, and reasons
- Users see ledger activity involving their assets or actions, allegiance members see their complete vault history, and administrators may inspect the global ledger
- ACE administrator access level 5 is revalidated for every sensitive web request
- Administrator inspection of another inventory and every intervention create **Activity Ledger** entries
- Administrator transfers require a written reason and delayed confirmation; force transfer may unwind uncommitted reservations but cannot override a closed auction's committed settlement
- Affected owners receive the administrator's intervention reason in an in-app notification
- Enabled **Marketplace State** permits all marketplace activity
- Disabled **Marketplace State** blocks new listings while allowing already-published auctions to bid, use Buy It Now, close, and settle normally
- Maintenance Frozen **Marketplace State** blocks all marketplace transactions and clock progress, shifting auction end times by the frozen duration when resumed
- No **Marketplace State** automatically cancels an auction or releases its reservations
- **Global Cloud Maintenance** keeps authenticated and public reads available but blocks deposits, withdrawals, links, transfers, sharing changes, vault actions, listings, bids, and settlements
- Entering Global Cloud Maintenance pauses auction, offer, reservation, and Withdrawal Token clocks; leaving it shifts deadlines by the exact frozen duration
- Entry and exit require an administrator reason and confirmation, write immutable ledger events, and notify the admin webhook
- Global Cloud Maintenance never cancels obligations, changes ownership, or releases reservations automatically
- Discord webhooks announce only successful listing publication and completed sale settlement
- Discord announcements use public Display Characters and never expose private deposits, transfers, vault activity, or ACE account names
- Webhook delivery is asynchronous and cannot block or roll back a marketplace transaction
- **Safe Regex Search** operates only on an authorization-scoped prepared item index and can be disabled independently of normal search
- Administrator activity recorded in the **Activity Ledger** also produces Discord webhook notifications
- Public marketplace events and administrator events use separate Discord webhooks
- The Admin Audit Webhook summarizes inspections, interventions, recovery, and configuration changes without exposing credentials, tokens, or unnecessary private item data
- Every Cloud Item belongs to exactly one **Inventory Category** for AC-style grid paging while all underlying properties remain searchable
- AC-style inventory grids are automatically sorted virtual views with no persistent manual slots or gaps in the first version
- Each category is divided into 102-item **Mule Pages** named `[Inventory Category] Mule [number]`
- A Mule Page uses a 6-by-17 desktop grid and may reflow on narrower screens while preserving the same page membership for the current filter and sort
- Category pages are created or removed automatically and share their filter and sort semantics with the spreadsheet view
- Grid sort order is deterministic, offers user-selectable sort keys, and uses stable item identity to break equal-value ties
- Owners and authorized viewers receive the same **Full Cloud Appraisal** without Display Character or appraisal-skill gating
- **Full Cloud Appraisal** excludes internal administrator-only fields and uses familiar player-facing sections and wording
- The Full Cloud Appraisal panel closely reproduces the in-game ID panel's typography, colors, spacing, section order, borders, and responsive reading flow
- Every item image uses **Icon Reconstruction** from instance properties rather than a generic WCID thumbnail
- Icon Reconstruction resolves ACE's base Icon DID and property-driven variants such as ClothingBase, PaletteTemplate, Shade values, IconUnderlay, IconOverlay, and UiEffects against the active Asset Import manifest
- Icon Reconstruction contains no animation; in-game UiEffects such as magical glow are composited as their accurate still layers
- Stack counts, selection, reservation, and other web state remain separate overlays and never alter the reconstructed source icon
- Missing or invalid asset references show an explicit neutral fallback and create an administrator-visible diagnostic rather than silently displaying a plausible but incorrect icon
- An ACE administrator performs the initial **Asset Import** through the web app and repeats it only when supplying updated client data
- A new **Asset Import** validates and extracts into staging, then atomically replaces the active asset manifest only after success
- The currently active assets remain available during import, and import progress and outcome are audited
- The latest uploaded source DAT is retained in protected, non-public storage for automatic reprocessing by future Cloud Mule versions
- Every deposited item remains backed by its original native ACE biota and exactly one **Cloud Custody Record**; only a derived **Cloud Stack Lot** may temporarily lack its own materialized biota
- Deposit atomically replaces in-world possession with a **Cloud Custody Record**, while withdrawal atomically performs the inverse
- ACE loading and integrity tools treat cloud custody as a valid persisted state and never spawn Cloud Items into playable landblocks
- Database constraints prohibit simultaneous cloud custody and in-world Container, Wielder, or Location ownership
- Cloud Mule is an opt-in, self-hosted extension for explicitly supported ACE releases and requires server-code changes, schema migrations, a private API, and the companion web application
- The ACE fork and companion Cloud Mule application are released under AGPL-3.0, while operator configuration, credentials, uploaded DAT files, and extracted assets remain private deployment data
- Cloud Mule adds no TOTP or product-specific second factor; server owners handle rare account compromises through audited administrator recovery
- Personal and Allegiance Vault storage is unlimited unless an administrator enables a server-wide **Storage Quota**
- One stackable biota counts as one item, each additional independently materializable Cloud Stack Lot counts as one projected item, and a Pyreal Remainder does not count toward a **Storage Quota**
- Exceeding a lowered or merged quota never removes assets; the owner may perform count-reducing actions but cannot receive new deposits, offers, purchases, or vault items
- Storage Quotas are checked when a new incoming obligation is created or accepted, but never invalidate an already-binding settlement
- A committed auction, confirmed Buy It Now, accepted Transfer Offer, or Vault Absorption may complete above a newly lowered quota and places the recipient into reduce-only state
- Ordinary deposited stackable Cloud Items retain separate native biotas and GUIDs; aggregate counts in the UI do not merge underlying stacks
- Partial Cloud actions may create Cloud Stack Lots, but only ACE creates their eventual child biotas and GUIDs
- Raw Pyreal conversion is the only automatic consolidation or replacement of deposited stackable assets
- Existing character and mule inventories enter Cloud Mule only through ordinary **Cloud Custodian** deposits; Cloud Mule will not provide a bulk-import or database-migration path for player items
- Every workflow follows the **Progressive Interface** rule: include the required power, but avoid unnecessary wording, checkboxes, selectors, and permanently visible controls
- The visual system is a deliberate hybrid: AC-authentic inventory and appraisal surfaces flow into quieter modern Marketplace, table, settings, and admin layouts using the same material and typographic language
- Desktop grids closely mirror AC where layout permits; mobile may reflow substantially while retaining accurate icons, familiar hierarchy, and direct access to appraisal and actions
- Responsive mobile behavior, keyboard navigation, visible focus, screen-reader labels, sufficient contrast, and reduced-motion support are release requirements

## Example dialogue

> **Dev:** "Can the marketplace transfer a **Cloud Item** by updating its owner in MySQL?"
> **Domain expert:** "Yes, but only through the **Cloud Transaction Authority** and dedicated custody tables. It never edits the native biota while that item remains in Cloud custody."

## Flagged ambiguities

- "The web app stores items in the ACE database" could imply direct mutation of live ACE objects — resolved: ACE alone controls entry to and exit from the world, while the narrowly privileged **Cloud Transaction Authority** may transact dedicated off-world custody records without editing native biotas.
- "Personal vendor" implied a separately persisted NPC for every player — resolved: **Cloud Custodians** are shared zero-payout vendor endpoints and the active player session determines the destination **Cloud Inventory**.
- "Drag items onto an NPC" was superseded by a faster bulk interaction — resolved: players place items in a **Cloud Custodian's** native sell pane, and submitting that window requests the Cloud deposit.
- Whether one invalid sell-pane row should roll back a Cloud Custodian batch was ambiguous — resolved: it does not; valid rows deposit, invalid rows remain with exact rejection messages, matching ordinary vendor behavior.
- Default **Custodian Locations** are every mansion and Marketplace; administrators may independently disable mansion locations or Marketplace and may add or remove custom positions in ACE position-string format.
- "Any item that is not Attuned" was initially proposed as the deposit rule — superseded: the first version follows ACE player-to-player trade legality and additionally rejects every container, active pet device, and other stateful or character-bound object.
- A **Raw Pyreal Deposit** that is not an exact multiple of 287,500 was ambiguous — resolved: conversion creates as many MMDs as possible and preserves the exact **Pyreal Remainder** for later deposit or withdrawal.
- "Link accounts" could mean a combined view over separately owned inventories — resolved: linking transfers the linked account's Cloud assets to the **Main Account**, creating one player-facing ownership domain.
- Whether linking may absorb an existing linked-account group was ambiguous — resolved: a source account must be standalone and may have neither a Main Account nor Linked Accounts of its own.
- Whether linking may reassign in-flight obligations was ambiguous — resolved: the source must be fully settled and hold only unreserved assets and balances before linking.
- Whether source-account sharing survives linking was ambiguous — resolved: all source-related personal Sharing Grants are revoked to prevent privilege expansion; Allegiance access remains character-specific.
- Whether unlinking restores previously transferred assets was ambiguous — resolved: unlinking changes future access and deposit routing only; prior assets remain with the **Main Account** unless manually transferred.
- Whether **Linked Account** credentials unlock the unified Cloud Inventory was ambiguous — resolved: only **Main Account** credentials grant web-management access.
- "Username" was used for both login and public identity — resolved: the ACE account name is a private login credential and **Display Character** is the public identity.
- Whether a character rename should update public identity automatically was ambiguous — resolved: a rename invalidates the selection in the same way as deletion and triggers the default-selection rule.
- Whether Cloud Mule independently verifies `ace_auth` password hashes was ambiguous — resolved: ACE remains the sole password verifier and returns a short-lived authentication grant to Cloud Mule.
- ACE-backed login availability during world maintenance was ambiguous — resolved: an independent private **ACE Auth Bridge** reuses ACE's verifier and remains online when the game process restarts.
- First-release packaging was unspecified — resolved: publish self-contained Windows services and Docker/Compose artifacts with one **Operator Bootstrap**, then keep ongoing administration web-based.
- Independent Cloud and shard backups could restore ownership records against the wrong native biotas — resolved: first-party **Coordinated Backup** and guarded offline restore tooling validate one consistent recovery point.
- A later quota reduction could otherwise break a binding transfer — resolved: committed obligations settle even above the new limit, after which the recipient remains reduce-only until usage falls below quota.
- The companion service technology was unspecified — resolved: the **Companion Stack** uses .NET 10 for authority and workers, TypeScript/React for the interactive client, MariaDB for authority, and a rebuildable search read model.
- Cross-tab and cross-device freshness was unspecified — resolved: a versioned **Live State Stream** updates public and private views, with committed server state overriding optimistic presentation.
- Whether a withdrawal is bound to one selected character was ambiguous — resolved: its **Withdrawal Token** may be redeemed by any character in the owner's current Main/Linked account group.
- Whether generating a **Withdrawal Token** leaves its items otherwise available was ambiguous — resolved: it creates an exclusive **Withdrawal Reservation** for the token's lifetime.
- Allowed location alone did not define a safe withdrawal state — resolved: redemption also requires ACE's safe player state and complete inventory-receive validation.
- The geometry of a custom withdrawal location was ambiguous — resolved: each custom location is a named, landblock-wide **Withdrawal Landblock** stored in `0x123E` format.
- Whether bids use actual items or an abstract unit wallet was ambiguous — resolved: every bid is collateralized by **Bid Escrow** containing actual accepted-currency Cloud Items or stack quantities.
- Proxy bidding could produce a price that physical escrow cannot tender — resolved: the auction advances only to the smallest **Exactly Payable Bid**, never manufactures change, and never silently overpays.
- The proxy-bid increment was unspecified — resolved: it advances by at least one Unit and selects the smallest exactly payable winning price, visibly jumping farther only when physical denominations require it.
- Whether the marketplace may choose any eligible bidder assets was ambiguous — resolved: bidders explicitly approve an **Authorized Payment Mix**, including quantities and specific non-stackable items.
- Auction extension behavior was unspecified — resolved: auctions use a **Hard Close** and permit last-second bidding without extending the end time.
- Whether **Buy It Now** survives the first bid was ambiguous — resolved: it remains available until used or reached by the current price.
- Whether a Linked Account may bid on its Main Account's listing was ambiguous — resolved: self-bidding is prohibited across the entire Main/Linked ownership group.
- Equal maximum bids had no tie-break rule — resolved: the earliest committed bid retains **Bid Priority** and wins the tie.
- Whether bidders can retract or reduce accepted maximums was ambiguous — resolved: bids cannot be cancelled, but the leader may reduce their maximum to the current **Binding Bid Floor** and release only excess escrow.
- Whether listing prices must be mathematically representable by accepted currency denominations was disputed — resolved: sellers may publish arbitrary positive Unit prices without GCD-based validation or normalization.
- Whether an inexact Buy It Now must remain unusable was disputed — resolved: the buyer may knowingly make a warned **Buy It Now Overpayment** with no change, and the seller receives the full tender.
- Whether overpayment extends to unattended proxy settlement was ambiguous — resolved: it does not; only a contemporaneously confirmed Buy It Now may overpay, while ordinary auction prices remain exactly payable.
- Bidder identity visibility was unspecified — resolved: active bidders are anonymized until successful settlement, when buyer and seller Display Characters become public.
- Whether seller currency changes alter active listings was ambiguous — resolved: each listing uses an immutable **Currency Terms Snapshot**.
- Whether non-currency listing fields remain editable was ambiguous — resolved: every listing field becomes immutable at publication.
- "MMD is available by default" could imply mandatory acceptance — resolved: MMD is initially available in the server's currency catalog at 1 Unit, but sellers may exclude it.
- Whether a seller must own an accepted-currency exemplar was ambiguous — resolved: sellers select WCIDs from the administrator-managed **Currency Catalog** without owning them.
- Whether listed items remain usable until the first bid was ambiguous — resolved: publication immediately creates an exclusive **Listing Reservation**.
- A transient failure after auction close could leave ownership indeterminate — resolved: the committed result enters **Settlement Pending** and retries an idempotent atomic Cloud transfer.
- Marketplace fees were unspecified — resolved: the initial marketplace is fee-free and sellers receive the full winning payment.
- Whether sending items to another player is immediate was ambiguous — resolved: it creates a recipient-approved, seven-day **Transfer Offer**; personal-to-**Allegiance Vault** contributions remain immediate.
- Whether personal sharing follows a character or a player account group was ambiguous — resolved: each **Sharing Grant** applies to a resolved Main/Linked account group.
- The View + Deposit tier duplicated **Transfer Offers** — resolved: personal sharing has only View Only and View & Withdraw, with no deposit permission.
- Conflicting individual and guild-derived personal access was ambiguous — resolved: explicit individual access overrides guild access, and None is an explicit denial.
- "Guild Bank" used terminology foreign to ACE's domain — resolved: the canonical term is **Allegiance Vault**, introduced informally as a guild bank.
- Whether allegiance access applies to a whole linked-account group was ambiguous — resolved: each vault action is authorized by a specific **Acting Character** in ACE's live allegiance tree.
- ACE defines an allegiance through its monarch, making mergers ambiguous — resolved: the vault follows its monarch and **Vault Absorption** transfers its contents into the destination allegiance's vault when that monarch joins another allegiance.
- Conventional guild-bank role restrictions conflicted with the intended "free to a good home" model — resolved: all current allegiance members have equal transfer-to and transfer-from access.
- "Default" access implied a configurable role policy — resolved: all current allegiance members always have equal vault access in the first version.
- Deleting a monarch can split an ACE allegiance among several new monarchs, leaving no single vault destination — resolved: ACE blocks deletion while the monarch's vault is nonempty.
- "Deposit and withdrawal history" was too narrow for auditable custody — resolved: one immutable **Activity Ledger** covers every consequential Cloud Mule event.
- Administrator visibility and transfer powers lacked accountability — resolved: privileges are revalidated, inspections are logged, transfer reasons are mandatory, and committed settlements cannot be overridden.
- "Disable Marketplace" could strand or cancel live auctions — resolved: Disabled blocks new listings but lets existing auctions finish, while Maintenance Frozen pauses both actions and clocks.
- Whole-system maintenance was conflated with Marketplace state — resolved: **Global Cloud Maintenance** is a separate audited read-only freeze that pauses every mutation and expiry clock without cancelling obligations.
- "New item added" in Discord notifications could include private deposits — resolved: notifications cover published marketplace listings and completed sales only.
- Unrestricted regex could expose the server to expensive expressions or injection — resolved: advanced regex uses **Safe Regex Search** constraints and never executes as SQL.
- Whether Discord covers administrator activity was unspecified — resolved: administrator **Activity Ledger** events generate notifications through a separate admin-only webhook.
- "Object class" did not identify a canonical ACE field — resolved: grid pages use normalized **Inventory Categories** derived from ItemType with deterministic handling of flags and a WeenieType fallback.
- Whether the familiar grid preserves manual backpack placement was ambiguous — resolved: the first version uses automatically sorted virtual pages rather than persistent slot arrangement.
- The capacity and responsive behavior of virtual mule pages were unspecified — resolved: each **Mule Page** contains 102 items, uses a 6-by-17 desktop layout, and reflows without changing current page membership.
- Whether AC styling should cover every web workflow was ambiguous — resolved: inventory and appraisal are fidelity surfaces, while complex workflows use a sleek modern layout derived from the same visual language.
- Item icons could be approximated from a weenie-class thumbnail — rejected: **Icon Reconstruction** must reproduce the instance-specific in-game composition from properties and active DAT assets.
- Icon UiEffects were assumed to require animation — corrected: AC inventory icons are static, including the blue magical layer, so web reconstruction is also static.
- The ID panel could merely expose equivalent data — rejected: **Full Cloud Appraisal** must also closely reconstruct the in-game font, colors, layout, section hierarchy, and interaction on desktop and mobile.
- Whether the web ID panel obeys character appraisal skills was ambiguous — resolved: it provides a character-independent **Full Cloud Appraisal** and deliberately acts as a convenient identification tool.
- "Server operators supply DAT files" could imply a recurring command-line deployment step — resolved: an ACE administrator performs a one-time web upload/import and may repeat it when client data changes.
- Whether the uploaded DAT survives extraction was unspecified — resolved: Cloud Mule retains the latest source file privately so later application versions can regenerate assets without another upload.
- Cloud Item persistence could rely on copies or fake containers — resolved: the original native biota remains intact under one first-class **Cloud Custody Record**.
- "Compatible with all ACE Emulator servers" implied support for unmodified or arbitrary ACE versions — resolved: Cloud Mule targets explicit ACE releases through a self-hosted server extension and versioned migrations.
- Licensing across the ACE fork and network application was unspecified — resolved: the complete Cloud Mule codebase uses AGPL-3.0 and exposes the required source and notices.
- Web-specific two-factor authentication was considered — rejected for the initial product given the community's size and risk profile; ACE-backed login and administrator recovery remain the model.
- Storage capacity was unspecified — resolved: storage is unlimited by default with optional server-wide quotas and a non-destructive reduce-only state when exceeded.
- Whether ordinary stackables are automatically consolidated was ambiguous — resolved: stacks preserve native identity and remain separate; aggregation is presentational only.
- Allowing partial stack actions during ACE downtime conflicted with the web service's prohibition on native-biota writes — resolved: **Cloud Stack Lots** transact quantities off-world and ACE materializes native child stacks only at the world boundary.
- Whether Cloud Mule inventories and commerce could span ACE worlds was ambiguous — resolved: each world is an isolated **Cloud Shard**; shared hosting or authentication never permits cross-shard custody, linking, vault access, transfers, or marketplace activity.
- Whether existing mule characters require a bulk migration utility was ambiguous — resolved: they do not; players permanently use normal Custodian deposits so every item passes the same ownership, eligibility, custody, and audit rules.
- Whether sellers enter arbitrary auction deadlines was ambiguous — resolved: they select an administrator-configured **Auction Duration**, defaulting to 1, 3, or 7 days, and publication fixes the exact deadline.
- Whether auctions have a secret reserve separate from minimum bid was ambiguous — resolved: the public **Opening Price** is the only threshold, and listings without a qualifying exactly payable bid close unsold.
- Multiple authorized currency combinations could spend items the bidder meant to preserve — resolved: bidders drag currency rows into priority order, and exact tender selection consumes higher-priority types first with deterministic within-WCID ordering.
- Feature completeness could produce a cluttered control-heavy web application — resolved: **Progressive Interface** is a cross-cutting requirement, favoring direct manipulation, defaults, and contextual disclosure.
- Whether Marketplace discovery requires an account was ambiguous — resolved: active and completed listings have public searchable shareable pages, while authentication remains mandatory for transactions and personal features.
- Public listing retention was unspecified — resolved: successful sales remain public indefinitely, while unsold and cancelled pages age out after 30 days without removing immutable ledger records.
- Time-dependent Cloud Items could either freeze timers or require a second simulation loop — resolved: finite-lifespan and other active runtime-state items are rejected, but ordinary runtime enchantments persist as **Frozen Enchantments** and resume after withdrawal.
- Private asynchronous events lacked a player-facing delivery channel — resolved: one compact **Notification Center** provides coalesced actionable notices without email or granular preferences in the first version.
- Whether in-game deposits depend on companion-web uptime was ambiguous — resolved: ACE commits them and their **Custody Outbox** events locally; the web catches up later, and existing locally represented Withdrawal Tokens remain redeemable.
- Whether ACE process downtime must freeze Cloud management and auctions was ambiguous — resolved: it does not; the **Cloud Transaction Authority** continues every off-world operation, and only withdrawal creation or redemption requires the running game process.
