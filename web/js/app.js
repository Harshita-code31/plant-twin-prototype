import { CONFIG } from "./config.js";
import { t, L, getLang, setLang } from "./i18n.js";
import { SCENARIOS, SENSORS, EQUIPMENT, ACTIONS, SKILLS, getScenario } from "./scenarios.js";
import { Drill } from "./drill.js";
import { Twin } from "./twin.js";
import { getAttempts, addAttempt, getAttempt, updateAttempt, newId, load, save } from "./store.js";
import { SKILL_ORDER, skillProgress, timeToCompetency, nextDue } from "./competency.js";
import { loadSops, sopSection } from "./sop.js";
import { listEntries, addEntry, isShared, missing, MAX } from "./vault.js";

// Anubhav: learn a rare event on the twin from the SOP, practise it as a drill, debrief it with a
// mentor, and read (or record) what people who handled it for real noticed and did.

const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const mmss = (sec) => {
  const s = Math.max(0, Math.floor(sec));
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
};
const fmtDate = (d, withTime = false) =>
  new Date(d).toLocaleString(getLang() === "hi" ? "hi-IN" : "en-IN", withTime ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" });

/** Link to the SOP copilot, pre-filling a question in the current language (copilot reads q, lang, scenario). */
function copilotLink(question, scenarioId) {
  const url = new URL(CONFIG.copilotUrl, location.href);
  url.searchParams.set("lang", getLang());
  if (question) url.searchParams.set("q", question);
  if (scenarioId) url.searchParams.set("scenario", scenarioId);
  return url.toString();
}

/** Renders a drill message: an i18n key reference, a { en, hi } object, or a plain string. */
function msgText(msg) {
  if (!msg) return "";
  if (msg.key) {
    const vars = {};
    for (const [k, v] of Object.entries(msg.vars || {})) vars[k] = L(v);
    return t(msg.key, vars);
  }
  return L(msg);
}

const app = {
  mode: "page", // page | lesson | drill
  drill: null,
  lesson: null,
  twin: null,
  raf: 0,
  lastFrame: 0,
  hudClock: 0,
  trendClock: 0,
  prev: {},
  trend: {},
  lastAttemptId: load("lastAttempt", null),
  sound: load("sound", true),
  exitArmed: 0,
  hintText: null,
  selected: null,
  vault: [],
};

// ------------------------------------------------------------------ sound
const siren = {
  ctx: null,
  osc: null,
  gain: null,
  timer: 0,
  unlock() {
    try {
      this.ctx = this.ctx || new (window.AudioContext || window.webkitAudioContext)();
      this.ctx.resume?.();
    } catch {
      this.ctx = null;
    }
  },
  start() {
    if (!this.ctx || !app.sound || this.osc) return;
    this.osc = this.ctx.createOscillator();
    this.gain = this.ctx.createGain();
    this.osc.type = "square";
    this.gain.gain.value = 0.035;
    this.osc.connect(this.gain).connect(this.ctx.destination);
    this.osc.start();
    let hi = false;
    this.timer = setInterval(() => {
      hi = !hi;
      this.osc?.frequency.setValueAtTime(hi ? 880 : 640, this.ctx.currentTime);
    }, 450);
  },
  stop() {
    clearInterval(this.timer);
    try {
      this.osc?.stop();
    } catch {
      /* already stopped */
    }
    this.osc = null;
  },
};

// ------------------------------------------------------------------ twin and render loop
function ensureTwin(host, labels) {
  if (!app.twin) app.twin = new Twin(host, labels, (id) => onPick(id));
  else app.twin.attach(host, labels);
  return app.twin;
}

function startLoop() {
  cancelAnimationFrame(app.raf);
  app.lastFrame = performance.now();
  app.raf = requestAnimationFrame(loop);
}

function loop(now) {
  const dt = Math.min(0.1, (now - app.lastFrame) / 1000);
  app.lastFrame = now;
  const d = app.drill;
  if (d) {
    d.tick(dt);
    if (d.s.acked || d.ended) siren.stop();
    app.trendClock += dt;
    if (app.trendClock >= 1.5) {
      app.trendClock = 0;
      for (const sn of SENSORS) {
        const v = d.s[sn.id];
        const p = app.prev[sn.id];
        const span = (sn.hihi ?? sn.hi ?? 100) * 0.01;
        app.trend[sn.id] = p == null ? "" : v - p > span ? "↑" : p - v > span ? "↓" : "";
        app.prev[sn.id] = v;
      }
    }
    app.hudClock += dt;
    if (app.hudClock >= 0.15) {
      app.hudClock = 0;
      updateHud();
    }
  }
  if (app.twin && (d || app.mode !== "page" || $("#home-twin"))) app.twin.frame(dt, d);
  app.raf = requestAnimationFrame(loop);
}

// ------------------------------------------------------------------ chrome
function renderAppbar() {
  const view = currentView();
  const tab = (href, key, id) => `<a class="tab${view === id ? " active" : ""}" href="${href}"${view === id ? ' aria-current="page"' : ""}>${t(key)}</a>`;
  const copilot = CONFIG.copilotUrl ? `<a class="tab" href="${esc(copilotLink("", ""))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>` : "";
  $("#appbar").innerHTML = `
    <div class="masthead">
      <a class="brand" href="#home" aria-label="Anubhav">
        <span class="brand-en">ANUBHAV</span>
        <span class="brand-hi" lang="hi">अनुभव</span>
      </a>
      <span class="brand-sub">${CONFIG.unit.tag} · ${esc(L(CONFIG.unit.name))}</span>
      <button class="btn small" id="lang-btn" type="button" lang="${getLang() === "en" ? "hi" : "en"}">${t("lang_toggle")}</button>
    </div>
    <nav class="tabs">
      ${tab("#home", "nav_home", "home")}
      ${tab("#vault", "nav_vault", "vault")}
      ${copilot}
      ${tab("#progress", "nav_progress", "progress")}
    </nav>`;
  $("#lang-btn").onclick = () => {
    setLang(getLang() === "en" ? "hi" : "en");
    route();
  };
}

function currentView() {
  const v = location.hash.replace("#", "") || "home";
  return v === "map" ? "progress" : v;
}

function route() {
  if (app.mode !== "page") {
    renderModeChrome();
    return;
  }
  renderAppbar();
  const view = currentView();
  window.scrollTo(0, 0);
  if (view === "vault") renderVault();
  else if (view === "progress") renderProgress();
  else if (view === "debrief") renderDebrief();
  else renderHome();
}

// ------------------------------------------------------------------ home: twin, scenarios, vault, copilot
function stageOf(id) {
  const attempts = getAttempts().filter((a) => a.scenarioId === id);
  const learnt = load(`learnt.${id}`, null);
  const best = attempts.length ? Math.max(...attempts.map((a) => a.score)) : null;
  return { attempts, learnt, best, due: nextDue(getAttempts(), id) };
}

function scenarioStatus(st) {
  if (!st.learnt && !st.attempts.length) return t("status_new");
  if (!st.attempts.length) return t("status_learnt");
  const due = st.due.due ? t("due_now") : t("due_on", { date: fmtDate(st.due.at) });
  return `${t("best_n", { n: st.best })} · ${due}`;
}

function vaultEntryHtml(e, compact = false) {
  const scn = getScenario(e.event);
  const event = scn ? L(scn.title) : t("event_other");
  const who = [e.name, e.years ? t("years_n", { n: e.years }) : "", e.role].filter(Boolean).map(esc).join(", ");
  const badge = e.localOnly ? t("vault_local_badge") : t("vault_unreviewed");
  if (compact) {
    return `
      <li class="log-line">
        <span class="log-date">${esc(fmtDate(e.createdAt))}</span>
        <div><p class="log-text">${esc(e.noticed)}</p><p class="log-who">${who} · ${esc(event)}</p></div>
      </li>`;
  }
  const row = (k, v) => (v ? `<dt>${t(k)}</dt><dd>${esc(v)}</dd>` : "");
  return `
    <article class="log-entry">
      <header>
        <span class="log-date">${esc(fmtDate(e.createdAt))}</span>
        <span class="stamp">${badge}</span>
      </header>
      <h3>${esc(event)}</h3>
      <p class="log-who">${who}</p>
      <dl>
        ${row("f_situation", e.situation)}
        ${row("f_noticed", e.noticed)}
        ${row("f_did", e.did)}
        ${row("f_why", e.why)}
        ${row("f_advice", e.advice)}
      </dl>
    </article>`;
}

function renderHome() {
  const plates = SCENARIOS.map((s, i) => {
    const st = stageOf(s.id);
    return `
      <article class="plate ${s.severity}">
        <div class="plate-band" aria-hidden="true"></div>
        <div class="plate-body">
          <div class="plate-ref"><span>${t("scenario_n", { n: i + 1 })}</span><span>${s.sop.id}</span></div>
          <h3>${esc(L(s.title))}</h3>
          <p>${esc(L(s.summary))}</p>
          <p class="plate-status">${scenarioStatus(st)}</p>
          <div class="plate-actions">
            <button class="btn ${st.learnt ? "" : "primary"}" type="button" data-learn="${s.id}">${t("btn_learn")}</button>
            <button class="btn ${st.learnt ? "primary" : ""}" type="button" data-start="${s.id}">${t("btn_practise")}</button>
          </div>
        </div>
      </article>`;
  }).join("");

  $("#view").innerHTML = `
    <div class="page home">
      <section class="stage">
        <div class="twin-frame" id="home-twin">
          <div class="labels" id="home-labels"></div>
          <p class="twin-caption">${t("twin_caption")}</p>
        </div>
        <aside class="stage-side">
          <div class="equip-card" id="equip-card"></div>
          <div class="vault-call">
            <p class="lesson-kicker">${t("vault_kicker")}</p>
            <h3>${t("vault_title")}</h3>
            <p>${t("vault_call")}</p>
            <a class="btn primary" href="#vault">${t("vault_add_btn")}</a>
          </div>
        </aside>
      </section>

      <section class="section">
        <div class="rule-head"><h2>${t("scenarios_title")}</h2></div>
        <ol class="stages">
          <li><b>${t("stage1_t")}</b><span>${t("stage1_d")}</span></li>
          <li><b>${t("stage2_t")}</b><span>${t("stage2_d")}</span></li>
          <li><b>${t("stage3_t")}</b><span>${t("stage3_d")}</span></li>
        </ol>
        <div class="plates">${plates}</div>
      </section>

      <section class="section">
        <div class="rule-head"><h2>${t("vault_title")}</h2><a href="#vault">${t("vault_read_all")}</a></div>
        <ul class="log" id="vault-latest"><li class="muted">${t("loading")}</li></ul>
      </section>

      ${CONFIG.copilotUrl ? `
      <section class="section">
        <div class="rule-head"><h2>${t("ask_title")}</h2></div>
        <form class="ask" id="ask-form">
          <label class="visually-hidden" for="ask-q">${t("ask_title")}</label>
          <input id="ask-q" type="text" placeholder="${esc(t("ask_ph"))}" autocomplete="off">
          <button class="btn primary" type="submit">${t("ask_btn")}</button>
        </form>
        <p class="muted small">${t("ask_note")}</p>
      </section>` : ""}

      <p class="site-note">${t("site_note")}</p>
    </div>`;

  showEquipmentCard(null);
  ensureTwin($("#home-twin"), $("#home-labels"));
  app.twin.reset();
  app.twin.setAutoRotate(true);
  startLoop();

  document.querySelectorAll("[data-learn]").forEach((b) => (b.onclick = () => startLesson(b.dataset.learn)));
  document.querySelectorAll("[data-start]").forEach((b) => (b.onclick = () => startDrill(b.dataset.start)));
  const ask = $("#ask-form");
  if (ask) {
    ask.onsubmit = (e) => {
      e.preventDefault();
      const q = $("#ask-q").value.trim();
      window.open(copilotLink(q, ""), "_blank", "noopener") || (location.href = copilotLink(q, ""));
    };
  }
  listEntries().then((entries) => {
    app.vault = entries;
    const el = $("#vault-latest");
    if (!el) return;
    el.innerHTML = entries.length
      ? entries.slice(0, 3).map((e) => vaultEntryHtml(e, true)).join("")
      : `<li class="empty">${t("vault_empty_home")}</li>`;
  });
}

/** What a piece of equipment is, and which SOP steps use it. */
function showEquipmentCard(id) {
  const card = $("#equip-card");
  if (!card) return;
  if (!id || !EQUIPMENT[id]) {
    card.innerHTML = `<h3>${t("twin_intro_t")}</h3><p>${t("twin_intro")}</p>`;
    return;
  }
  const eq = EQUIPMENT[id];
  const uses = [];
  for (const s of SCENARIOS) {
    s.steps.forEach((st, i) => {
      if (st.on.some((k) => k.startsWith(`${id}.`))) uses.push(`<li>${esc(L(s.title))}: <b>${t("step_n", { n: i + 1 })}</b> ${esc(L(st.label))} <span class="ref">${s.sop.id} §${st.sop}</span></li>`);
    });
    if (s.cues[id]) uses.push(`<li>${esc(L(s.title))}: <b>${t("warning_sign")}</b> ${esc(L(s.cues[id]))}</li>`);
  }
  card.innerHTML = `
    <p class="tagplate">${eq.tag}</p>
    <h3>${esc(L(eq.name))}</h3>
    ${uses.length ? `<p class="small muted">${t("used_in")}</p><ul class="uses">${uses.join("")}</ul>` : `<p class="muted">${t("not_in_scenarios")}</p>`}`;
}

// ------------------------------------------------------------------ lesson: the steps, taught on the twin
function lessonSteps(scn) {
  const steps = [{ kind: "intro" }];
  const cueIds = Object.keys(scn.cues);
  if (cueIds.length) steps.push({ kind: "cues", target: cueIds[0] });
  scn.steps.forEach((st, i) => steps.push({ kind: "step", step: st, n: i + 1, target: st.on[0].split(".")[0], action: st.on[0].split(".")[1] }));
  steps.push({ kind: "ready" });
  return steps;
}

function startLesson(id) {
  const scn = getScenario(id);
  if (!scn) return;
  app.mode = "lesson";
  app.drill = null;
  app.lesson = { scn, steps: lessonSteps(scn), i: 0 };
  enterFullView();
  app.twin.reset();
  app.twin.setAutoRotate(false);
  loadSops().then(() => renderLessonStep());
  renderModeChrome();
  startLoop();
}

function enterFullView() {
  $("#appbar").hidden = true;
  $("#view").hidden = true;
  $("#drill").hidden = false;
  ensureTwin($("#drill"), $("#labels"));
  app.twin.resize();
}

function renderLessonStep() {
  const ls = app.lesson;
  if (!ls) return;
  const { scn } = ls;
  const step = ls.steps[ls.i];
  const total = ls.steps.length;
  const el = $("#prompt");
  el.hidden = false;
  el.classList.add("lesson");
  app.twin.select(step.target || null);
  if (step.target) app.twin.focus(step.target);
  else app.twin.resetView();

  let body = "";
  if (step.kind === "intro") {
    body = `
      <p class="lesson-kicker">${t("lesson_kicker")} · ${scn.sop.id}</p>
      <h2>${esc(L(scn.title))}</h2>
      <p>${esc(L(scn.briefing))}</p>
      <p class="muted">${t("lesson_intro_note", { n: scn.steps.length })}</p>`;
  } else if (step.kind === "cues") {
    const items = Object.entries(scn.cues).map(([id, text]) => `<li><b>${EQUIPMENT[id].tag}</b> ${esc(L(EQUIPMENT[id].name))}: ${esc(L(text))}</li>`).join("");
    body = `
      <p class="lesson-kicker">${t("lesson_cues_kicker")}</p>
      <h2>${t("lesson_cues_title")}</h2>
      <ul class="cue-list">${items}</ul>
      <p class="muted">${esc(L(scn.walkdownHint))}</p>`;
  } else if (step.kind === "step") {
    const st = step.step;
    const ref = sopSection(scn.sop.id, st.sop);
    const eq = EQUIPMENT[step.target];
    body = `
      <p class="lesson-kicker">${t("step_of", { n: step.n, total: scn.steps.length })}${st.critical ? "" : ` · ${t("optional")}`}</p>
      <h2>${esc(L(st.label))}</h2>
      ${ref ? `<blockquote class="sop-quote"><p>${esc(getLang() === "hi" ? ref.section.hi : ref.section.en)}</p><cite>${ref.doc.id} §${ref.section.number}, ${esc(ref.section.heading)} · ${esc(ref.doc.revision)}</cite></blockquote>` : ""}
      <p><b>${t("why_label")}</b> ${esc(L(st.why))}</p>
      <p class="how">${t("how_label")} <b>${eq.tag}</b> ${esc(L(eq.name))} → <b>${esc(L(ACTIONS[step.action]))}</b></p>`;
  } else {
    const vault = (app.vault || []).filter((e) => e.event === scn.id).slice(0, 2);
    body = `
      <p class="lesson-kicker">${t("lesson_ready_kicker")}</p>
      <h2>${t("lesson_ready_title")}</h2>
      <p>${t("lesson_ready_body")}</p>
      ${vault.length ? `<div class="lesson-vault"><p class="small"><b>${t("from_vault")}</b></p><ul class="log">${vault.map((e) => vaultEntryHtml(e, true)).join("")}</ul></div>` : ""}`;
  }

  el.innerHTML = `
    ${body}
    <div class="row lesson-nav">
      <span class="lesson-count">${ls.i + 1} / ${total}</span>
      <span class="btns">
        <button class="btn small" id="lesson-back" type="button" ${ls.i === 0 ? "disabled" : ""}>${t("back")}</button>
        ${step.kind === "ready"
          ? `<button class="btn small primary" id="lesson-drill" type="button">${t("btn_practise")}</button>`
          : `<button class="btn small primary" id="lesson-next" type="button">${t("next")}</button>`}
      </span>
    </div>`;
  $("#lesson-back").onclick = () => {
    ls.i = Math.max(0, ls.i - 1);
    renderLessonStep();
  };
  const next = $("#lesson-next");
  if (next) next.onclick = () => {
    ls.i = Math.min(total - 1, ls.i + 1);
    renderLessonStep();
  };
  const go = $("#lesson-drill");
  if (go) go.onclick = () => {
    save(`learnt.${scn.id}`, new Date().toISOString());
    startDrill(scn.id);
  };
}

// ------------------------------------------------------------------ drill
function startDrill(id) {
  const scn = getScenario(id);
  if (!scn) return;
  app.mode = "drill";
  app.lesson = null;
  app.drill = new Drill(scn);
  app.prev = {};
  app.trend = {};
  app.hintText = null;
  app.selected = null;
  $("#prompt").classList.remove("lesson");
  enterFullView();
  app.twin.reset();
  loadSops();

  app.drill.onEvent = () => {
    app.hintText = null;
    siren.start();
    renderPrompt();
  };
  app.drill.onEnd = (result) => {
    siren.stop();
    closeSheet();
    const attempt = { id: newId(), date: new Date().toISOString(), ...result };
    addAttempt(attempt);
    app.lastAttemptId = attempt.id;
    save("lastAttempt", attempt.id);
    if (result.failed) {
      const flash = document.createElement("div");
      flash.className = "fireflash";
      $("#drill").appendChild(flash);
      setTimeout(() => flash.remove(), 1700);
    }
    setTimeout(() => showEndOverlay(result), result.failed ? 1400 : 600);
  };

  renderModeChrome();
  showBriefing();
  startLoop();
}

function renderModeChrome() {
  const lesson = app.mode === "lesson";
  const d = app.drill;
  const scn = lesson ? app.lesson?.scn : d?.scn;
  if (!scn) return;
  $("#hud-top").innerHTML = `
    <div class="hud-title">
      <span class="hud-kicker" id="phase-chip">${lesson ? t("lesson_kicker") : ""}</span>
      <b>${esc(L(scn.title))}</b>
    </div>
    <div class="hud-controls">
      ${lesson ? "" : `<span class="timer" id="timer">00:00</span>`}
      <button class="btn small" id="lang-drill" type="button">${t("lang_toggle")}</button>
      ${lesson ? "" : `<button class="btn small" id="sound-btn" type="button" aria-pressed="${app.sound}">${app.sound ? t("sound_on") : t("sound_off")}</button>`}
      <button class="btn small danger" id="exit-btn" type="button">${t("exit")}</button>
    </div>`;
  $("#sensors").hidden = lesson;
  $("#lang-drill").onclick = () => {
    setLang(getLang() === "en" ? "hi" : "en");
    renderModeChrome();
    if (lesson) renderLessonStep();
    if (d && !$("#overlay").hidden && d.phase === "briefing") showBriefing();
    if (d && !$("#overlay").hidden && d.ended) showEndOverlay(d.result);
  };
  const sound = $("#sound-btn");
  if (sound) sound.onclick = () => {
    app.sound = !app.sound;
    save("sound", app.sound);
    if (!app.sound) siren.stop();
    else if (d?.phase === "response" && !d.s.acked) siren.start();
    renderModeChrome();
  };
  $("#exit-btn").onclick = (e) => {
    const now = performance.now();
    if (lesson || now - app.exitArmed < 2500 || d?.ended) {
      exitFullView();
      return;
    }
    app.exitArmed = now;
    e.currentTarget.textContent = t("exit_confirm");
    setTimeout(() => {
      const b = $("#exit-btn");
      if (b) b.textContent = t("exit");
    }, 2500);
  };
  if (lesson) {
    $("#alarm").hidden = true;
    return;
  }
  $("#sensors-title").textContent = t("sensors");
  renderPrompt();
  if (app.selected) openSheet(app.selected);
  updateHud();
}

function exitFullView() {
  siren.stop();
  app.drill = null;
  app.lesson = null;
  app.mode = "page";
  closeSheet();
  $("#overlay").hidden = true;
  $("#prompt").hidden = true;
  $("#prompt").classList.remove("lesson");
  $("#alarm").hidden = true;
  $("#drill").hidden = true;
  $("#appbar").hidden = false;
  $("#view").hidden = false;
  route();
}

function updateHud() {
  const d = app.drill;
  if (!d) return;
  const s = d.s;
  const phaseKey = { briefing: "phase_briefing", walkdown: "phase_walkdown", response: "phase_response", ended: "phase_ended" }[d.phase];
  $("#phase-chip").textContent = t(phaseKey);
  $("#timer").textContent = d.phase === "response" || d.eventT != null ? mmss(d.rt()) : mmss(d.t);
  $("#timer").classList.toggle("hot", d.phase === "response");

  const banner = $("#alarm");
  if (d.eventT != null && s.alarm && !(d.ended && !d.failed)) {
    banner.hidden = false;
    banner.textContent = L(d.scn.eventBanner);
    banner.classList.toggle("flash", !s.acked && !d.ended);
  } else banner.hidden = true;

  $("#sensor-rows").innerHTML = SENSORS.map((sn) => {
    const v = s[sn.id];
    let tone = "";
    if (sn.hihi != null && v >= sn.hihi) tone = "alarm";
    else if (sn.hi != null && v >= sn.hi) tone = "warn";
    if (sn.lolo != null && v <= sn.lolo) tone = "alarm";
    else if (sn.lo != null && v <= sn.lo) tone = tone || "warn";
    return `<div class="sensor-row ${tone}">
      <span class="name" title="${esc(L(sn.name))}"><span class="full">${esc(L(sn.name))}</span><small>${sn.tag}</small></span>
      <span class="val">${Number(v).toFixed(sn.dp)}<small>${sn.unit}</small></span>
      <span class="trend">${app.trend[sn.id] || ""}</span>
    </div>`;
  }).join("");

  const pct = (x) => `${Math.round(x * 100)}%`;
  const valveTone = (x, wantClosed) => (d.phase !== "response" ? "" : wantClosed && x > 0.02 ? "warn" : "");
  const tw = app.twin;
  tw.setLabel("suction_valve", pct(s.sdv101), valveTone(s.sdv101, s.esd || s.tripped));
  tw.setLabel("discharge_valve", pct(s.sdv102), valveTone(s.sdv102, s.esd || s.tripped));
  tw.setLabel("blowdown_valve", s.bdv ? "OPEN" : "CLOSED", s.bdv ? "warn" : "");
  tw.setLabel("detector", `${Math.round(s.lel)}% LEL`, s.lel >= 40 ? "alarm" : s.lel >= 20 ? "warn" : "");
  tw.setLabel("compressor", s.rpm > 50 ? `${Math.round(s.rpm)} rpm` : "STOPPED", s.tripped ? "alarm" : "");
  tw.setLabel("panel", s.esd ? "ESD" : s.tripped ? "TRIP" : "", s.esd || s.tripped ? "alarm" : "");
  tw.setLabel("lube", `${s.lo.toFixed(1)} bar`, s.lo <= 1.8 ? "warn" : "");

  if (d.phase === "walkdown") {
    const c = $("#walk-count");
    if (c) c.textContent = d.pendingEventAt != null ? "" : t("walkdown_auto", { n: Math.ceil(d.walkdownRemaining()) });
  }
}

function hintHtml(hint, scn) {
  if (!hint) return "";
  if (hint.advice) return `<p class="hint-text">${esc(L(hint.advice))}</p>`;
  const st = hint.step;
  const ref = sopSection(scn.sop.id, st.sop);
  const text = ref ? (getLang() === "hi" ? ref.section.hi : ref.section.en) : L(st.label);
  return `<p class="hint-text"><b>${t("next_step")}: ${esc(L(st.label))}.</b> ${esc(text)} <span class="ref">${scn.sop.id} §${st.sop}</span></p>`;
}

function renderPrompt() {
  const d = app.drill;
  const el = $("#prompt");
  if (!d || d.phase === "briefing" || d.ended) {
    el.hidden = true;
    return;
  }
  el.hidden = false;
  const hint = hintHtml(app.hintText, d.scn);
  const askBtn = CONFIG.copilotUrl ? `<a class="btn small" href="${esc(copilotLink(L(d.scn.copilotQuestion), d.scn.id))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>` : "";
  if (d.phase === "walkdown") {
    el.innerHTML = `
      <p>${t("walkdown_prompt")}</p>
      ${hint}
      <div class="row">
        <span class="muted mono" id="walk-count"></span>
        <span class="btns">
          <button class="btn small" id="hint-btn" type="button">${t("hint_btn")}</button>
          ${askBtn}
          <button class="btn small primary" id="finish-btn" type="button" ${d.pendingEventAt != null ? "disabled" : ""}>${t("finish_walkdown")}</button>
        </span>
      </div>`;
    $("#finish-btn").onclick = () => {
      d.finishWalkdown();
      renderPrompt();
    };
  } else {
    el.innerHTML = `
      <p><b>${esc(L(d.scn.responsePrompt))}</b></p>
      ${hint}
      <div class="row">
        <span class="muted">${d.hints ? t("hints_n", { n: d.hints }) : ""}</span>
        <span class="btns">
          ${askBtn}
          <button class="btn small" id="hint-btn" type="button">${t("hint_btn")}</button>
        </span>
      </div>`;
  }
  $("#hint-btn").onclick = () => {
    app.hintText = d.hint();
    renderPrompt();
  };
}

function showBriefing() {
  const d = app.drill;
  const ov = $("#overlay");
  ov.hidden = false;
  ov.innerHTML = `
    <div class="card" role="dialog" aria-labelledby="brief-title">
      <p class="lesson-kicker">${d.scn.sop.id} · ${t("phase_briefing")}</p>
      <h2 id="brief-title">${esc(L(d.scn.title))}</h2>
      <p>${esc(L(d.scn.briefing))}</p>
      <p class="muted">${t("rotate_hint")}</p>
      <div class="btns">
        <button class="btn primary" id="begin-btn" type="button">${t("begin_walkdown")}</button>
        ${load(`learnt.${d.scn.id}`, null) ? "" : `<button class="btn" id="learn-first" type="button">${t("learn_first")}</button>`}
      </div>
    </div>`;
  $("#begin-btn").onclick = () => {
    siren.unlock();
    d.begin();
    ov.hidden = true;
    renderPrompt();
  };
  const lf = $("#learn-first");
  if (lf) lf.onclick = () => {
    ov.hidden = true;
    startLesson(d.scn.id);
  };
}

function showEndOverlay(result) {
  const d = app.drill;
  if (!d) return;
  const ov = $("#overlay");
  $("#toast").hidden = true;
  const title = result.passed ? t("drill_over_pass") : result.failed ? t("drill_over_fail") : t("drill_over_done");
  ov.hidden = false;
  ov.innerHTML = `
    <div class="card ${result.passed ? "pass" : result.failed ? "fail" : ""}" role="dialog" aria-labelledby="end-title">
      <p class="lesson-kicker">${esc(L(d.scn.title))}</p>
      <h2 id="end-title">${title}</h2>
      ${result.failed ? `<p>${esc(L(result.failed))}</p>` : ""}
      <p><span class="score-big">${result.score}</span><span class="muted"> / 100</span></p>
      <button class="btn primary" id="debrief-btn" type="button">${t("see_debrief")}</button>
    </div>`;
  $("#debrief-btn").onclick = () => {
    exitFullView();
    location.hash = "#debrief";
    route();
  };
}

function onPick(id) {
  if (app.mode === "page") {
    app.twin.select(id && EQUIPMENT[id] ? id : null);
    showEquipmentCard(id);
    return;
  }
  if (app.mode === "lesson") return;
  const d = app.drill;
  if (!d || d.ended || d.phase === "briefing" || !$("#overlay").hidden) return;
  if (!id || !EQUIPMENT[id]) {
    closeSheet();
    return;
  }
  openSheet(id);
}

function openSheet(id) {
  const d = app.drill;
  const eq = EQUIPMENT[id];
  const actions = d.actionsFor(id);
  if (!actions.length) {
    closeSheet();
    return;
  }
  app.selected = id;
  app.twin.select(id);
  const sheet = $("#sheet");
  sheet.hidden = false;
  sheet.innerHTML = `
    <div class="sheet-head">
      <div>
        <p class="tagplate">${eq.tag}</p>
        <h3>${esc(L(eq.name))}</h3>
      </div>
      <button class="btn small" id="sheet-close" type="button">${t("close")}</button>
    </div>
    ${eq.valve && d.phase === "response" ? `<p class="muted small">${t("sheet_note_valve")}</p>` : ""}
    <div class="sheet-actions">
      ${actions.map((a) => `<button class="btn ${a === "esd" ? "esd" : ""}" type="button" data-act="${a}">${esc(L(ACTIONS[a]))}</button>`).join("")}
    </div>`;
  $("#sheet-close").onclick = closeSheet;
  sheet.querySelectorAll("[data-act]").forEach((b) => {
    b.onclick = () => {
      const res = d.act(id, b.dataset.act);
      closeSheet();
      if (res.msg) toast(msgText(res.msg), res.tone);
      renderPrompt();
      updateHud();
    };
  });
}

function closeSheet() {
  app.selected = null;
  app.twin?.select(null);
  const sheet = $("#sheet");
  if (sheet) sheet.hidden = true;
}

let toastTimer = 0;
function toast(text, tone = "") {
  const el = $("#toast");
  el.textContent = text;
  el.className = `toast ${tone}`;
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (el.hidden = true), Math.min(7000, 2500 + text.length * 35));
}

