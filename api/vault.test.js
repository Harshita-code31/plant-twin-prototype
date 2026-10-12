"use strict";
// Tests for the Experience Vault API with a fake Redis (no network). Run: node --test api/vault.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const handler = require("./vault.js");

const realFetch = handler.fetch;
let ipCount = 0;

function res() {
  return {
    statusCode: 200,
    headers: {},
    setHeader(k, v) { this.headers[k] = v; },
    status(n) { this.statusCode = n; return this; },
    json(body) { return { status: this.statusCode, body }; },
  };
}
const call = (method, body, ip = `192.0.2.${++ipCount}`) => handler({ method, body, headers: { "x-forwarded-for": ip } }, res());

function withRedis(fn) {
  return async () => {
    const list = [];
    const calls = [];
    process.env.UPSTASH_REDIS_REST_URL = "https://redis.test";
    process.env.UPSTASH_REDIS_REST_TOKEN = "secret-token";
    handler.fetch = async (url, init) => {
      const cmd = JSON.parse(init.body);
      calls.push({ url, init, cmd });
      let result = "OK";
      if (cmd[0] === "LPUSH") { list.unshift(cmd[2]); result = list.length; }
      if (cmd[0] === "LRANGE") result = list.slice(Number(cmd[2]), Number(cmd[3]) + 1);
      return { ok: true, status: 200, json: async () => ({ result }) };
    };
    try {
      await fn({ list, calls });
    } finally {
      delete process.env.UPSTASH_REDIS_REST_URL;
      delete process.env.UPSTASH_REDIS_REST_TOKEN;
      handler.fetch = realFetch;
    }
  };
}

const good = { name: "R. K. Sharma", years: "26", role: "Shift in-charge", event: "gas_leak", noticed: "Frost on a flange", did: "Raised the alarm, pressed ESD", why: "Gas expands and cools at a leak" };

test("without storage: GET returns no entries and shared: false; POST is 503", async () => {
  delete process.env.UPSTASH_REDIS_REST_URL;
  delete process.env.KV_REST_API_URL;
  const g = await call("GET");
  assert.equal(g.status, 200);
  assert.deepEqual(g.body, { entries: [], shared: false });
  assert.equal((await call("POST", good)).status, 503);
});

test("saves an entry, marks it not reviewed, and lists it back", withRedis(async ({ calls }) => {
  const p = await call("POST", good);
  assert.equal(p.status, 201);
  assert.equal(p.body.entry.reviewed, false);
  assert.ok(p.body.entry.id && p.body.entry.createdAt);
  assert.equal(calls[0].init.headers.authorization, "Bearer secret-token");
  const g = await call("GET");
  assert.equal(g.body.shared, true);
  assert.equal(g.body.entries.length, 1);
  assert.equal(g.body.entries[0].name, "R. K. Sharma");
}));

test("rejects missing required fields, unknown events, long text and bad years", withRedis(async () => {
  assert.equal((await call("POST", { ...good, why: "" })).status, 400);
  assert.equal((await call("POST", { ...good, event: "party" })).status, 400);
  assert.equal((await call("POST", { ...good, noticed: "x".repeat(1201) })).status, 400);
  assert.equal((await call("POST", { ...good, years: "lots" })).status, 400);
  assert.equal((await call("POST", "{not json")).status, 400);
}));

test("rate limits posts from one connection", withRedis(async () => {
  let last;
  for (let i = 0; i < 11; i++) last = await call("POST", good, "198.51.100.77");
  assert.equal(last.status, 429);
}));

test("storage errors return 502, not a crash", withRedis(async () => {
  handler.fetch = async () => ({ ok: false, status: 500, json: async () => ({ error: "down" }) });
  assert.equal((await call("GET")).status, 502);
  assert.equal((await call("POST", good)).status, 502);
}));

test("other methods are 405", async () => {
  assert.equal((await call("DELETE")).status, 405);
});
