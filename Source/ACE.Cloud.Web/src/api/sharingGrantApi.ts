import type { HttpClient, HttpResult } from "./httpClient";

/** Mirrors `ACE.Cloud.Domain.CloudSharingGrantLevel` exactly, member for member. */
export type CloudSharingGrantLevel = "None" | "ViewOnly" | "ViewAndWithdraw";

export interface CloudSharingGrantSummary {
  readonly id: string;
  readonly ownerId: string;
  readonly granteeId: string;
  readonly level: CloudSharingGrantLevel;
  readonly version: number;
  readonly updatedAtUtc: string;
}

export interface CloudSharingGrantListResponse {
  readonly given: readonly CloudSharingGrantSummary[];
  readonly received: readonly CloudSharingGrantSummary[];
}

export interface SharingGrantApi {
  list(): Promise<HttpResult<CloudSharingGrantListResponse>>;
  set(granteeCharacterName: string, level: CloudSharingGrantLevel): Promise<HttpResult<CloudSharingGrantSummary>>;
}

export function createSharingGrantApi(httpClient: HttpClient): SharingGrantApi {
  return {
    list: () => httpClient.get<CloudSharingGrantListResponse>("/sharing-grants"),
    set: (granteeCharacterName, level) => httpClient.post<CloudSharingGrantSummary>("/sharing-grants", { granteeCharacterName, level }),
  };
}