// ------------------------------------------------------------------ debrief
function renderDebrief() {
  const a = getAttempt(app.lastAttemptId) || getAttempts()[getAttempts().length - 1];
  if (!a) {
    location.hash = "#home";
    return;
  }
  const scn = getScenario(a.scenarioId);
  loadSops().then(() => {
    if (currentView() === "debrief") paint();
  });
  paint();

  function paint() {
    const bar = (label, v, max) => `
      <div class="bar-row"><span>${label}</span><span class="bar"><i style="width:${Math.max(0, Math.min(100, (100 * v) / max))}%"></i></span><span class="mono">${v}/${max}</span></div>`;

    const rows = scn.steps.map((st, i) => {
      const d = a.done[st.id];
      const ref = sopSection(scn.sop.id, st.sop);
      const ask = CONFIG.copilotUrl ? ` <a href="${esc(copilotLink(L(st.label), scn.id))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>` : "";
      const you = d
        ? `<span class="mono">${d.rt.toFixed(0)} s</span>${d.orderOk ? "" : ` <span class="chip warn">${t("out_of_order")}</span>${ask}`}`
        : `<span class="chip alarm">${t("not_done")}</span>${ask}`;
      return `<tr>
        <td class="num">${i + 1}</td>
        <td>${esc(L(st.label))}${st.critical ? "" : ` <span class="muted">(${t("optional")})</span>`}${!d && ref ? `<p class="small muted">${esc(getLang() === "hi" ? ref.section.hi : ref.section.en)}</p>` : ""}</td>
        <td>${you}</td>
        <td class="num">${scn.sop.id} §${st.sop}</td>
      </tr>`;
    }).join("");

    const cues = Object.entries(scn.cues).map(([id, text]) => {
      const found = a.cuesFound.includes(id);
      return `<li><span class="mark ${found ? "ok" : "alarm"}">${found ? "✓" : "✕"}</span><div><b>${EQUIPMENT[id].tag} · ${esc(L(EQUIPMENT[id].name))}</b> <span class="chip ${found ? "ok" : "alarm"}">${found ? t("cue_found") : t("cue_missed")}</span><p>${esc(L(text))}</p></div></li>`;
    }).join("");
    const falseReps = a.falseReports.map((id) => `<li><span class="mark warn">!</span><div><b>${EQUIPMENT[id].tag} · ${esc(L(EQUIPMENT[id].name))}</b><p>${t("false_report")}</p></div></li>`).join("");
    const mistakes = a.mistakes.length
      ? a.mistakes.map((m) => `<li><span class="mark alarm">✕</span><div><b>${esc(L(m.msg))}</b><p class="muted mono">${m.rt.toFixed(0)} s · −${m.penalty}</p></div></li>`).join("")
      : `<li><span class="mark ok">✓</span><div>${t("no_mistakes")}</div></li>`;
    const skillChips = scn.skills.map((sk) => {
      const v = a.skills[sk];
      return `<span class="chip ${v >= CONFIG.passMark ? "ok" : v >= 50 ? "warn" : "alarm"}">${t(SKILLS[sk])} ${v}</span>`;
    }).join(" ");
    const due = nextDue(getAttempts(), scn.id);
    const vault = (app.vault || []).filter((e) => e.event === scn.id).slice(0, 2);

    $("#view").innerHTML = `
      <div class="page">
        <section class="section">
          <p class="lesson-kicker">${esc(L(scn.title))} · ${esc(fmtDate(a.date, true))}</p>
          <h1>${t("debrief_title")}</h1>
          <div class="score-row">
            <div><span class="score-big">${a.score}</span><span class="muted"> / 100</span></div>
            <div class="score-meta">
              <span class="chip solid ${a.passed ? "ok" : "alarm"}">${a.passed ? t("result_pass") : t("result_fail")}</span>
              <span class="muted small">${t("pass_rule", { n: CONFIG.passMark })}</span>
              <span class="small">${due.due ? t("next_practice_now") : t("next_practice_on", { date: fmtDate(due.at) })}</span>
            </div>
          </div>
          <div class="chips">${skillChips}</div>
        </section>

        ${a.failed ? `<section class="panel alarm-panel"><h3>${t("failed_reason")}</h3><p>${esc(L(a.failed))}</p></section>` : ""}

        <section class="section">
          <div class="rule-head"><h2>${t("timeline")}</h2></div>
          <div class="table-wrap">
            <table class="ledger">
              <thead><tr><th>#</th><th>${t("col_step")}</th><th>${t("col_you")}</th><th>${t("col_sop")}</th></tr></thead>
              <tbody>${rows}</tbody>
            </table>
          </div>
        </section>

        <div class="two-col">
          <section class="panel">
            <h3>${t("cues_title")}</h3>
            <ul class="list">${cues}${falseReps}</ul>
          </section>
          <section class="panel">
            <h3>${t("mistakes_title")}</h3>
            <ul class="list">${mistakes}</ul>
            <div class="breakdown">
              ${bar(t("part_observe"), a.parts.observe, 20)}
              ${bar(t("part_steps"), a.parts.steps, 80)}
              <div class="bar-row"><span>${t("part_penalty")}</span><span></span><span class="mono">−${a.parts.penalty}</span></div>
            </div>
          </section>
        </div>

        <section class="panel mentor-panel">
          <h3>${t("mentor_title")}</h3>
          <p>${t("mentor_body")}</p>
          <ol class="questions">${scn.debriefQuestions.map((q) => `<li>${esc(L(q))}</li>`).join("")}</ol>
          <div class="form-grid">
            <div class="field">
              <label for="mentor-name">${t("mentor_name")}</label>
              <input id="mentor-name" type="text" autocomplete="name" value="${esc(a.mentorName || a.signoff?.by || "")}">
            </div>
            <div class="field wide">
              <label for="mentor-notes">${t("mentor_notes")}</label>
              <textarea id="mentor-notes" placeholder="${esc(t("mentor_notes_ph"))}">${esc(a.mentorNotes || "")}</textarea>
            </div>
          </div>
          <div class="btns">
            <button class="btn" id="save-notes" type="button">${t("save_notes")}</button>
            ${a.signoff
              ? `<span class="chip solid ok">${t("signed_off_by", { name: esc(a.signoff.by) })}</span>`
              : a.passed
                ? `<button class="btn primary" id="sign-off" type="button">${t("sign_off")}</button>`
                : `<span class="muted small">${t("sign_off_needs_pass")}</span>`}
            <span id="notes-status" class="muted small" role="status"></span>
          </div>
        </section>

        <section class="panel">
          <h3>${t("vault_title")}</h3>
          ${vault.length ? `<ul class="log">${vault.map((e) => vaultEntryHtml(e, true)).join("")}</ul>` : `<p>${t("debrief_vault_empty")}</p>`}
          <p><a href="#vault">${t("vault_add_btn")}</a></p>
        </section>

        <div class="btns">
          <button class="btn primary" id="retry" type="button">${t("retry")}</button>
          <button class="btn" id="relearn" type="button">${t("btn_learn")}</button>
          <a class="btn" href="#home">${t("to_home")}</a>
        </div>
      </div>`;

    $("#save-notes").onclick = () => {
      updateAttempt(a.id, { mentorNotes: $("#mentor-notes").value, mentorName: $("#mentor-name").value.trim() });
      $("#notes-status").textContent = t("notes_saved");
    };
    const so = $("#sign-off");
    if (so) so.onclick = () => {
      const by = $("#mentor-name").value.trim();
      if (!by) {
        $("#notes-status").textContent = t("mentor_name_needed");
        $("#mentor-name").focus();
        return;
      }
      updateAttempt(a.id, { mentorNotes: $("#mentor-notes").value, mentorName: by, signoff: { by, at: new Date().toISOString() } });
      paint();
    };
    $("#retry").onclick = () => startDrill(a.scenarioId);
    $("#relearn").onclick = () => startLesson(a.scenarioId);
  }
}

