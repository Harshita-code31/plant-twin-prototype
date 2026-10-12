// Drill content and the simple process model behind each drill.
//
// The procedures here are ILLUSTRATIVE, written for the prototype. A plant replaces them with
// its own approved SOPs (the same SOPs the copilot answers from) before any real training.
//
// Each scenario has two phases:
//   walkdown: normal operation with early warning signs the trainee can spot and report;
//   response: the event has started; actions map to SOP steps, and the model reacts.

export const SKILLS = {
  recognise: "skill_recognise",
  communicate: "skill_communicate",
  isolate: "skill_isolate",
  evacuate: "skill_evacuate",
  diagnose: "skill_diagnose",
  restart: "skill_restart",
};

export const SENSORS = [
  { id: "P", tag: "PI-102", name: { en: "Discharge pressure", hi: "डिस्चार्ज दबाव" }, unit: "bar g", dp: 1, hi: 46, hihi: 50 },
  { id: "lel", tag: "GD-101", name: { en: "Gas at detector", hi: "डिटेक्टर पर गैस" }, unit: "% LEL", dp: 0, hi: 20, hihi: 40 },
  { id: "vib", tag: "VI-101", name: { en: "Vibration", hi: "कंपन" }, unit: "mm/s", dp: 1, hi: 7.1, hihi: 11 },
  { id: "brg", tag: "TI-103", name: { en: "DE bearing temp.", hi: "DE बेयरिंग तापमान" }, unit: "°C", dp: 0, hi: 90, hihi: 100 },
  { id: "lo", tag: "PI-201", name: { en: "Lube oil pressure", hi: "ल्यूब ऑयल दबाव" }, unit: "bar g", dp: 2, lo: 1.8, lolo: 1.5 },
  { id: "dp", tag: "PDI-202", name: { en: "Lube filter dP", hi: "ल्यूब फ़िल्टर dP" }, unit: "bar", dp: 2, hi: 1.2, hihi: 2.0 },
  { id: "rpm", tag: "SI-101", name: { en: "Compressor speed", hi: "कंप्रेसर गति" }, unit: "rpm", dp: 0 },
];

export const EQUIPMENT = {
  scrubber: { tag: "V-101", name: { en: "Suction scrubber", hi: "सक्शन स्क्रबर" }, actions: ["report", "inspect"] },
  suction_valve: { tag: "SDV-101", name: { en: "Suction shutdown valve", hi: "सक्शन शटडाउन वाल्व" }, actions: ["report", "inspect", "close", "open"], valve: true },
  discharge_valve: { tag: "SDV-102", name: { en: "Discharge shutdown valve", hi: "डिस्चार्ज शटडाउन वाल्व" }, actions: ["report", "inspect", "close", "open"], valve: true },
  blowdown_valve: { tag: "BDV-103", name: { en: "Blowdown valve (to flare)", hi: "ब्लोडाउन वाल्व (फ्लेयर की ओर)" }, actions: ["report", "inspect", "open", "close"], valve: true },
  compressor: { tag: "K-101", name: { en: "Gas compressor", hi: "गैस कंप्रेसर" }, actions: ["report", "inspect"] },
  motor: { tag: "PM-101", name: { en: "Compressor motor", hi: "कंप्रेसर मोटर" }, actions: ["report", "inspect"] },
  discharge_flange: { tag: "FL-102", name: { en: "Discharge flange", hi: "डिस्चार्ज फ्लेंज" }, actions: ["report", "inspect", "tighten"] },
  lube: { tag: "LO-101", name: { en: "Lube oil console", hi: "ल्यूब ऑयल कंसोल" }, actions: ["report", "check", "switch_filter"] },
  detector: { tag: "GD-101", name: { en: "Gas detector", hi: "गैस डिटेक्टर" }, actions: ["report", "check"] },
  panel: { tag: "LCP-101", name: { en: "Local control panel", hi: "लोकल कंट्रोल पैनल" }, actions: ["ack", "firstout", "esd", "reset", "start", "bypass"] },
  mcp: { tag: "MCP-1", name: { en: "Alarm call point and radio", hi: "अलार्म कॉल पॉइंट और रेडियो" }, actions: ["raise_alarm", "radio"] },
  windsock: { tag: "WS-1", name: { en: "Windsock", hi: "विंडसॉक" }, actions: ["check"] },
  muster_a: { tag: "MP-A", name: { en: "Muster point A", hi: "मस्टर पॉइंट A" }, actions: ["go"] },
  muster_b: { tag: "MP-B", name: { en: "Muster point B", hi: "मस्टर पॉइंट B" }, actions: ["go"] },
};

/** Actions that only look; available during the walkdown. */
export const OBSERVE_ACTIONS = new Set(["report", "inspect", "check"]);

export const ACTIONS = {
  report: { en: "Report as abnormal", hi: "असामान्य के रूप में रिपोर्ट करें" },
  inspect: { en: "Look closely", hi: "ध्यान से देखें" },
  check: { en: "Check it", hi: "जाँचें" },
  close: { en: "Close valve", hi: "वाल्व बंद करें" },
  open: { en: "Open valve", hi: "वाल्व खोलें" },
  tighten: { en: "Tighten the flange bolts", hi: "फ्लेंज के बोल्ट कसें" },
  switch_filter: { en: "Change over to standby filter", hi: "स्टैंडबाय फ़िल्टर पर बदलें" },
  ack: { en: "Acknowledge alarm", hi: "अलार्म स्वीकार करें" },
  firstout: { en: "Read first-out trip cause", hi: "फर्स्ट-आउट ट्रिप कारण पढ़ें" },
  esd: { en: "Press Emergency Shutdown (ESD)", hi: "इमरजेंसी शटडाउन (ESD) दबाएं" },
  reset: { en: "Reset trip", hi: "ट्रिप रीसेट करें" },
  start: { en: "Start compressor", hi: "कंप्रेसर चालू करें" },
  bypass: { en: "Bypass trip protection", hi: "ट्रिप सुरक्षा बायपास करें" },
  raise_alarm: { en: "Raise the plant alarm", hi: "प्लांट अलार्म बजाएं" },
  radio: { en: "Radio the control room", hi: "कंट्रोल रूम को रेडियो करें" },
  go: { en: "Evacuate to here", hi: "यहाँ निकासी करें" },
};

