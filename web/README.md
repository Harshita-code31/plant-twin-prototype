# Anubhav web prototype

A phone-friendly browser prototype for PS2 (*Anubhav: compressing years of experience through
technology and behaviour*). It covers three of the four parts of the platform:

| Part | Where |
|---|---|
| 3D plant digital twin | The K-101 gas compressor skid in the browser (three.js), with live simulated readings, alarms, a gas cloud and flare. `js/twin.js`, `js/scenarios.js` |
| Experience Vault | Veteran decisions (what they noticed, what they chose, why) shown on the home page, as hints during a drill, and in the debrief. `veteran` in `js/scenarios.js` |
| Mentor, debrief and competency map | Debrief after every drill with a mentor discussion, notes and sign-off; competency map with time to competency. `js/app.js`, `js/competency.js` |
| SOP copilot | Built separately. Set `copilotUrl` in `js/config.js` and the "Ask SOP" buttons link to it. |

Everything works in English and Hindi.

## Drills

Each drill starts with a normal **walkdown**, where the trainee can spot and report early warning signs. The emergency
then starts without warning.

- **Gas leak at compressor discharge** (`SOP-GL-01`): raise the alarm, ESD, notice SDV-102 stuck part open and close it,
  blow down to flare, check the wind, evacuate upwind. Doing nothing, or skipping isolation, ends in a flash fire.
- **Compressor trip on high vibration** (`SOP-CT-02`): acknowledge, read the first-out, inform the control room, trace
  the cause to a choked lube oil filter, change over, reset, restart. Bypassing the trip seizes the bearing.

The procedures are **illustrative**, written for the prototype. Replace them with the plant's approved SOPs (the same
ones the copilot answers from) before any real training.

Scoring: early warning signs 15, SOP steps 60, response time against the veteran 25, minus unsafe actions and hints.
A forced failure caps the score at 40. Pass mark 80, with no critical step missed (same pass mark as the AbhyasAR app).

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

Drill attempts, mentor notes and sign-offs are saved in the browser's local storage on that device. The competency map
mixes your own attempts with rows marked **example**, which are generated sample data so the map can be read on day one.
Moving attempts to Supabase (the `supabase/` migrations from AbhyasAR already have workers and training attempts) is the
next step for a multi-device pilot.

## Swapping in real 3D models

Each tappable piece of equipment is a `THREE.Group` named after its equipment id (`compressor`, `discharge_valve`, ...).
To use a GLB from Sketchfab, Fab or an AI model generator, load it with three.js's `GLTFLoader` and add it to that group
in `js/twin.js` in place of the primitive shapes. Picking, labels and highlighting keep working.
