"use strict";
// Tests for the copilot's grounding rules, with a fake Claude client (no network, no API key).
// Run: node --test api/copilot.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const handler = require("./copilot.js");

const realCreate = handler.createClient;
const realFetch = handler.fetch;
const KEYS = ["ANTHROPIC_API_KEY", "GEMINI_API_KEY", "GROQ_API_KEY", "LLM_PROVIDER"];
for (const k of KEYS) delete process.env[k];

/** Runs fn with one provider key set and a fake HTTP layer answering with `reply` (a JSON string). */
function withJsonProvider(provider, reply, fn) {
  return async () => {
    const calls = [];
    const envKey = provider === "gemini" ? "GEMINI_API_KEY" : "GROQ_API_KEY";
    process.env[envKey] = "test-key";
    handler.fetch = async (url, init) => {
      calls.push({ url, init, body: JSON.parse(init.body) });
      if (reply instanceof Error) throw reply;
      const payload = provider === "gemini"
        ? { candidates: [{ content: { parts: [{ text: reply }] } }] }
        : { choices: [{ message: { content: reply } }] };
      return { ok: true, status: 200, json: async () => payload };
    };
    try {
      await fn(calls);
    } finally {
      delete process.env[envKey];
      handler.fetch = realFetch;
    }
  };
}

function fakeClient(respond) {
  const calls = [];
  return {
    calls,
    client: { beta: { messages: { create: async (params) => { calls.push(params); return respond(params); } } } },
  };
}

async function ask(question, lang = "en") {
  const res = {
    statusCode: 200,
    headers: {},
    setHeader(k, v) { this.headers[k] = v; },
    status(n) { this.statusCode = n; return this; },
    json(body) { return { status: this.statusCode, body }; },
  };
  // A distinct visitor per question, so the suite never trips the per-visitor rate limit.
  return handler({ method: "POST", headers: { "x-forwarded-for": `198.51.100.${++askCount}` }, body: { question, lang } }, res);
}
let askCount = 0;

function withClaude(respond, fn) {
  return async () => {
    const fake = fakeClient(respond);
    process.env.ANTHROPIC_API_KEY = "test-key";
    handler.createClient = () => fake.client;
    try {
      await fn(fake);
    } finally {
      delete process.env.ANTHROPIC_API_KEY;
      handler.createClient = realCreate;
    }
  };
}

const textBlock = (text, citations) => ({ type: "text", text, ...(citations ? { citations } : {}) });
const cite = (i) => ({ type: "char_location", document_index: i, cited_text: "…", start_char_index: 0, end_char_index: 1 });

test("off-topic question is refused without calling Claude", withClaude(() => assert.fail("Claude must not be called"), async (fake) => {
  const r = await ask("What is the cafeteria menu today?");
  assert.equal(r.body.mode, "refusal");
  assert.deepEqual(r.body.sources, []);
  assert.equal(fake.calls.length, 0);
}));

test("LEL limit question gets the fixed no-number answer without calling Claude", withClaude(() => assert.fail("Claude must not be called"), async (fake) => {
  const r = await ask("What's the LEL alarm limit?");
  assert.equal(r.body.mode, "missing-lel");
  assert(r.body.sources.some((s) => s.id === "SOP-GT-003"));
  assert(!/\d+\s*%/.test(r.body.answer));
  assert.equal(fake.calls.length, 0);
}));

test("grounded answer: SOP sections go in as cited documents, sources are what Claude cited", withClaude(
  () => ({ stop_reason: "end_turn", content: [textBlock("1. Raise the alarm and tell the control room.", [cite(0)]), textBlock("\nTraining sample.")] }),
  async (fake) => {
    const r = await ask("Gas leak at the discharge flange, what first?");
    assert.equal(r.body.mode, "claude");
    assert.match(r.body.answer, /\[SOP-GC-004 §\d+\]/);
    assert.equal(r.body.sources.length, 1);
    assert.equal(r.body.sources[0].id, "SOP-GC-004");
    const params = fake.calls[0];
    assert.equal(params.model, "claude-opus-5-5");
    const docsSent = params.messages[0].content.filter((b) => b.type === "document");
    assert(docsSent.length > 0);
    assert(docsSent.every((d) => d.citations.enabled && d.title.startsWith("SOP-GC-004 §")));
    assert(!JSON.stringify(params).includes("test-key"), "API key must not be in the request body");
  },
));

