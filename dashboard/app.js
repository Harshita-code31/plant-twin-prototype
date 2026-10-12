import { MODULES, MODULE_KEYS, FIRE_SCENARIOS, FIRE_STEPS, SCORING, quizPassMark, parseScenario, quizResult, certification,
         parseCertificate, checksumMatches, matchCertificate } from "./training.js";
import { chartCard, columnChart, barList, meter } from "./charts.js";

const cfg = window.ABHYAS_CONFIG || {};
const configured = cfg.supabaseUrl && !/YOUR-PROJECT/.test(cfg.supabaseUrl) && cfg.supabaseAnonKey && !/YOUR-ANON-KEY/.test(cfg.supabaseAnonKey);
const db = configured ? supabase.createClient(cfg.supabaseUrl, cfg.supabaseAnonKey) : null;

const $ = (id) => document.getElementById(id);
const themeToggle = $("theme-toggle");
function updateThemeToggle() {
  const dark = document.documentElement.dataset.theme === "dark";
  themeToggle.textContent = dark ? "Light mode" : "Dark mode";
  themeToggle.setAttribute("aria-label", `Switch to ${dark ? "light" : "dark"} mode`);
  themeToggle.setAttribute("aria-pressed", String(dark));
}
themeToggle.addEventListener("click", () => {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem("abhyas-theme", next); } catch (_) {}
  updateThemeToggle();
});
updateThemeToggle();
const state = {
  workers: [],
  attempts: [],
  issuedCertificates: [],
  byWorker: new Map(),      // worker id -> attempts (newest first)
  certs: new Map(),         // worker id -> { fire_safety: cert, machine_training: cert }
  loadedAt: 0,
  currentWorker: null,
  sort: { key: "last_activity", dir: -1 },
  quizModule: "fire_safety",
};

// ------------------------------------------------------------ helpers

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === "class") node.className = v;
    else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v === true ? "" : v);
  }
  for (const child of children.flat()) {
    if (child == null) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

const DAY = 864e5;
function fmtDate(iso, withTime = true) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(undefined, withTime ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" });
}
function fmtDuration(seconds) {
  if (seconds == null) return "—";
  const s = Math.round(seconds);
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, "0")}s`;
}
const pct = (part, whole) => (whole ? `${Math.round((part / whole) * 100)}%` : "—");
const humanize = (key) => key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ").replace(/^./, (c) => c.toUpperCase());

const ERRORS = {
  not_admin: "This account is not a AbhyasAR admin.",
  name_required: "Enter a name.",
  names_required: "Enter at least one name.",
  too_many_names: "Add at most 200 workers at a time.",
  worker_not_found: "That worker no longer exists. Refresh the page.",
  user_not_found: "No Supabase account uses that email. Create it under Authentication → Users first.",
  cannot_remove_self: "You can't remove your own admin access.",
  last_admin: "There must always be at least one admin.",
};
const friendly = (err) => {
  const msg = String(err?.message || err);
  const code = Object.keys(ERRORS).find((c) => msg.includes(c));
  return code ? ERRORS[code] : msg;
};

function toast(message) {
  const t = $("toast");
  t.textContent = message;
  t.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (t.hidden = true), 3200);
}

function tile(label, value, sub, accent = false) {
  return el("div", { class: accent ? "tile accent" : "tile" }, el("div", { class: "label" }, label), el("div", { class: "value" }, value), sub ? el("div", { class: "sub" }, sub) : null);
}

function showView(name) {
  document.body.dataset.view = name;
  for (const v of document.querySelectorAll(".view")) v.hidden = v.id !== `view-${name}`;
  for (const a of document.querySelectorAll("[data-nav]")) a.classList.toggle("active", a.dataset.nav === name || (name === "worker" && a.dataset.nav === "workers"));
}

async function rpc(fn, args) {
  const { data, error } = await db.rpc(fn, args);
  if (error) throw new Error(error.message);
  return data;
}

function downloadCsv(filename, header, rows) {
  const cell = (v) => `"${String(v ?? "").replace(/"/g, '""')}"`;
  const text = "﻿" + [header.map(cell).join(","), ...rows.map((r) => r.map(cell).join(","))].join("\r\n");
  const a = el("a", { href: URL.createObjectURL(new Blob([text], { type: "text/csv;charset=utf-8" })), download: filename });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

const moduleLabel = (m) => MODULES[m]?.label || humanize(m);
const partLabel = (a) => parseScenario(a.module, a.scenario).label;

// ------------------------------------------------------------ data

async function loadAll() {
  document.body.classList.add("loading");
  try {
    const workers = (await rpc("admin_list_workers")) || [];
    const attempts = [];
    const PAGE = 1000;
    for (let from = 0; ; from += PAGE) {
      const { data, error } = await db.from("training_attempts")
        .select("id,worker_id,module,scenario,passed,score,elapsed_seconds,details,completed_at")
        .order("completed_at", { ascending: false })
        .range(from, from + PAGE - 1);
      if (error) throw new Error(error.message);
      attempts.push(...data);
      if (data.length < PAGE) break;
    }
    // A practice attempt scoring at least the pass mark counts as passed, whatever step-by-step flag the
    // device stored (older builds marked e.g. a 90 as "not passed"). Quizzes keep their stored flag.
    for (const a of attempts) {
      if (!a.passed && !/\/Quiz$/.test(a.scenario || "") && (a.score ?? 0) >= SCORING.passMark) a.passed = true;
    }
    const passedByWorker = new Map();
    for (const a of attempts) if (a.passed) passedByWorker.set(a.worker_id, (passedByWorker.get(a.worker_id) || 0) + 1);
    for (const w of workers) w.passed = passedByWorker.get(w.id) || 0;
    state.workers = workers;
    state.attempts = attempts;
    state.issuedCertificates = await rpc("admin_list_certificates").catch(() => []);
    state.byWorker = new Map(workers.map((w) => [w.id, []]));
    for (const a of attempts) (state.byWorker.get(a.worker_id) || state.byWorker.set(a.worker_id, []).get(a.worker_id)).push(a);
    state.certs = new Map(workers.map((w) => [w.id, Object.fromEntries(MODULE_KEYS.map((m) => [m, certification(state.byWorker.get(w.id), m)]))]));
    state.loadedAt = Date.now();
  } finally {
    document.body.classList.remove("loading");
  }
}

