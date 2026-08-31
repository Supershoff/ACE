# Local Acceptance Runbook (issue #34)

A reproducible, **local-only, disposable** environment for the Phase 5 inventory vertical slice
(Activity Ledger, Notification Center, and the resumable Live State Stream). It runs on an operator's
Windows development machine, creates a disposable `ace_cloud` schema beside the disposable test world's
existing `ace_shard` schema, and connects
to a **separately started ACE test world** built from this PR branch through explicit configuration.
It never requires public hosting, never bootstraps a production installation, and never touches an
existing ACE installation or its databases.

## What this is not

- Not the "Operator Bootstrap" production command (`CONTEXT.md`) -- it does not create schemas,
  restricted database identities, or secrets for a real deployment.
- Not a way to start or configure the ACE world server itself, unless you explicitly opt into
  `aceServerProjectPath` (step 4) -- and even then it only starts/restarts that process, never edits
  its `Config.js`.

## Co-located database topology

This launcher manages exactly **one** disposable schema: `ace_cloud`, on the same local MySQL/MariaDB
instance as the disposable test world's `ace_shard`. Co-location is mandatory: the custody boundary
uses database triggers in both schemas so world possession and Cloud custody can never coexist.
Putting `ace_cloud` in a separate container would make those invariants impossible to exercise.

