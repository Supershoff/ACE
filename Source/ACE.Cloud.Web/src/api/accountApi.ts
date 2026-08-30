import { IdempotencyKeyHeaderName } from "./constants";
import type { HttpClient, HttpResult } from "./httpClient";
import type { AccountLinkResponse, AccountOverviewResponse, AccountUnlinkResponse } from "./types";

export interface AccountApi {
  fetchOverview(): Promise<HttpResult<AccountOverviewResponse>>;
  link(sourceAccountName: string, sourcePassword: string): Promise<HttpResult<AccountLinkResponse>>;
  unlink(linkedAccountId: number): Promise<HttpResult<AccountUnlinkResponse>>;
}

function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function createAccountApi(httpClient: HttpClient): AccountApi {
  return {
    fetchOverview: () => httpClient.get<AccountOverviewResponse>("/account/overview"),
    link: (sourceAccountName, sourcePassword) =>
      httpClient.post<AccountLinkResponse>(
        "/account/link",
        { sourceAccountName, sourcePassword },
        { [IdempotencyKeyHeaderName]: newIdempotencyKey() },
      ),
    unlink: (linkedAccountId) =>
      httpClient.post<AccountUnlinkResponse>(
        "/account/unlink",
        { linkedAccountId },
        { [IdempotencyKeyHeaderName]: newIdempotencyKey() },
      ),
  };
}
