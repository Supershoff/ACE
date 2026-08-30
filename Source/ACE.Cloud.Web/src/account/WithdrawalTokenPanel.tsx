import { useCallback, useEffect, useId, useRef, useState } from "react";
import { createHttpClient } from "../api/httpClient";
import { createInventoryApi, type InventoryApi } from "../api/inventoryApi";
import type { CloudInventoryCategory, CloudInventoryItem, CloudWithdrawalTargetRequest } from "../api/types";
import type { WithdrawalApi } from "../api/withdrawalApi";
import { Button } from "../design-system/primitives/Button";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { RequireWritableService } from "../routes/RequireWritableService";

export interface WithdrawalTokenPanelProps {
  readonly withdrawalApi: WithdrawalApi;
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly inventoryApi?: InventoryApi;
}

const CATEGORIES: readonly CloudInventoryCategory[] = [
  "MeleeWeapons", "MissileWeapons", "Casters", "Armor", "Clothing", "Jewelry", "Foodstuffs", "Currency",
  "Gems", "SpellComponents", "WrittenMaterial", "Keys", "Portals", "ManaStones", "PromissoryNotes",
  "LifeStones", "CraftingMaterials", "Miscellaneous",
];

const CREATE_ERROR_MESSAGES: Record<string, string> = {
  invalid_request: "Select at least one item to withdraw.",
  linked_account_restricted: "Linked account credentials can't create Withdrawal Tokens.",
  world_boundary_unavailable: "ACE is currently offline, so Withdrawal Tokens can't be created right now. Try again once the world is back up.",
  conflict: "One or more selected items already have a pending action. Refresh and try again.",
  unavailable: "Withdrawal Tokens are temporarily unavailable. Try again shortly.",
};

