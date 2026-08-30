import type { HttpClient, HttpResult } from "./httpClient";
import type {
  CloudAppraisalPanel,
  CloudInventoryCategory,
  CloudInventoryQueryResponse,
  CloudInventorySortDirection,
  CloudInventorySortKey,
} from "./types";

export interface InventoryQueryParams {
  readonly category?: CloudInventoryCategory;
  readonly page?: number;
  readonly sortKey?: CloudInventorySortKey;
  readonly sortDirection?: CloudInventorySortDirection;
}

export interface InventoryApi {
  queryPages(params?: InventoryQueryParams): Promise<HttpResult<CloudInventoryQueryResponse>>;
  fetchAppraisal(itemId: number): Promise<HttpResult<CloudAppraisalPanel>>;
  /** Builds the same-origin icon URL a caller passes straight to an `<img>` element's `src`. */
  buildIconUrl(iconCacheKeyHex: string): string;
}

function buildQueryPagesPath(params: InventoryQueryParams): string {
  const search = new URLSearchParams();
  if (params.category) {
    search.set("category", params.category);
  }
  if (params.page !== undefined) {
    search.set("page", String(params.page));
  }
  if (params.sortKey) {
    search.set("sortKey", params.sortKey);
  }
  if (params.sortDirection) {
    search.set("sortDirection", params.sortDirection);
  }

  const query = search.toString();
  return query.length > 0 ? `/inventory/pages?${query}` : "/inventory/pages";
}

export function createInventoryApi(httpClient: HttpClient): InventoryApi {
  return {
    queryPages: (params = {}) => httpClient.get<CloudInventoryQueryResponse>(buildQueryPagesPath(params)),
    fetchAppraisal: (itemId) => httpClient.get<CloudAppraisalPanel>(`/inventory/items/${itemId}/appraisal`),
    buildIconUrl: (iconCacheKeyHex) => `/inventory/icons/${iconCacheKeyHex}`,
  };
}
