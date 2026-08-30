import { useCallback, useEffect, useRef, useState } from "react";
import { createActivityApi, type ActivityApi } from "../api/activityApi";
import { createHttpClient } from "../api/httpClient";
import type { CloudActivityLedgerEntry } from "../api/types";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useSession } from "../session/SessionContext";

export interface ActivityLedgerPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly activityApi?: ActivityApi;
}

/**
 * The scoped Activity Ledger view (issue #34, EVT-001/EVT-002): "users see ledger activity involving
 * their assets or actions, allegiance members see their complete vault history." The Vault toggle is
 * the only control offered here (Progressive Interface: no persistent scope selector for scopes the
 * viewer cannot reach -- an admin's global view is reached through the separate admin surface, not a
 * dropdown here).
 */
export function ActivityLedgerPage({ activityApi }: ActivityLedgerPageProps) {
  const { csrfToken, status, subscribeLiveStream } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultApiRef = useRef<ActivityApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createActivityApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedApi = activityApi ?? defaultApiRef.current;

  const [entries, setEntries] = useState<readonly CloudActivityLedgerEntry[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [includeVault, setIncludeVault] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await resolvedApi.queryLedger({ page, vault: includeVault });
    if (result.ok && result.data) {
      setEntries(result.data.entries);
      setTotalPages(result.data.totalPages);
    } else {
      setLoadError("Your activity history could not be loaded.");
    }
    setIsLoading(false);
    // resolvedApi is stable across renders (see the defaultApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, includeVault]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (status !== "authenticated") {
      return;
    }
    return subscribeLiveStream("custody", load);
  }, [status, subscribeLiveStream, load]);

  return (
    <section>
      <h1>Activity</h1>

      <label style={touchTargetStyle}>
        <input
          type="checkbox"
          checked={includeVault}
          onChange={(event) => {
            setPage(1);
            setIncludeVault(event.target.checked);
          }}
        />{" "}
        Include Allegiance Vault activity
      </label>

      {isLoading ? <LoadingState label="Loading activity…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Activity unavailable" description={loadError} onRetry={load} /> : null}

      {!isLoading && !loadError ? (
        <>
          <table>
            <thead>
              <tr>
                <th scope="col">Event</th>
                <th scope="col">Item</th>
                <th scope="col">Outcome</th>
                <th scope="col">Occurred</th>
              </tr>
            </thead>
            <tbody>
              {entries.length === 0 ? (
                <tr>
                  <td colSpan={4}>No activity yet.</td>
                </tr>
              ) : (
                entries.map((entry) => (
                  <tr key={entry.id}>
                    <td>{entry.eventType}</td>
                    <td>{entry.itemBiotaId ?? "—"}</td>
                    <td>{entry.outcome ?? "—"}</td>
                    <td>{new Date(entry.occurredAtUtc).toLocaleString()}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>

          <nav aria-label="Activity pages">
            <button type="button" style={touchTargetStyle} disabled={page <= 1} onClick={() => setPage((current) => current - 1)}>
              Previous
            </button>
            <span>
              Page {page} of {Math.max(totalPages, 1)}
            </span>
            <button type="button" style={touchTargetStyle} disabled={page >= totalPages} onClick={() => setPage((current) => current + 1)}>
              Next
            </button>
          </nav>
        </>
      ) : null}
    </section>
  );
}
