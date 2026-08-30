import { act, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession } from "./SessionContext";
import type { AccountApi } from "../api/accountApi";
import type { AuthApi } from "../api/authApi";
import type { HttpResult } from "../api/httpClient";

type FakeAuthApiOverrides = Partial<{ [K in keyof AuthApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

function fakeAuthApi(overrides: FakeAuthApiOverrides = {}): AuthApi {
  return {
    login: vi.fn(async () => ({ ok: true, status: 200, data: { csrfToken: "csrf" } }) as HttpResult<unknown>),
    logout: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    fetchAdminWhoAmI: vi.fn(
      async () => ({ ok: false, status: 401, error: { error: "unauthenticated" } }) as HttpResult<unknown>,
    ),
    fetchHealthReady: vi.fn(
      async () => ({ ok: true, status: 200, data: { mode: "Operational", results: [] } }) as HttpResult<unknown>,
    ),
    fetchVersion: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as AuthApi;
}

type FakeAccountApiOverrides = Partial<{ [K in keyof AccountApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

function fakeAccountApi(overrides: FakeAccountApiOverrides = {}): AccountApi {
  return {
    fetchOverview: vi.fn(async () => ({ ok: true, status: 200, data: { isLinkedAccount: false, mainAccountId: 42 } }) as HttpResult<unknown>),
    link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    unlink: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as AccountApi;
}

function Probe() {
  const session = useSession();
  return (
    <div>
      <span data-testid="status">{session.status}</span>
      <span data-testid="csrf">{session.csrfToken ?? "none"}</span>
      <span data-testid="service">{session.serviceAvailability}</span>
      <span data-testid="accountKind">{session.accountKind}</span>
      <span data-testid="mainAccountName">{session.mainAccountName ?? "none"}</span>
      <button onClick={() => session.login("PlayerOne", "hunter2")}>login</button>
      <button onClick={() => session.logout()}>logout</button>
    </div>
  );
}

describe("SessionProvider / useSession", () => {
  it("starts unknown with no CSRF token", () => {
    render(
      <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()}>
        <Probe />
      </SessionProvider>,
    );

    expect(screen.getByTestId("status")).toHaveTextContent("unknown");
    expect(screen.getByTestId("csrf")).toHaveTextContent("none");
  });

  it("becomes authenticated and stores the CSRF token after a successful login", async () => {
    const authApi = fakeAuthApi();
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(authApi.login).toHaveBeenCalledWith("PlayerOne", "hunter2");
    expect(screen.getByTestId("status")).toHaveTextContent("authenticated");
    expect(screen.getByTestId("csrf")).toHaveTextContent("csrf");
  });

  it("becomes unauthenticated and clears the CSRF token when login fails", async () => {
    const authApi = fakeAuthApi({
      login: vi.fn(async () => ({ ok: false, status: 401, error: { error: "invalid_credentials" } }) as HttpResult<unknown>),
    });
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(screen.getByTestId("status")).toHaveTextContent("unauthenticated");
    expect(screen.getByTestId("csrf")).toHaveTextContent("none");
  });

  it("clears session state on logout", async () => {
    const authApi = fakeAuthApi();
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });
    await act(async () => {
      screen.getByText("logout").click();
    });

    expect(authApi.logout).toHaveBeenCalled();
    expect(screen.getByTestId("status")).toHaveTextContent("unauthenticated");
    expect(screen.getByTestId("csrf")).toHaveTextContent("none");
  });

  it("loads the service availability mode from /health/ready on mount", async () => {
    const authApi = fakeAuthApi({
      fetchHealthReady: vi.fn(
        async () => ({ ok: true, status: 200, data: { mode: "ReadOnly", results: [] } }) as HttpResult<unknown>,
      ),
    });
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()}>
        <Probe />
      </SessionProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("service")).toHaveTextContent("ReadOnly"));
  });

  it("resolves accountKind Main and remembers the typed account name after a Main Account login", async () => {
    const authApi = fakeAuthApi();
    const accountApi = fakeAccountApi();
    render(
      <SessionProvider authApi={authApi} accountApi={accountApi}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(accountApi.fetchOverview).toHaveBeenCalled();
    expect(screen.getByTestId("accountKind")).toHaveTextContent("Main");
    expect(screen.getByTestId("mainAccountName")).toHaveTextContent("PlayerOne");
  });

  it("resolves accountKind Linked and never remembers an account name for a Linked Account login (AUTH-004)", async () => {
    const authApi = fakeAuthApi();
    const accountApi = fakeAccountApi({
      fetchOverview: vi.fn(async () => ({ ok: true, status: 200, data: { isLinkedAccount: true } }) as HttpResult<unknown>),
    });
    render(
      <SessionProvider authApi={authApi} accountApi={accountApi}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(screen.getByTestId("accountKind")).toHaveTextContent("Linked");
    expect(screen.getByTestId("mainAccountName")).toHaveTextContent("none");
  });

  it("clears accountKind and the remembered account name on logout", async () => {
    const authApi = fakeAuthApi();
    const accountApi = fakeAccountApi();
    render(
      <SessionProvider authApi={authApi} accountApi={accountApi}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });
    await act(async () => {
      screen.getByText("logout").click();
    });

    expect(screen.getByTestId("accountKind")).toHaveTextContent("Unknown");
    expect(screen.getByTestId("mainAccountName")).toHaveTextContent("none");
  });

  it("throws when useSession is used outside a SessionProvider", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Probe />)).toThrow(/SessionProvider/);
    consoleError.mockRestore();
  });
});
