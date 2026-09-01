import { useCallback, useEffect, useRef, useState } from "react";
import { createSharingGrantApi, type SharingGrantApi, type CloudSharingGrantLevel, type CloudSharingGrantSummary } from "../api/sharingGrantApi";
import { createHttpClient } from "../api/httpClient";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useSession } from "../session/SessionContext";

export interface SharingGrantsPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly sharingGrantApi?: SharingGrantApi;
}

const LEVELS: readonly CloudSharingGrantLevel[] = ["None", "ViewOnly", "ViewAndWithdraw"];

/**
 * The personal Sharing Grant web surface (issue #39, SHARE-001..004): setting access for a typed
 * grantee character, and viewing grants given/received. Setting "None" uses the exact same form as
 * every other level (SHARE-004: explicit None is a real, auditable revocation, not a separate
 * "revoke" action) -- Progressive Interface avoids a second destructive-looking control for what is
 * already just one more selectable level.
 */
export function SharingGrantsPage({ sharingGrantApi }: SharingGrantsPageProps) {
  const { csrfToken, status, subscribeLiveStream } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultApiRef = useRef<SharingGrantApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createSharingGrantApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedApi = sharingGrantApi ?? defaultApiRef.current;

  const [given, setGiven] = useState<readonly CloudSharingGrantSummary[]>([]);
  const [received, setReceived] = useState<readonly CloudSharingGrantSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const [granteeCharacterName, setGranteeCharacterName] = useState("");
  const [level, setLevel] = useState<CloudSharingGrantLevel>("ViewOnly");

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await resolvedApi.list();
    if (result.ok && result.data) {
      setGiven(result.data.given);
      setReceived(result.data.received);
    } else {
      setLoadError("Your Sharing Grants could not be loaded.");
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
    // Revoked live-view: if another Main Account sets this viewer's access to None (or any other
    // level), this list reconciles from the server without a manual refresh (EVT-007, SHARE-004).
    return subscribeLiveStream("notification", load);
  }, [status, subscribeLiveStream, load]);

  async function handleSet(event: React.FormEvent) {
    event.preventDefault();
    setActionError(null);
    if (!granteeCharacterName.trim()) {
      setActionError("Enter a grantee character name.");
      return;
    }

    const result = await resolvedApi.set(granteeCharacterName.trim(), level);
    if (result.ok) {
      setGranteeCharacterName("");
      await load();
    } else {
      setActionError("That grant could not be set. Check the character name and try again.");
    }
  }

  return (
    <section>
      <h1>Sharing Grants</h1>

      <form onSubmit={handleSet}>
        <h2>Grant access</h2>
        <label>
          Grantee character name
          <input value={granteeCharacterName} onChange={(event) => setGranteeCharacterName(event.target.value)} style={touchTargetStyle} />
        </label>
        <label>
          Access level
          <select value={level} onChange={(event) => setLevel(event.target.value as CloudSharingGrantLevel)} style={touchTargetStyle}>
            {LEVELS.map((candidate) => (
              <option key={candidate} value={candidate}>
                {candidate}
              </option>
            ))}
          </select>
        </label>
        <Button type="submit">Save</Button>
        {actionError ? <p role="alert">{actionError}</p> : null}
      </form>

      {isLoading ? <LoadingState label="Loading Sharing Grants…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Sharing Grants unavailable" description={loadError} onRetry={load} /> : null}

      {!isLoading && !loadError ? (
        <>
          <h2>Given</h2>
          <ul>
            {given.length === 0 ? <li>No grants given.</li> : null}
            {given.map((grant) => (
              <li key={grant.id}>Grant {grant.id.slice(0, 8)} — {grant.level}</li>
            ))}
          </ul>

          <h2>Received</h2>
          <ul>
            {received.length === 0 ? <li>No grants received.</li> : null}
            {received.map((grant) => (
              <li key={grant.id}>Grant {grant.id.slice(0, 8)} — {grant.level}</li>
            ))}
          </ul>
        </>
      ) : null}
    </section>
  );
}
