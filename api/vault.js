"use strict";
// Experience Vault storage (Vercel serverless function).
//   GET  /api/vault -> { entries, shared }
//   POST /api/vault -> { entry }   body: { name, years, role, event, situation, noticed, did, why, advice }
//
// Storage is Upstash Redis over its REST API (free tier; add it from the Vercel Marketplace and
// Vercel sets the variables). Supported variable names: UPSTASH_REDIS_REST_URL + UPSTASH_REDIS_REST_TOKEN,
// or KV_REST_API_URL + KV_REST_API_TOKEN. Without them, GET returns no entries with shared: false and
// POST answers 503, and the page keeps entries on the device instead.
//
// Entries are shown as "not yet reviewed": they are first-hand accounts, not approved procedures.

const KEY = "anubhav:vault:entries";
const LIMIT = 300;
const EVENTS = new Set(["gas_leak", "compressor_trip", "other"]);
const FIELDS = { name: 80, years: 3, role: 80, event: 40, situation: 1200, noticed: 1200, did: 1200, why: 1200, advice: 600 };
const REQUIRED = ["name", "event", "noticed", "did", "why"];

const RATE = { windowMs: 60 * 60 * 1000, max: Number(process.env.VAULT_POSTS_PER_HOUR || 10) };
const recent = new Map();

function storage() {
  const url = process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL;
  const token = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;
  return url && token ? { url: url.replace(/\/$/, ""), token } : null;
}

async function redis(cmd) {
  const s = storage();
  const res = await handler.fetch(s.url, {
    method: "POST",
    headers: { authorization: `Bearer ${s.token}`, "content-type": "application/json" },
    body: JSON.stringify(cmd),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok || data.error) throw new Error(data.error || `HTTP ${res.status}`);
  return data.result;
}

function visitor(req) {
  const fwd = req.headers?.["x-forwarded-for"];
  return (typeof fwd === "string" ? fwd.split(",")[0].trim() : "") || req.socket?.remoteAddress || "unknown";
}

function limited(req) {
  const ip = visitor(req);
  const now = Date.now();
  const hits = (recent.get(ip) || []).filter((t) => now - t < RATE.windowMs);
  hits.push(now);
  recent.set(ip, hits);
  if (recent.size > 5000) recent.clear();
  return hits.length > RATE.max;
}

function validate(body) {
  if (!body || typeof body !== "object") return { error: "Send the entry as JSON." };
  const entry = {};
  for (const [f, max] of Object.entries(FIELDS)) {
    const v = body[f] == null ? "" : String(body[f]).trim();
    if (v.length > max) return { error: `"${f}" is too long (at most ${max} characters).` };
    entry[f] = v;
  }
  const missing = REQUIRED.filter((f) => !entry[f]);
  if (missing.length) return { error: `Please fill in: ${missing.join(", ")}.` };
  if (!EVENTS.has(entry.event)) return { error: "Unknown event type." };
  if (entry.years && !/^\d{1,2}$/.test(entry.years)) return { error: "Years should be a number." };
  return { entry };
}

function newId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

async function handler(req, res) {
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("X-Content-Type-Options", "nosniff");
  const s = storage();

  if (req.method === "GET") {
    if (!s) return res.status(200).json({ entries: [], shared: false });
    try {
      const raw = (await redis(["LRANGE", KEY, "0", String(LIMIT - 1)])) || [];
      const entries = raw.map((x) => {
        try {
          return JSON.parse(x);
        } catch {
          return null;
        }
      }).filter(Boolean);
      return res.status(200).json({ entries, shared: true });
    } catch (e) {
      console.error("Vault: read failed:", e.message);
      return res.status(502).json({ error: "The vault could not be read. Try again shortly." });
    }
  }

  if (req.method === "POST") {
    if (!s) return res.status(503).json({ error: "Shared storage is not connected yet." });
    if (limited(req)) {
      res.setHeader("Retry-After", "3600");
      return res.status(429).json({ error: "Too many entries from this connection. Try again later." });
    }
    let body = req.body;
    if (typeof body === "string") {
      try {
        body = JSON.parse(body);
      } catch {
        return res.status(400).json({ error: "Send the entry as JSON." });
      }
    }
    const { entry, error } = validate(body);
    if (error) return res.status(400).json({ error });
    const stored = { ...entry, id: newId(), createdAt: new Date().toISOString(), reviewed: false };
    try {
      await redis(["LPUSH", KEY, JSON.stringify(stored)]);
      await redis(["LTRIM", KEY, "0", String(LIMIT - 1)]);
      return res.status(201).json({ entry: stored });
    } catch (e) {
      console.error("Vault: write failed:", e.message);
      return res.status(502).json({ error: "The entry could not be saved. Try again shortly." });
    }
  }

  res.setHeader("Allow", "GET, POST");
  return res.status(405).json({ error: "Method not allowed" });
}

handler.fetch = (...args) => fetch(...args);
module.exports = handler;