async function ensureData(force = false) {
  if (force || !state.loadedAt || Date.now() - state.loadedAt > 60_000) await loadAll();
}

// ------------------------------------------------------------ auth

async function requireAdmin() {
  const { data } = await db.auth.getSession();
  const session = data.session;
  $("session-box").hidden = !session;
  $("nav").hidden = !session;
  if (!session) return false;
  $("admin-email").textContent = session.user.email;
  return true;
}

$("login-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("login-error").textContent = "";
  const { error } = await db.auth.signInWithPassword({ email: $("login-email").value.trim(), password: $("login-password").value });
  if (error) {
    $("login-error").textContent = error.message;
    return;
  }
  const isAdmin = await rpc("is_admin").catch(() => false);
  if (!isAdmin) {
    await db.auth.signOut();
    $("login-error").textContent = ERRORS.not_admin;
    return;
  }
  location.hash = "#/overview";
  route();
});

$("signout-btn").addEventListener("click", async () => {
  await db.auth.signOut();
  state.loadedAt = 0;
  location.hash = "";
  route();
});

$("refresh-btn").addEventListener("click", async () => {
  try {
    await ensureData(true);
    await route(true);
    toast("Data refreshed");
  } catch (err) {
    toast(friendly(err));
  }
});

// ------------------------------------------------------------ overview

for (const id of ["f-range", "f-module"]) $(id).addEventListener("change", renderOverview);

