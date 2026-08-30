# ACE.Cloud.Web

The AC Cloud Mule TypeScript/React client shell and AC-derived accessible design system. See
[CONTEXT.md](../../CONTEXT.md) and [IMPLEMENTATION-BRIEF.md](../../IMPLEMENTATION-BRIEF.md) at the
repository root for the product/domain contract this client implements.

## Commands

Run from this directory (`Source/ACE.Cloud.Web`):

```sh
npm ci          # install exact locked dependencies
npm test        # run the vitest suite (unit, accessibility, design-token, privacy-boundary tests)
npm run typecheck
npm run build   # typecheck + production build
npm run dev     # local dev server
```

## Layout

- `src/design-system/` — shared AC-derived tokens and accessible primitives (`Button`, `Dialog`,
  `Menu`, loading/empty/error/read-only states). Every fidelity/shell component must consume these
  tokens instead of hard-coding colors or spacing; `noHardcodedValues.test.ts` enforces this.
- `src/shell/` — the responsive `AppShell` (skip link, landmarks, responsive nav) and the root
  `ErrorBoundary`.
- `src/session/` — `SessionContext`, the client's auth/CSRF/service-availability state.
- `src/routes/` — route guards (`RequireAuth`, `RequireAdmin`, `RequireMainAccount`,
  `RequireWritableService`) that gate UI only; the Cloud backend remains the authorization
  authority and revalidates every sensitive request server-side.
- `src/api/` — the typed HTTP client and Live State Stream scaffold wired to the real Cloud
  backend endpoints (`AuthSessionEndpoints`, `CloudDiagnosticsEndpoints`).
- `src/public/` — surfaces that may render another account's identity; these must use Display
  Characters only and never a private ACE account name (`privacyBoundaries.test.tsx`).
