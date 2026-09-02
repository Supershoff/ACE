// Regression test for issue #39 / PR #157: the same-origin proxy (issue #34) must proxy every API
// prefix the Backend actually maps, including /transfer-offers, /sharing-grants, and
// /allegiance-vault which were missing from API_PREFIXES. Without this, those requests fell through
// to serveStatic and got 200 text/html SPA content instead of JSON, crashing Transfer/Sharing and
// silently breaking Allegiance in the local acceptance stack.
import assert from "node:assert/strict";
import { test } from "node:test";
import http from "node:http";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { API_PREFIXES, createProxyServer } from "./same-origin-proxy.mjs";

async function withStack(t, run) {
  const distDir = await fs.mkdtemp(path.join(os.tmpdir(), "ace-proxy-test-"));
  await fs.writeFile(path.join(distDir, "index.html"), "<html><body>SPA shell</body></html>");

  const backend = http.createServer((req, res) => {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ stub: true, path: req.url }));
  });
  await new Promise((resolve) => backend.listen(0, "127.0.0.1", resolve));
  const backendPort = backend.address().port;

  const proxy = createProxyServer({ distDir, backendOrigin: `http://127.0.0.1:${backendPort}` });
  await new Promise((resolve) => proxy.listen(0, "127.0.0.1", resolve));
  const proxyPort = proxy.address().port;

  t.after(async () => {
    await new Promise((resolve) => proxy.close(resolve));
    await new Promise((resolve) => backend.close(resolve));
    await fs.rm(distDir, { recursive: true, force: true });
  });

  return run(`http://127.0.0.1:${proxyPort}`);
}

for (const prefix of ["/transfer-offers", "/sharing-grants", "/allegiance-vault"]) {
  test(`${prefix} is in API_PREFIXES`, () => {
    assert.ok(
      API_PREFIXES.includes(prefix),
      `expected API_PREFIXES to include ${prefix}`,
    );
  });

  test(`GET ${prefix} with an API Accept header is proxied to the backend as JSON`, async (t) => {
    await withStack(t, async (origin) => {
      const response = await fetch(`${origin}${prefix}`, {
        headers: { accept: "application/json" },
      });
      assert.equal(response.status, 200);
      assert.equal(response.headers.get("content-type"), "application/json");
      const body = await response.json();
      assert.equal(body.stub, true);
      assert.equal(body.path, prefix);
    });
  });

  test(`GET ${prefix} with a browser navigation Accept header still serves the SPA shell`, async (t) => {
    await withStack(t, async (origin) => {
      const response = await fetch(`${origin}${prefix}`, {
        headers: { accept: "text/html,application/xhtml+xml" },
      });
      assert.equal(response.status, 200);
      assert.ok(response.headers.get("content-type").startsWith("text/html"));
      const body = await response.text();
      assert.match(body, /SPA shell/);
    });
  });
}

test("a sub-path like /allegiance-vault/contribute is also proxied", async (t) => {
  await withStack(t, async (origin) => {
    const response = await fetch(`${origin}/allegiance-vault/contribute`, {
      method: "POST",
      headers: { accept: "application/json", "content-type": "application/json" },
      body: "{}",
    });
    assert.equal(response.status, 200);
    const body = await response.json();
    assert.equal(body.path, "/allegiance-vault/contribute");
  });
});
