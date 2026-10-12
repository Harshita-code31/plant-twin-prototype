import { CONFIG } from "./config.js";
import { t, L, getLang, setLang } from "./i18n.js";
import { SCENARIOS, SENSORS, EQUIPMENT, ACTIONS, SKILLS, getScenario } from "./scenarios.js";
import { Drill } from "./drill.js";
import { Twin } from "./twin.js";
import { getProfile, setProfile, getAttempts, addAttempt, getAttempt, updateAttempt, newId, load, save } from "./store.js";
import { SKILL_ORDER, SKILL_GROUPS, exampleAttempts, buildRows, median } from "./competency.js";

const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const mmss = (sec) => {
  const s = Math.max(0, Math.floor(sec));
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
};
/** Link to the SOP copilot, pre-filling a question in the current language (copilot reads q, lang, scenario). */
function copilotLink(question, scenarioId) {
  const url = new URL(CONFIG.copilotUrl, location.href);
  url.searchParams.set("lang", getLang());
  if (question) url.searchParams.set("q", question);
  if (scenarioId) url.searchParams.set("scenario", scenarioId);
  return url.toString();
}
const mentorById = (id) => CONFIG.mentors.find((m) => m.id === id) || CONFIG.mentors[0];

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
  drill: null,
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

// ------------------------------------------------------------------ chrome
function renderAppbar() {
  const copilot = CONFIG.copilotUrl
    ? `<a class="btn small" href="${esc(CONFIG.copilotUrl)}" target="_blank" rel="noopener">${t("nav_copilot")}</a>`
    : `<span class="btn small" aria-disabled="true" title="${esc(t("copilot_soon"))}">${t("nav_copilot")}</span>`;
  $("#appbar").innerHTML = `
    <a class="brand" href="#home" aria-label="Anubhav"><b>ANUBHAV</b><span class="brand-hi" lang="hi">अनुभव</span></a>
    <nav>
      <a class="btn small" href="#home">${t("nav_home")}</a>
      <a class="btn small" href="#map">${t("nav_map")}</a>
      ${copilot}
      <button class="btn small" id="lang-btn" type="button" lang="${getLang() === "en" ? "hi" : "en"}">${t("lang_toggle")}</button>
    </nav>`;
  $("#lang-btn").onclick = () => {
    setLang(getLang() === "en" ? "hi" : "en");
    renderAppbar();
    route();
  };
}

function route() {
  if (app.drill) {
    renderDrillChrome();
    return;
  }
  const view = location.hash.replace("#", "") || "home";
  window.scrollTo(0, 0);
  if (view === "map") renderMap();
  else if (view === "debrief") renderDebrief();
  else renderHome();
}

// ------------------------------------------------------------------ home
function scenarioStats(id) {
  const name = getProfile().name.trim();
  const mine = getAttempts().filter((a) => a.scenarioId === id && a.trainee === name);
  return { n: mine.length, best: mine.length ? Math.max(...mine.map((a) => a.score)) : null };
}

