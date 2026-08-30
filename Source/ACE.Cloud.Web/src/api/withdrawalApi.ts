import { IdempotencyKeyHeaderName } from "./constants";
import type { HttpClient, HttpResult } from "./httpClient";
import type {
  CancelWithdrawalReservationResponse,
  OpenWithdrawalReservationResponse,
  WithdrawalReservationTargetRequest,
} from "./types";

export interface WithdrawalApi {
  openReservation(targets: readonly WithdrawalReservationTargetRequest[]): Promise<HttpResult<OpenWithdrawalReservationResponse>>;
  cancelReservation(reservationId: string, expectedVersion: number): Promise<HttpResult<CancelWithdrawalReservationResponse>>;
}

function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function createWithdrawalApi(httpClient: HttpClient): WithdrawalApi {
  return {
    openReservation: (targets) =>
      httpClient.post<OpenWithdrawalReservationResponse>(
        "/withdrawal/reservations",
        { targets },
        { [IdempotencyKeyHeaderName]: newIdempotencyKey() },
      ),
    cancelReservation: (reservationId, expectedVersion) =>
      httpClient.post<CancelWithdrawalReservationResponse>(`/withdrawal/reservations/${reservationId}/cancel`, {
        expectedVersion,
      }),
  };
}