function renderOverview() {
  const range = $("f-range").value;
  const moduleFilter = $("f-module").value;
  const now = Date.now();
  const today = new Date(now);
  const since = range === "all" ? -Infinity
    : range === "this_month" ? new Date(today.getFullYear(), today.getMonth(), 1).getTime()
    : range === "this_year" ? new Date(today.getFullYear(), 0, 1).getTime()
    : now - Number(range) * DAY;
  const inModule = (m) => moduleFilter === "all" || m === moduleFilter;
  const list = state.attempts.filter((a) => inModule(a.module) && Date.parse(a.completed_at) >= since);
  const modules = MODULE_KEYS.filter(inModule);

  // ---- KPI row
  const activeWorkers = new Set(list.map((a) => a.worker_id)).size;
  const passed = list.filter((a) => a.passed).length;
  const timedAttempts = list.filter((a) => Number.isFinite(a.elapsed_seconds) && a.elapsed_seconds >= 0);
  const trainingHours = timedAttempts.reduce((seconds, a) => seconds + a.elapsed_seconds, 0) / 3600;
  let certsEarned = 0;
  for (const c of state.certs.values()) for (const m of modules) if (c[m].certifiedAt && Date.parse(c[m].certifiedAt) >= since) certsEarned++;
  const quizResults = list.filter((a) => parseScenario(a.module, a.scenario).kind === "quiz").map(quizResult);
  const quizzes = quizResults.filter((q) => q.completed && q.pct != null);
  const avgQuiz = quizzes.length ? Math.round(quizzes.reduce((n, q) => n + q.pct, 0) / quizzes.length) : null;
  $("overview-tiles").replaceChildren(
    tile("Active workers", activeWorkers, `of ${state.workers.length} workers`),
    tile("Training hours", timedAttempts.length ? `${trainingHours.toFixed(2)}h` : "—", timedAttempts.length ? `${timedAttempts.length} timed attempts` : "No timed attempts in this period"),
    tile("Pass rate", pct(passed, list.length)),
    tile("Certificates earned", certsEarned, null, true),
    tile("Average quiz score", avgQuiz == null ? "—" : `${avgQuiz}%`, `${quizzes.length} completed quizzes`),
  );

  const charts = [];

  // ---- activity over time (stacked: passed / not passed)
  {
    const first = list.length ? Math.min(...list.map((a) => Date.parse(a.completed_at))) : now;
    const start = range === "all" ? first : since;
    const spanDays = Math.max(1, Math.ceil((now - start) / DAY));
    const unit = range === "this_year" ? "month" : spanDays <= 92 ? "day" : spanDays <= 400 ? "week" : "month";
    const buckets = makeBuckets(start, now, unit);
    for (const a of list) {
      const b = buckets.find((bk) => Date.parse(a.completed_at) >= bk.from && Date.parse(a.completed_at) < bk.to);
      if (b) b.values[a.passed ? "passed" : "failed"]++;
    }
    const series = [
      { key: "passed", label: "Passed", color: "--viz-pass" },
      { key: "failed", label: "Not passed", color: "--viz-fail", hatch: true },
    ];
    const c = chartCard({
      title: "Training activity",
      legend: series,
      empty: list.length ? null : "No training attempts in this period.",
      table: { columns: [unit[0].toUpperCase() + unit.slice(1), "Passed", "Not passed", "Total"], rows: buckets.map((b) => [b.tipTitle, b.values.passed, b.values.failed, b.values.passed + b.values.failed]) },
    });
    c.card.classList.add("span-2");
    const rangeSelect = el("select", { "aria-label": "Training activity period" },
      [...$("f-range").options].map((option) => el("option", { value: option.value }, option.textContent)));
    rangeSelect.value = range;
    rangeSelect.addEventListener("change", () => {
      $("f-range").value = rangeSelect.value;
      renderOverview();
    });
    const actions = el("div", { class: "viz-actions" }, el("label", { class: "activity-picker" }, "Period", rangeSelect));
    const toggle = c.card.querySelector(".viz-toggle");
    if (toggle) actions.append(toggle);
    c.card.querySelector(".viz-head").append(actions);
    charts.push([c, () => list.length && columnChart(c.body, c.card, c.tooltip, { buckets, series, height: 180, yTitle: "Attempts" })]);
  }

  // ---- certification progress
  {
    const c = chartCard({ title: "Certification progress" });
    charts.push([c, () => {
      const total = state.workers.length;
      c.body.replaceChildren(...modules.map((m) => {
        let certified = 0, inProgress = 0;
        for (const cc of state.certs.values()) {
          if (cc[m].status === "certified") certified++;
          else if (cc[m].status === "in_progress") inProgress++;
        }
        return meter({ label: moduleLabel(m), value: certified, max: total, valueText: `${certified} of ${total}`,
          sub: `${inProgress} in progress · ${total - certified - inProgress} not started` });
      }));
    }]);
  }

  // ---- pass rate by training part
  {
    const parts = partStats(list, modules);
    const c = chartCard({
      title: "Pass rate by training part",
      subtitle: parts.some((p) => p.n > 0) ? null : "No attempts in this period.",
      table: { columns: ["Training part", "Attempts", "Passed", "Pass rate", "Average score"], rows: parts.map((p) => [p.label, p.n, p.passed, p.n ? pct(p.passed, p.n) : "No attempts", p.avg ?? "—"]) },
    });
    charts.push([c, () => barList(c.body, c.card, c.tooltip, {
      max: 1,
      rows: parts.map((p) => ({
        label: p.label, value: p.n ? p.passed / p.n : 0, valueText: p.n ? `${pct(p.passed, p.n)} of ${p.n}` : "No attempts",
        tip: [{ value: p.n, label: "attempts" }, { value: p.passed, label: "passed" }, { value: p.avg ?? "—", label: "average score" }],
      })),
    })]);
  }

  // ---- where trainees struggle (fire scenario steps)
  if (modules.includes("fire_safety")) {
    const steps = missedSteps(list);
    const c = chartCard({
      title: "Where trainees struggle",
      empty: steps.length ? null : "No fire scenario attempts in this period.",
      table: { columns: ["Step", "Attempts", "Missed", "Missed %"], rows: steps.map((s) => [s.label, s.n, s.missed, pct(s.missed, s.n)]) },
    });
    charts.push([c, () => steps.length && barList(c.body, c.card, c.tooltip, {
      max: 1, color: "--viz-fail", hatch: true,
      rows: steps.map((s) => ({ label: s.label, value: s.missed / s.n, valueText: `${pct(s.missed, s.n)} missed`,
        tip: [{ value: s.missed, label: "missed" }, { value: s.n, label: s.key === "forcedFailure" ? "furnace attempts" : "attempts" }] })),
    })]);
  }

  // ---- quiz score distribution
  const quizHost = el("div", { class: "quiz-chart-host" });
  function renderQuizChart() {
    const m = modules.includes(state.quizModule) ? state.quizModule : modules[0];
    const qs = list.filter((a) => a.module === m && parseScenario(m, a.scenario).kind === "quiz").map(quizResult);
    const done = qs.filter((q) => q.completed && q.correct != null);
    const closed = qs.filter((q) => !q.completed).length;
    const total = done[0]?.total || 10;
    // The pass mark depends on the practice score (see TrainingScoring); it is lowest with 100% practice.
    const passMark = quizPassMark(100, total);
    const buckets = Array.from({ length: total + 1 }, (_, i) => ({ label: String(i), tipTitle: `${i} of ${total} correct`, values: { passed: 0, failed: 0 } }));
    for (const q of done) buckets[Math.min(total, q.correct)].values[q.correct >= (q.passMark ?? passMark) ? "passed" : "failed"]++;
    const series = [{ key: "passed", label: "Passed", color: "--viz-pass" }, { key: "failed", label: "Not passed", color: "--viz-fail", hatch: true }];
    const c = chartCard({
      title: "Quiz scores",
      subtitle: `${done.length} completed · ${closed} closed early`,
      legend: series,
      empty: done.length ? null : "No completed quizzes in this period.",
      table: { columns: ["Correct answers", "Quizzes"], rows: buckets.map((b) => [b.label, b.values.passed + b.values.failed]) },
    });
    const selector = el("select", { "aria-label": "Quiz score training" }, modules.map((key) =>
      el("option", { value: key }, moduleLabel(key))));
    selector.value = m;
    selector.addEventListener("change", () => {
      state.quizModule = selector.value;
      renderQuizChart();
    });
    const actions = el("div", { class: "viz-actions" });
    const toggle = c.card.querySelector(".viz-toggle");
    if (toggle) actions.append(toggle);
    actions.append(el("label", { class: "quiz-picker" }, "Training", selector));
    c.card.querySelector(".viz-head").append(actions);
    quizHost.replaceChildren(c.card);
    if (done.length) columnChart(c.body, c.card, c.tooltip, { buckets, series, height: 150, marker: { index: passMark, label: "Pass mark" }, yTitle: "Quizzes" });
  }
  charts.push([{ card: quizHost }, renderQuizChart]);

  $("overview-charts").replaceChildren(...charts.map(([c]) => c.card));
  for (const [, draw] of charts) draw();
}

