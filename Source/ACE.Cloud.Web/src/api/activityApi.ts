import type { HttpClient, HttpResult } from "./httpClient";
import type { CloudActivityLedgerPageResponse } from "./types";

export interface ActivityQueryParams {
  readonly page?: number;
  readonly pageSize?: number;
  /** Adds the viewer's current Allegiance Vault owner ID(s) to the query scope (VAULT-001). */
  readonly vault?: boolean;
}

export interface ActivityApi {
  queryLedger(params?: ActivityQueryParams): Promise<HttpResult<CloudActivityLedgerPageResponse>>;
}

function buildQueryLedgerPath(params: ActivityQueryParams): string {
  const search = new URLSearchParams();
  if (params.page !== undefined) {
    search.set("page", String(params.page));
  }
  if (params.pageSize !== undefined) {
    search.set("pageSize", String(params.pageSize));
  }
  if (params.vault) {
    search.set("vault", "true");
  }

  const query = search.toString();
  return query.length > 0 ? `/activity?${query}` : "/activity";
}

export function createActivityApi(httpClient: HttpClient): ActivityApi {
  return {
    queryLedger: (params = {}) => httpClient.get<CloudActivityLedgerPageResponse>(buildQueryLedgerPath(params)),
  };
}
