// Experience Vault entries: what veterans noticed, did and why, in their own words.
// Shared through /api/vault when the deployment has storage connected; otherwise kept on this
// device and marked as such, so nothing is lost while storage is being set up.

import { load, save, newId } from "./store.js";

const LOCAL_KEY = "vault.local";

export const FIELDS = ["name", "years", "role", "event", "situation", "noticed", "did", "why", "advice"];
export const REQUIRED = ["name", "event", "noticed", "did", "why"];
export const MAX = { name: 80, years: 3, role: 80, event: 40, situation: 1200, noticed: 1200, did: 1200, why: 1200, advice: 600 };

let shared = null; // null = unknown, true = server storage works, false = local only

function localEntries() {
  return load(LOCAL_KEY, []);
}

/** Loads entries: shared ones from the server (if available) plus any saved only on this device. */
export async function listEntries() {
  let server = [];
  try {
    const res = await fetch("/api/vault", { headers: { accept: "application/json" } });
    if (res.ok) {
      const data = await res.json();
      server = Array.isArray(data.entries) ? data.entries : [];
      shared = data.shared !== false;
    } else {
      shared = false;
    }
  } catch {
    shared = false;
  }
  const local = localEntries().map((e) => ({ ...e, localOnly: true }));
  return [...local, ...server].sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));
}

export function isShared() {
  return shared === true;
}

export function clean(entry) {
  const out = {};
  for (const f of FIELDS) {
    const v = entry[f] == null ? "" : String(entry[f]).trim();
    out[f] = v.slice(0, MAX[f]);
  }
  return out;
}

export function missing(entry) {
  return REQUIRED.filter((f) => !entry[f]);
}

/** Saves an entry to the server, or to this device if shared storage is not available. */
export async function addEntry(raw) {
  const entry = clean(raw);
  try {
    const res = await fetch("/api/vault", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(entry),
    });
    if (res.ok) {
      shared = true;
      return { saved: "shared", entry: (await res.json()).entry };
    }
  } catch {
    /* fall through to local */
  }
  const local = { ...entry, id: newId(), createdAt: new Date().toISOString() };
  const list = localEntries();
  list.push(local);
  save(LOCAL_KEY, list);
  shared = false;
  return { saved: "local", entry: { ...local, localOnly: true } };
}