function makeBuckets(start, end, unit) {
  const d = new Date(start);
  d.setHours(0, 0, 0, 0);
  if (unit === "week") d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); // Monday
  if (unit === "month") d.setDate(1);
  const out = [];
  while (d.getTime() <= end) {
    const from = d.getTime();
    if (unit === "day") d.setDate(d.getDate() + 1);
    else if (unit === "week") d.setDate(d.getDate() + 7);
    else d.setMonth(d.getMonth() + 1);
    const f = new Date(from);
    const label = unit === "month" ? f.toLocaleDateString(undefined, { month: "short", year: "2-digit" }) : f.toLocaleDateString(undefined, { day: "numeric", month: "short" });
    const tipTitle = unit === "week" ? `Week of ${f.toLocaleDateString(undefined, { dateStyle: "medium" })}` : unit === "month" ? f.toLocaleDateString(undefined, { month: "long", year: "numeric" }) : f.toLocaleDateString(undefined, { dateStyle: "medium" });
    out.push({ from, to: d.getTime(), label, tipTitle, values: { passed: 0, failed: 0 } });
  }
  return out;
}

function partStats(list, modules) {
  const order = [];
  for (const m of modules) {
    if (m === "fire_safety") {
      for (const s of FIRE_SCENARIOS) order.push({ module: m, key: s.key, label: `Fire · ${s.label}` });
      for (const cls of ["A", "BC", "ABC"]) order.push({ module: m, key: `class_${cls}`, label: `Fire · Class ${cls} (older app)`, legacy: true });
      order.push({ module: m, key: "quiz", label: "Fire · Quiz" });
    } else if (m === "machine_training") {
      order.push({ module: m, key: "practice", label: "Machine · Conveyor practice" });
      order.push({ module: m, key: "quiz", label: "Machine · Quiz" });
    }
  }
  return order.map((o) => {
    const rows = list.filter((a) => a.module === o.module && parseScenario(a.module, a.scenario).key === o.key);
    const scored = rows.filter((a) => a.score != null);
    return { ...o, n: rows.length, passed: rows.filter((a) => a.passed).length, avg: scored.length ? Math.round(scored.reduce((n, a) => n + a.score, 0) / scored.length) : null };
  }).filter((p) => !p.legacy || p.n > 0);
}

function missedSteps(list) {
  const fire = list.filter((a) => a.module === "fire_safety" && parseScenario(a.module, a.scenario).kind === "scenario" && a.details);
  return FIRE_STEPS.map((s) => {
    const pool = s.key === "forcedFailure" ? fire.filter((a) => parseScenario(a.module, a.scenario).key === "furnace") : fire;
    const withFlag = pool.filter((a) => typeof a.details[s.key] === "boolean");
    const missed = withFlag.filter((a) => (s.bad ? a.details[s.key] : !a.details[s.key])).length;
    return { ...s, n: withFlag.length, missed };
  }).filter((s) => s.n > 0).sort((a, b) => b.missed / b.n - a.missed / a.n);
}

// ------------------------------------------------------------ workers list

function workerRow(w) {
  const certs = state.certs.get(w.id);
  const attempts = Number(w.attempts), passed = Number(w.passed);
  return { ...w, attempts, passed, passRate: attempts ? passed / attempts : -1, certs,
    fire_safety: certSortValue(certs.fire_safety), machine_training: certSortValue(certs.machine_training) };
}
const certSortValue = (c) => (c.status === "certified" ? 2 : c.status === "in_progress" ? 1 : 0);

function certCell(c) {
  if (c.status === "certified") return el("span", {}, el("span", { class: "badge pass" }, `✓ ${c.certifiedTotal}/100`), " ", el("span", { class: "muted small" }, fmtDate(c.certifiedAt, false)));
  if (c.status === "in_progress") return el("span", { class: "badge warn" }, c.best ? `Best ${c.best.total}/100` : "In progress");
  return el("span", { class: "muted" }, "Not started");
}

function renderWorkers() {
  const q = $("worker-search").value.trim().toLowerCase();
  const status = $("f-status").value;
  const certF = $("f-cert").value;
  let rows = state.workers.map(workerRow).filter((w) => {
    if (q && !w.display_name.toLowerCase().includes(q) && !w.worker_code.toLowerCase().includes(q)) return false;
    if (status === "active" && !w.active) return false;
    if (status === "disabled" && w.active) return false;
    const f = w.certs.fire_safety.status === "certified", m = w.certs.machine_training.status === "certified";
    if (certF === "both" && !(f && m)) return false;
    if (certF === "fire_safety" && !f) return false;
    if (certF === "machine_training" && !m) return false;
    if (certF === "none" && (f || m)) return false;
    return true;
  });
  const { key, dir } = state.sort;
  rows.sort((a, b) => {
    let x = a[key], y = b[key];
    if (key === "last_activity") { x = x ? Date.parse(x) : 0; y = y ? Date.parse(y) : 0; }
    if (typeof x === "string") return x.localeCompare(y) * dir;
    return ((x ?? 0) - (y ?? 0)) * dir;
  });
  for (const th of document.querySelectorAll("table.sortable th[data-sort]")) {
    th.setAttribute("aria-sort", th.dataset.sort === key ? (dir > 0 ? "ascending" : "descending") : "none");
  }

  $("workers-body").replaceChildren(...rows.map((w) => el("tr", { class: "clickable", tabindex: "0",
      onclick: () => (location.hash = `#/worker/${w.id}`),
      onkeydown: (e) => { if (e.key === "Enter") location.hash = `#/worker/${w.id}`; } },
    el("td", { class: "mono" }, w.worker_code),
    el("td", {}, w.display_name),
    el("td", {}, el("span", { class: w.active ? "badge pass" : "badge off" }, w.active ? "Active" : "Disabled")),
    el("td", {}, certCell(w.certs.fire_safety)),
    el("td", {}, certCell(w.certs.machine_training)),
    el("td", { class: "num" }, w.attempts),
    el("td", { class: "num" }, w.attempts ? pct(w.passed, w.attempts) : "—"),
    el("td", {}, fmtDate(w.last_activity)))));
  $("workers-count").textContent = `${rows.length} of ${state.workers.length}`;
  $("workers-empty").hidden = rows.length > 0;
  $("workers-empty").textContent = state.workers.length ? "No workers match these filters." : "No workers yet. Add some to get started.";
}