test("Hindi question is sent with the Hindi language flag", withClaude(
  () => ({ stop_reason: "end_turn", content: [textBlock("1. कंप्रेसर को सुरक्षित रूप से रोकें।", [cite(0)])] }),
  async (fake) => {
    const r = await ask("कंप्रेसर ट्रिप हो गया, क्या करूँ?", "hi");
    assert.equal(r.body.mode, "claude");
    assert(r.body.sources.every((s) => s.id === "SOP-CP-002"));
    assert.match(fake.calls[0].messages[0].content.at(-1).text, /Requested language: hi/);
  },
));

test("answer without any citation is not trusted: falls back to quoting the SOP", withClaude(
  () => ({ stop_reason: "end_turn", content: [textBlock("Restart the compressor at 9,800 rpm.")] }),
  async () => {
    const r = await ask("Compressor tripped, what do I do?");
    assert.equal(r.body.mode, "retrieval-fallback");
    assert(!r.body.answer.includes("9,800"));
    assert(r.body.sources.some((s) => s.id === "SOP-CP-002"));
  },
));

test("Claude saying the SOPs don't cover it becomes a refusal with no sources", withClaude(
  () => ({ stop_reason: "end_turn", content: [textBlock("This isn’t covered in the SOPs — ask your shift supervisor.")] }),
  async () => {
    const r = await ask("Compressor tripped, what is the bearing part number?");
    assert.equal(r.body.mode, "refusal");
    assert.deepEqual(r.body.sources, []);
  },
));

test("model refusal and API errors fall back to quoting the SOP", async (t) => {
  await t.test("refusal stop reason", withClaude(() => ({ stop_reason: "refusal", content: [] }), async () => {
    const r = await ask("Gas leak at the discharge flange, what first?");
    assert.equal(r.body.mode, "retrieval-fallback");
  }));
  await t.test("network error", withClaude(() => { throw new Error("timeout"); }, async () => {
    const r = await ask("Gas leak at the discharge flange, what first?");
    assert.equal(r.body.mode, "retrieval-fallback");
    assert(r.body.answer.startsWith("Training sample"));
  }));
});

test("no API key: local retrieval demo, no Claude client created", async () => {
  delete process.env.ANTHROPIC_API_KEY;
  handler.createClient = () => assert.fail("client must not be created without a key");
  try {
    const r = await ask("Gas leak at the discharge flange, what first?");
    assert.equal(r.body.mode, "retrieval-demo");
  } finally {
    handler.createClient = realCreate;
  }
});

test("rate limit returns 429 after too many questions from one visitor", async () => {
  const res = { setHeader() {}, status(n) { this.code = n; return this; }, json(b) { return { status: this.code, body: b }; } };
  const req = { method: "POST", headers: { "x-forwarded-for": "203.0.113.9" }, body: { question: "What is the cafeteria menu today?" } };
  let last;
  for (let i = 0; i < 21; i++) last = await handler(req, { ...res });
  assert.equal(last.status, 429);
});

test("rejects bad requests", async () => {
  const res = { setHeader() {}, status(n) { this.code = n; return this; }, json(b) { return { status: this.code, body: b }; } };
  assert.equal((await handler({ method: "GET" }, { ...res })).status, 405);
  assert.equal((await handler({ method: "POST", body: { question: "" } }, { ...res })).status, 400);
  assert.equal((await handler({ method: "POST", body: "{bad" }, { ...res })).status, 400);
});

// ---------------------------------------------------------------- free-tier providers (JSON replies)
const leakQ = "Gas leak at the discharge flange, what first?";

