import { useCallback, useEffect, useRef, useState } from "react";
import {
  createAllegianceVaultApi,
  type AllegianceVaultApi,
  type CloudActingCharacter,
  type CloudAllegianceVaultItem,
} from "../api/allegianceVaultApi";
import { createHttpClient } from "../api/httpClient";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useSession } from "../session/SessionContext";

export interface AllegianceVaultPageProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly allegianceVaultApi?: AllegianceVaultApi;
}

/**
 * The Allegiance Vault web surface (issue #39, VAULT-001..003): selecting a current Acting
 * Character from the caller's own roster, viewing that character's live allegiance vault, and
 * contributing/taking items. There is no "vault settings" or rank control (VAULT-002: every current
 * member always has equal privileges), so the only persistent control is the Acting Character
 * selector itself.
 */
export function AllegianceVaultPage({ allegianceVaultApi }: AllegianceVaultPageProps) {
  const { csrfToken, status, subscribeLiveStream } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultApiRef = useRef<AllegianceVaultApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createAllegianceVaultApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedApi = allegianceVaultApi ?? defaultApiRef.current;

  const [characters, setCharacters] = useState<readonly CloudActingCharacter[]>([]);
  const [selectedCharacterId, setSelectedCharacterId] = useState<number | null>(null);
  const [items, setItems] = useState<readonly CloudAllegianceVaultItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [contributeItemId, setContributeItemId] = useState("");
  const [takeItemId, setTakeItemId] = useState("");

  const loadCharacters = useCallback(async () => {
    const result = await resolvedApi.listActingCharacters();
    if (result.ok && result.data) {
      const withAllegiance = result.data.characters.filter((character) => character.hasAllegiance);
      setCharacters(withAllegiance);
      setSelectedCharacterId((current) => current ?? withAllegiance[0]?.characterId ?? null);
      return true;
    }
    setLoadError("Your Acting Characters could not be loaded.");
    return false;
    // resolvedApi is stable across renders (see the defaultApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const loadVault = useCallback(async () => {
    if (selectedCharacterId === null) {
      setItems([]);
      return;
    }
    const result = await resolvedApi.getVault(selectedCharacterId);
    if (result.ok && result.data) {
      setItems(result.data.page.items);
    } else {
      setLoadError("This Allegiance Vault could not be loaded.");
    }
    // resolvedApi is stable across renders (see the defaultApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedCharacterId]);

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const charactersLoaded = await loadCharacters();
    if (charactersLoaded) {
      await loadVault();
    }
    setIsLoading(false);
  }, [loadCharacters, loadVault]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (status !== "authenticated") {
      return;
    }
    return subscribeLiveStream("custody", loadVault);
  }, [status, subscribeLiveStream, loadVault]);

  async function handleContribute(event: React.FormEvent) {
    event.preventDefault();
    setActionError(null);
    const biotaId = Number(contributeItemId);
    if (selectedCharacterId === null || !Number.isInteger(biotaId) || biotaId <= 0) {
      setActionError("Select an Acting Character and enter a valid item ID.");
      return;
    }

    const result = await resolvedApi.contribute({ actingCharacterId: selectedCharacterId, kind: "Item", itemBiotaId: biotaId });
    if (result.ok) {
      setContributeItemId("");
      await loadVault();
    } else {
      setActionError("That item could not be contributed. It may not be in your personal inventory.");
    }
  }

  async function handleTake(event: React.FormEvent) {
    event.preventDefault();
    setActionError(null);
    const biotaId = Number(takeItemId);
    if (selectedCharacterId === null || !Number.isInteger(biotaId) || biotaId <= 0) {
      setActionError("Select an Acting Character and enter a valid item ID.");
      return;
    }

    const result = await resolvedApi.take({ actingCharacterId: selectedCharacterId, kind: "Item", itemBiotaId: biotaId });
    if (result.ok) {
      setTakeItemId("");
      await loadVault();
    } else {
      setActionError("That item could not be taken. It may no longer be in this Allegiance Vault.");
    }
  }

  return (
    <section>
      <h1>Allegiance Vault</h1>

      <label>
        Acting Character
        <select
          value={selectedCharacterId ?? ""}
          onChange={(event) => setSelectedCharacterId(event.target.value ? Number(event.target.value) : null)}
          style={touchTargetStyle}
        >
          {characters.length === 0 ? <option value="">No current allegiance</option> : null}
          {characters.map((character) => (
            <option key={character.characterId} value={character.characterId}>
              {character.characterName}
            </option>
          ))}
        </select>
      </label>

      {isLoading ? <LoadingState label="Loading Allegiance Vault…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Allegiance Vault unavailable" description={loadError} onRetry={load} /> : null}

      {!isLoading && !loadError && selectedCharacterId !== null ? (
        <>
          <form onSubmit={handleContribute}>
            <label>
              Contribute item ID
              <input value={contributeItemId} onChange={(event) => setContributeItemId(event.target.value)} style={touchTargetStyle} />
            </label>
            <Button type="submit">Contribute</Button>
          </form>

          <form onSubmit={handleTake}>
            <label>
              Take item ID
              <input value={takeItemId} onChange={(event) => setTakeItemId(event.target.value)} style={touchTargetStyle} />
            </label>
            <Button type="submit">Take</Button>
          </form>

          {actionError ? <p role="alert">{actionError}</p> : null}

          <ul>
            {items.length === 0 ? <li>This Allegiance Vault is empty.</li> : null}
            {items.map((item) => (
              <li key={`${item.itemId}-${item.stackLotId ?? ""}`}>
                {item.name} × {item.quantity}
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </section>
  );
}
