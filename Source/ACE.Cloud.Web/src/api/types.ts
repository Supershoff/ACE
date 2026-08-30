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
