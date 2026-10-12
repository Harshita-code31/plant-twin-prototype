import { EQUIPMENT, OBSERVE_ACTIONS, ACTIONS } from "./scenarios.js";
import { CONFIG } from "./config.js";

// Runs one drill attempt: phases, the process model, action handling and scoring.
// Scoring: early warning signs spotted 20, SOP steps done in order 80, minus unsafe actions and
// hints. A forced failure caps the score at 40, as in the AbhyasAR Unity app.

const MAX_RESPONSE_SECONDS = 240;

export class Drill {
  constructor(scn) {
    this.scn = scn;
    this.s = scn.init();
    this.phase = "briefing";
    this.t = 0;
    this.eventT = null;
    this.pendingEventAt = null;
    this.reports = new Set();
    this.done = {};
    this.mistakes = [];
    this.hints = 0;
    this.log = [];
    this.failed = null;
    this.ended = false;
    this.timedOut = false;
    this.result = null;
    this.onEvent = null;
    this.onEnd = null;
  }

  begin() {
    if (this.phase === "briefing") this.phase = "walkdown";
  }

  /** Seconds since the emergency started (0 before it). */
  rt() {
    return this.eventT == null ? 0 : this.t - this.eventT;
  }

  walkdownRemaining() {
    const until = this.pendingEventAt ?? this.scn.walkdownSeconds;
    return Math.max(0, until - this.t);
  }

  tick(dt) {
    if (this.ended || this.phase === "briefing") return;
    this.t += dt;
    this.scn.tick(this.s, dt, this.phase);
    if (this.phase === "walkdown") {
      if (this.pendingEventAt == null && this.t >= this.scn.walkdownSeconds) this.pendingEventAt = this.t;
      if (this.pendingEventAt != null && this.t >= this.pendingEventAt) this.startResponse();
    } else if (this.phase === "response") {
      const failure = this.scn.checkFailure(this.s);
      if (failure) this.end(failure);
      else if (this.rt() > MAX_RESPONSE_SECONDS) {
        this.timedOut = true;
        this.end(null);
      }
    }
  }

  finishWalkdown() {
    // A short, unannounced pause: the event should not feel like it was triggered by the button.
    if (this.phase === "walkdown" && this.pendingEventAt == null) this.pendingEventAt = this.t + 2 + Math.random() * 3;
  }

  startResponse() {
    this.phase = "response";
    this.eventT = this.t;
    this.scn.startEvent(this.s);
    this.onEvent?.();
  }

  actionsFor(target) {
    const eq = EQUIPMENT[target];
    if (!eq || this.ended || this.phase === "briefing") return [];
    if (this.phase === "walkdown") return eq.actions.filter((a) => OBSERVE_ACTIONS.has(a));
    return eq.actions.filter((a) => a !== "report");
  }

  /** Performs an action. Returns { msg, tone } where msg is a string or an { en, hi } object. */
  act(target, action) {
    const key = `${target}.${action}`;
    const entry = { t: +this.t.toFixed(1), rt: +this.rt().toFixed(1), phase: this.phase, key };
    this.log.push(entry);

    if (this.phase === "walkdown") {
      if (action === "report") {
        if (this.reports.has(target)) return { msg: { key: "already_reported" } };
        this.reports.add(target);
        return { msg: { key: "reported", vars: { name: EQUIPMENT[target].name } } };
      }
      return { msg: this.scn.observe(this.s, target, this.phase) ?? { key: "nothing_needed" } };
    }

    if (this.phase !== "response") return { msg: null };

    const eff = this.scn.effect(this.s, key) || {};
    if (eff.fail) {
      entry.outcome = "fail";
      this.end(eff.fail);
      return { msg: eff.fail, tone: "alarm" };
    }
    if (eff.mistake) {
      this.mistakes.push({ key, rt: entry.rt, msg: eff.mistake, penalty: eff.penalty || 0, skill: eff.skill || null });
      entry.outcome = "mistake";
    }

    const step = this.scn.steps.find((st) => st.on.includes(key));
    if (step && !eff.blocked && !this.done[step.id]) {
      const orderOk = (step.after || []).every((id) => this.done[id]);
      this.done[step.id] = { rt: entry.rt, orderOk };
      entry.step = step.id;
    }

    let msg = eff.msg;
    if (!msg && OBSERVE_ACTIONS.has(action)) msg = this.scn.observe(this.s, target, this.phase);
    if (!msg) msg = step ? { key: "logged", vars: { action: ACTIONS[action] } } : { key: "nothing_needed" };

    if (this.scn.endOn.includes(key) && !eff.blocked) this.end(null);
    return { msg, tone: eff.tone || (eff.blocked ? "warn" : "") };
  }

  /** Counts a hint and returns what to show: the next SOP step, or the walkdown advice. */
  hint() {
    this.hints += 1;
    if (this.phase === "walkdown" || this.phase === "briefing") return { advice: this.scn.walkdownHint };
    const next = this.scn.steps.find((st) => !this.done[st.id]);
    return next ? { step: next } : { advice: this.scn.walkdownHint };
  }

  end(failure) {
    if (this.ended) return;
    this.ended = true;
    this.failed = failure;
    this.phase = "ended";
    this.result = this.computeResult();
    this.onEnd?.(this.result);
  }

  computeResult() {
    const scn = this.scn;
    const cueIds = Object.keys(scn.cues);
    const found = cueIds.filter((c) => this.reports.has(c));
    const falseReports = [...this.reports].filter((r) => !cueIds.includes(r));

    const observe = Math.max(0, (20 * found.length) / cueIds.length - 4 * falseReports.length);

    const per = 80 / scn.steps.length;
    let stepsScore = 0;
    for (const st of scn.steps) {
      const d = this.done[st.id];
      if (d) stepsScore += d.orderOk ? per : Math.max(0, per - 5);
    }

    const critical = scn.steps.filter((st) => st.critical);
    const allCritical = critical.every((st) => this.done[st.id]);
    const lastT = Math.max(0, ...critical.map((st) => this.done[st.id]?.rt ?? 0));

    const penalty = this.mistakes.reduce((sum, m) => sum + m.penalty, 0) + this.hints * 3;
    let score = Math.round(Math.max(0, Math.min(100, observe + stepsScore - penalty)));
    if (this.failed) score = Math.min(score, 40);
    const passed = !this.failed && allCritical && score >= CONFIG.passMark;

    const skills = {};
    for (const sk of scn.skills) {
      let v;
      if (sk === "recognise") {
        v = (100 * found.length) / cueIds.length - 20 * falseReports.length;
      } else {
        const sts = scn.steps.filter((st) => st.skill === sk);
        const good = sts.filter((st) => this.done[st.id]?.orderOk).length;
        v = (100 * good) / sts.length - 25 * this.mistakes.filter((m) => m.skill === sk).length;
      }
      if (this.failed) v = Math.min(v, 40);
      skills[sk] = Math.round(Math.max(0, Math.min(100, v)));
    }

    return {
      scenarioId: scn.id,
      score,
      passed,
      failed: this.failed,
      timedOut: this.timedOut,
      parts: {
        observe: Math.round(observe),
        steps: Math.round(stepsScore),
        penalty: Math.round(penalty),
      },
      skills,
      done: this.done,
      cuesFound: found,
      falseReports,
      mistakes: this.mistakes,
      hints: this.hints,
      responseSeconds: +lastT.toFixed(1),
      log: this.log,
    };
  }
}
