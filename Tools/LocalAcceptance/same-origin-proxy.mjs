#!/usr/bin/env node
// A minimal same-origin reverse proxy for the disposable local acceptance stack (issue #34). No new
// npm dependency: Source/ACE.Cloud.Web's http client uses relative paths expecting the SPA and the
// Cloud backend API to share one origin, and several API paths (e.g. `GET /activity`) intentionally
// share their exact path with a client-routed SPA page -- a real browser navigation and this app's
// own `fetch()` calls are told apart the same way a production reverse proxy would: by the `Accept`
// header (a navigation sends `text/html`; this app's httpClient.ts never does).
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const distDir = path.resolve(__dirname, "../../Source/ACE.Cloud.Web/dist");

const proxyPort = Number(process.env.ACE_CLOUD_ACCEPTANCE_WEB_UI_PORT ?? 4173);
const backendPort = Number(process.env.ACE_CLOUD_ACCEPTANCE_BACKEND_PORT ?? 5280);
const backendOrigin = `http://127.0.0.1:${backendPort}`;

// Mirrors every route actually mapped in Source/ACE.Cloud.Backend (AuthSessionEndpoints,
// AccountIdentityEndpoints, CloudInventoryEndpoints, CloudActivityLedgerEndpoints,
// CloudWithdrawalEndpoints, CloudNotificationEndpoints, CloudLiveStreamEndpoints,
// ACE.Cloud.Hosting/CloudDiagnosticsEndpoints). Update this list if Backend gains a new prefix.
const API_PREFIXES = [
  "/auth",
  "/admin",
  "/notifications",
  "/live-stream",
  "/inventory",
  "/activity",
  "/withdrawal-locations",
  "/withdrawals",
  "/account",
  "/health",
  "/version",
];

const MIME_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".json": "application/json; charset=utf-8",
  ".ico": "image/x-icon",
  ".png": "image/png",
  ".woff2": "font/woff2",
};

function isApiRequest(req) {
  const accept = req.headers.accept ?? "";
  if (accept.includes("text/html")) {
    return false;
  }
  const { pathname } = new URL(req.url, "http://localhost");
  return API_PREFIXES.some((prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`));
}

function proxyToBackend(req, res) {
  const target = new URL(req.url, backendOrigin);
  const proxyReq = http.request(target, { method: req.method, headers: { ...req.headers, host: target.host } }, (proxyRes) => {
    res.writeHead(proxyRes.statusCode ?? 502, proxyRes.headers);
    proxyRes.pipe(res);
  });
  proxyReq.on("error", (error) => {
    res.writeHead(502, { "content-type": "text/plain" });
    res.end(`Bad gateway: ACE.Cloud.Backend is not reachable at ${backendOrigin} (${error.message}).`);
  });
  req.pipe(proxyReq);
}

function serveStatic(req, res) {
  const { pathname } = new URL(req.url, "http://localhost");
  let filePath = path.join(distDir, decodeURIComponent(pathname));
  if (pathname === "/" || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    filePath = path.join(distDir, "index.html");
  }
  fs.readFile(filePath, (error, content) => {
    if (error) {
      res.writeHead(404, { "content-type": "text/plain" });
      res.end("Not found. Did you run `npm run build` in Source/ACE.Cloud.Web first?");
      return;
    }
    res.writeHead(200, { "content-type": MIME_TYPES[path.extname(filePath)] ?? "application/octet-stream" });
    res.end(content);
  });
}

if (!fs.existsSync(distDir)) {
  console.error(`Not found: ${distDir}. Run "npm run build" in Source/ACE.Cloud.Web before starting the proxy.`);
  process.exit(1);
}

const server = http.createServer((req, res) => {
  if (isApiRequest(req)) {
    proxyToBackend(req, res);
    return;
  }
  serveStatic(req, res);
});

server.listen(proxyPort, "127.0.0.1", () => {
  console.log(`Same-origin local proxy listening at http://127.0.0.1:${proxyPort}`);
  console.log(`  -> static files from ${distDir}`);
  console.log(`  -> API requests proxied to ${backendOrigin}`);
});
