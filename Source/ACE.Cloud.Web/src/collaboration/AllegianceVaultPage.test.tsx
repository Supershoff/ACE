import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { AllegianceVaultPage } from "./AllegianceVaultPage";
import type {
  AllegianceVaultApi,
  CloudActingCharacterListResponse,
  CloudAllegianceVaultResponse,
  CloudAllegianceVaultTransferResponse,
} from "../api/allegianceVaultApi";
import type { HttpResult } from "../api/httpClient";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

function baseSession(): SessionContextValue {
  return {
    status: "authenticated",
    csrfToken: "csrf",
    accountKind: "Main",
    accountName: "MainPlayer",
    serviceAvailability: "Operational",
    liveStream: { status: "idle", stale: false },
    login: vi.fn(),
    logout: vi.fn(),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
    subscribeLiveStream: vi.fn(() => vi.fn()),
  };
}

const charactersResponse: CloudActingCharacterListResponse = {
  characters: [{ characterId: 7001, characterName: "Vassal", monarchId: 9001, hasAllegiance: true }],
};

const vaultResponse: CloudAllegianceVaultResponse = {
  characterId: 7001,
  monarchId: 9001,
  page: { pageNumber: 1, totalPages: 1, items: [{ itemId: 555, stackLotId: null, name: "Vault Sword", quantity: 1, value: 10, version: 1 }] },
};

const transferResponse: CloudAllegianceVaultTransferResponse = { itemBiotaId: 555, personalOwnerId: "owner-1", vaultOwnerId: "vault-1" };

function fakeApi(overrides: Partial<AllegianceVaultApi> = {}): AllegianceVaultApi {
  return {
    listActingCharacters: vi.fn(async () => ({ ok: true, status: 200, data: charactersResponse }) as HttpResult<CloudActingCharacterListResponse>),
    getVault: vi.fn(async () => ({ ok: true, status: 200, data: vaultResponse }) as HttpResult<CloudAllegianceVaultResponse>),
    contribute: vi.fn(async () => ({ ok: true, status: 200, data: transferResponse }) as HttpResult<CloudAllegianceVaultTransferResponse>),
    take: vi.fn(async () => ({ ok: true, status: 200, data: transferResponse }) as HttpResult<CloudAllegianceVaultTransferResponse>),
    ...overrides,
  };
}

function renderPage(allegianceVaultApi: AllegianceVaultApi) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <AllegianceVaultPage allegianceVaultApi={allegianceVaultApi} />
    </SessionContext.Provider>,
  );
}

describe("AllegianceVaultPage", () => {
  it("selects the Acting Character and lists that vault's items", async () => {
    renderPage(fakeApi());

    expect(await screen.findByText(/Vault Sword/)).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Vassal" })).toBeInTheDocument();
  });

  it("taking an item calls the API with the selected Acting Character", async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    renderPage(api);
    await screen.findByText(/Vault Sword/);

    await user.type(screen.getByLabelText("Take item ID"), "555");
    await user.click(screen.getByRole("button", { name: "Take" }));

    expect(api.take).toHaveBeenCalledWith({ actingCharacterId: 7001, kind: "Item", itemBiotaId: 555 });
  });

  it("contributing an item calls the API with the selected Acting Character", async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    renderPage(api);
    await screen.findByText(/Vault Sword/);

    await user.type(screen.getByLabelText("Contribute item ID"), "556");
    await user.click(screen.getByRole("button", { name: "Contribute" }));

    expect(api.contribute).toHaveBeenCalledWith({ actingCharacterId: 7001, kind: "Item", itemBiotaId: 556 });
  });

  it("has no detectable axe violations", async () => {
    const { container } = renderPage(fakeApi());
    await screen.findByText(/Vault Sword/);

    expect(await axe(container)).toHaveNoViolations();
  });
});