// ------------------------------------------------------------------ experience vault
function renderVault() {
  const opts = [...SCENARIOS.map((s) => [s.id, L(s.title)]), ["other", t("event_other")]]
    .map(([v, label]) => `<option value="${v}">${esc(label)}</option>`).join("");
  const field = (id, key, { area = false, req = false, hint = "", type = "text" } = {}) => `
    <div class="field ${area ? "wide" : ""}">
      <label for="v-${id}">${t(key)}${req ? " *" : ""}</label>
      ${hint ? `<span class="hint">${t(hint)}</span>` : ""}
      ${area
        ? `<textarea id="v-${id}" name="${id}" maxlength="${MAX[id]}" ${req ? "required" : ""}></textarea>`
        : `<input id="v-${id}" name="${id}" type="${type}" maxlength="${MAX[id]}" ${req ? "required" : ""} ${type === "number" ? 'min="0" max="60" inputmode="numeric"' : ""}>`}
    </div>`;

  $("#view").innerHTML = `
    <div class="page narrow">
      <section class="section">
        <p class="lesson-kicker">${t("vault_kicker")}</p>
        <h1>${t("vault_title")}</h1>
        <p class="lead">${t("vault_intro")}</p>
      </section>

      <section class="logbook">
        <h2>${t("vault_form_title")}</h2>
        <p class="muted small">${t("vault_form_note")}</p>
        <form id="vault-form" class="form-grid" novalidate>
          ${field("name", "f_name", { req: true })}
          ${field("years", "f_years", { type: "number" })}
          ${field("role", "f_role")}
          <div class="field">
            <label for="v-event">${t("f_event")} *</label>
            <select id="v-event" name="event">${opts}</select>
          </div>
          ${field("situation", "f_situation", { area: true, hint: "h_situation" })}
          ${field("noticed", "f_noticed", { area: true, req: true, hint: "h_noticed" })}
          ${field("did", "f_did", { area: true, req: true, hint: "h_did" })}
          ${field("why", "f_why", { area: true, req: true, hint: "h_why" })}
          ${field("advice", "f_advice", { area: true, hint: "h_advice" })}
          <div class="field wide btns">
            <button class="btn primary" type="submit">${t("vault_submit")}</button>
            <span id="vault-status" class="small" role="status"></span>
          </div>
        </form>
      </section>

      <section class="section">
        <div class="rule-head"><h2>${t("vault_entries_title")}</h2><span class="muted small" id="vault-count"></span></div>
        <div id="vault-list"><p class="muted">${t("loading")}</p></div>
      </section>
    </div>`;

  const paintList = (entries) => {
    app.vault = entries;
    $("#vault-count").textContent = entries.length === 1 ? t("entries_one") : entries.length ? t("entries_n", { n: entries.length }) : "";
    $("#vault-list").innerHTML = entries.length
      ? entries.map((e) => vaultEntryHtml(e)).join("")
      : `<p class="empty">${t("vault_empty")}</p>`;
  };
  listEntries().then(paintList);

  $("#vault-form").onsubmit = async (e) => {
    e.preventDefault();
    const form = e.currentTarget;
    const data = Object.fromEntries(new FormData(form).entries());
    const gaps = missing(Object.fromEntries(Object.entries(data).map(([k, v]) => [k, String(v).trim()])));
    const status = $("#vault-status");
    if (gaps.length) {
      status.className = "small warn-text";
      status.textContent = t("vault_missing", { fields: gaps.map((f) => t(`f_${f}`)).join(", ") });
      form.querySelector(`[name="${gaps[0]}"]`)?.focus();
      return;
    }
    status.className = "small muted";
    status.textContent = t("vault_saving");
    const res = await addEntry(data);
    status.className = "small ok-text";
    status.textContent = res.saved === "shared" ? t("vault_saved_shared") : t("vault_saved_local");
    form.reset();
    listEntries().then(paintList);
  };
}