for (const id of ["worker-search", "f-status", "f-cert"]) $(id).addEventListener(id === "worker-search" ? "input" : "change", renderWorkers);
for (const th of document.querySelectorAll("table.sortable th[data-sort]")) {
  th.addEventListener("click", () => {
    const key = th.dataset.sort;
    state.sort = state.sort.key === key ? { key, dir: -state.sort.dir } : { key, dir: key === "display_name" || key === "worker_code" ? 1 : -1 };
    renderWorkers();
  });
}

$("export-workers-btn").addEventListener("click", () => {
  const rows = state.workers.map(workerRow);
  downloadCsv("abhyasar-workers.csv",
    ["worker_id", "name", "status", "fire_safety", "fire_safety_certified_at", "machine_safety", "machine_safety_certified_at", "attempts", "passed", "last_activity", "added"],
    rows.map((w) => [w.worker_code, w.display_name, w.active ? "active" : "disabled",
      w.certs.fire_safety.status, w.certs.fire_safety.certifiedAt || "", w.certs.machine_training.status, w.certs.machine_training.certifiedAt || "",
      w.attempts, w.passed, w.last_activity || "", w.created_at]));
});

// ------------------------------------------------------------ add workers / credentials

for (const btn of document.querySelectorAll("[data-close]")) btn.addEventListener("click", () => btn.closest("dialog").close());

const parseNames = () => $("new-worker-names").value.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
$("new-worker-names").addEventListener("input", () => {
  const n = parseNames().length;
  $("new-worker-count").textContent = n ? `${n} worker${n === 1 ? "" : "s"} will be created, each with a worker ID and a password.` : "Each person gets a worker ID and a password.";
});

$("new-worker-btn").addEventListener("click", () => {
  $("new-worker-names").value = "";
  $("new-worker-error").textContent = "";
  $("new-worker-names").dispatchEvent(new Event("input"));
  $("new-worker-dialog").showModal();
});

$("new-worker-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const names = parseNames();
  if (!names.length) return;
  const submit = e.submitter;
  submit.disabled = true;
  try {
    const created = names.length === 1 ? [await rpc("admin_create_worker", { p_name: names[0] })] : await rpc("admin_bulk_create_workers", { p_names: names });
    $("new-worker-dialog").close();
    showCredentials(created.length === 1 ? "New worker created" : `${created.length} workers created`, created);
    await ensureData(true);
    renderWorkers();
  } catch (err) {
    $("new-worker-error").textContent = friendly(err);
  } finally {
    submit.disabled = false;
  }
});

function showCredentials(title, list) {
  $("cred-title").textContent = title;
  $("cred-body").replaceChildren(...list.map((c) => el("tr", {}, el("td", {}, c.display_name), el("td", {}, c.worker_code), el("td", {}, c.password))));
  $("credentials-dialog").showModal();
}

$("cred-copy").addEventListener("click", async () => {
  const lines = [...$("cred-body").rows].map((r) => `${r.cells[0].textContent}\tWorker ID: ${r.cells[1].textContent}\tPassword: ${r.cells[2].textContent}`);
  await navigator.clipboard.writeText(`AbhyasAR login details\n${lines.join("\n")}`);
  toast("Copied to clipboard");
});
$("cred-print").addEventListener("click", () => window.print());
$("credentials-dialog").addEventListener("close", () => $("cred-body").replaceChildren()); // passwords never linger in the page

// ------------------------------------------------------------ worker detail

$("f-worker-module").addEventListener("change", () => renderAttempts());

function renderWorker(id) {
  const w = state.workers.find((x) => x.id === id) || null;
  state.currentWorker = w;
  for (const b of ["export-csv-btn", "rename-btn", "reset-pw-btn", "toggle-active-btn", "delete-btn"]) $(b).disabled = !w;
  if (!w) {
    $("worker-name").textContent = "Worker not found";
    $("worker-meta").textContent = "They may have been deleted.";
    $("worker-modules").replaceChildren();
    $("attempts-body").replaceChildren();
    return;
  }
  $("worker-name").textContent = w.display_name;
  $("worker-meta").replaceChildren(el("span", { class: "id-tag" }, w.worker_code),
    el("span", { class: w.active ? "badge pass" : "badge off" }, w.active ? "Active" : "Login disabled"),
    `Added ${fmtDate(w.created_at, false)}`);
  $("toggle-active-btn").textContent = w.active ? "Disable login" : "Enable login";
  $("toggle-active-btn").classList.toggle("danger", w.active);

  const attempts = state.byWorker.get(w.id) || [];
  const certs = state.certs.get(w.id);
  $("worker-modules").replaceChildren(...MODULE_KEYS.map((m) => moduleCard(m, certs[m], attempts.filter((a) => a.module === m))));
  renderAttempts();
}