function renderHome() {
  const p = getProfile();
  const mentorOptions = CONFIG.mentors
    .map((m) => `<option value="${m.id}" ${m.id === p.mentorId ? "selected" : ""}>${esc(m.name)} · ${esc(L(m.role))}</option>`)
    .join("");

  const cards = SCENARIOS.map((s) => {
    const st = scenarioStats(s.id);
    return `
      <article class="panel scn-card">
        <div class="stripe ${s.severity}"></div>
        <div class="eyebrow">${s.sop.id} · ${esc(L(s.sop.title))}</div>
        <h3>${esc(L(s.title))}</h3>
        <p>${esc(L(s.summary))}</p>
        <div class="meta">
          <span class="chip">${esc(L(s.rarity))}</span>
          <span class="chip ${st.best != null && st.best >= CONFIG.passMark ? "ok" : ""}">${st.n ? `${t("attempts_n", { n: st.n })} · ${t("best_n", { n: st.best })}` : t("not_tried")}</span>
        </div>
        <div class="actions">
          <button class="btn primary" type="button" data-start="${s.id}">${t("start_drill")}</button>
        </div>
      </article>`;
  }).join("");

  const vault = SCENARIOS.map((s) => {
    const v = s.veteran;
    return `
      <article class="panel vault-card">
        <div class="vault-head">
          <div>
            <div class="eyebrow">${esc(L(v.incident))}</div>
            <h3>${esc(v.name)}</h3>
            <p class="muted">${esc(L(v.role))}</p>
          </div>
          <div class="years"><b>${v.years}</b><span>${t("years_label")}</span></div>
        </div>
        <blockquote>${esc(L(v.quote))}</blockquote>
        <dl>
          <div><dt>${t("vault_noticed")}</dt><dd>${esc(L(v.noticed))}</dd></div>
          <div><dt>${t("vault_chose")}</dt><dd>${esc(L(v.chose))}</dd></div>
          <div><dt>${t("vault_why")}</dt><dd>${esc(L(v.why))}</dd></div>
        </dl>
        <div><button class="btn" type="button" data-start="${s.id}">${t("vault_practise")}</button></div>
        <p class="sample-note">${t("vault_sample")}</p>
      </article>`;
  }).join("");

  const copilotHref = CONFIG.copilotUrl ? esc(copilotLink("", "")) : "";
  const pillar = (key, attrs) => `
      <${attrs.tag} class="pillar" ${attrs.extra}>
        <span class="eyebrow">${t(`pillar_${key}_tag`)}</span>
        <h3>${t(`pillar_${key}_t`)}</h3>
        <p>${t(`pillar_${key}_d`)}</p>
      </${attrs.tag}>`;
  const vet = SCENARIOS[0].veteran;

  $("#view").innerHTML = `
    <div class="page home">
      <section class="hero">
        <div class="lockup"><span class="lockup-hi" lang="hi">अनुभव</span><span class="lockup-en">ANUBHAV</span></div>
        <h1>${t("hero_title")}</h1>
        <p>${t("hero_body")}</p>
        <div class="compress">
          <div><b>${vet.years}<small>${t("compress_unit_years")}</small></b><span>${t("compress_years")}</span></div>
          <span class="arrow" aria-hidden="true">→</span>
          <div><b>3<small>${t("compress_unit_minutes")}</small></b><span>${t("compress_minutes")}</span></div>
        </div>
      </section>

      <nav class="pillars" aria-label="Anubhav">
        ${pillar("copilot", copilotHref ? { tag: "a", extra: `href="${copilotHref}" target="_blank" rel="noopener"` } : { tag: "div", extra: "" })}
        ${pillar("twin", { tag: "button", extra: 'type="button" data-scroll="drills"' })}
        ${pillar("vault", { tag: "button", extra: 'type="button" data-scroll="vault"' })}
        ${pillar("map", { tag: "a", extra: 'href="#map"' })}
      </nav>

      <section class="panel">
        <h3>${t("profile_title")}</h3>
        <div class="profile">
          <div class="field">
            <label for="trainee-name">${t("profile_name")}</label>
            <input id="trainee-name" type="text" autocomplete="name" placeholder="${esc(t("profile_name_ph"))}" value="${esc(p.name)}">
          </div>
          <div class="field">
            <label for="mentor">${t("profile_mentor")}</label>
            <select id="mentor">${mentorOptions}</select>
          </div>
        </div>
        <p id="name-msg" class="muted" hidden>${t("name_needed")}</p>
      </section>

      <section class="section" id="drills">
        <div class="section-head"><h2>${t("drills_title")}</h2><p class="muted">${t("drills_note")}</p></div>
        <div class="grid-2">${cards}</div>
      </section>

      <section class="section" id="vault">
        <div class="section-head"><h2>${t("vault_title")}</h2><p class="muted">${t("vault_body")}</p></div>
        <div class="grid-2">${vault}</div>
      </section>

      <p class="site-note">${t("site_note")} ${CONFIG.unit.tag} · ${esc(L(CONFIG.unit.name))}</p>
    </div>`;

  document.querySelectorAll("[data-scroll]").forEach((b) => {
    b.onclick = () => document.getElementById(b.dataset.scroll)?.scrollIntoView({ behavior: "smooth", block: "start" });
  });
  const nameEl = $("#trainee-name");
  nameEl.oninput = () => setProfile({ ...getProfile(), name: nameEl.value });
  $("#mentor").onchange = (e) => setProfile({ ...getProfile(), mentorId: e.target.value });
  document.querySelectorAll("[data-start]").forEach((b) => {
    b.onclick = () => {
      if (!getProfile().name.trim()) {
        $("#name-msg").hidden = false;
        nameEl.focus();
        nameEl.scrollIntoView({ block: "center" });
        return;
      }
      startDrill(b.dataset.start);
    };
  });
}

