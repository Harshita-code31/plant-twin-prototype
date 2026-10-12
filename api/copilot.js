"use strict";
// ANUBHAV SOP copilot backend (Vercel serverless function, POST /api/copilot).
//
// Grounding rules, enforced in code rather than only in the prompt:
//   1. Retrieval picks the SOP (the same engine the page uses, web/copilot/retrieval.js).
//      No matching SOP -> refusal, and no AI model is called.
//   2. Questions asking for a setpoint or LEL limit the sample SOPs do not contain get the fixed
//      "not specified" answer, so no model can invent a number.
//   3. The model sees only that SOP's sections and must say which ones it used:
//        - Claude: the Messages API citations feature; the answer must cite at least one section.
//        - Gemini / Groq (free tiers): a JSON reply listing the section labels used; every label must
//          be one we sent, and every number in the answer must appear in those sections.
//      The sources returned are the sections the model actually used. Anything that fails these
//      checks, and any API error, falls back to quoting the retrieved sections.
//
// Provider: LLM_PROVIDER = gemini | groq | claude, or picked from whichever key is set
// (GEMINI_API_KEY, GROQ_API_KEY, ANTHROPIC_API_KEY). No key -> local retrieval only.
// Keys are read from the server environment only and never reach the browser.

const retrieval = require("../web/copilot/retrieval.js");

const docs = [
  require("../web/copilot/sops/gas-leak.json"),
  require("../web/copilot/sops/compressor-trip.json"),
  require("../web/copilot/sops/permit-to-work.json"),
  require("../web/copilot/sops/gas-testing.json"),
  require("../web/copilot/sops/ppe.json"),
  require("../web/copilot/sops/emergency-communications.json"),
];

const TIMEOUT_MS = 20000;

const unsupported = {
  en: "This isn’t covered in the SOPs — ask your shift supervisor.",
  hi: "यह SOP में शामिल नहीं है — अपने शिफ्ट सुपरवाइज़र से पूछें।",
};
const banner = {
  en: "Training sample — NOT an approved plant procedure:",
  hi: "प्रशिक्षण नमूना — स्वीकृत साइट SOP नहीं:",
};

// ---------------------------------------------------------------- rate limit
// Per-instance request limit per visitor. Serverless instances do not share memory, so this is a
// brake against accidental loops and casual abuse, not a quota.
const RATE_LIMIT = { windowMs: 60000, max: Number(process.env.COPILOT_RATE_LIMIT_PER_MIN || 20) };
const recent = new Map();

function rateLimited(req) {
  const fwd = req.headers?.["x-forwarded-for"];
  const ip = (typeof fwd === "string" ? fwd.split(",")[0].trim() : "") || req.socket?.remoteAddress || "unknown";
  const now = Date.now();
  const hits = (recent.get(ip) || []).filter((t) => now - t < RATE_LIMIT.windowMs);
  hits.push(now);
  recent.set(ip, hits);
  if (recent.size > 5000) recent.clear();
  return hits.length > RATE_LIMIT.max;
}

// ---------------------------------------------------------------- prompts
const RULES = `You are ANUBHAV, a training copilot for operators at an oil and gas compression plant. You answer questions about the plant's SOPs. You are not an operational safety authority.

Rules:
- Answer ONLY from the SOP sections provided. Do not add steps, facts, equipment names, thresholds, alarm values, pressures or setpoints that are not written in those sections, even if you know typical values.
- Reply in the requested language: natural Hindi in Devanagari script for "hi", plain English for "en". Keep technical terms such as ESD, LEL, PPE and tag numbers as written.
- For an emergency, lead with the immediate safety steps as a short numbered list, then tell the worker to follow the site's own emergency instructions.
- Keep answers short: what to do, in order. No headings, no Markdown formatting.
- These SOPs are illustrative training samples. End with one short line saying the answer is from a training sample and site-approved procedures take precedence.`;

