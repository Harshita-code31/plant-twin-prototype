// Per-device storage for the prototype. Every read and write is guarded: private windows and
// some embedded viewers refuse localStorage, and the app must still work (it just forgets).

const PREFIX = "anubhav.";

export function load(key, fallback) {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw == null ? fallback : JSON.parse(raw);
  } catch {
    return fallback;
  }
}

export function save(key, value) {
  try {
    localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    /* storage unavailable: keep going in memory */
  }
}

const memory = { attempts: null, signoffs: null };

export function getAttempts() {
  if (!memory.attempts) memory.attempts = load("attempts", []);
  return memory.attempts;
}

export function addAttempt(attempt) {
  const list = getAttempts();
  list.push(attempt);
  save("attempts", list);
}

export function getAttempt(id) {
  return getAttempts().find((a) => a.id === id) || null;
}

export function updateAttempt(id, patch) {
  const list = getAttempts();
  const a = list.find((x) => x.id === id);
  if (!a) return;
  Object.assign(a, patch);
  save("attempts", list);
}

export function newId() {
  if (globalThis.crypto?.randomUUID) return crypto.randomUUID();
  return "a" + Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}