const approach = (x, target, tau, dt) => target + (x - target) * Math.exp(-dt / Math.max(tau, 1e-3));
const moveTo = (x, target, step) => (Math.abs(target - x) <= step ? target : x + Math.sign(target - x) * step);
const fmt = (v, dp = 0) => Number(v).toFixed(dp);

function baseState() {
  return {
    P: 42, T: 118, vib: 3.1, brg: 76, lo: 2.6, dp: 0.5, rpm: 9800, lel: 0,
    running: true, esd: false, tripped: false, resetDone: false, filterSwitched: false, acked: false,
    sdv101: 1, sdv102: 1, sdv101Target: 1, sdv102Target: 1, bdv: 0,
    leak: 0, event: false, alarm: false, firstOut: "",
  };
}

// ---------------------------------------------------------------------------
// Gas leak at the compressor discharge flange
// ---------------------------------------------------------------------------
const gasLeak = {
  id: "gas_leak",
  group: "group_gas",
  title: { en: "Gas leak at compressor discharge", hi: "कंप्रेसर डिस्चार्ज पर गैस लीक" },
  summary: {
    en: "A gasket on the 42 bar discharge flange starts to fail during a night shift.",
    hi: "रात की शिफ्ट में 42 बार डिस्चार्ज फ्लेंज का गैस्केट खराब होने लगता है।",
  },
  rarity: { en: "Last real event on this unit: 2011", hi: "इस यूनिट पर पिछली असली घटना: 2011" },
  lastEvent: 2011,
  severity: "alarm",
  sop: { id: "SOP-GL-01", title: { en: "Hydrocarbon gas release at K-101", hi: "K-101 पर हाइड्रोकार्बन गैस रिसाव" } },
  copilotQuestion: { en: "Gas leak at the discharge flange, what first?", hi: "डिस्चार्ज फ्लेंज पर गैस लीक हो रही है, सबसे पहले क्या करें?" },
  skills: ["recognise", "communicate", "isolate", "evacuate"],
  walkdownSeconds: 45,
  briefing: {
    en: "Night shift, 02:10. Compressor K-101 is running normally at 42 bar g. Walk the unit before the control room hands over.",
    hi: "रात की शिफ्ट, 02:10। कंप्रेसर K-101 सामान्य रूप से 42 बार g पर चल रहा है। कंट्रोल रूम हैंडओवर से पहले यूनिट का राउंड लें।",
  },
  eventBanner: { en: "GAS DETECTED · GD-101 · HIGH", hi: "गैस का पता चला · GD-101 · हाई" },
  responsePrompt: {
    en: "The gasket has blown. Make the unit safe and get yourself out, the way the SOP requires.",
    hi: "गैस्केट फट गया है। SOP के अनुसार यूनिट को सुरक्षित करें और खुद को बाहर निकालें।",
  },
  cues: {
    discharge_flange: {
      en: "Frost on the flange bolts on a warm night, and a faint hiss. Gas is escaping and cooling as it expands.",
      hi: "गर्म रात में फ्लेंज के बोल्ट पर पाला और हल्की सी सीटी की आवाज़। गैस निकल रही है और फैलते हुए ठंडी हो रही है।",
    },
    detector: {
      en: "GD-101 read about 4% LEL during the walkdown. It normally reads 0.",
      hi: "राउंड के दौरान GD-101 लगभग 4% LEL दिखा रहा था। यह सामान्यतः 0 दिखाता है।",
    },
  },
  steps: [
    {
      id: "alarm", on: ["mcp.raise_alarm", "mcp.radio"], skill: "communicate", vet: 5, critical: true,
      label: { en: "Raise the alarm and inform the control room", hi: "अलार्म बजाएं और कंट्रोल रूम को बताएं" },
      sop: "4.1",
      hint: { en: "Before anything else, tell people. Hit the call point; the control room starts the response while you work.", hi: "सबसे पहले लोगों को बताओ। कॉल पॉइंट दबाओ; तुम काम करो तब तक कंट्रोल रूम कार्रवाई शुरू कर देगा।" },
    },
    {
      id: "esd", on: ["panel.esd"], skill: "isolate", vet: 9, critical: true,
      label: { en: "Press Emergency Shutdown (ESD) at the local panel", hi: "लोकल पैनल पर इमरजेंसी शटडाउन (ESD) दबाएं" },
      sop: "4.2",
      hint: { en: "Stop the machine from the panel. The panel is upwind; never walk to the leak to do it.", hi: "मशीन को पैनल से बंद करो। पैनल हवा की उल्टी दिशा में है; इसके लिए कभी लीक के पास मत जाओ।" },
    },
    {
      id: "isolate", on: ["discharge_valve.close"], after: ["esd"], skill: "isolate", vet: 18, critical: true,
      label: { en: "Verify isolation: SDV-102 stuck part open, close it", hi: "आइसोलेशन जाँचें: SDV-102 आधा खुला अटका, इसे बंद करें" },
      sop: "4.3",
      hint: { en: "ESD tells the valves to close. It doesn't promise they did. Look at the valve positions.", hi: "ESD वाल्व को बंद होने का आदेश देता है, पर यह गारंटी नहीं कि वे बंद हुए। वाल्व की स्थिति देखो।" },
    },
    {
      id: "blowdown", on: ["blowdown_valve.open"], after: ["isolate"], skill: "isolate", vet: 26, critical: true,
      label: { en: "Depressurise the isolated section via BDV-103 to flare", hi: "आइसोलेटेड हिस्से को BDV-103 से फ्लेयर में डिप्रेशराइज़ करें" },
      sop: "4.4",
      hint: { en: "Isolated is not safe. That pipe still holds 42 bar feeding the leak. Send it to flare.", hi: "आइसोलेट होना सुरक्षित होना नहीं है। उस पाइप में अब भी 42 बार दबाव है जो लीक को बढ़ा रहा है। इसे फ्लेयर में भेजो।" },
    },
    {
      id: "windcheck", on: ["windsock.check"], skill: "evacuate", vet: 32, critical: false,
      label: { en: "Check the wind direction", hi: "हवा की दिशा जाँचें" },
      sop: "4.5",
      hint: { en: "Look at the windsock before you walk anywhere.", hi: "कहीं भी जाने से पहले विंडसॉक देखो।" },
    },
    {
      id: "evacuate", on: ["muster_a.go"], after: ["alarm"], skill: "evacuate", vet: 38, critical: true,
      label: { en: "Evacuate upwind to Muster point A", hi: "हवा की उल्टी दिशा में मस्टर पॉइंट A पर जाएं" },
      sop: "4.5",
      hint: { en: "Go upwind. The nearest muster point is not always the right one.", hi: "हवा की उल्टी दिशा में जाओ। सबसे पास वाला मस्टर पॉइंट हमेशा सही नहीं होता।" },
    },
  ],
  endOn: ["muster_a.go", "muster_b.go"],
  walkdownHint: {
    en: "Look for things that don't fit the weather or the shift. Frost where it shouldn't be, a reading that should be zero.",
    hi: "ऐसी चीज़ें ढूँढो जो मौसम या शिफ्ट से मेल न खाएं। जहाँ नहीं होना चाहिए वहाँ पाला, जो रीडिंग शून्य होनी चाहिए वह शून्य न हो।",
  },
  veteran: {
    name: "Ramesh Yadav",
    years: 28,
    role: { en: "Shift in-charge, 28 years, retired 2025", hi: "शिफ्ट इंचार्ज, 28 वर्ष, 2025 में सेवानिवृत्त" },
    incident: { en: "Booster compressor flange gasket failure, 2011", hi: "बूस्टर कंप्रेसर फ्लेंज गैस्केट विफलता, 2011" },
    quote: {
      en: "ESD tells the valves to close. It doesn't promise they did.",
      hi: "ESD वाल्व को बंद होने का आदेश देता है। यह वादा नहीं करता कि वे बंद हो गए।",
    },
    noticed: {
      en: "Frost on a flange on a warm night, and a hiss you feel more than hear. The detector read only 4%, but frost means gas is expanding right there.",
      hi: "गर्म रात में फ्लेंज पर पाला, और ऐसी सीटी जो सुनाई कम, महसूस ज़्यादा होती है। डिटेक्टर सिर्फ़ 4% दिखा रहा था, पर पाला मतलब गैस वहीं फैल रही है।",
    },
    chose: {
      en: "Raised the alarm first, pressed ESD from the panel, then read the valve positions instead of trusting the shutdown. SDV-102 had stuck at about 30%. He closed it, blew the section down to flare and walked out upwind.",
      hi: "पहले अलार्म बजाया, पैनल से ESD दबाया, फिर शटडाउन पर भरोसा करने के बजाय वाल्व की स्थिति पढ़ी। SDV-102 लगभग 30% पर अटका था। उन्होंने उसे बंद किया, हिस्से को फ्लेयर में खाली किया और हवा की उल्टी दिशा में बाहर निकले।",
    },
    why: {
      en: "A stuck discharge valve keeps feeding the leak from the header. Muster point B was closer that night, but it was downwind of the cloud.",
      hi: "अटका हुआ डिस्चार्ज वाल्व हेडर से लीक को गैस देता रहता है। उस रात मस्टर पॉइंट B पास था, पर वह गैस के बादल की हवा की दिशा में था।",
    },
  },
  debriefQuestions: [
    { en: "What told you there was a leak before the alarm went off?", hi: "अलार्म बजने से पहले आपको कैसे पता चला कि लीक है?" },
    { en: "Why did Ramesh read the valve positions after pressing ESD?", hi: "ESD दबाने के बाद रमेश ने वाल्व की स्थिति क्यों पढ़ी?" },
    { en: "If the windsock was not visible, how would you choose the muster point?", hi: "अगर विंडसॉक दिखाई न दे, तो आप मस्टर पॉइंट कैसे चुनेंगे?" },
  ],

  init() {
    const s = baseState();
    s.leak = 0.03;
    s.lel = 3.8;
    return s;
  },

  startEvent(s) {
    s.event = true;
    s.leak = 1;
    s.lel += 18;
    s.alarm = true;
    s.firstOut = "GD-101 GAS HIGH";
  },

  tick(s, dt) {
    s.sdv101 = moveTo(s.sdv101, s.sdv101Target, dt / 2);
    s.sdv102 = moveTo(s.sdv102, s.sdv102Target, dt / 2);
    s.rpm = approach(s.rpm, s.running ? 9800 : 0, s.running ? 3 : 4, dt);

    const fed = s.running || s.sdv102 > 0.05;
    let target = 0;
    let tau = 1e9;
    if (s.bdv) {
      if (fed) { target = 34; tau = 3; } else { target = 0; tau = 5; }
    } else if (fed) {
      target = 42; tau = 3;
    } else if (s.leak > 0) {
      target = 0; tau = 150;
    }
    s.P = approach(s.P, target, tau, dt);
    s.T = approach(s.T, s.running ? 118 : 60, 40, dt);
    s.vib = approach(s.vib, s.running ? 3.1 : 0.2, 2, dt);
    s.brg = approach(s.brg, s.running ? 76 : 55, 60, dt);

    const source = 2.52 * s.leak * (s.P / 42);
    s.lel = Math.max(0, s.lel + (source - 0.018 * s.lel) * dt);
    if (s.event) s.alarm = s.lel >= 20 || s.alarm;
  },

  /** Returns a failure once the model reaches an unsafe end state. */
  checkFailure(s) {
    if (s.lel >= 100) {
      return {
        en: "The gas cloud reached its lower explosive limit and found an ignition source. Flash fire on the skid.",
        hi: "गैस का बादल अपनी निचली विस्फोटक सीमा तक पहुँचा और आग का स्रोत मिल गया। स्किड पर फ़्लैश फ़ायर।",
      };
    }
    return null;
  },

  /** Model side effects of an action in the response phase, plus what the trainee is told. */
  effect(s, key) {
    switch (key) {
      case "mcp.raise_alarm":
        return { msg: { en: "Plant alarm raised. The control room is starting the gas release response.", hi: "प्लांट अलार्म बजा। कंट्रोल रूम गैस रिसाव की कार्रवाई शुरू कर रहा है।" } };
      case "mcp.radio":
        return { msg: { en: "Control room informed by radio. They are raising the plant alarm.", hi: "रेडियो से कंट्रोल रूम को बताया। वे प्लांट अलार्म बजा रहे हैं।" } };
      case "panel.esd":
        if (s.esd) return { msg: { en: "ESD is already active.", hi: "ESD पहले से सक्रिय है।" } };
        s.esd = true;
        s.running = false;
        s.sdv101Target = 0;
        s.sdv102Target = 0.32; // the actuator sticks: the lesson of this drill
        return { msg: { en: "ESD pressed. Compressor stopping, shutdown valves commanded closed.", hi: "ESD दबाया। कंप्रेसर रुक रहा है, शटडाउन वाल्व बंद करने का आदेश दिया।" } };
      case "panel.ack":
        s.acked = true;
        return { msg: { en: "Alarm acknowledged. The siren is silenced; the beacon stays on until the gas clears.", hi: "अलार्म स्वीकार किया। सायरन बंद; गैस साफ़ होने तक बीकन चालू रहेगा।" } };
      case "panel.firstout":
        return { msg: { en: `First-out: GD-101 GAS HIGH. Valve positions: SDV-101 ${fmt(s.sdv101 * 100)}%, SDV-102 ${fmt(s.sdv102 * 100)}%, BDV-103 ${s.bdv ? "open" : "closed"}.`, hi: `फर्स्ट-आउट: GD-101 गैस हाई। वाल्व स्थिति: SDV-101 ${fmt(s.sdv101 * 100)}%, SDV-102 ${fmt(s.sdv102 * 100)}%, BDV-103 ${s.bdv ? "खुला" : "बंद"}।` } };
      case "panel.reset":
        return { blocked: true, msg: { en: "Reset refused: ESD is latched and gas is still detected.", hi: "रीसेट नहीं हुआ: ESD लॉक है और गैस अभी भी मौजूद है।" } };
      case "panel.start":
        if (s.lel > 10) {
          return { fail: { en: "You restarted the compressor inside a gas cloud. The motor ignited it.", hi: "आपने गैस के बादल के बीच कंप्रेसर चालू किया। मोटर से आग लग गई।" } };
        }
        return { blocked: true, penalty: 15, skill: "isolate", mistake: { en: "Tried to restart the compressor during a gas release", hi: "गैस रिसाव के दौरान कंप्रेसर चालू करने की कोशिश की" }, msg: { en: "Start refused: ESD is latched.", hi: "स्टार्ट नहीं हुआ: ESD लॉक है।" } };
      case "panel.bypass":
        return { penalty: 20, skill: "isolate", mistake: { en: "Bypassed protection during an emergency", hi: "आपात स्थिति में सुरक्षा बायपास की" }, tone: "alarm", msg: { en: "Bypassing protection during an emergency is prohibited. This has been logged.", hi: "आपात स्थिति में सुरक्षा बायपास करना मना है। यह दर्ज कर लिया गया है।" } };
      case "suction_valve.close":
        s.sdv101Target = 0;
        return { msg: { en: "SDV-101 closing.", hi: "SDV-101 बंद हो रहा है।" } };
      case "suction_valve.open":
        s.sdv101Target = 1;
        return { penalty: 10, skill: "isolate", mistake: { en: "Opened the suction valve during a gas release", hi: "गैस रिसाव के दौरान सक्शन वाल्व खोला" }, tone: "warn", msg: { en: "SDV-101 opening. That reconnects gas to the leaking section.", hi: "SDV-101 खुल रहा है। इससे गैस लीक वाले हिस्से में फिर पहुँचेगी।" } };
      case "discharge_valve.close":
        s.sdv102Target = 0;
        return { msg: { en: "SDV-102 re-stroked from the panel. It is closing fully.", hi: "SDV-102 को पैनल से फिर चलाया। यह पूरी तरह बंद हो रहा है।" } };
      case "discharge_valve.open":
        s.sdv102Target = 1;
        return { penalty: 10, skill: "isolate", mistake: { en: "Opened the discharge valve during a gas release", hi: "गैस रिसाव के दौरान डिस्चार्ज वाल्व खोला" }, tone: "warn", msg: { en: "SDV-102 opening. The header is feeding the leak again.", hi: "SDV-102 खुल रहा है। हेडर फिर से लीक को गैस दे रहा है।" } };
      case "blowdown_valve.open":
        s.bdv = 1;
        return { msg: s.sdv102 > 0.05 && !s.running
          ? { en: "BDV-103 open. Pressure is falling only slowly: the section is still connected to the header.", hi: "BDV-103 खुला। दबाव धीरे गिर रहा है: हिस्सा अभी भी हेडर से जुड़ा है।" }
          : { en: "BDV-103 open. The section is blowing down to flare.", hi: "BDV-103 खुला। हिस्सा फ्लेयर में खाली हो रहा है।" } };
      case "blowdown_valve.close":
        s.bdv = 0;
        return { msg: { en: "BDV-103 closed.", hi: "BDV-103 बंद।" } };
      case "discharge_flange.tighten":
        return { penalty: 25, skill: "isolate", mistake: { en: "Walked into the gas cloud to tighten a live, pressurised flange", hi: "दबाव वाली लीक फ्लेंज कसने के लिए गैस के बादल में गए" }, tone: "alarm", msg: { en: "Never work on a live, pressurised leak. You walked into the gas cloud.", hi: "दबाव वाली लीक पर कभी काम न करें। आप गैस के बादल में चले गए।" } };
      case "detector.check":
        return { msg: { en: `GD-101 reads ${fmt(s.lel)}% LEL.`, hi: `GD-101 ${fmt(s.lel)}% LEL दिखा रहा है।` } };
      case "windsock.check":
        return { msg: { en: "The wind is blowing from Muster point A towards Muster point B, across the leak.", hi: "हवा मस्टर पॉइंट A से मस्टर पॉइंट B की ओर, लीक के ऊपर से बह रही है।" } };
      case "muster_a.go":
        return { msg: { en: "You are at Muster point A, upwind of the release. Headcount reported.", hi: "आप मस्टर पॉइंट A पर हैं, रिसाव से हवा की उल्टी दिशा में। हाज़िरी दर्ज की गई।" } };
      case "muster_b.go":
        return { penalty: 20, skill: "evacuate", mistake: { en: "Evacuated downwind, through the gas cloud, to Muster point B", hi: "हवा की दिशा में, गैस के बादल से होकर मस्टर पॉइंट B पर गए" }, tone: "alarm", msg: { en: "Muster point B is downwind. You walked through the gas cloud to get there.", hi: "मस्टर पॉइंट B हवा की दिशा में है। वहाँ पहुँचने के लिए आप गैस के बादल से गुज़रे।" } };
      case "lube.check":
        return { msg: { en: `Lube oil ${fmt(s.lo, 1)} bar. Normal.`, hi: `ल्यूब ऑयल ${fmt(s.lo, 1)} बार। सामान्य।` } };
      default:
        return null;
    }
  },

  /** What the trainee sees when looking closely, by phase. */
  observe(s, target, phase) {
    const walk = phase === "walkdown";
    const M = {
      discharge_flange: walk
        ? { en: "Light frost on the flange bolts and a faint hiss. It is a warm night.", hi: "फ्लेंज के बोल्ट पर हल्का पाला और धीमी सीटी की आवाज़। रात गर्म है।" }
        : { en: "A white jet of gas from the gasket. Stay away.", hi: "गैस्केट से गैस की सफ़ेद धार। दूर रहें।" },
      detector: { en: `GD-101 reads ${fmt(s.lel)}% LEL. It normally reads 0.`, hi: `GD-101 ${fmt(s.lel)}% LEL दिखा रहा है। यह सामान्यतः 0 दिखाता है।` },
      compressor: s.running
        ? { en: `Running smoothly at ${fmt(s.rpm)} rpm.`, hi: `${fmt(s.rpm)} rpm पर सुचारु रूप से चल रहा है।` }
        : { en: "Stopped.", hi: "रुका हुआ।" },
      motor: { en: "Normal sound, normal temperature.", hi: "आवाज़ सामान्य, तापमान सामान्य।" },
      scrubber: { en: "Level normal, no liquid carry-over.", hi: "लेवल सामान्य, कोई तरल कैरी-ओवर नहीं।" },
      lube: { en: `Lube oil ${fmt(s.lo, 1)} bar, filter dP ${fmt(s.dp, 1)} bar. Normal.`, hi: `ल्यूब ऑयल ${fmt(s.lo, 1)} बार, फ़िल्टर dP ${fmt(s.dp, 1)} बार। सामान्य।` },
      suction_valve: { en: `Position ${fmt(s.sdv101 * 100)}% open.`, hi: `स्थिति ${fmt(s.sdv101 * 100)}% खुला।` },
      discharge_valve: { en: `Position ${fmt(s.sdv102 * 100)}% open.`, hi: `स्थिति ${fmt(s.sdv102 * 100)}% खुला।` },
      blowdown_valve: { en: s.bdv ? "Open, venting to flare." : "Closed.", hi: s.bdv ? "खुला, फ्लेयर में जा रहा है।" : "बंद।" },
      windsock: { en: "The wind is blowing from Muster point A towards Muster point B.", hi: "हवा मस्टर पॉइंट A से मस्टर पॉइंट B की ओर बह रही है।" },
    };
    return M[target] || null;
  },
};

