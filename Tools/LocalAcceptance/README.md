# Local Acceptance Runbook (issue #34)

A reproducible, **local-only, disposable** environment for the Phase 5 inventory vertical slice
(Activity Ledger, Notification Center, and the resumable Live State Stream). It runs on an operator's
Windows development machine, uses Docker Compose only for an isolated MariaDB container, and connects
to a **separately started ACE test world** built from this PR branch through explicit configuration.
It never requires public hosting, never bootstraps a production installation, and never touches an
existing ACE installation or its databases.

## What this is not

- Not the "Operator Bootstrap" production command (`CONTEXT.md`) -- it does not create schemas,
  restricted database identities, or secrets for a real deployment.
- Not a way to start or configure the ACE world server itself. You start your own disposable ACE test
  world first (see "1. Start a disposable ACE test world" below); this launcher only connects to it.

## Prerequisites

- Windows, with [Docker Desktop](https://www.docker.com/products/docker-desktop/) (WSL2 backend) running.
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Node.js 20+](https://nodejs.org/) (npm ships with it).
- PowerShell 7+ (`pwsh`). Run `Test-Prerequisites.ps1` any time to check all of the above plus free
  ports and a reachable ACE test world -- it reports every problem it finds rather than skipping
  silently.

## 1. Start a disposable ACE test world

Build and run your own ACE server **from this PR branch**, separately from this launcher (e.g. via the
repository's own `docker-compose.yml`/`Dockerfile`, or `dotnet run --project Source/ACE.Server`). Note
its world-boundary health endpoint (default `http://127.0.0.1:9600/health/live`) and its `ace_auth`
database connection details -- you will put both into `acceptance.settings.json` in the next step.
Create at least one synthetic test account on it (never a real player's account) for `testAccounts` in
step 2, and, if you want account-linking coverage, a second synthetic account to link to it.

## 2. Configure this launcher

```powershell
Copy-Item acceptance.settings.example.json acceptance.settings.json
```

Edit `acceptance.settings.json` (git-ignored -- it holds your local, disposable secrets) and replace
every `CHANGE_ME` value: local ports, a throwaway MariaDB password for the acceptance container, your
test world's health endpoint and `ace_auth` connection string, a shard ID, a service-key ID/secret pair
matching what your test world's ACE-side extension expects, and your synthetic test accounts.

## 3. Start the stack

```powershell
cd Tools/LocalAcceptance
./Start-LocalAcceptance.ps1
```

This, in order:

1. Runs `Test-Prerequisites.ps1` and stops with a specific, actionable diagnostic if anything is missing.
2. Starts an isolated MariaDB 11.4 container (`docker-compose.acceptance.yml`, project `ace-cloud-acceptance`) for the `ace_cloud` schema only, on a distinct port (default `3307`) so it never collides with an existing ACE installation's own database.
3. Applies Cloud schema migrations against that disposable database (`ACE.Cloud.LocalAcceptanceMigrator`, wrapping the same `CloudSchemaMigrator` the integration tests already exercise).
4. Starts the ACE Auth Bridge, `ACE.Cloud.Backend`, and `ACE.Cloud.Worker` as background processes (logs under `.local-run/logs/`), configured to talk to your separately started ACE test world.
5. Waits for the Backend's `/health/ready` to report ready.
6. Builds the web client (`npm run build`; skip with `-SkipWebBuild` to reuse a previous build) and starts `same-origin-proxy.mjs`, a small dependency-free Node proxy that serves the built web client and proxies its API calls to the Backend from one origin (matching how the app's `fetch()` calls -- and its cookie-based session -- expect to work).
7. Prints the local URL once the proxy itself reports ready.

## 4. Run the human acceptance checklist

Open the printed URL and, using your synthetic test account(s):

- [ ] Log in, and confirm the Notification Center and Activity link appear.
- [ ] Browse the Mule Page grid and spreadsheet view; confirm icons render and reflow at a narrow width without changing page membership.
- [ ] Open a Full Cloud Appraisal for an item.
- [ ] Create a Withdrawal Token for at least one item; confirm it appears once in the Activity Ledger.
- [ ] In a second tab logged into the same account, confirm the new Activity Ledger entry appears **without reloading** (the Live State Stream).
- [ ] Open the Notification Center; confirm it opens/closes accessibly and the unread badge behaves.
- [ ] Resize to a narrow/mobile viewport; confirm navigation and every above action still works.
- [ ] Stop `ACE.Cloud.Backend` (e.g. `Stop-Process` on its pid from `.local-run/processes.json`, or kill its logged pid) and confirm the shell shows the stale Live State Stream notice, then restart it (`./Start-LocalAcceptance.ps1 -SkipWebBuild`) and confirm it reconnects and catches up without a manual reload.
- [ ] Point `acceptance.settings.json`'s `worldBoundaryHealthEndpoint` at a stopped world (or stop your test world) and confirm the read-only banner appears while browsing/inventory activity keeps working and withdrawal creation is blocked with a clear message.
- [ ] Confirm no private account names, Withdrawal Token secrets, DAT paths, or operator secrets appear anywhere in the UI, browser console, or `.local-run/logs/`.

Automated coverage for the first six items above (plus the read-only/stale scenarios, reproduced
deterministically via request interception since a Playwright spec cannot itself restart the
launcher's own background processes) lives in `Source/ACE.Cloud.Web/e2e/`:

```powershell
cd Source/ACE.Cloud.Web
npm install --no-save @playwright/test
npx playwright install --with-deps chromium
$env:ACE_CLOUD_ACCEPTANCE_BASE_URL = "http://127.0.0.1:4173"   # match webUiPort in acceptance.settings.json
$env:ACE_ACCEPTANCE_MAIN_ACCOUNT_NAME = "..."                   # from acceptance.settings.json's testAccounts
$env:ACE_ACCEPTANCE_MAIN_ACCOUNT_PASSWORD = "..."
$env:ACE_ACCEPTANCE_LINKED_ACCOUNT_NAME = "..."
$env:ACE_ACCEPTANCE_LINKED_ACCOUNT_PASSWORD = "..."
npm run test:e2e
```

`@playwright/test` is deliberately not a committed `package.json`/lockfile dependency (see
`vite.config.ts`'s test `include` comment) -- it is test tooling installed on demand, not a permanent
addition to the shipped app.

## 5. Stop and clean up

```powershell
./Stop-LocalAcceptance.ps1          # stops the background AuthBridge/Backend/Worker/proxy processes
./Stop-LocalAcceptance.ps1 -Purge   # also removes the disposable acceptance MariaDB container and its volume
```

Cleanup is scoped strictly to resources this launcher created (its own background process PIDs
recorded in `.local-run/processes.json`, and the `ace-cloud-acceptance` Compose project's own
container/volume) -- it never stops, modifies, or deletes an existing ACE installation, its
`docker-compose.yml`/`ace-db`, or its `db-data`. Your separately started ACE test world is yours to
stop on your own.

## Troubleshooting

- **A prerequisite check fails**: the message names exactly what is missing and how to fix it; nothing
  is ever silently skipped.
- **Backend never becomes ready**: check `.local-run/logs/Backend.err.log`. A common cause is an
  unreachable `worldBoundaryHealthEndpoint` or a wrong `aceAuthConnectionString` -- confirm your test
  world is actually running first.
- **Port already in use**: change the conflicting port in `acceptance.settings.json` (a different
  ACE installation, or a previous unstopped acceptance run, is likely still using it -- try
  `./Stop-LocalAcceptance.ps1` first).
