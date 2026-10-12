import { CONFIG } from "./config.js";

// Progress on this device. A skill becomes competent the first time it scores at least the pass
// mark in two drills in a row. Time to competency runs from the first drill to the day the last
// skill became competent. Practice is spaced: after each passed drill the next one is due later
// (1, 3, 7, 14, then 30 days); a failed drill is due again the next day.

export const SKILL_ORDER = ["recognise", "communicate", "isolate", "evacuate", "diagnose", "restart"];
const DAY = 86400000;
const SPACING_DAYS = [1, 3, 7, 14, 30];

export function skillProgress(attempts) {
  const list = [...attempts].sort((a, b) => a.date.localeCompare(b.date));
  const out = {};
  for (const sk of SKILL_ORDER) {
    const hits = list.filter((a) => a.skills && sk in a.skills);
    let streak = 0;
    let competentAt = null;
    for (const a of hits) {
      streak = a.skills[sk] >= CONFIG.passMark ? streak + 1 : 0;
      if (streak >= 2 && competentAt == null) competentAt = new Date(a.date).getTime();
    }
    const last = hits.length ? hits[hits.length - 1].skills[sk] : null;
    const signed = competentAt != null && hits.some((a) => a.signoff && a.skills[sk] >= CONFIG.passMark);
    let status = "none";
    if (competentAt != null) status = signed ? "signed" : "competent";
    else if (last != null) status = last >= CONFIG.passMark ? "practising" : "learning";
    out[sk] = { status, last, competentAt, tries: hits.length };
  }
  return out;
}

/** Days from the first drill to competency in every skill, or null while still in progress. */
export function timeToCompetency(attempts, skills) {
  if (!attempts.length) return null;
  const first = Math.min(...attempts.map((a) => new Date(a.date).getTime()));
  if (!SKILL_ORDER.every((sk) => skills[sk].competentAt != null)) return null;
  return Math.round((Math.max(...SKILL_ORDER.map((sk) => skills[sk].competentAt)) - first) / DAY);
}

/** When a scenario is due again, from its attempts on this device. */
export function nextDue(attempts, scenarioId, now = Date.now()) {
  const mine = attempts.filter((a) => a.scenarioId === scenarioId).sort((a, b) => a.date.localeCompare(b.date));
  if (!mine.length) return { due: true, at: null };
  const last = mine[mine.length - 1];
  let passes = 0;
  for (let i = mine.length - 1; i >= 0 && mine[i].passed; i--) passes++;
  const days = last.passed ? SPACING_DAYS[Math.min(passes - 1, SPACING_DAYS.length - 1)] : 1;
  const at = new Date(last.date).getTime() + days * DAY;
  return { due: at <= now, at };
}
