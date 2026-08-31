import { Route, Routes } from "react-router-dom";
import { ActivityLedgerPage } from "./activity/ActivityLedgerPage";
import { LiveStreamStaleBanner } from "./design-system/primitives/LiveStreamStaleBanner";
import { ReadOnlyBanner } from "./design-system/primitives/ReadOnlyBanner";
import { Button } from "./design-system/primitives/Button";
import { NotificationCenter } from "./notifications/NotificationCenter";
import { AccountOverviewPage } from "./pages/AccountOverviewPage";
import { AdminPage } from "./pages/AdminPage";
import { DashboardPage } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { MarketplacePage } from "./pages/MarketplacePage";
import { RequireAdmin } from "./routes/RequireAdmin";
import { RequireAuth } from "./routes/RequireAuth";
import { RequireMainAccount } from "./routes/RequireMainAccount";
import { ErrorBoundary } from "./shell/ErrorBoundary";
import { AppShell } from "./shell/AppShell";
import { useSession } from "./session/SessionContext";

const NAV_ITEMS = [
  { to: "/", label: "Marketplace" },
  { to: "/dashboard", label: "Dashboard" },
  { to: "/activity", label: "Activity" },
  { to: "/account", label: "Account" },
];

export function App() {
  const { status, serviceAvailability, liveStream, logout } = useSession();
  const readOnlyBanner = serviceAvailability === "unknown" ? null : <ReadOnlyBanner mode={serviceAvailability} />;
  const staleBanner = status === "authenticated" && liveStream.stale ? <LiveStreamStaleBanner /> : null;
  const banner = readOnlyBanner || staleBanner ? (
    <>
      {readOnlyBanner}
      {staleBanner}
    </>
  ) : null;

  return (
    <AppShell
      navItems={NAV_ITEMS}
      banner={banner}
      headerActions={
        <>
          <NotificationCenter />
          {status === "authenticated" ? (
            <Button variant="secondary" onClick={() => void logout()}>
              Log out
            </Button>
          ) : null}
        </>
      }
    >
      <ErrorBoundary>
        <Routes>
          <Route path="/" element={<MarketplacePage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/dashboard"
            element={
              <RequireAuth>
                <DashboardPage />
              </RequireAuth>
            }
          />
          <Route
            path="/activity"
            element={
              <RequireAuth>
                <ActivityLedgerPage />
              </RequireAuth>
            }
          />
          <Route
            path="/account"
            element={
              <RequireAuth>
                <RequireMainAccount>
                  <AccountOverviewPage />
                </RequireMainAccount>
              </RequireAuth>
            }
          />
          <Route
            path="/admin"
            element={
              <RequireAuth>
                <RequireAdmin>
                  <AdminPage />
                </RequireAdmin>
              </RequireAuth>
            }
          />
        </Routes>
      </ErrorBoundary>
    </AppShell>
  );
}