// ------------------------------------------------------------------ drill
function ensureTwin() {
  if (app.twin) return app.twin;
  app.twin = new Twin($("#drill"), $("#labels"), (id) => onPick(id));
  return app.twin;
}

function startDrill(id) {
  const scn = getScenario(id);
  if (!scn) return;
  app.drill = new Drill(scn);
  app.prev = {};
  app.trend = {};
  app.hintText = null;
  app.selected = null;
  $("#appbar").hidden = true;
  $("#view").hidden = true;
  $("#drill").hidden = false;
  ensureTwin().reset();
  app.twin.resize();

  app.drill.onEvent = () => {
    app.hintText = null;
    siren.start();
    renderPrompt();
  };
  app.drill.onEnd = (result) => {
    siren.stop();
    closeSheet();
    const p = getProfile();
    const attempt = { id: newId(), trainee: p.name.trim(), mentorId: p.mentorId, date: new Date().toISOString(), ...result };
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

  renderDrillChrome();
  showBriefing();
  cancelAnimationFrame(app.raf);
  app.lastFrame = performance.now();
  app.raf = requestAnimationFrame(loop);
}

function loop(now) {
  const dt = Math.min(0.1, (now - app.lastFrame) / 1000);
  app.lastFrame = now;
  const d = app.drill;
  if (!d) return;
  d.tick(dt);
  if (d.s.acked || d.ended) siren.stop();
  app.twin.frame(dt, d);
  app.hudClock += dt;
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
  if (app.hudClock >= 0.15) {
    app.hudClock = 0;
    updateHud();
  }
  app.raf = requestAnimationFrame(loop);
}

function renderDrillChrome() {
  const d = app.drill;
  if (!d) return;
  $("#hud-top").innerHTML = `
    <div class="hud-title">
      <span class="eyebrow" id="phase-chip"></span>
      <b>${esc(L(d.scn.title))}</b>
    </div>
    <div class="hud-controls">
      <span class="timer" id="timer">00:00</span>
      <button class="btn small" id="lang-drill" type="button">${t("lang_toggle")}</button>
      <button class="btn small" id="sound-btn" type="button" aria-pressed="${app.sound}">${app.sound ? t("sound_on") : t("sound_off")}</button>
      <button class="btn small danger" id="exit-btn" type="button">${t("exit")}</button>
    </div>`;
  $("#lang-drill").onclick = () => {
    setLang(getLang() === "en" ? "hi" : "en");
    renderAppbar();
    renderDrillChrome();
    if (!$("#overlay").hidden && d.phase === "briefing") showBriefing();
    if (!$("#overlay").hidden && d.ended) showEndOverlay(d.result);
  };
  $("#sound-btn").onclick = () => {
    app.sound = !app.sound;
    save("sound", app.sound);
    if (!app.sound) siren.stop();
    else if (d.phase === "response" && !d.s.acked) siren.start();
    renderDrillChrome();
  };
  $("#exit-btn").onclick = (e) => {
    const now = performance.now();
    if (now - app.exitArmed < 2500 || d.ended) {
      exitDrill();
      return;
    }
    app.exitArmed = now;
    e.currentTarget.textContent = t("exit_confirm");
    setTimeout(() => {
      const b = $("#exit-btn");
      if (b) b.textContent = t("exit");
    }, 2500);
  };
  $("#sensors-title").textContent = t("sensors");
  renderPrompt();
  if (app.selected) openSheet(app.selected);
  updateHud();
}

function exitDrill() {
  cancelAnimationFrame(app.raf);
  siren.stop();
  app.drill = null;
  closeSheet();
  $("#overlay").hidden = true;
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
  $("#timer").style.color = d.phase === "response" ? "var(--alarm)" : "";

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
    else if (sn.lo != null && v <= sn.lo && s.running !== undefined) tone = tone || "warn";
    return `<div class="sensor-row ${tone}">
      <span class="name" title="${esc(L(sn.name))}"><span class="full">${esc(L(sn.name))}</span><small>${sn.tag}</small></span>
      <span class="val">${Number(v).toFixed(sn.dp)}<small>${sn.unit}</small></span>
      <span class="trend">${app.trend[sn.id] || ""}</span>
    </div>`;
  }).join("");

  // Equipment labels: short live status where it matters.
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

