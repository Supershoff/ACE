import type { HttpClient, HttpResult } from "./httpClient";
import type {
  CloudCancelWithdrawalResponse,
  CloudCreateWithdrawalResponse,
  CloudCurrentWithdrawalResponse,
  CloudSplitStackLotResponse,
  CloudWithdrawalLocationsResponse,
  CloudWithdrawalTargetRequest,
} from "./types";

export interface WithdrawalApi {
  fetchLocations(): Promise<HttpResult<CloudWithdrawalLocationsResponse>>;
  fetchCurrent(): Promise<HttpResult<CloudCurrentWithdrawalResponse>>;
  create(targets: readonly CloudWithdrawalTargetRequest[]): Promise<HttpResult<CloudCreateWithdrawalResponse>>;
  cancel(reservationId: string, expectedVersion: number): Promise<HttpResult<CloudCancelWithdrawalResponse>>;
  splitStackLot(lotId: string, expectedVersion: number, quantity: number): Promise<HttpResult<CloudSplitStackLotResponse>>;
}

export function createWithdrawalApi(httpClient: HttpClient): WithdrawalApi {
  return {
    fetchLocations: () => httpClient.get<CloudWithdrawalLocationsResponse>("/withdrawal-locations"),
    fetchCurrent: () => httpClient.get<CloudCurrentWithdrawalResponse>("/withdrawals/current"),
    create: (targets) => httpClient.post<CloudCreateWithdrawalResponse>("/withdrawals", { targets }),
    cancel: (reservationId, expectedVersion) =>
      httpClient.post<CloudCancelWithdrawalResponse>(`/withdrawals/${reservationId}/cancel`, { expectedVersion }),
    splitStackLot: (lotId, expectedVersion, quantity) =>
      httpClient.post<CloudSplitStackLotResponse>(`/inventory/stack-lots/${lotId}/split`, { expectedVersion, quantity }),
  };
}
