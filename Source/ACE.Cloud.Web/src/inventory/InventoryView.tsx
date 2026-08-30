import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { createHttpClient } from "../api/httpClient";
import { createInventoryApi, type InventoryApi } from "../api/inventoryApi";
import { createWithdrawalApi, type WithdrawalApi } from "../api/withdrawalApi";
import type {
  CloudAppraisalPanel,
  CloudInventoryCategory,
  CloudInventoryItem,
  CloudInventoryQueryResponse,
  CloudInventorySortDirection,
  CloudInventorySortKey,
} from "../api/types";
import { Button } from "../design-system/primitives/Button";
import { EmptyState } from "../design-system/primitives/EmptyState";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { useIsNarrowViewport } from "../shell/useIsNarrowViewport";
import { iconGridTokens } from "../design-system/inventoryFidelityTokens";
import { FullCloudAppraisalDialog } from "./FullCloudAppraisalDialog";
import { InventoryQuantityControl } from "./InventoryQuantityControl";
import { InventorySpreadsheet } from "./InventorySpreadsheet";
import { MulePageGrid } from "./MulePageGrid";
import { inventoryItemKey } from "./selection";
import { WithdrawalDialog, type WithdrawalSelectionEntry } from "./WithdrawalDialog";

export interface InventoryViewProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly inventoryApi?: InventoryApi;
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly withdrawalApi?: WithdrawalApi;
}

const CATEGORIES: readonly CloudInventoryCategory[] = [
  "MeleeWeapons",
  "MissileWeapons",
  "Casters",
  "Armor",
  "Clothing",
  "Jewelry",
  "Foodstuffs",
  "Currency",
  "Gems",
  "SpellComponents",
  "WrittenMaterial",
  "Keys",
  "Portals",
  "ManaStones",
  "PromissoryNotes",
  "LifeStones",
  "CraftingMaterials",
  "Miscellaneous",
];

const NARROW_COLUMNS = 3;

type InventoryViewMode = "grid" | "spreadsheet";

interface AppraisalDialogState {
  readonly open: boolean;
  readonly itemId: number | null;
  readonly itemName: string;
  readonly panel: CloudAppraisalPanel | null;
  readonly isLoading: boolean;
  readonly error: string | null;
}

const CLOSED_APPRAISAL_STATE: AppraisalDialogState = {
  open: false,
  itemId: null,
  itemName: "",
  panel: null,
  isLoading: false,
  error: null,
};

function queryErrorMessage(status: number): string {
  if (status === 401) {
    return "Your session has expired. Log in again to see your Cloud Inventory.";
  }
  if (status === 403) {
    return "Linked account credentials cannot view or manage the unified Cloud Inventory. Log in with the Main Account to continue.";
  }
  return "Your Cloud Inventory could not be loaded.";
}

function appraisalErrorMessage(status: number): string {
  if (status === 404) {
    return "This item's appraisal is no longer available.";
  }
  return "This item's appraisal could not be loaded.";
}

/**
 * The Mule Page grid, spreadsheet, and Full Cloud Appraisal vertical slice (issue #31): fetches
 * pages from the shared query contract (#30), renders accurate icons (UI-005/UI-006), reflows the
 * desktop grid at narrow widths without changing page membership (UI-002/UI-003), and opens a
 * complete, character-independent Full Cloud Appraisal on click/right-click/keyboard/touch (UI-004).
 */
