import { useCallback, useEffect, useRef, useState } from "react";
import { AccountLinkingPanel } from "../account/AccountLinkingPanel";
import { WithdrawalTokenPanel } from "../account/WithdrawalTokenPanel";
import { createAccountApi, type AccountApi } from "../api/accountApi";
import { createHttpClient } from "../api/httpClient";
import type { CloudAccountIdentityResponse } from "../api/types";
import { createWithdrawalApi, type WithdrawalApi } from "../api/withdrawalApi";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { useSession } from "../session/SessionContext";

export interface AccountOverviewPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly accountApi?: AccountApi;
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly withdrawalApi?: WithdrawalApi;
}

/**
 * AUTH-003, AUTH-005..009, WDR-001..008, EVT-007 (issue #33): progressive account/display identity,
 * destructive account linking with delayed confirmation, and the Withdrawal Token web flow. This
 * page itself resolves the fuller identity payload (linked accounts, Display Character) that
 * `SessionContext.accountKind` alone does not carry; the `/account` route's own `RequireMainAccount`
 * guard already refuses a Linked Account session before this component ever mounts (AUTH-004).
 */
export function AccountOverviewPage({ accountApi, withdrawalApi }: AccountOverviewPageProps) {
  const { csrfToken } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultAccountApiRef = useRef<AccountApi | null>(null);
  if (!defaultAccountApiRef.current) {
    defaultAccountApiRef.current = createAccountApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedAccountApi = accountApi ?? defaultAccountApiRef.current;

  const defaultWithdrawalApiRef = useRef<WithdrawalApi | null>(null);
  if (!defaultWithdrawalApiRef.current) {
    defaultWithdrawalApiRef.current = createWithdrawalApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedWithdrawalApi = withdrawalApi ?? defaultWithdrawalApiRef.current;

  const [identity, setIdentity] = useState<CloudAccountIdentityResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadIdentity = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await resolvedAccountApi.fetchIdentity();
    if (result.ok && result.data) {
      setIdentity(result.data);
    } else {
      setLoadError("Your account identity could not be loaded.");
    }
    setIsLoading(false);
    // resolvedAccountApi is stable across renders (see the defaultAccountApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    loadIdentity();
  }, [loadIdentity]);

  return (
    <section>
      <h1>Account overview</h1>

      {isLoading ? <LoadingState label="Loading your account…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Account overview unavailable" description={loadError} onRetry={loadIdentity} /> : null}

      {!isLoading && !loadError && identity ? (
        <>
          <p>
            Display Character:{" "}
            {identity.displayCharacter ? identity.displayCharacter.characterName : "None selected yet"}
          </p>

          <AccountLinkingPanel identity={identity} accountApi={resolvedAccountApi} onChanged={loadIdentity} />
          <WithdrawalTokenPanel withdrawalApi={resolvedWithdrawalApi} />
        </>
      ) : null}
    </section>
  );
}
