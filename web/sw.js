// Offline support for the installed app (also what PWABuilder needs to package it as an APK).
// Static files are served from the cache and refreshed in the background, so a new deployment
// shows up on the next launch. The copilot API is never cached: it needs the network, and the
// copilot page falls back to quoting the SOP when offline.
const CACHE = "anubhav-v2";
const SHELL = [
  "./",
  "index.html",
  "styles.css",
  "manifest.webmanifest",
  "js/app.js",
  "js/competency.js",
  "js/config.js",
  "js/drill.js",
  "js/i18n.js",
  "js/scenarios.js",
  "js/store.js",
  "js/twin.js",
  "js/sop.js",
  "js/vault.js",
  "vendor/three/three.module.min.js",
  "vendor/three/addons/OrbitControls.js",
  "vendor/three/addons/loaders/GLTFLoader.js",
  "vendor/three/addons/utils/BufferGeometryUtils.js",
  "models/booster_skid.glb",
  "icons/icon-192.png",
  "icons/icon-512.png",
  "copilot/",
  "copilot/index.html",
  "copilot/app.js",
  "copilot/retrieval.js",
  "copilot/styles.css",
  "copilot/sources.json",
  "copilot/sops/index.json",
  "copilot/sops/gas-leak.json",
  "copilot/sops/compressor-trip.json",
  "copilot/sops/permit-to-work.json",
  "copilot/sops/gas-testing.json",
  "copilot/sops/ppe.json",
  "copilot/sops/emergency-communications.json",
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) =>
      // One missing file must not block installation.
      Promise.all(SHELL.map((url) => cache.add(url).catch(() => null))),
    ).then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const req = event.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.pathname.startsWith("/api/")) return;
  const sameOrigin = url.origin === self.location.origin;
  const fonts = url.hostname === "fonts.googleapis.com" || url.hostname === "fonts.gstatic.com";
  if (!sameOrigin && !fonts) return;

  event.respondWith(
    caches.open(CACHE).then(async (cache) => {
      const cached = await cache.match(req, { ignoreSearch: req.mode === "navigate" });
      const fresh = fetch(req)
        .then((res) => {
          if (res && (res.ok || res.type === "opaque")) cache.put(req, res.clone());
          return res;
        })
        .catch(() => null);
      if (cached) {
        event.waitUntil(fresh);
        return cached;
      }
      const res = await fresh;
      return res || new Response("Offline and not cached yet.", { status: 503, headers: { "content-type": "text/plain" } });
    }),
  );
});
