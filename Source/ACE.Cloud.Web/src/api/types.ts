/**
 * Typed mirrors of the Cloud backend's current browser-facing wire contracts
 * (`Source/ACE.Cloud.Backend/AuthSessionEndpoints.cs`, `LoginRequest.cs`,
 * `Source/ACE.Cloud.Hosting/CloudDiagnosticsEndpoints.cs`,
 * `Source/ACE.Cloud.Hosting/CloudServiceAvailabilityMode.cs`).
 *
 * Only endpoints that exist on the server today are represented here. Inventory, marketplace,
 * account-linking, and quota HTTP contracts land with their own issues (#30, #33, #40+) and must
 * not be invented ahead of the server that will serve them.
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