const CLAUDE_SYSTEM = `${RULES}
- Cite the documents for every instruction you give.
- If the documents do not answer the question, reply with exactly this sentence and nothing else: "${unsupported.en}" (or, when the requested language is Hindi: "${unsupported.hi}").`;

const JSON_SYSTEM = `${RULES}
- Each section is labelled like [S1]. Put the label of the section you used after each instruction, for example "Raise the alarm. [S1]".
- Reply with a JSON object only: {"covered": boolean, "answer": string, "used": [labels]}.
  "covered" is false when the sections do not answer the question; then "answer" is "" and "used" is [].
  "used" lists every label you relied on.`;

// ---------------------------------------------------------------- helpers
function extractive(sections, lang) {
  return banner[lang] + "\n" + sections.map(({ section }, i) => `${i + 1}. ${lang === "hi" ? section.hi : section.en}`).join("\n\n");
}

function sourceOf({ doc, section }) {
  return { id: doc.id, section: String(section.number), revision: doc.revision };
}

const DEVANAGARI_DIGITS = "०१२३४५६७८९";
const toAsciiDigits = (s) => s.replace(/[०-९]/g, (d) => String(DEVANAGARI_DIGITS.indexOf(d)));

/** Numbers in an answer (ignoring list numbering and [S1] labels) must all appear in the sources. */
function numbersSupported(answer, sourceText) {
  const body = toAsciiDigits(answer)
    .replace(/\[S\d+\]/g, " ")
    .replace(/^\s*\d+[.)]\s/gm, " ");
  const allowed = new Set((toAsciiDigits(sourceText).match(/\d+(?:[.,]\d+)?/g) || []));
  return (body.match(/\d+(?:[.,]\d+)?/g) || []).every((n) => allowed.has(n));
}

function parseJsonReply(text) {
  if (typeof text !== "string") return null;
  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start < 0 || end <= start) return null;
  try {
    return JSON.parse(text.slice(start, end + 1));
  } catch {
    return null;
  }
}

/** Validates a JSON-mode reply against the sections that were sent. */
function checkJsonAnswer(reply, sections) {
  if (!reply || typeof reply !== "object") return { kind: "invalid" };
  if (reply.covered === false) return { kind: "unsupported" };
  const answer = typeof reply.answer === "string" ? reply.answer.trim() : "";
  if (!answer) return { kind: "empty" };
  const labels = new Set([...(Array.isArray(reply.used) ? reply.used : []), ...(answer.match(/\[S\d+\]/g) || [])]
    .map((l) => String(l).replace(/[[\]\s]/g, "").toUpperCase()));
  const used = [];
  for (const l of labels) {
    const i = Number(l.replace(/^S/, "")) - 1;
    if (!/^S\d+$/.test(l) || !sections[i]) return { kind: "bad-label" };
    used.push(i);
  }
  if (!used.length) return { kind: "uncited" };
  used.sort((a, b) => a - b);
  const sourceText = used
    .map((i) => `${sections[i].doc.id} ${sections[i].doc.revision} ${sections[i].section.number} ${sections[i].section.heading} ${sections[i].section.en} ${sections[i].section.hi}`)
    .join(" ");
  if (!numbersSupported(answer, sourceText)) return { kind: "unsupported-number" };
  const text = answer.replace(/\[S(\d+)\]/g, (_, n) => {
    const s = sections[Number(n) - 1];
    return `[${s.doc.id} §${s.section.number}]`;
  });
  return { kind: "ok", answer: text, sources: used.map((i) => sourceOf(sections[i])) };
}

function labelledSections(sections) {
  return sections
    .map(({ doc, section }, i) => `[S${i + 1}] ${doc.id} §${section.number} — ${section.heading} (revision ${doc.revision})\nEnglish: ${section.en}\nहिंदी: ${section.hi}`)
    .join("\n\n");
}

async function postJson(url, headers, body) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await handler.fetch(url, {
      method: "POST",
      headers: { "content-type": "application/json", ...headers },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) {
      const err = new Error(data?.error?.message || `HTTP ${res.status}`);
      err.status = res.status;
      throw err;
    }
    return data;
  } finally {
    clearTimeout(timer);
  }
}