function moduleCard(m, cert, attempts) {
  const status = cert.status === "certified" ? el("span", { class: "badge pass" }, "✓ Certified")
    : cert.status === "in_progress" ? el("span", { class: "badge warn" }, "In progress") : el("span", { class: "badge off" }, "Not started");
  const items = [];
  const partState = (rows, bestText) => {
    if (!rows.length) return el("span", { class: "state none" }, "Not tried");
    const ok = rows.some((a) => a.passed);
    const tries = `${rows.length} ${rows.length === 1 ? "try" : "tries"}`;
    return el("span", { class: `state ${ok ? "ok" : "no"}` }, ok ? `✓ Passed · ${bestText(rows)} · ${tries}` : `✗ Not passed yet · ${tries}`);
  };
  const bestScore = (rows) => `best ${Math.max(...rows.map((a) => a.score ?? 0))}`;
  if (m === "fire_safety") {
    for (const s of FIRE_SCENARIOS) {
      const rows = attempts.filter((a) => parseScenario(m, a.scenario).key === s.key);
      items.push([`${s.label} (Class ${s.fireClass})`, partState(rows, bestScore)]);
    }
    for (const cls of ["A", "BC", "ABC"]) {
      const rows = attempts.filter((a) => parseScenario(m, a.scenario).key === `class_${cls}`);
      if (rows.length) items.push([`Class ${cls} fire (older app version)`, partState(rows, bestScore)]);
    }
  } else {
    const rows = attempts.filter((a) => parseScenario(m, a.scenario).kind === "practice");
    items.push(["Conveyor practice (all 7 controls)", partState(rows, () => "all controls used")]);
  }
  const quizRows = attempts.filter((a) => parseScenario(m, a.scenario).kind === "quiz");
  const q = cert.bestQuiz;
  items.push(["Quiz", partState(quizRows, () => (q && q.correct != null ? `best ${q.correct}/${q.total}` : `best ${q?.pct ?? 0}%`))]);

  return el("div", { class: "module-card" },
    el("div", { class: "module-head" }, el("h2", {}, moduleLabel(m)), status),
    scoreBar(cert),
    el("ul", { class: "steps" }, items.map(([label, st]) => el("li", {}, el("span", {}, label), st))),
    el("p", { class: "module-foot" }, cert.certifiedAt
      ? `Certified ${fmtDate(cert.certifiedAt)} with ${cert.certifiedTotal}/100.`
      : `Certificate requires ${SCORING.passMark}/100.`));
}

/**
 * 100-point bar: the 0-40 slot fills with the practice contribution, the 40-100 slot with the
 * quiz contribution, and a notch marks the pass mark. Uses the best-scoring quiz, or the current
 * practice score alone when no quiz has been completed yet.
 */
function scoreBar(cert) {
  const practice = cert.best?.practice ?? cert.practicePercent ?? 0;
  const quiz = cert.best?.quiz ?? 0;
  const pPts = Math.round(practice * SCORING.practiceWeight);
  const qPts = Math.round(quiz * SCORING.quizWeight);
  const total = cert.best?.total ?? pPts;
  const passed = total >= SCORING.passMark;
  return el("div", { class: "scorebar", role: "img", "aria-label": `Practice ${pPts} of 40, quiz ${qPts} of 60, total ${total} of 100; pass mark ${SCORING.passMark}` },
    el("div", { class: "scorebar-track" },
      el("div", { class: "scorebar-slot practice" }, el("div", { class: "scorebar-fill", style: `width:${practice}%` })),
      el("div", { class: "scorebar-slot quiz" }, el("div", { class: "scorebar-fill", style: `width:${quiz}%` })),
      el("div", { class: "scorebar-notch" })),
    el("div", { class: "scorebar-legend" },
      el("span", {}, "Practice ", el("b", {}, `${pPts}`), "/40"),
      el("span", {}, "Quiz ", el("b", {}, cert.best ? `${qPts}` : "—"), "/60"),
      el("span", { class: `scorebar-total ${passed ? "pass" : "fail"}` }, `${total}`)));
}

function renderAttempts() {
  const w = state.currentWorker;
  if (!w) return;
  const filter = $("f-worker-module").value;
  const a = (state.byWorker.get(w.id) || []).filter((x) => filter === "all" || x.module === filter);
  const rows = [];
  for (const x of a) {
    const details = renderDetails(x);
    const detailsRow = el("tr", { class: "details-row", hidden: true }, el("td", { colspan: "7" }, details));
    const toggle = details ? el("button", { class: "link", "aria-expanded": "false" }, "Details") : null;
    toggle?.addEventListener("click", () => {
      detailsRow.hidden = !detailsRow.hidden;
      toggle.setAttribute("aria-expanded", String(!detailsRow.hidden));
    });
    rows.push(el("tr", {},
      el("td", {}, fmtDate(x.completed_at)),
      el("td", {}, moduleLabel(x.module)),
      el("td", {}, partLabel(x)),
      el("td", {}, el("span", { class: x.passed ? "badge pass" : "badge fail" }, x.passed ? "✓ PASS" : "✗ FAIL")),
      el("td", { class: "num" }, scoreText(x)),
      el("td", { class: "num" }, fmtDuration(x.elapsed_seconds)),
      el("td", { class: "num" }, toggle)), detailsRow);
  }
  $("attempts-body").replaceChildren(...rows);
  $("attempts-empty").hidden = a.length > 0;
}

function scoreText(x) {
  if (parseScenario(x.module, x.scenario).kind === "quiz") {
    const q = quizResult(x);
    if (q.correct != null) return `${q.correct}/${q.total}`;
  }
  return x.score ?? "—";
}

function renderDetails(x) {
  const d = x.details;
  if (!d) return null;
  const kind = parseScenario(x.module, x.scenario).kind;
  if (kind === "quiz") {
    const q = quizResult(x);
    return el("p", {}, q.completed
      ? `${q.correct} of ${q.total} correct${q.passMark ? ` (pass mark ${q.passMark})` : ""}.`
      : `Closed before finishing: ${d.answered ?? 0} of ${q.total} answered, ${q.correct} correct. Counted as not passed.`);
  }
  if (kind === "practice") return el("p", {}, `Practised all ${d.tasksCompleted ?? 7} conveyor controls.`);
  if (x.module === "fire_safety") {
    const items = FIRE_STEPS.filter((s) => typeof d[s.key] === "boolean" && !(s.key === "forcedFailure" && !d[s.key] && parseScenario(x.module, x.scenario).key !== "furnace"))
      .map((s) => el("li", { class: (s.bad ? !d[s.key] : d[s.key]) ? "yes" : "no" }, s.label));
    return items.length ? el("ul", { class: "checklist" }, ...items) : null;
  }
  const bools = Object.entries(d).filter(([, v]) => typeof v === "boolean");
  return bools.length ? el("ul", { class: "checklist" }, ...bools.map(([k, v]) => el("li", { class: v ? "yes" : "no" }, humanize(k)))) : null;
}