test("gemini: valid answer is used, labels become SOP citations, key goes in a header", withJsonProvider("gemini",
  JSON.stringify({ covered: true, answer: "1. Raise the site alarm and tell the control room. [S1]\nTraining sample.", used: ["S1"] }),
  async (calls) => {
    const r = await ask(leakQ);
    assert.equal(r.body.mode, "gemini");
    assert.equal(r.body.ai, true);
    assert.match(r.body.answer, /\[SOP-GC-004 §\d+\]/);
    assert.equal(r.body.sources.length, 1);
    assert.equal(calls[0].init.headers["x-goog-api-key"], "test-key");
    assert(!calls[0].url.includes("test-key"), "key must not be in the URL");
    assert.equal(calls[0].body.generationConfig.responseMimeType, "application/json");
  }));

test("groq: valid answer is used", withJsonProvider("groq",
  JSON.stringify({ covered: true, answer: "1. कंप्रेसर को सुरक्षित रूप से रोकें। [S1]", used: ["S1"] }),
  async (calls) => {
    const r = await ask("कंप्रेसर ट्रिप हो गया, क्या करूँ?", "hi");
    assert.equal(r.body.mode, "groq");
    assert(r.body.sources.every((s) => s.id === "SOP-CP-002"));
    assert.equal(calls[0].init.headers.authorization, "Bearer test-key");
    assert.match(calls[0].body.messages[1].content, /Requested language: hi/);
  }));

test("json providers: a number not in the SOP is rejected (no invented setpoints)", withJsonProvider("gemini",
  JSON.stringify({ covered: true, answer: "Restart when pressure is below 42 bar. [S1]", used: ["S1"] }),
  async () => {
    const r = await ask("Compressor tripped, what do I do?");
    assert.equal(r.body.mode, "retrieval-fallback");
    assert(!r.body.answer.includes("42"));
  }));

test("json providers: list numbering is not treated as an invented number", () => {
  const sections = [{ doc: { id: "SOP-X-001", revision: "R1" }, section: { number: "1", heading: "Alarm", en: "Raise the alarm.", hi: "अलार्म बजाएं।" } }];
  const r = handler.checkJsonAnswer({ covered: true, answer: "1. Raise the alarm. [S1]\n2. Wait. [S1]", used: ["S1"] }, sections);
  assert.equal(r.kind, "ok");
  assert.equal(r.answer, "1. Raise the alarm. [SOP-X-001 §1]\n2. Wait. [SOP-X-001 §1]");
});

test("json providers: a label we did not send is rejected", withJsonProvider("gemini",
  JSON.stringify({ covered: true, answer: "Do this. [S9]", used: ["S9"] }),
  async () => assert.equal((await ask(leakQ)).body.mode, "retrieval-fallback")));

test("json providers: an answer with no labels is rejected", withJsonProvider("groq",
  JSON.stringify({ covered: true, answer: "Raise the alarm.", used: [] }),
  async () => assert.equal((await ask(leakQ)).body.mode, "retrieval-fallback")));

test("json providers: covered=false becomes the refusal", withJsonProvider("gemini",
  JSON.stringify({ covered: false, answer: "", used: [] }),
  async () => {
    const r = await ask("Compressor tripped, what is the bearing part number?");
    assert.equal(r.body.mode, "refusal");
    assert.deepEqual(r.body.sources, []);
  }));

test("json providers: malformed JSON and network errors fall back to quoting the SOP", async (t) => {
  await t.test("malformed", withJsonProvider("gemini", "not json at all", async () =>
    assert.equal((await ask(leakQ)).body.mode, "retrieval-fallback")));
  await t.test("network", withJsonProvider("groq", new Error("timeout"), async () =>
    assert.equal((await ask(leakQ)).body.mode, "retrieval-fallback")));
});

test("provider choice: gemini key wins when several are set; LLM_PROVIDER overrides", async () => {
  const seen = [];
  handler.fetch = async (url) => {
    seen.push(url);
    return { ok: true, status: 200, json: async () => ({}) };
  };
  process.env.GEMINI_API_KEY = "g";
  process.env.GROQ_API_KEY = "q";
  try {
    await ask(leakQ);
    process.env.LLM_PROVIDER = "groq";
    await ask(leakQ);
  } finally {
    for (const k of KEYS) delete process.env[k];
    handler.fetch = realFetch;
  }
  assert.match(seen[0], /generativelanguage\.googleapis\.com/);
  assert.match(seen[1], /api\.groq\.com/);
});