// ---------------------------------------------------------------- providers
const providers = {
  /** Google Gemini API (free tier available). */
  async gemini(question, lang, sections) {
    const model = process.env.GEMINI_MODEL || "gemini-flash-latest";
    const data = await postJson(
      `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`,
      { "x-goog-api-key": process.env.GEMINI_API_KEY },
      {
        systemInstruction: { parts: [{ text: JSON_SYSTEM }] },
        contents: [{ role: "user", parts: [{ text: `SOP sections:\n\n${labelledSections(sections)}\n\nRequested language: ${lang}\nQuestion: ${question}` }] }],
        generationConfig: {
          temperature: 0,
          maxOutputTokens: 2048,
          responseMimeType: "application/json",
          responseSchema: {
            type: "OBJECT",
            properties: {
              covered: { type: "BOOLEAN" },
              answer: { type: "STRING" },
              used: { type: "ARRAY", items: { type: "STRING" } },
            },
            required: ["covered", "answer", "used"],
          },
        },
      },
    );
    const text = (data?.candidates?.[0]?.content?.parts || []).map((p) => p.text || "").join("");
    return checkJsonAnswer(parseJsonReply(text), sections);
  },

  /** Groq (OpenAI-compatible API, free tier available). */
  async groq(question, lang, sections) {
    const data = await postJson(
      "https://api.groq.com/openai/v1/chat/completions",
      { authorization: `Bearer ${process.env.GROQ_API_KEY}` },
      {
        model: process.env.GROQ_MODEL || "llama-3.3-70b-versatile",
        temperature: 0,
        max_tokens: 2048,
        response_format: { type: "json_object" },
        messages: [
          { role: "system", content: JSON_SYSTEM },
          { role: "user", content: `SOP sections:\n\n${labelledSections(sections)}\n\nRequested language: ${lang}\nQuestion: ${question}` },
        ],
      },
    );
    return checkJsonAnswer(parseJsonReply(data?.choices?.[0]?.message?.content), sections);
  },

  /** Anthropic Claude, with the Messages API citations feature (paid). */
  async claude(question, lang, sections) {
    const client = handler.createClient();
    const response = await client.beta.messages.create({
      model: process.env.ANTHROPIC_MODEL || "claude-opus-5-5",
      max_tokens: 16000,
      output_config: { effort: process.env.ANTHROPIC_EFFORT || "low" },
      // Server-side refusal fallback: a declined request is re-run on a fallback model in the same call.
      betas: ["server-side-fallback-2026-07-01"],
      fallbacks: "default",
      system: CLAUDE_SYSTEM,
      messages: [
        {
          role: "user",
          content: [
            ...sections.map(({ doc, section }) => ({
              type: "document",
              source: { type: "text", media_type: "text/plain", data: `${section.heading}\n\nEnglish: ${section.en}\n\nहिंदी: ${section.hi}` },
              title: `${doc.id} §${section.number} — ${section.heading}`,
              context: `${doc.title}. Revision ${doc.revision}. ${doc.label}.`,
              citations: { enabled: true },
            })),
            { type: "text", text: `Requested language: ${lang}\nQuestion: ${question}` },
          ],
        },
      ],
    });
    if (response.stop_reason === "refusal") return { kind: "declined" };

    let answer = "";
    const cited = new Set();
    for (const block of response.content || []) {
      if (block.type !== "text") continue;
      answer += block.text;
      const marks = new Set();
      for (const c of block.citations || []) {
        if (typeof c.document_index !== "number" || !sections[c.document_index]) continue;
        cited.add(c.document_index);
        const { doc, section } = sections[c.document_index];
        marks.add(`${doc.id} §${section.number}`);
      }
      if (marks.size) answer += ` [${[...marks].join(", ")}]`;
    }
    answer = answer.trim();
    if (!answer) return { kind: "empty" };
    if (answer.includes(unsupported.en) || answer.includes(unsupported.hi)) return { kind: "unsupported" };
    if (!cited.size) return { kind: "uncited" };
    return { kind: "ok", answer, sources: [...cited].sort((a, b) => a - b).map((i) => sourceOf(sections[i])) };
  },
};