$("reset-pw-btn").addEventListener("click", async () => {
  const w = state.currentWorker;
  if (!w || !confirm(`Reset the password for ${w.display_name}? Their old password stops working, and their devices will need to log in online once.`)) return;
  try {
    showCredentials("Password reset", [await rpc("admin_reset_password", { p_worker_id: w.id })]);
  } catch (err) {
    toast(friendly(err));
  }
});

$("toggle-active-btn").addEventListener("click", async () => {
  const w = state.currentWorker;
  if (!w) return;
  if (w.active && !confirm(`Disable login for ${w.display_name}? Progress already synced is kept.`)) return;
  try {
    await rpc("admin_set_active", { p_worker_id: w.id, p_active: !w.active });
    await ensureData(true);
    renderWorker(w.id);
    toast(state.currentWorker?.active ? "Login enabled" : "Login disabled");
  } catch (err) {
    toast(friendly(err));
  }
});

$("rename-btn").addEventListener("click", () => {
  const w = state.currentWorker;
  if (!w) return;
  $("rename-input").value = w.display_name;
  $("rename-error").textContent = "";
  $("rename-dialog").showModal();
});

$("rename-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const w = state.currentWorker;
  try {
    await rpc("admin_rename_worker", { p_worker_id: w.id, p_name: $("rename-input").value });
    $("rename-dialog").close();
    await ensureData(true);
    renderWorker(w.id);
    toast("Name updated");
  } catch (err) {
    $("rename-error").textContent = friendly(err);
  }
});

$("delete-btn").addEventListener("click", () => {
  const w = state.currentWorker;
  if (!w) return;
  $("delete-name").textContent = w.display_name;
  $("delete-code").textContent = w.worker_code;
  $("delete-confirm").value = "";
  $("delete-error").textContent = "";
  $("delete-submit").disabled = true;
  $("delete-dialog").showModal();
});
$("delete-confirm").addEventListener("input", () => {
  $("delete-submit").disabled = $("delete-confirm").value.trim().toUpperCase() !== state.currentWorker?.worker_code;
});
$("delete-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const w = state.currentWorker;
  if (!w || $("delete-confirm").value.trim().toUpperCase() !== w.worker_code) return;
  try {
    await rpc("admin_delete_worker", { p_worker_id: w.id });
    $("delete-dialog").close();
    await ensureData(true);
    toast(`${w.display_name} deleted`);
    location.hash = "#/workers";
  } catch (err) {
    $("delete-error").textContent = friendly(err);
  }
});

$("export-csv-btn").addEventListener("click", () => {
  const w = state.currentWorker;
  if (!w) return;
  const rows = state.byWorker.get(w.id) || [];
  downloadCsv(`${w.worker_code}-progress.csv`, ["completed_at", "training", "part", "passed", "score", "elapsed_seconds"],
    rows.map((x) => [x.completed_at, moduleLabel(x.module), partLabel(x), x.passed, scoreText(x), x.elapsed_seconds]));
});

// ------------------------------------------------------------ certificates

$("f-cert-module").addEventListener("change", renderCertificates);

function certificateRows() {
  const filter = $("f-cert-module").value;
  const rows = [];
  for (const w of state.workers) {
    const certs = state.certs.get(w.id);
    for (const m of MODULE_KEYS) {
      if ((filter === "all" || filter === m) && certs[m].certifiedAt) {
        const issued = state.issuedCertificates.find((x) => x.worker_id === w.id && x.module === m) || null;
        rows.push({ w, m, c: certs[m], issued });
      }
    }
  }
  return rows.sort((a, b) => Date.parse(b.issued?.issued_at || b.c.certifiedAt) - Date.parse(a.issued?.issued_at || a.c.certifiedAt));
}

function renderCertificates() {
  const rows = certificateRows();
  $("certs-body").replaceChildren(...rows.map(({ w, m, c, issued }) => el("tr", { class: "clickable", onclick: () => (location.hash = `#/worker/${w.id}`) },
    el("td", {}, fmtDate(issued?.issued_at || c.certifiedAt)),
    el("td", { class: "mono" }, w.worker_code),
    el("td", {}, w.display_name),
    el("td", {}, moduleLabel(m)),
    el("td", { class: "num" }, `${issued?.score ?? c.certifiedTotal}/100`),
    el("td", {}, el("span", { class: issued ? "badge pass" : "badge warn" }, issued ? "Synced & verifiable" : "Passed · awaiting issue")))));
  $("certs-empty").hidden = rows.length > 0;
  $("certs-count").textContent = `${rows.length} certificate${rows.length === 1 ? "" : "s"}`;
}

$("export-certs-btn").addEventListener("click", () => {
  downloadCsv("abhyasar-certificates.csv", ["certified_at", "worker_id", "name", "training", "total_score", "certificate_id", "verification_url", "sync_status"],
    certificateRows().map(({ w, m, c, issued }) => [issued?.issued_at || c.certifiedAt, w.worker_code, w.display_name, moduleLabel(m), issued?.score ?? c.certifiedTotal,
      issued?.id || "", issued ? `${location.origin}${location.pathname}#/verify/${issued.id}` : "", issued ? "synced" : "awaiting_issue"]));
});