export function InventoryView({ inventoryApi, withdrawalApi }: InventoryViewProps) {
  const defaultApiRef = useRef<InventoryApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createInventoryApi(createHttpClient({ baseUrl: "", getCsrfToken: () => null }));
  }
  const api = inventoryApi ?? defaultApiRef.current;

  const defaultWithdrawalApiRef = useRef<WithdrawalApi | null>(null);
  if (!defaultWithdrawalApiRef.current) {
    defaultWithdrawalApiRef.current = createWithdrawalApi(createHttpClient({ baseUrl: "", getCsrfToken: () => null }));
  }
  const resolvedWithdrawalApi = withdrawalApi ?? defaultWithdrawalApiRef.current;

  const isNarrow = useIsNarrowViewport();
  const columns = isNarrow ? NARROW_COLUMNS : iconGridTokens.desktopColumns;

  const [viewMode, setViewMode] = useState<InventoryViewMode>("grid");
  const [category, setCategory] = useState<CloudInventoryCategory | undefined>("Armor");
  const [page, setPage] = useState(1);
  const [sortKey, setSortKey] = useState<CloudInventorySortKey>("Name");
  const [sortDirection, setSortDirection] = useState<CloudInventorySortDirection>("Ascending");

  const [response, setResponse] = useState<CloudInventoryQueryResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [selectedKeys, setSelectedKeys] = useState<ReadonlySet<string>>(new Set());
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const [quantities, setQuantities] = useState<ReadonlyMap<string, number>>(new Map());

  const [appraisal, setAppraisal] = useState<AppraisalDialogState>(CLOSED_APPRAISAL_STATE);
  const [isWithdrawalDialogOpen, setIsWithdrawalDialogOpen] = useState(false);

  const categorySelectId = useId();

  const effectiveCategory = viewMode === "grid" ? category : undefined;

  const load = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    const result = await api.queryPages({ category: effectiveCategory, page, sortKey, sortDirection });
    if (result.ok && result.data) {
      setResponse(result.data);
    } else {
      setResponse(null);
      setLoadError(queryErrorMessage(result.status));
    }
    setIsLoading(false);
  }, [api, effectiveCategory, page, sortKey, sortDirection]);

  useEffect(() => {
    load();
  }, [load]);

  const items = useMemo(() => response?.page.items ?? [], [response]);

  function openAppraisal(item: CloudInventoryItem) {
    setAppraisal({ open: true, itemId: item.itemId, itemName: item.name, panel: null, isLoading: true, error: null });
    api.fetchAppraisal(item.itemId).then((result) => {
      if (result.ok && result.data) {
        setAppraisal((current) =>
          current.itemId === item.itemId ? { ...current, panel: result.data!, isLoading: false } : current,
        );
      } else {
        setAppraisal((current) =>
          current.itemId === item.itemId
            ? { ...current, isLoading: false, error: appraisalErrorMessage(result.status) }
            : current,
        );
      }
    });
  }

  function handleActivate(item: CloudInventoryItem, additive: boolean) {
    const key = inventoryItemKey(item);

    if (additive) {
      setSelectedKeys((current) => {
        const next = new Set(current);
        if (next.has(key)) {
          next.delete(key);
        } else {
          next.add(key);
        }
        return next;
      });
      setQuantities((current) => {
        const next = new Map(current);
        if (next.has(key)) {
          next.delete(key);
        } else if (item.quantity > 1) {
          next.set(key, item.quantity);
        }
        return next;
      });
      setActiveKey(key);
      return;
    }

    setSelectedKeys(new Set([key]));
    setQuantities(item.quantity > 1 ? new Map([[key, item.quantity]]) : new Map());
    setActiveKey(key);
    openAppraisal(item);
  }

  function handleQuantityChange(key: string, value: number) {
    setQuantities((current) => new Map(current).set(key, value));
  }

  const selectedStackItems = items.filter((item) => selectedKeys.has(inventoryItemKey(item)) && item.quantity > 1);

  const selectedItems = items.filter((item) => selectedKeys.has(inventoryItemKey(item)));
  const withdrawableSelection: WithdrawalSelectionEntry[] = selectedItems
    .filter((item) => item.permittedActions.canWithdraw)
    .map((item) => ({ item, quantity: quantities.get(inventoryItemKey(item)) ?? item.quantity }));
  const canWithdrawSelection = selectedItems.length > 0 && withdrawableSelection.length === selectedItems.length;

  function clearSelectionAndReload() {
    setSelectedKeys(new Set());
    setQuantities(new Map());
    load();
  }

  return (
    <section className="inventory-view">
      <h2>Cloud Inventory</h2>

      <div className="inventory-view__controls">
        <div role="radiogroup" aria-label="Inventory view">
          <Button
            variant={viewMode === "grid" ? "primary" : "secondary"}
            aria-pressed={viewMode === "grid"}
            onClick={() => setViewMode("grid")}
          >
            Grid
          </Button>
          <Button
            variant={viewMode === "spreadsheet" ? "primary" : "secondary"}
            aria-pressed={viewMode === "spreadsheet"}
            onClick={() => setViewMode("spreadsheet")}
          >
            Spreadsheet
          </Button>
        </div>

        {viewMode === "grid" ? (
          <div>
            <label htmlFor={categorySelectId}>Category</label>
            <select
              id={categorySelectId}
              value={category}
              onChange={(event) => {
                setCategory(event.target.value as CloudInventoryCategory);
                setPage(1);
                setSelectedKeys(new Set());
              }}
            >
              {CATEGORIES.map((candidate) => (
                <option key={candidate} value={candidate}>
                  {candidate}
                </option>
              ))}
            </select>
          </div>
        ) : null}
      </div>

      {isLoading ? <LoadingState label="Loading your Cloud Inventory…" /> : null}
      {!isLoading && loadError ? <ErrorState title="Cloud Inventory unavailable" description={loadError} onRetry={load} /> : null}

      {!isLoading && !loadError && response ? (
        <>
          {items.length === 0 ? (
            <EmptyState title="No items here" description="This Mule Page has no items yet." />
          ) : viewMode === "grid" ? (
            <MulePageGrid
              pageName={response.page.pageName ?? "Cloud Inventory"}
              items={items}
              columns={columns}
              selectedKeys={selectedKeys}
              activeKey={activeKey}
              onActivate={handleActivate}
              onFocusItem={(item) => setActiveKey(inventoryItemKey(item))}
              buildIconUrl={api.buildIconUrl}
            />
          ) : (
            <InventorySpreadsheet
              items={items}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortChange={(nextKey, nextDirection) => {
                setSortKey(nextKey);
                setSortDirection(nextDirection);
                setPage(1);
              }}
              selectedKeys={selectedKeys}
              onActivate={handleActivate}
              buildIconUrl={api.buildIconUrl}
            />
          )}

          {selectedStackItems.map((item) => {
            const key = inventoryItemKey(item);
            return (
              <InventoryQuantityControl
                key={key}
                itemName={item.name}
                maxQuantity={item.quantity}
                value={quantities.get(key) ?? item.quantity}
                onChange={(value) => handleQuantityChange(key, value)}
              />
            );
          })}

          {selectedItems.length > 0 ? (
            <Button variant="primary" onClick={() => setIsWithdrawalDialogOpen(true)} disabled={!canWithdrawSelection}>
              Withdraw selected
            </Button>
          ) : null}

          <nav aria-label="Mule Page navigation">
            <Button variant="secondary" onClick={() => setPage((current) => current - 1)} disabled={page <= 1}>
              Previous page
            </Button>
            <span>
              Page {response.page.pageNumber} of {Math.max(response.page.totalPages, 1)}
            </span>
            <Button
              variant="secondary"
              onClick={() => setPage((current) => current + 1)}
              disabled={page >= response.page.totalPages}
            >
              Next page
            </Button>
          </nav>
        </>
      ) : null}

      <FullCloudAppraisalDialog
        open={appraisal.open}
        onClose={() => setAppraisal(CLOSED_APPRAISAL_STATE)}
        itemName={appraisal.itemName}
        panel={appraisal.panel}
        isLoading={appraisal.isLoading}
        error={appraisal.error}
        onRetry={() => {
          const reopenItem = items.find((item) => item.itemId === appraisal.itemId);
          if (reopenItem) {
            openAppraisal(reopenItem);
          }
        }}
      />

      <WithdrawalDialog
        open={isWithdrawalDialogOpen}
        onClose={() => setIsWithdrawalDialogOpen(false)}
        selection={withdrawableSelection}
        withdrawalApi={resolvedWithdrawalApi}
        onSettled={clearSelectionAndReload}
      />
    </section>
  );
}
