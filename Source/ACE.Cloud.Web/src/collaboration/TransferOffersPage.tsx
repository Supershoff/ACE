import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { createTransferOfferApi, type TransferOfferApi, type CloudTransferOfferSummary } from "../api/transferOfferApi";
import { createHttpClient } from "../api/httpClient";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { useSession } from "../session/SessionContext";

export interface TransferOffersPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly transferOfferApi?: TransferOfferApi;
}

/**
 * The Transfer Offer Sent/Received management surface (issue #39, XFER-001, XFER-002): accepting,
 * declining, or cancelling a pending offer. Creating an offer is no longer done here -- it is a
 * contextual action on the currently selected Inventory item (`ItemActionDialog`, opened from
 * `InventoryView`/`FullCloudAppraisalDialog`'s Actions menu), so the item ID always comes from
 * application state and is never typed by the user (PR #157's blocking human-acceptance feedback).
 * Progressive Interface: per-offer actions appear only on the offer they apply to, and only for the
 * side authorized to take them.
 */
export function TransferOffersPage({ transferOfferApi }: TransferOffersPageProps) {
  const { csrfToken, status, subscribeLiveStream } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultApiRef = useRef<TransferOfferApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createTransferOfferApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedApi = transferOfferApi ?? defaultApiRef.current;

  const [sent, setSent] = useState<readonly CloudTransferOfferSummary[]>([]);
  const [received, setReceived] = useState<readonly CloudTransferOfferSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await resolvedApi.list();
    if (result.ok && result.data) {
      setSent(result.data.sent);
      setReceived(result.data.received);
    } else {
      setLoadError("Your Transfer Offers could not be loaded.");
    }
    setIsLoading(false);
    // resolvedApi is stable across renders (see the defaultApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (status !== "authenticated") {
      return;
    }
    // Live-view: an accept/decline/cancel/expiry by the other side reconciles this list without a
    // manual refresh (EVT-007).
    return subscribeLiveStream("custody", load);
  }, [status, subscribeLiveStream, load]);

  async function handleResolve(offer: CloudTransferOfferSummary, action: "accept" | "decline" | "cancel") {
    setActionError(null);
    const result = await resolvedApi[action](offer.id, offer.version);
    if (result.ok) {
      await load();
    } else {
      setActionError("That action could not be completed. The offer may have already been resolved.");
    }
  }

  return (
    <section>
      <h1>Transfer Offers</h1>
      <p>
        To send a new offer, select an item in your <Link to="/inventory">Inventory</Link> and choose{" "}
        <strong>Send Transfer Offer…</strong> from its Actions menu.
      </p>
      {actionError ? <p role="alert">{actionError}</p> : null}

      {isLoading ? <LoadingState label="Loading Transfer Offers…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Transfer Offers unavailable" description={loadError} onRetry={load} /> : null}

      {!isLoading && !loadError ? (
        <>
          <h2>Received</h2>
          <ul>
            {received.length === 0 ? <li>No offers received.</li> : null}
            {received.map((offer) => (
              <li key={offer.id}>
                Offer {offer.id.slice(0, 8)} — {offer.status}
                {offer.status === "Pending" ? (
                  <>
                    <Button variant="primary" onClick={() => handleResolve(offer, "accept")}>
                      Accept
                    </Button>
                    <Button variant="secondary" onClick={() => handleResolve(offer, "decline")}>
                      Decline
                    </Button>
                  </>
                ) : null}
              </li>
            ))}
          </ul>

          <h2>Sent</h2>
          <ul>
            {sent.length === 0 ? <li>No offers sent.</li> : null}
            {sent.map((offer) => (
              <li key={offer.id}>
                Offer {offer.id.slice(0, 8)} — {offer.status}
                {offer.status === "Pending" ? (
                  <Button variant="danger" onClick={() => handleResolve(offer, "cancel")}>
                    Cancel
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </section>
  );
}
