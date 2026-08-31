import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";
import { RequireAdmin } from "./RequireAdmin";
import { RequireAuth } from "./RequireAuth";
import { RequireMainAccount } from "./RequireMainAccount";
import { RequireWritableService } from "./RequireWritableService";

function baseSessionValue(overrides: Partial<SessionContextValue> = {}): SessionContextValue {
  return {
    status: "unknown",
    csrfToken: null,
    accountKind: "Unknown",
    accountName: null,
    serviceAvailability: "unknown",
    liveStream: { status: "idle", stale: false },
    login: vi.fn(),
    logout: vi.fn(),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
    subscribeLiveStream: vi.fn(() => vi.fn()),
    ...overrides,
  };
}

function renderWithSession(value: SessionContextValue, ui: React.ReactElement, initialRoute = "/protected") {
  return render(
    <SessionContext.Provider value={value}>
      <MemoryRouter initialEntries={[initialRoute]}>
        <Routes>
          <Route path="/login" element={<div>Login page</div>} />
          <Route path="/protected" element={ui} />
        </Routes>
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

describe("RequireAuth", () => {
  it("renders children when authenticated", () => {
    renderWithSession(
      baseSessionValue({ status: "authenticated" }),
      <RequireAuth>
        <div>secret dashboard</div>
      </RequireAuth>,
    );

    expect(screen.getByText("secret dashboard")).toBeInTheDocument();
  });

  it("redirects to /login when unauthenticated", () => {
    renderWithSession(
      baseSessionValue({ status: "unauthenticated" }),
      <RequireAuth>
        <div>secret dashboard</div>
      </RequireAuth>,
    );

    expect(screen.getByText("Login page")).toBeInTheDocument();
    expect(screen.queryByText("secret dashboard")).not.toBeInTheDocument();
  });

  it("fails closed (redirects) when auth status is unknown", () => {
    renderWithSession(
      baseSessionValue({ status: "unknown" }),
      <RequireAuth>
        <div>secret dashboard</div>
      </RequireAuth>,
    );

    expect(screen.getByText("Login page")).toBeInTheDocument();
  });
});

describe("RequireMainAccount", () => {
  it("renders children for a Main account", () => {
    renderWithSession(
      baseSessionValue({ status: "authenticated", accountKind: "Main" }),
      <RequireMainAccount>
        <div>main-only assets</div>
      </RequireMainAccount>,
    );

    expect(screen.getByText("main-only assets")).toBeInTheDocument();
  });

  it("blocks a Linked account from a Main-only route", () => {
    renderWithSession(
      baseSessionValue({ status: "authenticated", accountKind: "Linked" }),
      <RequireMainAccount>
        <div>main-only assets</div>
      </RequireMainAccount>,
    );

    expect(screen.queryByText("main-only assets")).not.toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(/linked account/i);
  });

  it("fails closed when account kind is not yet known", () => {
    renderWithSession(
      baseSessionValue({ status: "authenticated", accountKind: "Unknown" }),
      <RequireMainAccount>
        <div>main-only assets</div>
      </RequireMainAccount>,
    );

    expect(screen.queryByText("main-only assets")).not.toBeInTheDocument();
  });
});

describe("RequireAdmin", () => {
  it("shows a loading state while revalidating access", () => {
    const checkAdminAccess = vi.fn(() => new Promise<never>(() => {}));
    renderWithSession(
      baseSessionValue({ status: "authenticated", checkAdminAccess }),
      <RequireAdmin>
        <div>admin console</div>
      </RequireAdmin>,
    );

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("renders children once the server confirms access level 5", async () => {
    const checkAdminAccess = vi.fn(async () => ({ checked: true, isAdmin: true, accessLevel: 5 }));
    renderWithSession(
      baseSessionValue({ status: "authenticated", checkAdminAccess }),
      <RequireAdmin>
        <div>admin console</div>
      </RequireAdmin>,
    );

    await waitFor(() => expect(screen.getByText("admin console")).toBeInTheDocument());
  });

  it("denies access when the server reports a lower access level, never trusting client claims", async () => {
    const checkAdminAccess = vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: 1 }));
    renderWithSession(
      baseSessionValue({ status: "authenticated", checkAdminAccess }),
      <RequireAdmin>
        <div>admin console</div>
      </RequireAdmin>,
    );

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.queryByText("admin console")).not.toBeInTheDocument();
  });
});

describe("RequireWritableService", () => {
  it("renders children when the service is Operational", () => {
    renderWithSession(
      baseSessionValue({ serviceAvailability: "Operational" }),
      <RequireWritableService>
        <div>deposit form</div>
      </RequireWritableService>,
    );

    expect(screen.getByText("deposit form")).toBeInTheDocument();
  });

  it("blocks mutation UI and explains why when the database is ReadOnly", () => {
    renderWithSession(
      baseSessionValue({ serviceAvailability: "ReadOnly" }),
      <RequireWritableService>
        <div>deposit form</div>
      </RequireWritableService>,
    );

    expect(screen.queryByText("deposit form")).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/read-only/i);
  });

  it("blocks mutation UI when versions are incompatible", () => {
    renderWithSession(
      baseSessionValue({ serviceAvailability: "VersionIncompatible" }),
      <RequireWritableService>
        <div>deposit form</div>
      </RequireWritableService>,
    );

    expect(screen.queryByText("deposit form")).not.toBeInTheDocument();
  });
});
