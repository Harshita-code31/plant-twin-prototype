import { CONFIG } from "./config.js";
import { SCENARIOS } from "./scenarios.js";

// Competency map maths. A skill becomes competent the first time a trainee scores at least the
// pass mark on it in two drills in a row. Time to competency runs from a trainee's first drill
// to the day their last skill became competent.

export const SKILL_ORDER = ["recognise", "communicate", "isolate", "evacuate", "diagnose", "restart"];
export const SKILL_GROUPS = [
  { key: "group_both", skills: ["recognise", "communicate"] },
  { key: "group_gas", skills: ["isolate", "evacuate"] },
  { key: "group_trip", skills: ["diagnose", "restart"] },
];

const DAY = 86400000;

function mulberry32(seed) {
  return function () {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Example cohort so the map can be read before real drills exist. Clearly marked in the UI. */
export function exampleAttempts(now = Date.now()) {
  const cohort = [
    { name: "Pooja Singh", mentorId: "m1", started: 58, pace: 30 },
    { name: "Imran Khan", mentorId: "m2", started: 64, pace: 38 },
    { name: "Kavya Reddy", mentorId: "m1", started: 70, pace: 52 },
    { name: "Deepak Kumar", mentorId: "m2", started: 24, pace: 44 },
    { name: "Arjun Mehta", mentorId: "m1", started: 7, pace: 40 },
  ];
  const difficulty = { recognise: 1.1, communicate: 0.7, isolate: 1, evacuate: 0.85, diagnose: 1.2, restart: 1.05 };
  const out = [];
  cohort.forEach((c, ci) => {
    const rnd = mulberry32(1000 + ci * 77);
    const start = now - c.started * DAY;
    let day = 0;
    let n = 0;
    while (day <= c.started) {
      const scn = SCENARIOS[n % SCENARIOS.length];
      const skills = {};
      for (const sk of scn.skills) {
        const progress = day / (c.pace * difficulty[sk] * 0.8);
        const v = 42 + 52 * Math.min(1.1, progress) + (rnd() - 0.5) * 12;
        skills[sk] = Math.round(Math.max(20, Math.min(98, v)));
      }
      const score = Math.round(Object.values(skills).reduce((a, b) => a + b, 0) / scn.skills.length);
      out.push({
        id: `ex-${ci}-${n}`,
        example: true,
        trainee: c.name,
        mentorId: c.mentorId,
        scenarioId: scn.id,
        date: new Date(start + day * DAY).toISOString(),
        score,
        passed: score >= CONFIG.passMark,
        skills,
        signoff: score >= 88 && rnd() > 0.4 ? { by: CONFIG.mentors.find((m) => m.id === c.mentorId).name } : null,
      });
      day += 3 + Math.floor(rnd() * 4);
      n += 1;
    }
  });
  return out;
}

export function buildRows(attempts, now = Date.now()) {
  const byTrainee = new Map();
  for (const a of attempts) {
    const key = (a.example ? "ex:" : "me:") + (a.trainee || "");
    if (!byTrainee.has(key)) byTrainee.set(key, []);
    byTrainee.get(key).push(a);
  }

  const rows = [];
  for (const list of byTrainee.values()) {
    list.sort((a, b) => a.date.localeCompare(b.date));
    const first = new Date(list[0].date).getTime();
    const skills = {};
    for (const sk of SKILL_ORDER) {
      const hits = list.filter((a) => a.skills && sk in a.skills);
      let streak = 0;
      let competentAt = null;
      for (const a of hits) {
        streak = a.skills[sk] >= CONFIG.passMark ? streak + 1 : 0;
        if (streak >= 2 && competentAt == null) competentAt = new Date(a.date).getTime();
      }
      const last = hits.length ? hits[hits.length - 1].skills[sk] : null;
      let status = "none";
      if (competentAt != null) status = "competent";
      else if (last != null && last >= CONFIG.passMark) status = "practising";
      else if (last != null) status = "learning";
      const signed = status === "competent" && hits.some((a) => a.signoff && a.skills[sk] >= CONFIG.passMark);
      skills[sk] = { status: signed ? "signed" : status, last, competentAt };
    }
    const allDone = SKILL_ORDER.every((sk) => skills[sk].competentAt != null);
    const ttcDays = allDone
      ? Math.round((Math.max(...SKILL_ORDER.map((sk) => skills[sk].competentAt)) - first) / DAY)
      : Math.max(0, Math.round((now - first) / DAY));
    rows.push({
      name: list[0].trainee,
      example: !!list[0].example,
      mentorId: list[list.length - 1].mentorId,
      attempts: list.length,
      skills,
      done: allDone,
      ttcDays,
    });
  }
  return rows;
}

export function median(values) {
  if (!values.length) return null;
  const s = [...values].sort((a, b) => a - b);
  const m = Math.floor(s.length / 2);
  return s.length % 2 ? s[m] : Math.round((s[m - 1] + s[m]) / 2);
}
