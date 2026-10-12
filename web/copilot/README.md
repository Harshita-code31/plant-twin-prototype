# ANUBHAV | Ask SOP (Krishna's copilot)
A mobile-first bilingual (English/Hindi) SOP-grounded training assistant. **All six procedures are illustrative and not approved for real plant operation.**

## What's implemented
- Static HTML/CSS/JS with no build step, designed for 400px phone screens and light/dark mode.
- Six JSON SOPs in `sops/`, each with an ID, revision, numbered sections, English/Hindi text and explicit non-approved label.
- Section-level keyword retrieval with Hindi-to-English query mapping for included scenarios; a local deterministic demo works without any API credentials.
- Tap-to-open source citations showing exact sections.
- Query params `?q=...&lang=hi&scenario=gas_leak` prefill the chat (scenario retained in textarea title). Use `web/copilot/` as the configured Ask SOP URL.
- Optional live Claude responses via `api/copilot.js`, a Vercel serverless function with the secret only on the server.

## Run the static demonstration locally
From the repository root:
```shell
python -m http.server 8000
```
Open `http://localhost:8000/web/copilot/`. The local demo retrieves sections and gives strictly extractive responses, not Claude-generated text. **Do not open with file://**: browsers block SOP JSON fetches.

## Use the live Claude backend (Vercel)
1. Import this repo into Vercel; Framework Preset: **Other**; Root Directory: repository root. Do **not** select `web/copilot` as root because `api/copilot.js` is at repository root.
2. Add the environment variable `ANTHROPIC_API_KEY` in Vercel project settings. Optionally set `ANTHROPIC_MODEL` to a model supported by your account. **Do not** put the key in frontend files or GitHub.
3. Deploy. Visit `https://YOUR-VERCEL-DOMAIN/web/copilot/`. Browser requests are made to the same origin at `/api/copilot`.
4. For local full-stack testing use `vercel dev` (after linking the Vercel project). Otherwise run static demo using Python.

If no API key is configured or the model call fails, the backend returns an extractive retrieval fallback. API errors are logged server-side, not shown to workers. No Supabase project is needed for this standalone prototype. If credentials/data are later stored in Supabase, create a **new** project; do not reuse AbhyasAR.

## Add an SOP
Create `sops/my-sop.json` with `id`, `revision`, `label`, `title`, `scenario`, `keywords` and `sections` (each section: `number`, `heading`, `en`, `hi`). Add filename to `sops/index.json`. Also add a `require("../web/copilot/sops/my-sop.json")` entry to `api/copilot.js` for backend retrieval. **Have a qualified SME review all content and thresholds before any operational usage.**

## Smoke tests
1. `कंप्रेसर ट्रिप हो गया, क्या करूँ?` → stop safely, first-out alarm, no blind restart, cite SOP-CP-002.
2. `What's the LEL alarm limit?` → site-specific, no numerical limit stated, cite SOP-GT-003 §2.
3. `Gas leak at the discharge flange, what first?` → alarm/control room, emergency response and evacuation, cite SOP-GC-004.
4. `What is today's cafeteria menu?` → mandatory refusal and **no** citations.

## Integration / cautions
Coordinate with Harshita on the training app's link destination. The `web/` training application was **not yet present on main when this branch was created**; don't replace or move colleagues' files. Frontend uses textContent (no HTML injection). It never stores secrets. Hindi query search is a small dictionary-based demo, **not** production-grade multilingual semantic retrieval. For production, add multilingual embeddings or secure query translation, stronger relevance gating, rate limits/auth, observability, SOP approval/versioning, and verified operational safety review.
