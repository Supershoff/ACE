/**
 * Typed mirrors of the Cloud backend's current browser-facing wire contracts
 * (`Source/ACE.Cloud.Backend/AuthSessionEndpoints.cs`, `LoginRequest.cs`,
 * `Source/ACE.Cloud.Backend/CloudInventoryEndpoints.cs`,
 * `Source/ACE.Cloud.Hosting/CloudDiagnosticsEndpoints.cs`,
 * `Source/ACE.Cloud.Hosting/CloudServiceAvailabilityMode.cs`).
 *
 * Only endpoints that exist on the server today are represented here. Marketplace, account-linking,
 * and quota HTTP contracts land with their own issues (#33, #40+) and must not be invented ahead of
 * the server that will serve them.
 */

export interface LoginRequest {
  readonly accountName: string;
  readonly password: string;
}

/** The CSRF token the client must echo back via the `X-Csrf-Token` header on state-changing requests. */
export interface LoginResponse {
  readonly csrfToken: string;
}

export type LoginErrorKind =
  | "origin_denied"
  | "invalid_request"
  | "rate_limited"
  | "authentication_unavailable"
  | "invalid_credentials"
  | "invalid_grant"
  | "grant_already_used";

export interface AdminWhoAmIResponse {
  readonly accountId: number;
  readonly accessLevel: number;
}

export type AdminWhoAmIErrorKind = "unauthenticated" | "forbidden";

/** Mirrors `ACE.Cloud.Hosting.CloudServiceAvailabilityMode` exactly, member for member. */
export type CloudServiceAvailabilityMode = "Operational" | "ReadOnly" | "VersionIncompatible" | "WorldBoundaryUnavailable";

export interface CloudComponentHealthResult {
  readonly component: string;
  readonly healthy: boolean;
  readonly reason: string | null;
}

export interface HealthReadyResponse {
  readonly mode: CloudServiceAvailabilityMode;
  readonly results: readonly CloudComponentHealthResult[];
}

export interface VersionResponse {
  readonly aceExtensionVersion: string;
  readonly cloudSchemaVersion: string;
  readonly contractProtocolVersion: string;
}

/** ARCH-008: only WorldBoundaryUnavailable keeps ordinary off-world reads/writes routable alongside Operational. */
export function isServiceRoutable(mode: CloudServiceAvailabilityMode): boolean {
  return mode === "Operational" || mode === "WorldBoundaryUnavailable";
}

/** ARCH-009: only a healthy database permits mutations; ReadOnly and VersionIncompatible never do. */
export function isServiceWritable(mode: CloudServiceAvailabilityMode): boolean {
  return mode === "Operational";
}

/** Mirrors `ACE.Cloud.Domain.CloudInventoryCategory` exactly, member for member (UI-001). */
export type CloudInventoryCategory =
  | "MeleeWeapons"
  | "MissileWeapons"
  | "Casters"
  | "Armor"
  | "Clothing"
  | "Jewelry"
  | "Foodstuffs"
  | "Currency"
  | "Gems"
  | "SpellComponents"
  | "WrittenMaterial"
  | "Keys"
  | "Portals"
  | "ManaStones"
  | "PromissoryNotes"
  | "LifeStones"
  | "CraftingMaterials"
  | "Miscellaneous";

export type CloudInventorySortKey = "Name" | "Value" | "Burden";

export type CloudInventorySortDirection = "Ascending" | "Descending";

export interface CloudInventoryPermittedActions {
  readonly canWithdraw: boolean;
  readonly canList: boolean;
  readonly canTransfer: boolean;
  readonly canShare: boolean;
}

/** One Mule Page row (`CloudInventoryQueryResultItem`). `stackLotId` is null for a whole (non-stack) Cloud Item. */
export interface CloudInventoryItem {
  readonly itemId: number;
  readonly stackLotId: string | null;
  readonly name: string;
  readonly category: CloudInventoryCategory;
  readonly quantity: number;
  readonly value: number | null;
  readonly burden: number | null;
  readonly isReserved: boolean;
  readonly version: number;
  readonly permittedActions: CloudInventoryPermittedActions;
  /** Content-addressed key for `GET /inventory/icons/{hex}`; null until Icon Reconstruction has composed one. */
  readonly iconCacheKeyHex: string | null;
}

/** One requested Mule Page (`CloudInventoryQueryPageResult`, UI-002). */
export interface CloudInventoryPage {
  readonly category: CloudInventoryCategory | null;
  readonly pageName: string | null;
  readonly pageNumber: number;
  readonly pageExists: boolean;
  readonly totalItemsInScope: number;
  readonly totalPages: number;
  readonly items: readonly CloudInventoryItem[];
}

export interface CloudInventoryQueryResponse {
  readonly page: CloudInventoryPage;
  /** ARCH-007 projection-lag cursor: compare against a Live State Stream cursor, not a wall-clock timestamp. */
  readonly asOfCustodyOutboxSequenceNumber: number;
}

export type CloudInventoryQueryErrorKind = "unauthenticated" | "linked_account_restricted" | "invalid_page";

/** Mirrors `ACE.Cloud.Domain.CloudAppraisalSectionKind` exactly, in ACE's own player-facing section order. */
export type CloudAppraisalSectionKind =
  | "Header"
  | "Description"
  | "Requirements"
  | "Activation"
  | "ArmorProtection"
  | "WeaponStatistics"
  | "Spells"
  | "ValueAndBurden"
  | "SpecialProperties";

