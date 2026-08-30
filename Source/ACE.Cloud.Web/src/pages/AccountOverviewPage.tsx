import { useCallback, useEffect, useRef, useState } from "react";
import { createAccountApi, type AccountApi } from "../api/accountApi";
import { createHttpClient } from "../api/httpClient";
import type { AccountOverviewResponse } from "../api/types";
import { AccountLinkDialog } from "../account/AccountLinkDialog";
import { AccountUnlinkDialog } from "../account/AccountUnlinkDialog";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { useSession } from "../session/SessionContext";

export interface AccountOverviewPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly accountApi?: AccountApi;
}

/**
 * AUTH-003, AUTH-004..009: read-only Main Account/Display Character status plus the destructive
 * account-linking and unlinking flows. Withdrawal Token creation lives on the inventory grid itself
 * (the selection it acts on), not here.
 */
export function AccountOverviewPage({ accountApi }: AccountOverviewPageProps) {
  const defaultApiRef = useRef<AccountApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createAccountApi(createHttpClient({ baseUrl: "", getCsrfToken: () => null }));
  }
  const api = accountApi ?? defaultApiRef.current;

  const { mainAccountName } = useSession();

  const [overview, setOverview] = useState<AccountOverviewResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isLinkDialogOpen, setIsLinkDialogOpen] = useState(false);
  const [unlinkTarget, setUnlinkTarget] = useState<number | null>(null);

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await api.fetchOverview();
    if (result.ok && result.data) {
      setOverview(result.data);
    } else {
      setOverview(null);
      setLoadError(
        result.status === 401
          ? "Your session has expired. Log in again to see your account."
          : "Your account overview could not be loaded.",
      );
    }
    setIsLoading(false);
  }, [api]);

  useEffect(() => {
    load();
  }, [load]);

  if (isLoading) {
    return <LoadingState label="Loading your account…" />;
  }

  if (loadError || !overview) {
    return <ErrorState title="Account overview unavailable" description={loadError ?? "Unknown error."} onRetry={load} />;
  }

  if (overview.isLinkedAccount) {
    return (
      <section>
        <h1>Account overview</h1>
        <p>This is a Linked Account. Log in with the Main Account to manage the unified Cloud Inventory.</p>
      </section>
    );
  }

  return (
    <section>
      <h1>Account overview</h1>

      <section>
        <h2>Display Character</h2>
        {overview.displayCharacter ? (
          <p>{overview.displayCharacter.characterName}</p>
        ) : (
          <p>No current character to display yet.</p>
        )}
      </section>

      <section>
        <h2>Linked accounts</h2>
        {overview.linkedAccountIds && overview.linkedAccountIds.length > 0 ? (
          <ul>
            {overview.linkedAccountIds.map((linkedAccountId) => (
              <li key={linkedAccountId}>
                Linked account
                <Button variant="secondary" onClick={() => setUnlinkTarget(linkedAccountId)}>
                  Unlink
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <p>No linked accounts yet.</p>
        )}
        <Button variant="secondary" onClick={() => setIsLinkDialogOpen(true)}>
          Link another account
        </Button>
      </section>

      {mainAccountName ? (
        <AccountLinkDialog
          open={isLinkDialogOpen}
          onClose={() => setIsLinkDialogOpen(false)}
          onLinked={() => {
            setIsLinkDialogOpen(false);
            load();
          }}
          mainAccountName={mainAccountName}
          accountApi={api}
        />
      ) : null}

      <AccountUnlinkDialog
        open={unlinkTarget !== null}
        onClose={() => setUnlinkTarget(null)}
        onUnlinked={() => {
          setUnlinkTarget(null);
          load();
        }}
        linkedAccountId={unlinkTarget ?? 0}
        accountApi={api}
      />
    </section>
  );
}
