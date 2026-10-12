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

## Use a live AI backend (Vercel)
The copilot works with **no AI at all** (local retrieval, exact SOP text). To get conversational answers, give the server **one** AI key. Free tiers work.

| Provider | Cost | Key variable | Model variable (optional) |
|---|---|---|---|
| **Google Gemini** (recommended free option) | Free tier with daily limits | `GEMINI_API_KEY` | `GEMINI_MODEL` (default `gemini-flash-latest`) |
| **Groq** | Free tier with daily limits | `GROQ_API_KEY` | `GROQ_MODEL` (default `llama-3.3-70b-versatile`) |
| Anthropic Claude | Paid | `ANTHROPIC_API_KEY` | `ANTHROPIC_MODEL` (default `claude-opus-5-5`), `ANTHROPIC_EFFORT` (default `low`) |

If several keys are set, Gemini is used first, then Groq, then Claude; `LLM_PROVIDER=gemini|groq|claude` forces one. Free-tier providers may use prompts to improve their models: fine for these illustrative SOPs, not for confidential plant documents.

1. Get a key: Gemini at [aistudio.google.com](https://aistudio.google.com) → **Get API key**; Groq at [console.groq.com](https://console.groq.com) → **API Keys**.
2. In Vercel: **Add New → Project → Import** `Harshita-code31/plant-twin-prototype`. Framework Preset **Other**, Root Directory = repository root (not `web/copilot`, because `api/copilot.js` lives at the root). Leave build and output settings empty. `.vercelignore` keeps the Unity project out of the deployment.
3. **Settings → Environment Variables**: add the key (Production and Preview). `COPILOT_RATE_LIMIT_PER_MIN` (default 20 per visitor per instance) is optional. **Never** put a key in frontend files or GitHub.
4. Deploy, then open `https://YOUR-PROJECT.vercel.app/` (it redirects to `/web/copilot/`). The status chip shows **AI CONNECTED** when an answer came from the AI.
5. For local full-stack testing use `vercel dev` after `npm install`; the static Python server runs the local demo only.

### How answers stay grounded (`api/copilot.js`)
- **Retrieval decides the SOP**, using the same engine as the page (`retrieval.js`). No matching SOP → the fixed refusal, and no AI is called.
- **Missing values are never generated**: questions about setpoints or the LEL alarm limit get the fixed "not specified, ask the control room" answer, without calling the AI.
- **The AI sees only that SOP's sections** and must say which ones it used:
  - Gemini / Groq reply in JSON with the labels of the sections used (`[S1]`, ...). Every label must be one the server sent, at least one is required, and **every number in the answer must appear in those sections**, so a model cannot slip in a setpoint.
  - Claude uses the Messages API **citations** feature; the answer must cite at least one section.
  The returned sources are the sections actually used, and the answer carries inline markers such as `[SOP-GC-004 §1]`.
- **Anything else falls back to quoting the SOP**: a failed check, a refusal, a timeout, a rate limit or an API error all return the extractive answer (`mode: "retrieval-fallback"`), and errors are logged server-side only.
- `node --test api/copilot.test.js` checks all of the above with fake providers (no key, no network); it runs in CI.

## Add an SOP
Create `sops/my-sop.json` with `id`, `revision`, `label`, `title`, `scenario`, `keywords` and `sections` (each section: `number`, `heading`, `en`, `hi`). Add filename to `sops/index.json`. Also add a `require("../web/copilot/sops/my-sop.json")` entry to `api/copilot.js`, and an intent for it in `retrieval.js` (`ids` and `getIntent`), for backend retrieval. **Have a qualified SME review all content and thresholds before any operational usage.**

## Smoke tests
1. `कंप्रेसर ट्रिप हो गया, क्या करूँ?` → stop safely, first-out alarm, no blind restart, cite SOP-CP-002.
2. `What's the LEL alarm limit?` → site-specific, no numerical limit stated, cite SOP-GT-003 §2.
3. `Gas leak at the discharge flange, what first?` → alarm/control room, emergency response and evacuation, cite SOP-GC-004.
4. `What is today's cafeteria menu?` → mandatory refusal and **no** citations.

## Integration / cautions
Coordinate with Harshita on the training app's link destination. The `web/` training application was **not yet present on main when this branch was created**; don't replace or move colleagues' files. Frontend uses textContent (no HTML injection). It never stores secrets. Hindi query search is a small dictionary-based demo, **not** production-grade multilingual semantic retrieval. For production, add multilingual embeddings or secure query translation, stronger relevance gating, rate limits/auth, observability, SOP approval/versioning, and verified operational safety review.
