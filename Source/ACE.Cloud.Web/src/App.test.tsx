import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { SessionContext, type SessionContextValue } from "./session/SessionContext";

function baseSessionValue(overrides: Partial<SessionContextValue> = {}): SessionContextValue {
  return {
    status: "unknown",
    csrfToken: null,
    accountKind: "Unknown",
    accountName: null,
    serviceAvailability: "Operational",
    login: vi.fn(async () => ({ ok: true })),
    logout: vi.fn(async () => {}),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
    ...overrides,
  };
}

function renderApp(session: SessionContextValue, initialRoute = "/") {
  return render(
    <SessionContext.Provider value={session}>
      <MemoryRouter initialEntries={[initialRoute]}>
        <App />
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

describe("App routing", () => {
  it("renders the public marketplace at / without requiring authentication", () => {
    renderApp(baseSessionValue({ status: "unauthenticated" }));
    expect(screen.getByRole("heading", { name: /marketplace/i })).toBeInTheDocument();
  });

  it("redirects an unauthenticated visitor away from the authenticated dashboard", () => {
    renderApp(baseSessionValue({ status: "unauthenticated" }), "/dashboard");
    expect(screen.getByRole("heading", { name: /log in/i })).toBeInTheDocument();
  });

  it("renders the dashboard for an authenticated visitor", () => {
    renderApp(baseSessionValue({ status: "authenticated" }), "/dashboard");
    expect(screen.getByRole("heading", { name: /dashboard/i })).toBeInTheDocument();
  });

  it("blocks a Linked account from the Main-only account overview route", () => {
    renderApp(baseSessionValue({ status: "authenticated", accountKind: "Linked" }), "/account");
    expect(screen.getByRole("alert")).toHaveTextContent(/linked account/i);
  });

  it("shows the offline/read-only banner across the shell when the database is unavailable", () => {
    renderApp(baseSessionValue({ status: "unauthenticated", serviceAvailability: "ReadOnly" }));
    expect(screen.getByRole("status")).toHaveTextContent(/read-only/i);
  });
});