function renderPrompt() {
  const d = app.drill;
  const el = $("#prompt");
  if (!d || d.phase === "briefing" || d.ended) {
    el.hidden = true;
    return;
  }
  el.hidden = false;
  const vet = d.scn.veteran.name.split(" ")[0];
  const hint = app.hintText ? `<p class="hint-text"><b>${esc(d.scn.veteran.name)}:</b> ${esc(L(app.hintText))}</p>` : "";
  if (d.phase === "walkdown") {
    el.innerHTML = `
      <p>${t("walkdown_prompt")}</p>
      ${hint}
      <div class="row">
        <span class="muted mono" id="walk-count"></span>
        <span style="display:flex;gap:.4rem;flex-wrap:wrap">
          <button class="btn small" id="hint-btn" type="button">${t("hint_btn", { name: vet })}</button>
          ${askSopButton(d.scn)}
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
        <span style="display:flex;gap:.4rem;flex-wrap:wrap">
          ${askSopButton(d.scn)}
          <button class="btn small" id="hint-btn" type="button">${t("hint_btn", { name: vet })}</button>
        </span>
      </div>`;
  }
  $("#hint-btn").onclick = () => {
    app.hintText = d.hint();
    renderPrompt();
  };
}

function askSopButton(scn) {
  if (!CONFIG.copilotUrl) return "";
  return `<a class="btn small" href="${esc(copilotLink(L(scn.copilotQuestion), scn.id))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>`;
}

