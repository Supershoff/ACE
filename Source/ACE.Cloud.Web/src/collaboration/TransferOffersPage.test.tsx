import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { TransferOffersPage } from "./TransferOffersPage";
import type { TransferOfferApi, CloudTransferOfferListResponse, CloudTransferOfferSummary } from "../api/transferOfferApi";
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

const receivedOffer: CloudTransferOfferSummary = {
  id: "11111111-1111-1111-1111-111111111111",
  senderOwnerId: "sender-1",
  recipientOwnerId: "recipient-1",
  status: "Pending",
  version: 1,
  createdAtUtc: "2026-01-01T00:00:00Z",
  expiresAtUtc: "2026-01-08T00:00:00Z",
  targets: [{ kind: "Item", itemBiotaId: 555, stackLotId: null }],
};

function fakeApi(overrides: Partial<TransferOfferApi> = {}): TransferOfferApi {
  const listResponse: CloudTransferOfferListResponse = { sent: [], received: [receivedOffer] };
  return {
    list: vi.fn(async () => ({ ok: true, status: 200, data: listResponse }) as HttpResult<CloudTransferOfferListResponse>),
    create: vi.fn(async () => ({ ok: true, status: 200, data: receivedOffer }) as HttpResult<CloudTransferOfferSummary>),
    accept: vi.fn(async () => ({ ok: true, status: 200, data: receivedOffer }) as HttpResult<CloudTransferOfferSummary>),
    decline: vi.fn(async () => ({ ok: true, status: 200, data: receivedOffer }) as HttpResult<CloudTransferOfferSummary>),
    cancel: vi.fn(async () => ({ ok: true, status: 200, data: receivedOffer }) as HttpResult<CloudTransferOfferSummary>),
    ...overrides,
  };
}

function renderPage(transferOfferApi: TransferOfferApi) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <MemoryRouter>
        <TransferOffersPage transferOfferApi={transferOfferApi} />
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

describe("TransferOffersPage", () => {
  it("lists received offers with accept/decline actions", async () => {
    renderPage(fakeApi());

    expect(await screen.findByText(/Pending/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Decline" })).toBeInTheDocument();
  });

  it("accepting calls the API with the offer's id and version", async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    renderPage(api);

    await user.click(await screen.findByRole("button", { name: "Accept" }));

    expect(api.accept).toHaveBeenCalledWith(receivedOffer.id, receivedOffer.version);
  });

  it("points the user at the Inventory's contextual action instead of offering a raw item-ID form", async () => {
    renderPage(fakeApi());
    await screen.findByText(/Pending/);

    // PR #157's blocking human-acceptance feedback: users must never type or know an internal item
    // ID. Offer creation is a contextual action on the selected Inventory item, not a form here.
    expect(screen.queryByLabelText(/item id/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send offer" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Inventory" })).toHaveAttribute("href", "/inventory");
  });

  it("has no detectable axe violations", async () => {
    const { container } = renderPage(fakeApi());
    await screen.findByText(/Pending/);

    expect(await axe(container)).toHaveNoViolations();
  });
});
