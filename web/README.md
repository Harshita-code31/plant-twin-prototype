# Anubhav web prototype

**Live:** https://plant-twin-prototype.vercel.app/web/ (deployed from `main` on Vercel; `/` redirects here).

A phone-friendly browser prototype for PS2 (*Anubhav: compressing years of experience through
technology and behaviour*). The first page is the digital twin; the main goal is a place where veterans record what
they know, and new operators learn the SOP steps before they are tested on them.

| Part | Where |
|---|---|
| 3D plant digital twin | The K-101 gas compressor skid from the CAD drawing (three.js). Tap equipment to see what it is and which SOP steps use it. `js/twin.js` |
| Scenarios: learn, practise, debrief | **Learn** walks through each SOP step on the twin, quoting the SOP text and why the step matters. **Practise** is the drill: a walkdown, then the emergency without warning. **Debrief** goes through each step with the SOP reference and a mentor sign-off. `js/app.js`, `js/scenarios.js`, `js/drill.js` |
| Experience Vault | An open form for people who have handled a real leak, trip or other event: what they noticed, what they did and why, under their own name. Entries show in the lessons and debrief for that event, marked "Not yet reviewed". `js/vault.js`, `api/vault.js` |
| SOP copilot | `web/copilot/` (see its README). Lessons and drills quote the same SOP files the copilot answers from (`copilot/sops/`). |
| Progress | Skills, time to competency and spaced practice (next drill due 1, 3, 7, 14, then 30 days after a pass; the next day after a fail). `js/competency.js` |

Everything works in English and Hindi. Nothing in the app is invented: there are no sample people, sample entries or
example cohort data. Until a veteran adds an entry, the Vault shows an invitation instead.

## Drills

Each drill starts with a normal **walkdown**, where the trainee can spot and report early warning signs. The emergency
then starts without warning. The trainee is the authorised panel operator for K-101.

- **Gas leak at compressor discharge** (`SOP-GC-004`): raise the alarm, ESD, close SDV-102, blow down to flare, check
  the wind, evacuate upwind. Doing nothing, or skipping isolation, ends in a flash fire.
- **Compressor trip on high vibration** (`SOP-CP-002`): acknowledge, read the first-out, inform the control room, check
  the lube oil filter, change over, reset, restart. Bypassing the trip seizes the bearing.

Each step names the SOP section it comes from. The SOPs are **illustrative samples** written for the prototype. Replace
the files in `copilot/sops/` with the plant's approved SOPs before any real training; lessons, drills and the copilot
all read from them.

Scoring: early warning signs 20 (minus 4 per false report), SOP steps 80 (minus 5 per step out of order), minus each unsafe
action's penalty and 3 per hint. A forced failure caps the score at 40. Pass mark 80. A skill is competent after two
passes in a row.

## Running it

No build step. Serve the `web/` folder with any static server and open it on a phone or desktop:

```sh
cd web
python3 -m http.server 8000
# open http://localhost:8000
```

ES modules need a server; opening `index.html` straight from disk will not work. three.js 0.160.0 is vendored in
`vendor/three/` (MIT licence included), so the app has no runtime CDN dependency apart from Google Fonts.

## Data

Drill attempts, mentor notes and sign-offs are saved in the browser's local storage on that device.

Experience Vault entries are shared through `api/vault.js`, which stores them in Redis. On Vercel, add **Upstash Redis**
from the Marketplace (Storage tab) and connect it to the project; it sets `KV_REST_API_URL` and `KV_REST_API_TOKEN`
(or set `UPSTASH_REDIS_REST_URL` and `UPSTASH_REDIS_REST_TOKEN` yourself). Without storage, entries are kept on the
device that wrote them and the app says so. New entries are saved with `reviewed: false`; a reviewer step is the next
thing to build before entries are trusted for training. The API accepts at most 10 entries per hour from one address.

## The 3D model

`models/booster_skid.glb` is the fuel gas booster compressor skid, converted from the source IGES via FreeCAD's glTF
export and reduced with [gltf-transform](https://gltf-transform.dev/) (flatten, join by material, weld, simplify to
~12%, quantize): 1.96 M triangles in 13,063 pieces became ~247 k triangles in 27 meshes, 5.2 MB.

```sh
npx @gltf-transform/cli@4.1.1 flatten compressor.gltf s1.glb
npx @gltf-transform/cli@4.1.1 join s1.glb s2.glb
npx @gltf-transform/cli@4.1.1 weld s2.glb s3.glb
npx @gltf-transform/cli@4.1.1 simplify s3.glb s4.glb --ratio 0.12 --error 0.0008
npx @gltf-transform/cli@4.1.1 prune s4.glb s5.glb
npx @gltf-transform/cli@4.1.1 quantize s5.glb booster_skid.glb
```

The CAD has no part names, so tappable equipment is defined by hit boxes in metres (`MODEL_TARGETS` in `js/twin.js`),
along with the leak point, stack tip and gas detector position (`MODEL_POINTS`). `MODEL_ROTATION_X` and `MODEL_OFFSET`
stand the Z-down CAD upright and centre it. If the model fails to load, `buildPrimitiveSkid()` draws a stand-in so the
drills still run.

## Android app (APK)

The web app is installable (`manifest.webmanifest`, icons in `icons/`) and works offline once
loaded (`sw.js` caches the app, the 3D model and the copilot's SOPs; the copilot's AI needs the
network and otherwise quotes the SOP). To package it for Android:

1. Open [pwabuilder.com](https://www.pwabuilder.com), enter `https://plant-twin-prototype.vercel.app/web/` and start.
2. **Package for stores → Android → Generate package** (package ID e.g. `app.vercel.plant_twin_prototype`).
3. The download contains a signed `.apk` for installing directly and an `.aab` for the Play Store,
   plus `assetlinks.json` and a signing key. **Keep the key file**: updates must be signed with it.
4. Optional: to hide the browser address bar inside the app, commit the generated `assetlinks.json`
   at the repository root as `.well-known/assetlinks.json`, so it is served at
   `https://plant-twin-prototype.vercel.app/.well-known/assetlinks.json`.

On a phone you can also skip the APK: open the site in Chrome and choose **Add to Home screen / Install app**.