function showBriefing() {
  const d = app.drill;
  const ov = $("#overlay");
  ov.hidden = false;
  ov.innerHTML = `
    <div class="card" role="dialog" aria-labelledby="brief-title">
      <div class="eyebrow">${d.scn.sop.id} · ${t("phase_briefing")}</div>
      <h2 id="brief-title">${esc(L(d.scn.title))}</h2>
      <p>${esc(L(d.scn.briefing))}</p>
      <p class="muted">${t("rotate_hint")}</p>
      <button class="btn primary" id="begin-btn" type="button">${t("begin_walkdown")}</button>
    </div>`;
  $("#begin-btn").onclick = () => {
    siren.unlock();
    d.begin();
    ov.hidden = true;
    renderPrompt();
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
      <div class="eyebrow">${esc(L(d.scn.title))}</div>
      <h2 id="end-title">${title}</h2>
      ${result.failed ? `<p>${esc(L(result.failed))}</p>` : ""}
      <p><span class="score-big">${result.score}</span><span class="muted"> / 100</span></p>
      <button class="btn primary" id="debrief-btn" type="button">${t("see_debrief")}</button>
    </div>`;
  $("#debrief-btn").onclick = () => {
    exitDrill();
    location.hash = "#debrief";
    route();
  };
}

function onPick(id) {
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
        <div class="tag-code">${eq.tag}</div>
        <h3>${esc(L(eq.name))}</h3>
      </div>
      <button class="btn small" id="sheet-close" type="button">${t("close")}</button>
    </div>
    ${eq.valve && d.phase === "response" ? `<p class="muted" style="font-size:var(--step--1)">${t("sheet_note_valve")}</p>` : ""}
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
  const vet = scn.veteran;
  const mentor = mentorById(a.mentorId);
  const date = new Date(a.date).toLocaleString(getLang() === "hi" ? "hi-IN" : "en-IN", { dateStyle: "medium", timeStyle: "short" });

  const bar = (label, v, max, tone) => `
    <div class="bar-row"><span>${label}</span><span class="bar"><i class="${tone}" style="width:${Math.max(0, Math.min(100, (100 * v) / max))}%"></i></span><span class="mono">${v}/${max}</span></div>`;

  const rows = scn.steps.map((st) => {
    const d = a.done[st.id];
    const ask = CONFIG.copilotUrl
      ? ` <a href="${esc(copilotLink(L(st.label), scn.id))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>`
      : "";
    const you = d ? `${d.rt.toFixed(0)} s${d.orderOk ? "" : ` <span class="chip warn">${t("out_of_order")}</span>${ask}`}` : `<span class="chip alarm">${t("not_done")}</span>${ask}`;
    return `<tr>
      <td>${esc(L(st.label))}${st.critical ? "" : ` <span class="muted">(${getLang() === "hi" ? "वैकल्पिक" : "optional"})</span>`}</td>
      <td class="num">${you}</td>
      <td class="num">${st.vet} s</td>
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

  const vetEnd = Math.max(...scn.steps.filter((s) => s.critical).map((s) => s.vet));
  const askSop = CONFIG.copilotUrl
    ? `<a class="btn small" href="${esc(copilotLink(L(scn.copilotQuestion), scn.id))}" target="_blank" rel="noopener">${t("nav_copilot")}</a>`
    : "";

  $("#view").innerHTML = `
    <div class="page">
      <section class="hero">
        <div class="eyebrow">${esc(L(scn.title))} · ${esc(date)} · ${esc(a.trainee || t("no_name"))}</div>
        <h1>${t("debrief_title")}</h1>
        <div class="score-row">
          <div><span class="score-big">${a.score}</span><span class="muted"> / 100</span></div>
          <div style="display:grid;gap:.4rem">
            <span class="chip solid ${a.passed ? "ok" : "alarm"}">${a.passed ? t("result_pass") : t("result_fail")}</span>
            <span class="muted">${t("pass_rule", { n: CONFIG.passMark })}</span>
          </div>
        </div>
        <div>${skillChips}</div>
      </section>

      ${a.failed ? `<section class="panel" style="border-color:var(--alarm)"><h3>${t("failed_reason")}</h3><p>${esc(L(a.failed))}</p></section>` : ""}

      <div class="grid-2">
        <section class="panel">
          <h3>${t("breakdown")}</h3>
          <div class="breakdown">
            ${bar(t("part_observe"), a.parts.observe, 15, a.parts.observe >= 12 ? "ok" : "warn")}
            ${bar(t("part_steps"), a.parts.steps, 60, a.parts.steps >= 50 ? "ok" : "warn")}
            ${bar(t("part_time"), a.parts.time, 25, a.parts.time >= 15 ? "ok" : "warn")}
            <div class="bar-row"><span>${t("part_penalty")}</span><span></span><span class="mono">−${a.parts.penalty}</span></div>
          </div>
          <p class="muted">${t("hints_n", { n: a.hints })}</p>
        </section>

        <section class="panel">
          <h3>${t("vet_title", { name: esc(vet.name) })}</h3>
          <p class="muted">${esc(L(vet.role))}</p>
          <div class="compare">
            <div><span class="eyebrow">${t("vault_noticed")}</span><p>${esc(L(vet.noticed))}</p></div>
            <div><span class="eyebrow">${t("vault_chose")}</span><p>${esc(L(vet.chose))}</p></div>
            <div><span class="eyebrow">${t("vault_why")}</span><p>${esc(L(vet.why))}</p></div>
          </div>
          <p class="mono">${t("col_you")}: ${a.done && Object.keys(a.done).length ? `${a.responseSeconds.toFixed(0)} s` : "—"} · ${esc(vet.name)}: ${vetEnd} s</p>
        </section>
      </div>

      <section class="section">
        <div class="section-head"><h2>${t("timeline")}</h2>${askSop}</div>
        <div class="timeline panel">
          <table>
            <thead><tr><th>${t("col_step")}</th><th>${t("col_you")}</th><th>${esc(vet.name)}</th><th>${t("col_sop")}</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
        </div>
      </section>

      <div class="grid-2">
        <section class="panel">
          <h3>${t("cues_title")}</h3>
          <ul class="list">${cues}${falseReps}</ul>
        </section>
        <section class="panel">
          <h3>${t("mistakes_title")}</h3>
          <ul class="list">${mistakes}</ul>
        </section>
      </div>

      <section class="panel">
        <h3>${t("mentor_title")}</h3>
        <p>${t("mentor_with", { name: `<b>${esc(mentor.name)}</b>` })}</p>
        <ol class="list" style="list-style:decimal;padding-left:1.2rem;display:grid">
          ${scn.debriefQuestions.map((q) => `<li style="display:list-item">${esc(L(q))}</li>`).join("")}
        </ol>
        <div class="field">
          <label for="mentor-notes">${t("mentor_notes")}</label>
          <textarea id="mentor-notes" placeholder="${esc(t("mentor_notes_ph"))}">${esc(a.mentorNotes || "")}</textarea>
        </div>
        <div style="display:flex;gap:.5rem;flex-wrap:wrap;align-items:center">
          <button class="btn" id="save-notes" type="button">${t("save_notes")}</button>
          ${a.signoff
            ? `<span class="chip solid ok">${t("signed_off_by", { name: esc(a.signoff.by) })}</span>`
            : a.passed
              ? `<button class="btn primary" id="sign-off" type="button">${t("sign_off")} · ${esc(mentor.name)}</button>`
              : `<span class="muted">${t("sign_off_needs_pass")}</span>`}
          <span id="notes-status" class="muted" role="status"></span>
        </div>
      </section>

      <div style="display:flex;gap:.5rem;flex-wrap:wrap">
        <button class="btn primary" id="retry" type="button">${t("retry")}</button>
        <a class="btn" href="#map">${t("to_map")}</a>
        <a class="btn" href="#home">${t("to_home")}</a>
      </div>
    </div>`;

  $("#save-notes").onclick = () => {
    updateAttempt(a.id, { mentorNotes: $("#mentor-notes").value });
    $("#notes-status").textContent = t("notes_saved");
  };
  const so = $("#sign-off");
  if (so) {
    so.onclick = () => {
      updateAttempt(a.id, { mentorNotes: $("#mentor-notes").value, signoff: { by: mentor.name, at: new Date().toISOString() } });
      renderDebrief();
    };
  }
  $("#retry").onclick = () => startDrill(a.scenarioId);
}

// ------------------------------------------------------------------ competency map
function renderMap() {
  const me = getProfile().name.trim();
  const local = getAttempts().filter((a) => a.trainee);
  const rows = buildRows([...local, ...exampleAttempts()]);
  rows.sort((x, y) => (x.example === y.example ? (x.name === me ? -1 : y.name === me ? 1 : 0) : x.example ? 1 : -1));

  const done = rows.filter((r) => r.done);
  const med = median(done.map((r) => r.ttcDays));
  const statusLabel = { none: "legend_none", learning: "legend_learning", practising: "legend_practising", competent: "legend_competent", signed: "legend_signed" };

  const head1 = SKILL_GROUPS.map((g) => `<th colspan="${g.skills.length}" style="text-align:center">${t(g.key)}</th>`).join("");
  const head2 = SKILL_GROUPS.flatMap((g) => g.skills).map((sk) => `<th style="text-align:center">${t(SKILLS[sk])}</th>`).join("");

  const body = rows.map((r) => {
    const cells = SKILL_ORDER.map((sk) => {
      const c = r.skills[sk];
      return `<td class="cell"><span class="cellchip ${c.status}" title="${esc(t(statusLabel[c.status]))}">${c.last == null ? "—" : c.last}${c.status === "signed" ? " ✓" : ""}</span></td>`;
    }).join("");
    const ttc = r.done ? t("days_n", { n: r.ttcDays }) : `${t("in_progress")} · ${t("day_n", { n: r.ttcDays })}`;
    return `<tr class="${!r.example && r.name === me ? "you" : ""}">
      <td><b>${esc(r.name)}</b>${!r.example && r.name === me ? ` <span class="chip accent">${t("you")}</span>` : ""}${r.example ? ` <span class="chip">${t("example")}</span>` : ""}</td>
      <td>${esc(mentorById(r.mentorId).name)}</td>
      <td class="mono">${r.attempts}</td>
      ${cells}
      <td class="mono">${ttc}</td>
    </tr>`;
  }).join("");

  const maxDays = Math.max(CONFIG.targetDays * 1.4, ...rows.map((r) => r.ttcDays));
  const ttcRows = rows.map((r) => `
    <div class="ttc-row">
      <span>${esc(r.name)}</span>
      <span class="ttc-track">
        <i class="${r.done ? "done" : "open"}" style="width:${(100 * r.ttcDays) / maxDays}%"></i>
        <span class="ttc-target" style="left:${(100 * CONFIG.targetDays) / maxDays}%" title="${t("target_line")}"></span>
      </span>
      <span class="mono">${r.done ? t("days_n", { n: r.ttcDays }) : t("day_n", { n: r.ttcDays })}</span>
    </div>`).join("");

  $("#view").innerHTML = `
    <div class="page">
      <section class="hero">
        <div class="eyebrow">${CONFIG.unit.tag} · ${esc(L(CONFIG.unit.name))}</div>
        <h1>${t("map_title")}</h1>
        <p>${t("map_body")}</p>
      </section>

      <div class="tiles">
        <div class="tile"><b>${rows.length}</b><span>${t("tile_trainees")}</span></div>
        <div class="tile"><b>${done.length}</b><span>${t("tile_competent")}</span></div>
        <div class="tile"><b>${med == null ? "—" : med}</b><span>${t("tile_median")}</span></div>
        <div class="tile"><b>${CONFIG.targetDays}</b><span>${t("tile_target")} (${t("days_n", { n: "" }).trim()})</span></div>
      </div>

      <section class="section">
        <div class="legend">
          <span><span class="cellchip">—</span> ${t("legend_none")}</span>
          <span><span class="cellchip learning">64</span> ${t("legend_learning")}</span>
          <span><span class="cellchip practising">82</span> ${t("legend_practising")}</span>
          <span><span class="cellchip competent">88</span> ${t("legend_competent")}</span>
          <span><span class="cellchip signed">91 ✓</span> ${t("legend_signed")}</span>
        </div>
        <div class="map-wrap">
          <table class="map">
            <thead>
              <tr class="group"><th></th><th></th><th></th>${head1}<th></th></tr>
              <tr><th>${t("col_trainee")}</th><th>${t("col_mentor")}</th><th>${t("col_attempts")}</th>${head2}<th>${t("col_ttc")}</th></tr>
            </thead>
            <tbody>${body}</tbody>
          </table>
        </div>
        <p class="muted">${t("example_note")}</p>
      </section>

      <section class="panel">
        <h3>${t("ttc_title")}</h3>
        <div class="ttc">${ttcRows}</div>
        <p class="muted mono">│ = ${t("target_line")} ${t("days_n", { n: CONFIG.targetDays })}</p>
      </section>
    </div>`;
}

// ------------------------------------------------------------------ boot
function watchTheme() {
  const refresh = () => app.twin?.applyTheme();
  window.matchMedia?.("(prefers-color-scheme: dark)").addEventListener?.("change", refresh);
  new MutationObserver(refresh).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
}

window.addEventListener("hashchange", () => {
  if (app.drill) exitDrill();
  else route();
});
// Lets automated checks select equipment without aiming taps at the 3D scene.
window.anubhavDebug = { app, pick: (id) => onPick(id), modelState: () => app.twin?.modelState };

renderAppbar();
watchTheme();
route();

// Offline support and installability (Android app via PWABuilder). Skipped where service
// workers are unavailable, such as embedded previews.
if ("serviceWorker" in navigator && (location.protocol === "https:" || location.hostname === "localhost")) {
  navigator.serviceWorker.register("sw.js").catch(() => {});
}
