import { act, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession, type SessionContextValue } from "./SessionContext";
import type { AccountApi } from "../api/accountApi";
import type { AuthApi } from "../api/authApi";
import type { HttpResult } from "../api/httpClient";
import type { CloudLiveStreamStatus } from "../api/liveStream";
import type { CloudResumableLiveStreamClient, CloudResumableLiveStreamOptions } from "../api/liveStreamClient";
import type { CloudLiveStreamMessage } from "../api/types";

type FakeAuthApiOverrides = Partial<{ [K in keyof AuthApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;
type FakeAccountApiOverrides = Partial<{ [K in keyof AccountApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

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

function fakeAccountApi(overrides: FakeAccountApiOverrides = {}): AccountApi {
  return {
    fetchIdentity: vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { accountId: 42, accountKind: "Main", mainAccountId: 42, linkedAccounts: [], displayCharacter: null },
        }) as HttpResult<unknown>,
    ),
    link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as HttpResult<unknown>),
    unlink: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as AccountApi;
}

interface FakeLiveStreamInstance {
  readonly options: CloudResumableLiveStreamOptions;
  emitMessage(message: CloudLiveStreamMessage): void;
  emitStatus(status: CloudLiveStreamStatus, stale: boolean): void;
  readonly disconnect: ReturnType<typeof vi.fn>;
}

function fakeLiveStreamClient() {
  const instances: FakeLiveStreamInstance[] = [];
  const factory = vi.fn((options: CloudResumableLiveStreamOptions): CloudResumableLiveStreamClient => {
    let status: CloudLiveStreamStatus = "idle";
    let stale = true;
    const disconnect = vi.fn(() => {
      status = "idle";
      stale = true;
    });
    instances.push({
      options,
      emitMessage: (message) => options.onMessage(message),
      emitStatus: (nextStatus, nextStale) => {
        status = nextStatus;
        stale = nextStale;
        options.onStatusChange?.(nextStatus, nextStale);
      },
      disconnect,
    });
    return {
      get status() {
        return status;
      },
      get stale() {
        return stale;
      },
      connect: vi.fn(),
      disconnect,
    };
  });
  return { factory, instances };
}

function Probe() {
  const session = useSession();
  return (
    <div>
      <span data-testid="status">{session.status}</span>
      <span data-testid="csrf">{session.csrfToken ?? "none"}</span>
      <span data-testid="service">{session.serviceAvailability}</span>
      <span data-testid="accountKind">{session.accountKind}</span>
      <span data-testid="accountName">{session.accountName ?? "none"}</span>
      <span data-testid="liveStreamStatus">{session.liveStream.status}</span>
      <span data-testid="liveStreamStale">{session.liveStream.stale ? "stale" : "fresh"}</span>
      <button onClick={() => session.login("PlayerOne", "hunter2")}>login</button>
      <button onClick={() => session.logout()}>logout</button>
    </div>
  );
}

describe("SessionProvider / useSession", () => {
  it("starts unknown with no CSRF token", () => {
    render(
      <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
        <Probe />
      </SessionProvider>,
    );

    expect(screen.getByTestId("status")).toHaveTextContent("unknown");
    expect(screen.getByTestId("csrf")).toHaveTextContent("none");
    expect(screen.getByTestId("accountKind")).toHaveTextContent("Unknown");
  });

  it("becomes authenticated and stores the CSRF token after a successful login", async () => {
    const authApi = fakeAuthApi();
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
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

  it("resolves AUTH-004 Main/Linked status from /account/identity right after login", async () => {
    const accountApi = fakeAccountApi({
      fetchIdentity: vi.fn(
        async () =>
          ({
            ok: true,
            status: 200,
            data: { accountId: 43, accountKind: "Linked", mainAccountId: 42, linkedAccounts: [], displayCharacter: null },
          }) as HttpResult<unknown>,
      ),
    });
    render(
      <SessionProvider authApi={fakeAuthApi()} accountApi={accountApi} createLiveStreamClient={fakeLiveStreamClient().factory}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(accountApi.fetchIdentity).toHaveBeenCalled();
    expect(screen.getByTestId("accountKind")).toHaveTextContent("Linked");
  });

  it("keeps AUTH-004 status fail-closed as Unknown when the identity resolution fails", async () => {
    const accountApi = fakeAccountApi({
      fetchIdentity: vi.fn(async () => ({ ok: false, status: 401, error: { error: "unauthenticated" } }) as HttpResult<unknown>),
    });
    render(
      <SessionProvider authApi={fakeAuthApi()} accountApi={accountApi} createLiveStreamClient={fakeLiveStreamClient().factory}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(screen.getByTestId("accountKind")).toHaveTextContent("Unknown");
  });

  it("retains the Main account name the user themself typed to log in, for the linking confirmation control", async () => {
    render(
      <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
        <Probe />
      </SessionProvider>,
    );

    await act(async () => {
      screen.getByText("login").click();
    });

    expect(screen.getByTestId("accountName")).toHaveTextContent("PlayerOne");
  });

  it("becomes unauthenticated and clears the CSRF token when login fails", async () => {
    const authApi = fakeAuthApi({
      login: vi.fn(async () => ({ ok: false, status: 401, error: { error: "invalid_credentials" } }) as HttpResult<unknown>),
    });
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
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
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
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
    expect(screen.getByTestId("accountKind")).toHaveTextContent("Unknown");
    expect(screen.getByTestId("accountName")).toHaveTextContent("none");
  });

  it("loads the service availability mode from /health/ready on mount", async () => {
    const authApi = fakeAuthApi({
      fetchHealthReady: vi.fn(
        async () => ({ ok: true, status: 200, data: { mode: "ReadOnly", results: [] } }) as HttpResult<unknown>,
      ),
    });
    render(
      <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={fakeLiveStreamClient().factory}>
        <Probe />
      </SessionProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("service")).toHaveTextContent("ReadOnly"));
  });

  it("throws when useSession is used outside a SessionProvider", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Probe />)).toThrow(/SessionProvider/);
    consoleError.mockRestore();
  });

  describe("Live State Stream wiring (EVT-007, issue #34)", () => {
    it("does not connect the Live State Stream before authentication", () => {
      const liveStream = fakeLiveStreamClient();
      render(
        <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <Probe />
        </SessionProvider>,
      );

      expect(liveStream.factory).not.toHaveBeenCalled();
      expect(screen.getByTestId("liveStreamStatus")).toHaveTextContent("idle");
    });

    it("connects to /live-stream once authenticated", async () => {
      const liveStream = fakeLiveStreamClient();
      render(
        <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <Probe />
        </SessionProvider>,
      );

      await act(async () => {
        screen.getByText("login").click();
      });

      expect(liveStream.factory).toHaveBeenCalledExactlyOnceWith(
        expect.objectContaining({ url: "/live-stream" }),
      );
    });

    it("reports the live stream's status and stale flag through context", async () => {
      const liveStream = fakeLiveStreamClient();
      render(
        <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <Probe />
        </SessionProvider>,
      );

      await act(async () => {
        screen.getByText("login").click();
      });

      act(() => {
        liveStream.instances[0]!.emitStatus("open", false);
      });

      expect(screen.getByTestId("liveStreamStatus")).toHaveTextContent("open");
      expect(screen.getByTestId("liveStreamStale")).toHaveTextContent("fresh");
    });

    it("updates serviceAvailability from a Live State Stream state message", async () => {
      const liveStream = fakeLiveStreamClient();
      render(
        <SessionProvider authApi={fakeAuthApi()} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <Probe />
        </SessionProvider>,
      );

      await act(async () => {
        screen.getByText("login").click();
      });

      act(() => {
        liveStream.instances[0]!.emitMessage({ kind: "state", mode: "ReadOnly" });
      });

      await waitFor(() => expect(screen.getByTestId("service")).toHaveTextContent("ReadOnly"));
    });

    it("notifies a subscribeLiveStream listener for a matching custody event, deduplicated and coalesced", async () => {
      const authApi = fakeAuthApi();
      const liveStream = fakeLiveStreamClient();
      const onCustodyRefresh = vi.fn();
      let subscribe: SessionContextValue["subscribeLiveStream"] = () => () => {};

      function SubscribingProbe() {
        const session = useSession();
        subscribe = session.subscribeLiveStream;
        return <button onClick={() => session.login("PlayerOne", "hunter2")}>login</button>;
      }

      render(
        <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <SubscribingProbe />
        </SessionProvider>,
      );

      const unsubscribe = subscribe("custody", onCustodyRefresh);

      await act(async () => {
        screen.getByText("login").click();
      });

      act(() => {
        liveStream.instances[0]!.emitMessage({
          kind: "event",
          eventKind: "Deposit",
          sequenceNumber: 1,
          sourceEventId: "dedupe-me",
        });
      });

      await waitFor(() => expect(onCustodyRefresh).toHaveBeenCalledTimes(1));

      act(() => {
        liveStream.instances[0]!.emitMessage({
          kind: "event",
          eventKind: "Deposit",
          sequenceNumber: 1,
          sourceEventId: "dedupe-me",
        });
      });

      expect(onCustodyRefresh).toHaveBeenCalledTimes(1);
      unsubscribe();
    });

    it("disconnects the Live State Stream on logout", async () => {
      const authApi = fakeAuthApi();
      const liveStream = fakeLiveStreamClient();
      render(
        <SessionProvider authApi={authApi} accountApi={fakeAccountApi()} createLiveStreamClient={liveStream.factory}>
          <Probe />
        </SessionProvider>,
      );

      await act(async () => {
        screen.getByText("login").click();
      });
      const instance = liveStream.instances[0]!;

      await act(async () => {
        screen.getByText("logout").click();
      });

      expect(instance.disconnect).toHaveBeenCalled();
      expect(screen.getByTestId("liveStreamStatus")).toHaveTextContent("idle");
    });
  });
});
