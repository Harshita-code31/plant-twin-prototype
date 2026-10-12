"use strict";
// ANUBHAV SOP copilot backend (Vercel serverless function, POST /api/copilot).
//
// Grounding rules, enforced in code rather than only in the prompt:
//   1. Retrieval picks the SOP sections (the same engine the page uses, web/copilot/retrieval.js).
//      No matching SOP -> refusal, and Claude is never called.
//   2. Questions asking for a setpoint or LEL limit the sample SOPs do not contain get the fixed
//      "not specified" answer, so no model can invent a number.
//   3. Claude sees only the retrieved sections, passed as documents with citations enabled.
//      An answer is returned only if it cites at least one of them; the sources returned are the
//      sections Claude actually cited. Otherwise the server falls back to quoting the sections.
// ANTHROPIC_API_KEY is read from the server environment only and never reaches the browser.

const retrieval = require("../web/copilot/retrieval.js");

const docs = [
  require("../web/copilot/sops/gas-leak.json"),
  require("../web/copilot/sops/compressor-trip.json"),
  require("../web/copilot/sops/permit-to-work.json"),
  require("../web/copilot/sops/gas-testing.json"),
  require("../web/copilot/sops/ppe.json"),
  require("../web/copilot/sops/emergency-communications.json"),
];

const MODEL = process.env.ANTHROPIC_MODEL || "claude-opus-5-5";
// Per-instance request limit per visitor. Serverless instances do not share memory, so this is a
// brake against accidental loops and casual abuse, not a quota: also set a spend limit on the
// Anthropic Console for the key.
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
const EFFORT = process.env.ANTHROPIC_EFFORT || "low";

const unsupported = {
  en: "This isn’t covered in the SOPs — ask your shift supervisor.",
  hi: "यह SOP में शामिल नहीं है — अपने शिफ्ट सुपरवाइज़र से पूछें।",
};
const banner = {
  en: "Training sample — NOT an approved plant procedure:",
  hi: "प्रशिक्षण नमूना — स्वीकृत साइट SOP नहीं:",
};

const SYSTEM = `You are ANUBHAV, a training copilot for operators at an oil and gas compression plant. You answer questions about the plant's SOPs. You are not an operational safety authority.

Rules:
- Answer ONLY from the SOP section documents provided in the user turn. Do not add steps, facts, equipment names, thresholds, alarm values, pressures or setpoints that are not written in those documents, even if you know typical values.
- Cite the documents for every instruction you give.
- If the documents do not answer the question, reply with exactly this sentence and nothing else: "${unsupported.en}" (or, when the requested language is Hindi: "${unsupported.hi}").
- Reply in the requested language: natural Hindi in Devanagari script for "hi", plain English for "en". Keep technical terms such as ESD, LEL, PPE and tag numbers as written.
- For an emergency, lead with the immediate safety steps as a short numbered list, then tell the worker to follow the site's own emergency instructions.
- Keep answers short: what to do, in order. No headings, no Markdown formatting.
- These SOPs are illustrative training samples. End with one short line saying the answer is from a training sample and site-approved procedures take precedence.`;

function sectionDocument(doc, section) {
  return {
    type: "document",
    source: {
      type: "text",
      media_type: "text/plain",
      data: `${section.heading}\n\nEnglish: ${section.en}\n\nहिंदी: ${section.hi}`,
    },
    title: `${doc.id} §${section.number} — ${section.heading}`,
    context: `${doc.title}. Revision ${doc.revision}. ${doc.label}.`,
    citations: { enabled: true },
  };
}

function extractive(sections, lang) {
  return banner[lang] + "\n" + sections.map(({ section }, i) => `${i + 1}. ${lang === "hi" ? section.hi : section.en}`).join("\n\n");
}

/** Builds the Anthropic client. Replaced in tests. */
function createClient() {
  const Anthropic = require("@anthropic-ai/sdk");
  return new Anthropic({ timeout: 25000, maxRetries: 1 });
}

/** Calls Claude with the retrieved sections; returns null when the answer can't be trusted. */
async function askClaude(client, question, lang, sections) {
  const response = await client.beta.messages.create({
    model: MODEL,
    max_tokens: 16000,
    output_config: { effort: EFFORT },
    // Server-side refusal fallback: a declined request is re-run on a fallback model in the same call.
    betas: ["server-side-fallback-2026-07-01"],
    fallbacks: "default",
    system: SYSTEM,
    messages: [
      {
        role: "user",
        content: [
          ...sections.map(({ doc, section }) => sectionDocument(doc, section)),
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

  const sources = [...cited].sort((a, b) => a - b).map((i) => {
    const { doc, section } = sections[i];
    return { id: doc.id, section: String(section.number), revision: doc.revision };
  });
  return { kind: "ok", answer, sources };
}

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

  // Give Claude every section of the matched SOP, so it can answer follow-on detail; it must cite
  // what it uses. The sections retrieval chose come first.
  const doc = docs.find((d) => d.id === local.sources[0].id);
  const chosen = new Set(local.sources.map((s) => s.section));
  const sections = [
    ...doc.sections.filter((s) => chosen.has(String(s.number))),
    ...doc.sections.filter((s) => !chosen.has(String(s.number))),
  ].map((section) => ({ doc, section }));
  const retrievedOnly = sections.filter(({ section }) => chosen.has(String(section.number)));

  if (!process.env.ANTHROPIC_API_KEY) {
    return res.status(200).json({ answer: extractive(retrievedOnly, lang), sources: local.sources, mode: "retrieval-demo" });
  }

  try {
    const result = await askClaude(handler.createClient(), question, lang, sections);
    if (result.kind === "ok") return res.status(200).json({ answer: result.answer, sources: result.sources, mode: "claude" });
    if (result.kind === "unsupported") return res.status(200).json({ answer: unsupported[lang], sources: [], mode: "refusal" });
    console.warn("Copilot: Claude answer not used:", result.kind);
  } catch (e) {
    console.error("Copilot: Claude unavailable:", e?.status ?? "", e?.message ?? e);
  }
  return res.status(200).json({ answer: extractive(retrievedOnly, lang), sources: local.sources, mode: "retrieval-fallback" });
}

handler.createClient = createClient;
module.exports = handler;
