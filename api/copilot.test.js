"use strict";
// Tests for the copilot's grounding rules, with a fake Claude client (no network, no API key).
// Run: node --test api/copilot.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const handler = require("./copilot.js");

const realCreate = handler.createClient;

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
  return handler({ method: "POST", body: { question, lang } }, res);
}

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
