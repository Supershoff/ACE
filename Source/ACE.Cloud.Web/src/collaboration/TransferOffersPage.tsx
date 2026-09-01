import { useCallback, useEffect, useRef, useState } from "react";
import { createTransferOfferApi, type TransferOfferApi, type CloudTransferOfferSummary } from "../api/transferOfferApi";
import { createHttpClient } from "../api/httpClient";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useSession } from "../session/SessionContext";

export interface TransferOffersPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly transferOfferApi?: TransferOfferApi;
}

/**
 * The Transfer Offer web surface (issue #39, XFER-001, XFER-002): sending an offer by typed
 * recipient character name, and the recipient/sender resolving a pending offer (accept, decline,
 * cancel). Progressive Interface: the send form is the only persistently visible control; per-offer
 * actions appear only on the offer they apply to, and only for the side authorized to take them.
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

  const [recipientCharacterName, setRecipientCharacterName] = useState("");
  const [itemBiotaId, setItemBiotaId] = useState("");

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

  async function handleSend(event: React.FormEvent) {
    event.preventDefault();
    setActionError(null);
    const biotaId = Number(itemBiotaId);
    if (!recipientCharacterName.trim() || !Number.isInteger(biotaId) || biotaId <= 0) {
      setActionError("Enter a recipient character name and a valid item ID.");
      return;
    }

    const result = await resolvedApi.create(recipientCharacterName.trim(), [{ kind: "Item", itemBiotaId: biotaId }]);
    if (result.ok) {
      setRecipientCharacterName("");
      setItemBiotaId("");
      await load();
    } else {
      setActionError("That offer could not be sent. Check the recipient name and try again.");
    }
  }

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

      <form onSubmit={handleSend}>
        <h2>Send an offer</h2>
        <label>
          Recipient character name
          <input
            value={recipientCharacterName}
            onChange={(event) => setRecipientCharacterName(event.target.value)}
            style={touchTargetStyle}
          />
        </label>
        <label>
          Item ID
          <input value={itemBiotaId} onChange={(event) => setItemBiotaId(event.target.value)} style={touchTargetStyle} />
        </label>
        <Button type="submit">Send offer</Button>
        {actionError ? <p role="alert">{actionError}</p> : null}
      </form>

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