// ------------------------------------------------------------------ progress (this device)
function renderProgress() {
  const attempts = [...getAttempts()].sort((a, b) => b.date.localeCompare(a.date));
  const skills = skillProgress(attempts);
  const ttc = timeToCompetency(attempts, skills);
  const passRate = attempts.length ? Math.round((100 * attempts.filter((a) => a.passed).length) / attempts.length) : null;
  const statusKey = { none: "legend_none", learning: "legend_learning", practising: "legend_practising", competent: "legend_competent", signed: "legend_signed" };

  const skillRows = SKILL_ORDER.map((sk) => {
    const s = skills[sk];
    return `<tr><td>${t(SKILLS[sk])}</td><td><span class="cellchip ${s.status}">${t(statusKey[s.status])}</span></td><td class="num">${s.last ?? "—"}</td><td class="num">${s.tries}</td></tr>`;
  }).join("");

  const logRows = attempts.map((a) => {
    const scn = getScenario(a.scenarioId);
    return `<tr>
      <td class="num">${esc(fmtDate(a.date, true))}</td>
      <td>${esc(scn ? L(scn.title) : a.scenarioId)}</td>
      <td class="num">${a.score}</td>
      <td>${a.passed ? `<span class="chip ok">${t("result_pass")}</span>` : `<span class="chip alarm">${t("result_fail")}</span>`}</td>
      <td class="num">${a.mistakes?.length ?? 0}</td>
      <td class="num">${a.hints ?? 0}</td>
      <td>${a.signoff ? esc(a.signoff.by) : "—"}</td>
    </tr>`;
  }).join("");

  const first = attempts.length ? attempts[attempts.length - 1].date : null;
  const days = first ? Math.max(0, Math.round((Date.now() - new Date(first).getTime()) / 86400000)) : 0;

  $("#view").innerHTML = `
    <div class="page">
      <section class="section">
        <h1>${t("progress_title")}</h1>
        <p class="lead">${t("progress_intro")}</p>
      </section>
      ${attempts.length ? `
      <section class="facts">
        <p><b>${attempts.length}</b> ${t("facts_drills")}</p>
        <p><b>${passRate}%</b> ${t("facts_pass")}</p>
        <p><b>${ttc != null ? t("days_n", { n: ttc }) : t("day_n", { n: days })}</b> ${ttc != null ? t("facts_ttc_done") : t("facts_ttc_open")}</p>
      </section>
      <section class="section">
        <div class="rule-head"><h2>${t("skills_title")}</h2></div>
        <div class="table-wrap"><table class="ledger">
          <thead><tr><th>${t("col_skill")}</th><th>${t("col_status")}</th><th>${t("col_last")}</th><th>${t("col_tries")}</th></tr></thead>
          <tbody>${skillRows}</tbody>
        </table></div>
        <p class="muted small">${t("skills_rule")}</p>
      </section>
      <section class="section">
        <div class="rule-head"><h2>${t("log_title")}</h2></div>
        <div class="table-wrap"><table class="ledger">
          <thead><tr><th>${t("col_date")}</th><th>${t("col_drill")}</th><th>${t("score")}</th><th>${t("col_result")}</th><th>${t("col_mistakes")}</th><th>${t("col_hints")}</th><th>${t("col_signed")}</th></tr></thead>
          <tbody>${logRows}</tbody>
        </table></div>
      </section>` : `<p class="empty">${t("progress_empty")}</p>`}
    </div>`;
}

// ------------------------------------------------------------------ boot
window.addEventListener("hashchange", () => {
  if (app.mode !== "page") exitFullView();
  else route();
});

// Lets automated checks select equipment without aiming taps at the 3D scene.
window.anubhavDebug = { app, pick: (id) => onPick(id), modelState: () => app.twin?.modelState };

loadSops();
route();

// Offline support and installability (Android app via PWABuilder). Skipped where service
// workers are unavailable, such as embedded previews.
if ("serviceWorker" in navigator && (location.protocol === "https:" || location.hostname === "localhost")) {
  navigator.serviceWorker.register("sw.js").catch(() => {});
}
