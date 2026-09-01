import type { HttpClient, HttpResult } from "./httpClient";

export interface CloudActingCharacter {
  readonly characterId: number;
  readonly characterName: string;
  readonly monarchId: number | null;
  readonly hasAllegiance: boolean;
}

export interface CloudActingCharacterListResponse {
  readonly characters: readonly CloudActingCharacter[];
}

export interface CloudAllegianceVaultItem {
  readonly itemId: number;
  readonly stackLotId: string | null;
  readonly name: string;
  readonly quantity: number;
  readonly value: number | null;
  readonly version: number;
}

export interface CloudAllegianceVaultResponse {
  readonly characterId: number;
  readonly monarchId: number;
  readonly page: {
    readonly pageNumber: number;
    readonly totalPages: number;
    readonly items: readonly CloudAllegianceVaultItem[];
  };
}

export interface CloudAllegianceVaultTargetRequest {
  readonly actingCharacterId: number;
  readonly kind: "Item" | "StackLot";
  readonly itemBiotaId?: number;
  readonly stackLotId?: string;
}

export interface CloudAllegianceVaultTransferResponse {
  readonly itemBiotaId: number;
  readonly personalOwnerId: string;
  readonly vaultOwnerId: string;
}

export interface AllegianceVaultApi {
  listActingCharacters(): Promise<HttpResult<CloudActingCharacterListResponse>>;
  getVault(characterId: number, page?: number): Promise<HttpResult<CloudAllegianceVaultResponse>>;
  contribute(request: CloudAllegianceVaultTargetRequest): Promise<HttpResult<CloudAllegianceVaultTransferResponse>>;
  take(request: CloudAllegianceVaultTargetRequest): Promise<HttpResult<CloudAllegianceVaultTransferResponse>>;
}

export function createAllegianceVaultApi(httpClient: HttpClient): AllegianceVaultApi {
  return {
    listActingCharacters: () => httpClient.get<CloudActingCharacterListResponse>("/allegiance-vault/acting-characters"),
    getVault: (characterId, page) =>
      httpClient.get<CloudAllegianceVaultResponse>(
        page ? `/allegiance-vault?characterId=${characterId}&page=${page}` : `/allegiance-vault?characterId=${characterId}`,
      ),
    contribute: (request) => httpClient.post<CloudAllegianceVaultTransferResponse>("/allegiance-vault/contribute", request),
    take: (request) => httpClient.post<CloudAllegianceVaultTransferResponse>("/allegiance-vault/take", request),
  };
}
