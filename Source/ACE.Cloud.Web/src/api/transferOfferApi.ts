import type { HttpClient, HttpResult } from "./httpClient";

/** Mirrors `ACE.Cloud.Persistence.CloudTransferOfferStatus` exactly, member for member. */
export type CloudTransferOfferStatus = "Pending" | "Accepted" | "Declined" | "Cancelled" | "Expired";

export interface CloudTransferOfferTarget {
  readonly kind: "Item" | "StackLot";
  readonly itemBiotaId: number | null;
  readonly stackLotId: string | null;
}

export interface CloudTransferOfferSummary {
  readonly id: string;
  readonly senderOwnerId: string;
  readonly recipientOwnerId: string;
  readonly status: CloudTransferOfferStatus;
  readonly version: number;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly targets: readonly CloudTransferOfferTarget[];
}

export interface CloudTransferOfferListResponse {
  readonly sent: readonly CloudTransferOfferSummary[];
  readonly received: readonly CloudTransferOfferSummary[];
}

export interface CloudTransferOfferTargetRequest {
  readonly kind: "Item" | "StackLot";
  readonly itemBiotaId?: number;
  readonly stackLotId?: string;
}

export interface TransferOfferApi {
  list(): Promise<HttpResult<CloudTransferOfferListResponse>>;
  create(recipientCharacterName: string, targets: readonly CloudTransferOfferTargetRequest[]): Promise<HttpResult<CloudTransferOfferSummary>>;
  accept(offerId: string, expectedVersion: number): Promise<HttpResult<CloudTransferOfferSummary>>;
  decline(offerId: string, expectedVersion: number): Promise<HttpResult<CloudTransferOfferSummary>>;
  cancel(offerId: string, expectedVersion: number): Promise<HttpResult<CloudTransferOfferSummary>>;
}

export function createTransferOfferApi(httpClient: HttpClient): TransferOfferApi {
  return {
    list: () => httpClient.get<CloudTransferOfferListResponse>("/transfer-offers"),
    create: (recipientCharacterName, targets) =>
      httpClient.post<CloudTransferOfferSummary>("/transfer-offers", { recipientCharacterName, targets }),
    accept: (offerId, expectedVersion) => httpClient.post<CloudTransferOfferSummary>(`/transfer-offers/${offerId}/accept`, { expectedVersion }),
    decline: (offerId, expectedVersion) => httpClient.post<CloudTransferOfferSummary>(`/transfer-offers/${offerId}/decline`, { expectedVersion }),
    cancel: (offerId, expectedVersion) => httpClient.post<CloudTransferOfferSummary>(`/transfer-offers/${offerId}/cancel`, { expectedVersion }),
  };
}
