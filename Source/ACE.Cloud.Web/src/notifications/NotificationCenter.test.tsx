import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { NotificationCenter } from "./NotificationCenter";
import type { HttpResult } from "../api/httpClient";
import type { NotificationApi } from "../api/notificationApi";
import type { CloudNotification, CloudNotificationListResponse, CloudNotificationUnreadCountResponse } from "../api/types";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

function baseSession(overrides: Partial<SessionContextValue> = {}): SessionContextValue {
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
    ...overrides,
  };
}

const unreadNotification: CloudNotification = {
  id: "n1",
  kind: "OwnershipReceived",
  destination: "/dashboard",
  count: 2,
  isRead: false,
  firstOccurredAtUtc: "2026-01-01T00:00:00Z",
  lastOccurredAtUtc: "2026-01-02T00:00:00Z",
};

function fakeNotificationApi(overrides: Partial<NotificationApi> = {}): NotificationApi {
  return {
    list: vi.fn(
      async () => ({ ok: true, status: 200, data: { notifications: [unreadNotification] } }) as HttpResult<CloudNotificationListResponse>,
    ),
    fetchUnreadCount: vi.fn(async () => ({ ok: true, status: 200, data: { unreadCount: 1 } }) as HttpResult<CloudNotificationUnreadCountResponse>),
    markRead: vi.fn(async () => ({ ok: true, status: 200 }) as HttpResult<void>),
    ...overrides,
  };
}

function renderCenter(notificationApi: NotificationApi, session: SessionContextValue = baseSession()) {
  return render(
    <MemoryRouter>
      <SessionContext.Provider value={session}>
        <NotificationCenter notificationApi={notificationApi} />
      </SessionContext.Provider>
    </MemoryRouter>,
  );
}

describe("NotificationCenter", () => {
  it("renders nothing when the viewer is not authenticated", () => {
    const { container } = renderCenter(fakeNotificationApi(), baseSession({ status: "unauthenticated" }));
    expect(container).toBeEmptyDOMElement();
  });

  it("shows the unread badge count once loaded", async () => {
    renderCenter(fakeNotificationApi());

    expect(await screen.findByLabelText("1 unread notification")).toBeInTheDocument();
  });

  it("hides the badge when there are no unread notifications", async () => {
    const api = fakeNotificationApi({
      fetchUnreadCount: vi.fn(async () => ({ ok: true, status: 200, data: { unreadCount: 0 } }) as HttpResult<CloudNotificationUnreadCountResponse>),
    });
    renderCenter(api);

    await waitFor(() => expect(api.fetchUnreadCount).toHaveBeenCalled());
    expect(screen.queryByLabelText(/unread notification/)).not.toBeInTheDocument();
  });

  it("expands to show the coalesced notification with its occurrence count", async () => {
    const user = userEvent.setup();
    renderCenter(fakeNotificationApi());

    await user.click(await screen.findByRole("button", { name: /notifications/i }));

    expect(screen.getByRole("link", { name: /you received an item \(2\)/i })).toBeInTheDocument();
  });

  it("marks a notification read when its destination is visited", async () => {
    const user = userEvent.setup();
    const api = fakeNotificationApi();
    renderCenter(api);

    await user.click(await screen.findByRole("button", { name: /notifications/i }));
    await user.click(screen.getByRole("link", { name: /you received an item/i }));

    expect(api.markRead).toHaveBeenCalledWith("n1");
  });

  it("never re-marks an already-read notification when visited again", async () => {
    const user = userEvent.setup();
    const api = fakeNotificationApi({
      list: vi.fn(
        async () =>
          ({ ok: true, status: 200, data: { notifications: [{ ...unreadNotification, isRead: true }] } }) as HttpResult<CloudNotificationListResponse>,
      ),
    });
    renderCenter(api);

    await user.click(await screen.findByRole("button", { name: /notifications/i }));
    await user.click(screen.getByRole("link", { name: /you received an item/i }));

    expect(api.markRead).not.toHaveBeenCalled();
  });

  it("has no detectable axe violations when expanded", async () => {
    const user = userEvent.setup();
    const { container } = renderCenter(fakeNotificationApi());

    await user.click(await screen.findByRole("button", { name: /notifications/i }));

    expect(await axe(container)).toHaveNoViolations();
  });
});