async function renderPublicCertificate(certificateId) {
  const out = $("public-certificate-result");
  const validId = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(certificateId);
  if (!validId) {
    out.replaceChildren(el("div", { class: "public-status invalid" }, el("span", { class: "status-mark" }, "×"),
      el("h1", {}, "Invalid certificate link"), el("p", {}, "The certificate ID is invalid.")));
    return;
  }
  try {
    const result = await rpc("verify_certificate", { p_certificate_id: certificateId });
    if (!result?.valid) {
      out.replaceChildren(el("div", { class: "public-status pending" }, el("span", { class: "status-mark" }, "…"),
        el("h1", {}, "Certificate not found"),
        el("p", {}, "The certificate may still be waiting to sync."),
        el("p", { class: "certificate-id" }, `ID ${certificateId}`)));
      return;
    }
    out.replaceChildren(el("div", { class: "public-status valid" }, el("span", { class: "status-mark" }, "✓"),
      el("h1", {}, "Certificate verified"),
      el("p", { class: "public-lead" }, "Issued by AbhyasAR."),
      el("dl", {},
        el("dt", {}, "Trainee"), el("dd", {}, result.trainee_name),
        el("dt", {}, "Training"), el("dd", {}, moduleLabel(result.module)),
        el("dt", {}, "Score"), el("dd", {}, `${result.score}/100`),
        el("dt", {}, "Issued"), el("dd", {}, fmtDate(result.issued_at)),
        el("dt", {}, "Certificate ID"), el("dd", { class: "mono" }, result.certificate_id))));
  } catch (err) {
    out.replaceChildren(el("div", { class: "public-status invalid" }, el("span", { class: "status-mark" }, "!"),
      el("h1", {}, "Verification unavailable"), el("p", {}, "Please try again.")));
  }
}

$("verify-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const out = $("verify-result");
  const cert = parseCertificate($("verify-input").value);
  if (!cert) {
    out.replaceChildren(el("div", { class: "verify-box bad" }, el("h3", {}, "✗ Not a AbhyasAR certificate"),
      el("p", {}, "Check the pasted certificate code.")));
    return;
  }
  let checksumOk = false;
  try { checksumOk = await checksumMatches(cert); } catch { /* WebCrypto needs https or localhost */ }
  const facts = el("dl", {},
    el("dt", {}, "Name"), el("dd", {}, cert.name),
    el("dt", {}, "Training"), el("dd", {}, cert.module ? moduleLabel(cert.module) : cert.moduleName),
    el("dt", {}, "Score on certificate"), el("dd", {}, `${cert.score}/100`),
    el("dt", {}, "Issued"), el("dd", {}, fmtDate(cert.issuedAt)));
  if (!checksumOk) {
    out.replaceChildren(el("div", { class: "verify-box bad" }, el("h3", {}, "✗ Invalid certificate"),
      el("p", {}, "The certificate code was altered or is invalid."), facts));
    return;
  }
  await ensureData();
  const match = matchCertificate(cert, state.workers, state.byWorker);
  if (match.exact) {
    const w = match.worker;
    out.replaceChildren(el("div", { class: "verify-box ok" }, el("h3", {}, "✓ Verified against training records"),
      el("p", {}, "Matches ", el("a", { href: `#/worker/${w.id}` }, `${w.display_name} (${w.worker_code})`), "."), facts));
  } else {
    out.replaceChildren(el("div", { class: "verify-box warn" }, el("h3", {}, "⚠ Not confirmed — no matching training record"),
      el("p", {}, match.nameMatches
        ? "No matching passed training was found."
        : "No worker with this name exists in the dashboard."),
      el("p", { class: "small" }, "A valid code alone is not proof. Training records may still be waiting to sync."),
      facts));
  }
});

// ------------------------------------------------------------ admins

async function renderAdmins() {
  const admins = (await rpc("admin_list_admins")) || [];
  $("admins-body").replaceChildren(...admins.map((a) => {
    const remove = a.is_me ? el("span", { class: "muted small" }, "You") : el("button", { class: "ghost small danger" }, "Remove");
    if (!a.is_me) {
      remove.addEventListener("click", async () => {
        if (!confirm(`Remove admin access for ${a.email}? Their Supabase account is kept.`)) return;
        try {
          await rpc("admin_remove_admin", { p_user_id: a.user_id });
          toast("Admin removed");
          renderAdmins();
        } catch (err) {
          toast(friendly(err));
        }
      });
    }
    return el("tr", {}, el("td", {}, a.email), el("td", {}, fmtDate(a.added_at, false)), el("td", {}, fmtDate(a.last_sign_in_at)), el("td", { class: "num" }, remove));
  }));
}

$("add-admin-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("add-admin-error").textContent = "";
  try {
    await rpc("admin_add_admin", { p_email: $("add-admin-email").value });
    $("add-admin-email").value = "";
    toast("Admin added");
    renderAdmins();
  } catch (err) {
    $("add-admin-error").textContent = friendly(err);
  }
});

// ------------------------------------------------------------ routing

async function route(keepScroll = false) {
  if (!configured) {
    showView("login");
    $("login-form").replaceChildren(el("h1", {}, "Dashboard not configured"),
      el("p", {}, "Fill in your Supabase project URL and anon key in ", el("span", { class: "mono" }, "dashboard/config.js"), " (see the README)."));
    return;
  }
  const hash = location.hash || "#/overview";
  if (hash.startsWith("#/verify/")) {
    $("session-box").hidden = true;
    $("nav").hidden = true;
    showView("public-certificate");
    await renderPublicCertificate(decodeURIComponent(hash.slice("#/verify/".length)));
    return;
  }
  if (!(await requireAdmin())) {
    showView("login");
    return;
  }
  try {
    await ensureData();
    if (hash.startsWith("#/worker/")) {
      showView("worker");
      renderWorker(decodeURIComponent(hash.slice("#/worker/".length)));
    } else if (hash === "#/workers") {
      showView("workers");
      renderWorkers();
    } else if (hash === "#/certificates") {
      showView("certificates");
      renderCertificates();
    } else if (hash === "#/admins") {
      showView("admins");
      await renderAdmins();
    } else {
      showView("overview");
      renderOverview();
    }
    if (!keepScroll) window.scrollTo(0, 0);
  } catch (err) {
    toast(friendly(err));
  }
}

window.addEventListener("hashchange", () => route());
route();