/** Mirrors `ACE.Cloud.Domain.CloudAppraisalTextStyle`: ACE's own appraisal color/emphasis semantics. */
export type CloudAppraisalTextStyle = "Title" | "Body" | "Muted" | "Positive" | "Negative";

export interface CloudAppraisalLine {
  readonly text: string;
  readonly style: CloudAppraisalTextStyle;
}

export interface CloudAppraisalSection {
  readonly kind: CloudAppraisalSectionKind;
  readonly lines: readonly CloudAppraisalLine[];
}

/** The versioned Full Cloud Appraisal presentation contract (UI-004, `CloudAppraisalPanel`). */
export interface CloudAppraisalPanel {
  readonly contractVersion: number;
  readonly itemName: string;
  readonly sections: readonly CloudAppraisalSection[];
}

export type CloudAppraisalErrorKind = "unauthenticated" | "linked_account_restricted" | "invalid_item_id" | "not_found";

export type CloudIconErrorKind = "unauthenticated" | "invalid_cache_key" | "icon_unavailable";

/**
 * Typed mirrors of `ACE.Cloud.Backend.AccountIdentityEndpoints` and
 * `ACE.Cloud.Backend.CloudWithdrawalEndpoints` (issue #33's account identity/linking and Withdrawal
 * Token web surface, AUTH-003..009, WDR-001..008).
 */
export interface CloudAccountLinkedAccountSummary {
  readonly accountId: number;
  readonly linkedAtUtc: string;
}

export interface CloudDisplayCharacterSummary {
  readonly characterId: number;
  readonly characterName: string;
}

export interface CloudAccountIdentityResponse {
  readonly accountId: number;
  readonly accountKind: "Main" | "Linked";
  readonly mainAccountId: number;
  readonly linkedAccounts: readonly CloudAccountLinkedAccountSummary[];
  readonly displayCharacter: CloudDisplayCharacterSummary | null;
}

export type CloudAccountIdentityErrorKind = "unauthenticated";

/** Mirrors `ACE.Cloud.Domain.CloudAccountLinkRejectionCode` exactly, member for member. */
export type CloudAccountLinkRejectionCode =
  | "None"
  | "MutationsFrozen"
  | "SameAccount"
  | "MainAccountIsLinkedElsewhere"
  | "SourceAlreadyLinked"
  | "SourceHasLinkedAccounts"
  | "SourceHasPendingObligations"
  | "WouldCreateAuctionConflict"
  | "LinkNotActive";

export interface CloudAccountLinkOutcomeResponse {
  readonly approved: boolean;
  readonly rejectionCode: CloudAccountLinkRejectionCode;
}

export type CloudAccountLinkErrorKind =
  | "unauthenticated"
  | "linked_account_restricted"
  | "origin_denied"
  | "csrf_denied"
  | "invalid_request"
  | "invalid_source_credentials"
  | "authentication_unavailable"
  | "invalid_grant";

export type CloudAccountUnlinkErrorKind =
  | "unauthenticated"
  | "linked_account_restricted"
  | "origin_denied"
  | "csrf_denied"
  | "invalid_request";

export interface CloudWithdrawalNamedLandblockSummary {
  readonly id: string;
  /** `0x`-prefixed 16-bit landblock, matching CONTEXT.md's `0x123E` format -- never a coordinate radius. */
  readonly landblock: string;
  readonly name: string;
}

export interface CloudWithdrawalLocationsResponse {
  readonly withdrawAnywhereEnabled: boolean;
  readonly namedLandblocks: readonly CloudWithdrawalNamedLandblockSummary[];
}

export type CloudWithdrawalTargetKind = "Item" | "StackLot";

export interface CloudWithdrawalTargetRequest {
  readonly kind: CloudWithdrawalTargetKind;
  readonly itemBiotaId?: number;
  readonly stackLotId?: string;
}

export interface CloudWithdrawalReservationTargetSummary {
  readonly kind: CloudWithdrawalTargetKind;
  readonly itemBiotaId: number | null;
  readonly stackLotId: string | null;
  readonly quantity: number | null;
}

/** `active: false` means no Withdrawal Reservation is currently open; every other field is then absent. */
export type CloudCurrentWithdrawalResponse =
  | { readonly active: false }
  | {
      readonly active: true;
      readonly reservationId: string;
      readonly version: number;
      readonly expiresAtUtc: string;
      readonly targets: readonly CloudWithdrawalReservationTargetSummary[];
    };

/**
 * The one and only response that ever carries the raw Withdrawal Token secret (security baseline:
 * never in a URL, log, or subsequent status read) -- WDR-001's single-reveal rule.
 */
export interface CloudCreateWithdrawalResponse {
  readonly secret: string;
  readonly reservationId: string;
  readonly version: number;
  readonly expiresAtUtc: string;
}

export type CloudCreateWithdrawalErrorKind =
  | "unauthenticated"
  | "linked_account_restricted"
  | "origin_denied"
  | "csrf_denied"
  | "invalid_request"
  | "world_boundary_unavailable"
  | "conflict"
  | "unavailable";

export interface CloudCancelWithdrawalResponse {
  readonly reservationId: string;
  readonly version: number;
  readonly status: "Active" | "Released";
}

export type CloudCancelWithdrawalErrorKind =
  | "unauthenticated"
  | "linked_account_restricted"
  | "origin_denied"
  | "csrf_denied"
  | "invalid_request"
  | "conflict"
  | "unavailable";

export interface CloudSplitStackLotResponse {
  readonly remainingLot: { readonly id: string; readonly quantity: number; readonly version: number };
  readonly newLot: { readonly id: string; readonly quantity: number; readonly version: number };
}