function pickProvider() {
  const forced = (process.env.LLM_PROVIDER || "").toLowerCase();
  const keyFor = { gemini: "GEMINI_API_KEY", groq: "GROQ_API_KEY", claude: "ANTHROPIC_API_KEY" };
  if (forced && keyFor[forced]) return process.env[keyFor[forced]] ? forced : null;
  return ["gemini", "groq", "claude"].find((p) => process.env[keyFor[p]]) || null;
}

/** Builds the Anthropic client. Replaced in tests. */
function createClient() {
  const Anthropic = require("@anthropic-ai/sdk");
  return new Anthropic({ timeout: 25000, maxRetries: 1 });
}

// ---------------------------------------------------------------- handler
async function handler(req, res) {
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("X-Content-Type-Options", "nosniff");
  if (req.method !== "POST") {
    res.setHeader("Allow", "POST");
    return res.status(405).json({ error: "Method not allowed" });
  }
  if (rateLimited(req)) {
    res.setHeader("Retry-After", "60");
    return res.status(429).json({ error: "Too many questions in a minute. Wait a moment and try again." });
  }
  let body = req.body;
  if (typeof body === "string") {
    try {
      body = JSON.parse(body);
    } catch {
      return res.status(400).json({ error: "Invalid JSON" });
    }
  }
  const question = body?.question;
  if (typeof question !== "string" || !question.trim() || question.length > 1000) {
    return res.status(400).json({ error: "Question must be 1–1000 characters" });
  }
  const lang = body?.lang === "hi" || body?.lang === "en" ? body.lang : retrieval.detectLanguage(question);

  const local = retrieval.answer(question, docs, lang);
  if (local.mode === "refusal" || !local.sources.length) {
    return res.status(200).json({ answer: unsupported[lang], sources: [], mode: "refusal" });
  }
  // Fixed answers for values the sample SOPs deliberately do not contain.
  if (local.mode === "missing-value" || local.mode === "missing-lel") {
    return res.status(200).json({ answer: local.answer, sources: local.sources, mode: local.mode });
  }

  // The model gets every section of the matched SOP, so it can answer follow-on detail; it must
  // say what it used. The sections retrieval chose come first.
  const doc = docs.find((d) => d.id === local.sources[0].id);
  const chosen = new Set(local.sources.map((s) => s.section));
  const sections = [
    ...doc.sections.filter((s) => chosen.has(String(s.number))),
    ...doc.sections.filter((s) => !chosen.has(String(s.number))),
  ].map((section) => ({ doc, section }));
  const retrievedOnly = sections.filter(({ section }) => chosen.has(String(section.number)));

  const provider = pickProvider();
  if (!provider) {
    return res.status(200).json({ answer: extractive(retrievedOnly, lang), sources: local.sources, mode: "retrieval-demo" });
  }

  try {
    const result = await providers[provider](question, lang, sections);
    if (result.kind === "ok") return res.status(200).json({ answer: result.answer, sources: result.sources, mode: provider, ai: true });
    if (result.kind === "unsupported") return res.status(200).json({ answer: unsupported[lang], sources: [], mode: "refusal" });
    console.warn(`Copilot: ${provider} answer not used:`, result.kind);
  } catch (e) {
    console.error(`Copilot: ${provider} unavailable:`, e?.status ?? "", e?.message ?? e);
  }
  return res.status(200).json({ answer: extractive(retrievedOnly, lang), sources: local.sources, mode: "retrieval-fallback" });
}

handler.createClient = createClient;
handler.fetch = (...args) => fetch(...args);
handler.checkJsonAnswer = checkJsonAnswer;
module.exports = handler;