// ---------------------------------------------------------------------------
// Compressor trip on high vibration (root cause: choked lube oil filter)
// ---------------------------------------------------------------------------
const compressorTrip = {
  id: "compressor_trip",
  group: "group_trip",
  title: { en: "Compressor trip on high vibration", hi: "हाई वाइब्रेशन पर कंप्रेसर ट्रिप" },
  summary: {
    en: "K-101 trips mid-shift. Getting gas flowing again fast is tempting. Finding out why comes first.",
    hi: "शिफ्ट के बीच K-101 ट्रिप होता है। जल्दी से गैस फिर चालू करने का मन करता है, पर पहले कारण पता करना ज़रूरी है।",
  },
  rarity: { en: "Last real event on this unit: 2019", hi: "इस यूनिट पर पिछली असली घटना: 2019" },
  lastEvent: 2019,
  severity: "warn",
  sop: { id: "SOP-CT-02", title: { en: "K-101 trip response and restart", hi: "K-101 ट्रिप प्रतिक्रिया और रीस्टार्ट" } },
  copilotQuestion: { en: "Compressor tripped, what do I do?", hi: "कंप्रेसर ट्रिप हो गया, क्या करूँ?" },
  skills: ["recognise", "communicate", "diagnose", "restart"],
  walkdownSeconds: 45,
  briefing: {
    en: "Day shift, 14:30. K-101 has been running for 41 days since its last overhaul. Walk the unit and note anything unusual.",
    hi: "दिन की शिफ्ट, 14:30। पिछले ओवरहॉल के बाद K-101 को चलते हुए 41 दिन हो गए हैं। यूनिट का राउंड लें और कुछ भी असामान्य हो तो नोट करें।",
  },
  eventBanner: { en: "K-101 TRIPPED · HIGH VIBRATION", hi: "K-101 ट्रिप · हाई वाइब्रेशन" },
  responsePrompt: {
    en: "K-101 has tripped. Respond and return it to service the way the SOP requires.",
    hi: "K-101 ट्रिप हो गया है। SOP के अनुसार कार्रवाई करें और इसे फिर से चालू करें।",
  },
  cues: {
    compressor: {
      en: "Vibration was higher than usual and creeping up through the walkdown.",
      hi: "राउंड के दौरान कंपन सामान्य से ज़्यादा था और धीरे-धीरे बढ़ रहा था।",
    },
    lube: {
      en: "Lube oil pressure was falling and the filter dP gauge was climbing into the red.",
      hi: "ल्यूब ऑयल का दबाव गिर रहा था और फ़िल्टर dP गेज लाल निशान की ओर बढ़ रहा था।",
    },
  },
  steps: [
    {
      id: "ack", on: ["panel.ack"], skill: "diagnose", vet: 4, critical: false,
      label: { en: "Acknowledge the alarm", hi: "अलार्म स्वीकार करें" },
      sop: "3.1",
      hint: { en: "Silence it so you can think, then read what it is telling you.", hi: "इसे शांत करो ताकि सोच सको, फिर पढ़ो यह क्या बता रहा है।" },
    },
    {
      id: "firstout", on: ["panel.firstout"], skill: "diagnose", vet: 8, critical: true,
      label: { en: "Read the first-out trip cause", hi: "फर्स्ट-आउट ट्रिप कारण पढ़ें" },
      sop: "3.2",
      hint: { en: "A trip is the machine telling you something. Read the first-out before you touch anything.", hi: "ट्रिप मशीन का संदेश है। कुछ भी छूने से पहले फर्स्ट-आउट पढ़ो।" },
    },
    {
      id: "report", on: ["mcp.radio"], skill: "communicate", vet: 14, critical: true,
      label: { en: "Radio the control room with the trip cause", hi: "कंट्रोल रूम को ट्रिप का कारण रेडियो पर बताएं" },
      sop: "3.3",
      hint: { en: "The control room needs to reroute gas. Tell them what tripped and why.", hi: "कंट्रोल रूम को गैस का रास्ता बदलना होगा। उन्हें बताओ क्या ट्रिप हुआ और क्यों।" },
    },
    {
      id: "checklube", on: ["lube.check"], after: ["firstout"], skill: "diagnose", vet: 25, critical: true,
      label: { en: "Check the lube oil system", hi: "ल्यूब ऑयल सिस्टम जाँचें" },
      sop: "3.4",
      hint: { en: "Vibration creeping up over a shift is rarely the bearing itself. Look at what feeds the bearing.", hi: "शिफ्ट भर में बढ़ता कंपन ज़्यादातर बेयरिंग की गलती नहीं होता। देखो बेयरिंग को क्या मिल रहा है।" },
    },
    {
      id: "switch", on: ["lube.switch_filter"], after: ["checklube"], skill: "diagnose", vet: 38, critical: true,
      label: { en: "Change over to the standby lube oil filter", hi: "स्टैंडबाय ल्यूब ऑयल फ़िल्टर पर बदलें" },
      sop: "3.5",
      hint: { en: "The duty filter is choked. Change over to the standby one and watch the pressure come back.", hi: "ड्यूटी फ़िल्टर जाम है। स्टैंडबाय पर बदलो और दबाव वापस आते देखो।" },
    },
    {
      id: "reset", on: ["panel.reset"], after: ["switch"], skill: "restart", vet: 44, critical: true,
      label: { en: "Reset the trip once the cause is cleared", hi: "कारण दूर होने के बाद ट्रिप रीसेट करें" },
      sop: "3.6",
      hint: { en: "Only reset once the permissives are healthy.", hi: "परमिसिव ठीक होने के बाद ही रीसेट करो।" },
    },
    {
      id: "restart", on: ["panel.start"], after: ["reset"], skill: "restart", vet: 50, critical: true,
      label: { en: "Restart K-101 and watch vibration settle", hi: "K-101 फिर से चालू करें और कंपन सामान्य होते देखें" },
      sop: "3.7",
      hint: { en: "Now start it, and stay to watch the vibration settle.", hi: "अब चालू करो, और कंपन ठीक होने तक वहीं रहकर देखो।" },
    },
  ],
  endOn: ["panel.start"],
  walkdownHint: {
    en: "Compare each reading with what it was at the start of the walkdown. Trends matter more than alarms.",
    hi: "हर रीडिंग की तुलना राउंड की शुरुआत वाली रीडिंग से करो। अलार्म से ज़्यादा ट्रेंड मायने रखता है।",
  },
  veteran: {
    name: "Sunita Meena",
    years: 31,
    role: { en: "Rotating equipment technician, 31 years", hi: "रोटेटिंग इक्विपमेंट तकनीशियन, 31 वर्ष" },
    incident: { en: "Repeated K-101 vibration trips, 2019", hi: "K-101 के बार-बार वाइब्रेशन ट्रिप, 2019" },
    quote: {
      en: "A trip is the machine telling you something. Bypass it and it stops telling you.",
      hi: "ट्रिप मशीन का संदेश है। बायपास करोगे तो वह बताना बंद कर देगी।",
    },
    noticed: {
      en: "Vibration creeping up over a shift is rarely the bearing itself. Lube oil pressure was drifting down and the filter dP was high.",
      hi: "शिफ्ट भर में बढ़ता कंपन ज़्यादातर बेयरिंग की गलती नहीं होता। ल्यूब ऑयल का दबाव गिर रहा था और फ़िल्टर dP ज़्यादा था।",
    },
    chose: {
      en: "Read the first-out before touching anything, told the control room, checked the lube oil, changed over to the standby filter, and only then reset and restarted.",
      hi: "कुछ भी छूने से पहले फर्स्ट-आउट पढ़ा, कंट्रोल रूम को बताया, ल्यूब ऑयल जाँचा, स्टैंडबाय फ़िल्टर पर बदला, और उसके बाद ही रीसेट करके चालू किया।",
    },
    why: {
      en: "In 2019 the previous shift bypassed the trip to keep gas flowing. The bearing seized and the unit was down for 9 days.",
      hi: "2019 में पिछली शिफ्ट ने गैस चालू रखने के लिए ट्रिप बायपास किया था। बेयरिंग जाम हो गई और यूनिट 9 दिन बंद रही।",
    },
  },
  debriefQuestions: [
    { en: "What was the trip actually telling you?", hi: "ट्रिप असल में आपको क्या बता रहा था?" },
    { en: "Which readings were trending before the trip, and when could you have acted?", hi: "ट्रिप से पहले कौन सी रीडिंग बदल रही थीं, और आप कब कार्रवाई कर सकते थे?" },
    { en: "When is a bypass ever acceptable, and who has to authorise it?", hi: "बायपास कब स्वीकार्य होता है, और इसे कौन अधिकृत करता है?" },
  ],

  init() {
    const s = baseState();
    s.vib = 3.4;
    s.brg = 78;
    s.lo = 2.45;
    s.dp = 0.9;
    return s;
  },

  startEvent(s) {
    s.event = true;
    s.tripped = true;
    s.running = false;
    s.alarm = true;
    s.vib = 11.4;
    s.sdv101Target = 0;
    s.sdv102Target = 0;
    s.firstOut = "VI-101 HIGH VIBRATION";
  },

  tick(s, dt, phase) {
    s.sdv101 = moveTo(s.sdv101, s.sdv101Target, dt / 2);
    s.sdv102 = moveTo(s.sdv102, s.sdv102Target, dt / 2);
    s.rpm = approach(s.rpm, s.running ? 9800 : 0, s.running ? 3 : 4, dt);

    if (phase === "walkdown") {
      s.vib = Math.min(6.6, s.vib + 0.075 * dt);
      s.brg = Math.min(87, s.brg + 0.2 * dt);
      s.lo = Math.max(1.92, s.lo - 0.012 * dt);
      s.dp = Math.min(1.9, s.dp + 0.022 * dt);
    } else if (s.event) {
      s.lo = approach(s.lo, s.filterSwitched ? 2.6 : 1.9, 1.5, dt);
      s.dp = approach(s.dp, s.filterSwitched ? 0.4 : 1.9, 1.5, dt);
      s.vib = approach(s.vib, s.running ? 2.8 : 0.3, s.running ? 4 : 1.5, dt);
      s.brg = approach(s.brg, s.running ? 74 : 62, 40, dt);
    }
    s.P = approach(s.P, s.running ? 42 : 38, 6, dt);
    s.T = approach(s.T, s.running ? 118 : 70, 40, dt);
  },

  checkFailure() {
    return null;
  },

  effect(s, key) {
    switch (key) {
      case "panel.ack":
        s.acked = true;
        return { msg: { en: "Alarm acknowledged. Siren silenced.", hi: "अलार्म स्वीकार किया। सायरन बंद।" } };
      case "panel.firstout":
        return { msg: { en: "First-out: VI-101 HIGH VIBRATION (11.4 mm/s, trip at 11.0). Second alarm: PI-201 LUBE OIL PRESSURE LOW.", hi: "फर्स्ट-आउट: VI-101 हाई वाइब्रेशन (11.4 mm/s, ट्रिप 11.0 पर)। दूसरा अलार्म: PI-201 ल्यूब ऑयल दबाव कम।" } };
      case "mcp.radio":
        return { msg: { en: "Control room informed. They are rerouting gas to K-102 while you investigate.", hi: "कंट्रोल रूम को बताया। आपकी जाँच तक वे गैस K-102 पर भेज रहे हैं।" } };
      case "mcp.raise_alarm":
        return { penalty: 5, skill: "communicate", mistake: { en: "Raised the general plant alarm for a single machine trip", hi: "एक मशीन के ट्रिप पर पूरे प्लांट का अलार्म बजाया" }, tone: "warn", msg: { en: "A trip is not a plant emergency. The general alarm sent the whole site to muster.", hi: "ट्रिप प्लांट की आपात स्थिति नहीं है। आम अलार्म से पूरा प्लांट मस्टर पर चला गया।" } };
      case "lube.check":
        return { msg: { en: `Lube oil ${fmt(s.lo, 2)} bar, filter dP ${fmt(s.dp, 1)} bar (limit 1.2). The duty filter is choked.`, hi: `ल्यूब ऑयल ${fmt(s.lo, 2)} बार, फ़िल्टर dP ${fmt(s.dp, 1)} बार (सीमा 1.2)। ड्यूटी फ़िल्टर जाम है।` } };
      case "lube.switch_filter":
        if (s.filterSwitched) return { msg: { en: "Already on the standby filter.", hi: "पहले से स्टैंडबाय फ़िल्टर पर है।" } };
        s.filterSwitched = true;
        return { msg: { en: "Changed over to the standby filter. Lube oil pressure is recovering.", hi: "स्टैंडबाय फ़िल्टर पर बदला। ल्यूब ऑयल का दबाव वापस आ रहा है।" } };
      case "panel.reset":
        if (s.lo < 2.3) {
          return { blocked: true, penalty: 5, skill: "restart", mistake: { en: "Tried to reset before fixing the cause", hi: "कारण ठीक किए बिना रीसेट करने की कोशिश की" }, msg: { en: `Reset refused: lube oil pressure ${fmt(s.lo, 2)} bar is below the 2.3 bar start permissive.`, hi: `रीसेट नहीं हुआ: ल्यूब ऑयल दबाव ${fmt(s.lo, 2)} बार है, स्टार्ट के लिए 2.3 बार चाहिए।` } };
        }
        s.resetDone = true;
        s.tripped = false;
        s.alarm = false;
        return { msg: { en: "Trip reset. Permissives healthy, ready to start.", hi: "ट्रिप रीसेट। परमिसिव ठीक, चालू करने के लिए तैयार।" } };
      case "panel.start":
        if (!s.resetDone) {
          return { blocked: true, msg: { en: "Start refused: the trip is not reset.", hi: "स्टार्ट नहीं हुआ: ट्रिप रीसेट नहीं है।" } };
        }
        s.running = true;
        s.sdv101Target = 1;
        s.sdv102Target = 1;
        return { msg: { en: "K-101 starting. Vibration is settling at about 3 mm/s.", hi: "K-101 चालू हो रहा है। कंपन लगभग 3 mm/s पर ठीक हो रहा है।" } };
      case "panel.bypass":
        return { fail: { en: "You bypassed the trip and restarted. Starved of oil, the drive-end bearing seized within minutes.", hi: "आपने ट्रिप बायपास करके चालू किया। तेल न मिलने से कुछ ही मिनट में ड्राइव-एंड बेयरिंग जाम हो गई।" } };
      case "panel.esd":
        return { penalty: 5, skill: "diagnose", mistake: { en: "Pressed ESD on a unit that had already tripped safely", hi: "पहले से सुरक्षित ट्रिप हुई यूनिट पर ESD दबाया" }, tone: "warn", msg: { en: "The unit had already tripped safely. ESD has now depressurised it and added hours to the restart.", hi: "यूनिट पहले ही सुरक्षित ट्रिप हो चुकी थी। ESD से अब इसका दबाव निकल गया और रीस्टार्ट में घंटों की देरी होगी।" } };
      case "discharge_flange.tighten":
        return { msg: { en: "The flange is dry and tight. Nothing to do here.", hi: "फ्लेंज सूखी और कसी हुई है। यहाँ कुछ करने की ज़रूरत नहीं।" } };
      case "windsock.check":
        return { msg: { en: "Light wind from the west.", hi: "पश्चिम से हल्की हवा।" } };
      case "muster_a.go":
      case "muster_b.go":
        return { penalty: 10, skill: "communicate", mistake: { en: "Left the unit during a trip that needed an operator", hi: "ऐसे ट्रिप के दौरान यूनिट छोड़ दी जिसमें ऑपरेटर की ज़रूरत थी" }, tone: "warn", msg: { en: "There is no gas release. Leaving the unit delays the restart.", hi: "कोई गैस रिसाव नहीं है। यूनिट छोड़ने से रीस्टार्ट में देरी होती है।" } };
      case "blowdown_valve.open":
        s.bdv = 1;
        return { penalty: 5, skill: "diagnose", mistake: { en: "Blew down a healthy unit after a trip", hi: "ट्रिप के बाद ठीक यूनिट को ब्लोडाउन किया" }, tone: "warn", msg: { en: "Blowing down flares gas you will need to repressurise later.", hi: "ब्लोडाउन से वह गैस फ्लेयर हो रही है जिसकी बाद में फिर ज़रूरत पड़ेगी।" } };
      case "blowdown_valve.close":
        s.bdv = 0;
        return { msg: { en: "BDV-103 closed.", hi: "BDV-103 बंद।" } };
      case "detector.check":
        return { msg: { en: "GD-101 reads 0% LEL.", hi: "GD-101 0% LEL दिखा रहा है।" } };
      default:
        return null;
    }
  },

  observe(s, target, phase) {
    const walk = phase === "walkdown";
    const M = {
      compressor: walk
        ? { en: `Vibration ${fmt(s.vib, 1)} mm/s and creeping up. The casing feels rougher than last shift.`, hi: `कंपन ${fmt(s.vib, 1)} mm/s और धीरे-धीरे बढ़ रहा है। केसिंग पिछली शिफ्ट से ज़्यादा खुरदरा महसूस हो रहा है।` }
        : s.running
          ? { en: `Running, vibration ${fmt(s.vib, 1)} mm/s.`, hi: `चल रहा है, कंपन ${fmt(s.vib, 1)} mm/s।` }
          : { en: "Stopped. Casing still warm.", hi: "रुका हुआ। केसिंग अभी भी गर्म है।" },
      lube: { en: `Lube oil ${fmt(s.lo, 2)} bar and falling. Filter dP ${fmt(s.dp, 1)} bar; the gauge is near the red.`, hi: `ल्यूब ऑयल ${fmt(s.lo, 2)} बार और गिर रहा है। फ़िल्टर dP ${fmt(s.dp, 1)} बार; गेज लाल निशान के पास है।` },
      motor: { en: "Normal sound, normal temperature.", hi: "आवाज़ सामान्य, तापमान सामान्य।" },
      discharge_flange: { en: "Dry, no frost, no smell.", hi: "सूखी, कोई पाला नहीं, कोई गंध नहीं।" },
      detector: { en: "GD-101 reads 0% LEL.", hi: "GD-101 0% LEL दिखा रहा है।" },
      scrubber: { en: "Level normal.", hi: "लेवल सामान्य।" },
      suction_valve: { en: `Position ${fmt(s.sdv101 * 100)}% open.`, hi: `स्थिति ${fmt(s.sdv101 * 100)}% खुला।` },
      discharge_valve: { en: `Position ${fmt(s.sdv102 * 100)}% open.`, hi: `स्थिति ${fmt(s.sdv102 * 100)}% खुला।` },
      blowdown_valve: { en: s.bdv ? "Open." : "Closed.", hi: s.bdv ? "खुला।" : "बंद।" },
      windsock: { en: "Light wind from the west.", hi: "पश्चिम से हल्की हवा।" },
    };
    return M[target] || null;
  },
};

export const SCENARIOS = [gasLeak, compressorTrip];

export function getScenario(id) {
  return SCENARIOS.find((s) => s.id === id) || null;
}
