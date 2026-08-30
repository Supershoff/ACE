import type { HttpClient, HttpResult } from "./httpClient";
import type { CloudAccountIdentityResponse, CloudAccountLinkOutcomeResponse } from "./types";

export interface AccountApi {
  fetchIdentity(): Promise<HttpResult<CloudAccountIdentityResponse>>;
  /** AUTH-005: the *source* account's own password re-entry, verified by ACE's private Auth Bridge, never Cloud Mule itself. */
  link(sourceAccountName: string, sourcePassword: string): Promise<HttpResult<CloudAccountLinkOutcomeResponse>>;
  unlink(linkedAccountId: number): Promise<HttpResult<CloudAccountLinkOutcomeResponse>>;
}

export function createAccountApi(httpClient: HttpClient): AccountApi {
  return {
    fetchIdentity: () => httpClient.get<CloudAccountIdentityResponse>("/account/identity"),
    link: (sourceAccountName, sourcePassword) =>
      httpClient.post<CloudAccountLinkOutcomeResponse>("/account/link", { sourceAccountName, sourcePassword }),
    unlink: (linkedAccountId) => httpClient.post<CloudAccountLinkOutcomeResponse>("/account/unlink", { linkedAccountId }),
  };
}