function formatRemaining(msRemaining: number): string {
  if (msRemaining <= 0) {
    return "expired";
  }
  const totalSeconds = Math.ceil(msRemaining / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

function itemKey(item: CloudInventoryItem): string {
  return item.stackLotId ?? String(item.itemId);
}

/**
 * Issue #33's Withdrawal Token web creation/status/cancellation flow (WDR-001, WDR-002, WDR-003,
 * WDR-006, WDR-008): select Cloud-eligible items, reveal the minted token exactly once with a copy
 * affordance, track its 15-minute clock, and reconcile from the authoritative reservation on every
 * load (a page reload, a second tab, or a second device all see the same server-committed state --
 * never the secret again after the first reveal).
 */
export function WithdrawalTokenPanel({ withdrawalApi, inventoryApi }: WithdrawalTokenPanelProps) {
  const defaultApiRef = useRef<InventoryApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createInventoryApi(createHttpClient({ baseUrl: "", getCsrfToken: () => null }));
  }
  const resolvedInventoryApi = inventoryApi ?? defaultApiRef.current;

  const categorySelectId = useId();
  const [category, setCategory] = useState<CloudInventoryCategory>("Armor");
  const [items, setItems] = useState<readonly CloudInventoryItem[]>([]);
  const [itemsLoading, setItemsLoading] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<ReadonlySet<string>>(new Set());
  const [quantities, setQuantities] = useState<ReadonlyMap<string, number>>(new Map());

  const [locations, setLocations] = useState<{ withdrawAnywhereEnabled: boolean; namedLandblocks: readonly { id: string; landblock: string; name: string }[] } | null>(null);

  const [current, setCurrent] = useState<Awaited<ReturnType<WithdrawalApi["fetchCurrent"]>>["data"] | null>(null);
  const [statusLoading, setStatusLoading] = useState(true);
  const [justCreatedSecret, setJustCreatedSecret] = useState<string | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createPending, setCreatePending] = useState(false);
  const [cancelPending, setCancelPending] = useState(false);
  const [nowMs, setNowMs] = useState(() => Date.now());

  const loadCurrent = useCallback(async () => {
    setStatusLoading(true);
    const result = await withdrawalApi.fetchCurrent();
    setCurrent(result.ok ? (result.data ?? null) : null);
    setStatusLoading(false);
  }, [withdrawalApi]);

  useEffect(() => {
    loadCurrent();
    withdrawalApi.fetchLocations().then((result) => {
      if (result.ok && result.data) {
        setLocations(result.data);
      }
    });
  }, [loadCurrent, withdrawalApi]);

  useEffect(() => {
    setItemsLoading(true);
    resolvedInventoryApi.queryPages({ category, page: 1 }).then((result) => {
      setItems(result.ok && result.data ? result.data.page.items.filter((item) => item.permittedActions.canWithdraw) : []);
      setItemsLoading(false);
    });
    // resolvedInventoryApi is stable across renders (see the defaultApiRef pattern above); omitting
    // it from the dependency list matches InventoryView's own identical convention.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [category]);

  useEffect(() => {
    if (!current?.active) {
      return;
    }
    const interval = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(interval);
  }, [current]);

  useEffect(() => {
    if (current?.active && new Date(current.expiresAtUtc).getTime() <= nowMs) {
      // WDR-002: the token expired at its 15-minute mark; reconcile from the server rather than
      // trusting the local countdown alone (EVT-007: authoritative version wins).
      loadCurrent();
    }
  }, [current, nowMs, loadCurrent]);

  function toggleSelected(item: CloudInventoryItem) {
    const key = itemKey(item);
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
    setQuantities((prev) => {
      const next = new Map(prev);
      if (!next.has(key)) {
        next.set(key, item.quantity);
      }
      return next;
    });
  }

  async function handleCreate() {
    setCreatePending(true);
    setCreateError(null);

    const targets: CloudWithdrawalTargetRequest[] = [];
    for (const item of items) {
      const key = itemKey(item);
      if (!selectedKeys.has(key)) {
        continue;
      }

      if (item.stackLotId === null) {
        targets.push({ kind: "Item", itemBiotaId: item.itemId });
        continue;
      }

      const requestedQuantity = quantities.get(key) ?? item.quantity;
      if (requestedQuantity >= item.quantity) {
        targets.push({ kind: "StackLot", stackLotId: item.stackLotId });
        continue;
      }

      // CONTEXT.md: a partial-quantity selection first splits a new lot for exactly that quantity,
      // then reserves the new lot -- never the original.
      const splitResult = await withdrawalApi.splitStackLot(item.stackLotId, item.version, requestedQuantity);
      if (!splitResult.ok || !splitResult.data) {
        setCreatePending(false);
        setCreateError(`Could not split ${item.name} into the requested quantity. Try again.`);
        return;
      }
      targets.push({ kind: "StackLot", stackLotId: splitResult.data.newLot.id });
    }

    if (targets.length === 0) {
      setCreatePending(false);
      setCreateError(CREATE_ERROR_MESSAGES.invalid_request!);
      return;
    }

    const result = await withdrawalApi.create(targets);
    setCreatePending(false);

    if (!result.ok || !result.data) {
      const kind = (result.error as { error?: string } | undefined)?.error ?? "";
      setCreateError(CREATE_ERROR_MESSAGES[kind] ?? "This Withdrawal Token couldn't be created. Try again.");
      return;
    }

    setJustCreatedSecret(result.data.secret);
    setSelectedKeys(new Set());
    await loadCurrent();
  }

  async function handleCancel() {
    if (!current?.active) {
      return;
    }
    setCancelPending(true);
    const result = await withdrawalApi.cancel(current.reservationId, current.version);
    setCancelPending(false);
    if (result.ok) {
      setJustCreatedSecret(null);
      await loadCurrent();
    }
  }

  async function handleCopySecret() {
    if (justCreatedSecret) {
      await navigator.clipboard.writeText(justCreatedSecret);
    }
  }

  return (
    <section aria-label="Withdrawal Token">
      <h2>Withdrawal Token</h2>

      {locations ? (
        <p>
          Redeem at: {locations.withdrawAnywhereEnabled ? "any location" : "a Custodian Location, "}
          {locations.namedLandblocks.map((landblock) => `${landblock.name} (${landblock.landblock})`).join(", ") || null}
        </p>
      ) : null}

      {statusLoading ? <LoadingState label="Checking for an active Withdrawal Token…" /> : null}

      {!statusLoading && current?.active ? (
        <div aria-label="Active Withdrawal Token">
          <p>
            Time remaining: <strong>{formatRemaining(new Date(current.expiresAtUtc).getTime() - nowMs)}</strong>
          </p>
          <p>{current.targets.length} item(s) reserved. Insufficient recipient capacity in game leaves this reservation active and retryable until it expires.</p>
          {justCreatedSecret ? (
            <div>
              <p>Your Withdrawal Token (shown once -- copy it now):</p>
              <code>{justCreatedSecret}</code>
              <Button onClick={handleCopySecret}>Copy token</Button>
            </div>
          ) : (
            <p>This token was already issued; its secret is no longer shown here for your protection.</p>
          )}
          <Button variant="danger" disabled={cancelPending} onClick={handleCancel}>
            {cancelPending ? "Cancelling…" : "Cancel Withdrawal Token"}
          </Button>
        </div>
      ) : null}

      {!statusLoading && !current?.active ? (
        <RequireWritableService>
          <div>
            <label htmlFor={categorySelectId}>Category</label>
            <select
              id={categorySelectId}
              value={category}
              onChange={(event) => {
                setCategory(event.target.value as CloudInventoryCategory);
                setSelectedKeys(new Set());
              }}
            >
              {CATEGORIES.map((candidate) => (
                <option key={candidate} value={candidate}>
                  {candidate}
                </option>
              ))}
            </select>

            {itemsLoading ? <LoadingState label="Loading withdrawable items…" /> : null}

            <ul>
              {items.map((item) => {
                const key = itemKey(item);
                const checked = selectedKeys.has(key);
                return (
                  <li key={key}>
                    <label>
                      <input type="checkbox" checked={checked} onChange={() => toggleSelected(item)} />
                      {item.name}
                      {item.quantity > 1 ? ` (${item.quantity})` : ""}
                    </label>
                    {checked && item.quantity > 1 ? (
                      <input
                        type="number"
                        min={1}
                        max={item.quantity}
                        aria-label={`Quantity of ${item.name} to withdraw`}
                        value={quantities.get(key) ?? item.quantity}
                        onChange={(event) =>
                          setQuantities((prev) => new Map(prev).set(key, Number(event.target.value)))
                        }
                      />
                    ) : null}
                  </li>
                );
              })}
            </ul>

            <Button disabled={selectedKeys.size === 0 || createPending} onClick={handleCreate}>
              {createPending ? "Creating…" : "Create Withdrawal Token"}
            </Button>
            {createError ? <ErrorState title="Withdrawal Token not created" description={createError} /> : null}
          </div>
        </RequireWritableService>
      ) : null}
    </section>
  );
}