Your **existing disposable** `ace_auth`, `ace_shard`, and `ace_world` schemas stay in place. The
launcher validates all three, creates/migrates only `ace_cloud`, and installs/removes only the named
Cloud Mule custody triggers in `ace_shard`. Never point this acceptance launcher at a production world.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Node.js 20+](https://nodejs.org/) (npm ships with it).
- PowerShell 7+ (`pwsh`). Run `Test-Prerequisites.ps1` any time to check all of the above plus free
  ports and `acceptance.settings.json`'s shape -- it reports every problem it finds rather than
  skipping silently. (It does *not* check ACE's liveness -- see phase 2 below for why.)

## 1. Start a disposable ACE test world

Build and run your own ACE server **from this PR branch**, separately from this launcher (e.g. via the
repository's own `docker-compose.yml`/`Dockerfile`, or `dotnet run --project Source/ACE.Server`), with
its own `ace_auth`/`ace_shard`/`ace_world` databases already imported. Create at least one synthetic
test account on it (never a real player's account) for `testAccounts` in step 2, and, if you want
account-linking coverage, a second synthetic account to link to it.

You do **not** need `CloudMule.Enabled = true` yet -- ace_cloud does not exist until step 3, and a
Cloud-enabled ACE process needs that database and its `CloudShardBinding` row to already exist to do
anything useful. Confirm the client can connect to your test world normally first.

## 2. Configure this launcher

```powershell
Copy-Item acceptance.settings.example.json acceptance.settings.json
```

Edit `acceptance.settings.json` (git-ignored -- it holds your local, disposable secrets) and replace
every `CHANGE_ME` value: local ports, a restricted `ace_cloud` runtime user/password, your
test world's `ace_auth`/`ace_shard`/`ace_world` connection strings, a shard ID, a service-key ID/secret
pair matching what your test world's ACE-side extension expects, and your synthetic test accounts.
Leave `aceServerProjectPath` blank unless you want this launcher to manage restarting your ACE process
for you (see step 4).

## 3. Prepare the disposable ace_cloud database (phase 1)

```powershell
cd Tools/LocalAcceptance
./Prepare-LocalAcceptanceCloudDatabase.ps1
```

This, in order:

1. Runs `Test-Prerequisites.ps1` and stops with a specific, actionable diagnostic if anything is missing.
2. Validates (read-only) that your existing `ace_auth`, `ace_shard`, and `ace_world` databases are reachable, without touching them.
3. Creates `ace_cloud` beside `ace_shard`, creates/updates the non-root runtime identity, and grants it access only to `ace_cloud`.
4. Applies Cloud schema migrations with the local `aceShardConnectionString` admin identity (`ACE.Cloud.LocalAcceptanceMigrator`, wrapping the same `CloudSchemaMigrator` the integration tests already exercise), including the required cross-schema custody triggers.
5. Idempotently bootstraps (or strictly validates -- never overwrites a different existing one) the mandatory singleton `CloudShardBinding` row, using `acceptance.settings.json`'s `shardId`, `cloudAceExtensionVersion`, and `cloudContractProtocolVersion`.

Safe to re-run any time -- every step here is idempotent.

## 4. Start (or restart) your Cloud-enabled ACE process

Now that `ace_cloud` and its `CloudShardBinding` exist, (re)start your ACE test world with:

- `CloudMule.Enabled = true`
- `CloudMule.ShardId` matching `acceptance.settings.json`'s `shardId`
- `CloudMule.CustodianBaseWeenieClassId` set to a real Vendor-type weenie in your `ace_world`
- `MySql.Cloud` pointing at the co-located `ace_cloud` database using an **ACE world-boundary database identity** that can read/write both `ace_cloud` and `ace_shard` (for this disposable world, the identity already used by `MySql.Shard` is sufficient). Do not use the restricted `dbUser`; it is for companion services only.
- `CloudMule.HealthEndpoint.Enabled = true` (the default), bound/ported to match `acceptance.settings.json`'s `worldBoundaryHealthEndpoint`

You can do this manually (your own `dotnet run --project Source/ACE.Server`, or however you normally
run it), **or** set `acceptance.settings.json`'s `aceServerProjectPath` to that project's path (e.g.
`..\..\Source\ACE.Server`) once its `Config.js` is configured as above, and let
`Start-LocalAcceptance.ps1` (step 5) start/restart it for you as a managed background process -- it
only runs `dotnet run` against that path; it never edits `Config.js` itself, and it is never used
against a non-disposable ACE instance.

## 5. Start the rest of the stack (phase 2)

```powershell
./Start-LocalAcceptance.ps1
```

This, in order:

1. Re-runs `Prepare-LocalAcceptanceCloudDatabase.ps1` (idempotent; skip with `-SkipPrepare` if you already ran it this session).
2. If `aceServerProjectPath` is set, starts/restarts that ACE.Server process as a managed background process.
3. Waits for ACE's real `worldBoundaryHealthEndpoint` (its own `CloudWorldBoundaryHealthHost`, not a fake listener) to report live, then checks `/cloudmule/deposit-readiness` on the same origin and reports every remaining blocker -- ShardId mismatch, a missing/mismatched `CloudShardBinding`, a missing or non-Vendor Custodian weenie, or zero resolved Custodian locations -- before doing anything else.
4. Starts the ACE Auth Bridge, `ACE.Cloud.Backend`, and `ACE.Cloud.Worker` as background processes (logs under `.local-run/logs/`), with their shared protected asset storage under the git-ignored `.local-run/protected-assets/` directory.
5. Waits for the Backend's `/health/ready` to report ready.
6. Builds the web client (`npm run build`; skip with `-SkipWebBuild` to reuse a previous build) and starts `same-origin-proxy.mjs`, a small dependency-free Node proxy that serves the built web client and proxies its API calls to the Backend from one origin (matching how the app's `fetch()` calls -- and its cookie-based session -- expect to work).
7. Prints the local URL once the proxy itself reports ready.

## 6. Run the human acceptance checklist

Open the printed URL and, using your synthetic test account(s):

- [ ] Log in, and confirm the Notification Center and Activity link appear.
- [ ] Browse the Mule Page grid and spreadsheet view; confirm icons render and reflow at a narrow width without changing page membership.
- [ ] Open a Full Cloud Appraisal for an item.
- [ ] Deposit an item from your ACE Custodian and confirm it appears in your live inventory.
- [ ] Create a Withdrawal Token for at least one item; confirm it appears once in the Activity Ledger.
- [ ] In a second tab logged into the same account, confirm the new Activity Ledger entry appears **without reloading** (the Live State Stream).
- [ ] Open the Notification Center; confirm it opens/closes accessibly and the unread badge behaves.
- [ ] Resize to a narrow/mobile viewport; confirm navigation and every above action still works.
- [ ] Stop `ACE.Cloud.Backend` (e.g. `Stop-Process` on its pid from `.local-run/processes.json`, or kill its logged pid) and confirm the shell shows the stale Live State Stream notice, then restart it (`./Start-LocalAcceptance.ps1 -SkipWebBuild -SkipPrepare`) and confirm it reconnects and catches up without a manual reload.
- [ ] Point `acceptance.settings.json`'s `worldBoundaryHealthEndpoint` at a stopped world (or stop your test world) and confirm the read-only banner appears while browsing/inventory activity keeps working and withdrawal creation is blocked with a clear message.
- [ ] Withdraw an item and confirm it returns to your ACE inventory.
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

## 7. Stop and clean up

```powershell
./Stop-LocalAcceptance.ps1          # stops the background AuthBridge/Backend/Worker/proxy (and managed AceServer, if used) processes
./Stop-LocalAcceptance.ps1 -Purge   # also removes ace_cloud and the known Cloud Mule triggers from ace_shard
```

Cleanup is scoped strictly to resources this launcher created (its own background process PIDs,
the `ace_cloud` schema, and the five named Cloud Mule custody triggers in `ace_shard`) -- it never
drops or purges `ace_auth`, `ace_shard`, or `ace_world`. If you are managing your own separately started ACE test world (i.e.
`aceServerProjectPath` is blank), that process is yours to stop on your own.

## Troubleshooting

- **A prerequisite check fails**: the message names exactly what is missing and how to fix it; nothing
  is ever silently skipped.
- **`Prepare-LocalAcceptanceCloudDatabase.ps1` refuses to bootstrap the CloudShardBinding**: an
  existing row in `ace_cloud` already has a different ShardId or version than requested. This tool
  never overwrites it -- either point `acceptance.settings.json` at a fresh disposable `ace_cloud`
  database, or fix the mismatched setting.
- **`Test-AceWorldReadiness.ps1` (called from `Start-LocalAcceptance.ps1`) times out waiting for
  liveness**: confirm your ACE process actually restarted with `CloudMule.Enabled = true` and its
  `CloudMule.HealthEndpoint` bound to the address/port `worldBoundaryHealthEndpoint` expects.
- **`Test-AceWorldReadiness.ps1` reports a deposit-readiness problem**: it names the exact missing
  prerequisite (ShardId mismatch, missing/mismatched CloudShardBinding, a missing or non-Vendor
  Custodian weenie, or zero resolved Custodian locations) -- fix that one thing in ACE's `Config.js`
  or the admin Custodian configuration and restart ACE.
- **Backend never becomes ready**: check `.local-run/logs/Backend.err.log`. A common cause is an
  unreachable `worldBoundaryHealthEndpoint` or a wrong `aceAuthConnectionString` -- confirm your test
  world is actually running first.
- **Port already in use**: change the conflicting web/backend/worker/auth-bridge port in
  `acceptance.settings.json`; the existing database port is expected to be in use by the disposable
  ACE test world's MySQL/MariaDB instance.
