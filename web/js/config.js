// Settings a plant changes without touching the code.
export const CONFIG = {
  // Link to the SOP copilot, relative to this page (same site: web/copilot/) or a full URL. Empty hides the "Ask SOP" links.
  copilotUrl: "copilot/",

  // Same pass rule as the AbhyasAR Unity app (Assets/ScriptsQuiz/TrainingScoring.cs).
  passMark: 80,

  // Plant target for time-to-competency, in days, drawn as a line on the competency map.
  targetDays: 45,

  unit: { tag: "K-101", name: { en: "Fuel gas booster compressor", hi: "फ्यूल गैस बूस्टर कंप्रेसर" } },

  mentors: [
    { id: "m1", name: "Anil Sharma", role: { en: "Shift supervisor", hi: "शिफ्ट सुपरवाइज़र" } },
    { id: "m2", name: "Priya Nair", role: { en: "Senior panel operator", hi: "सीनियर पैनल ऑपरेटर" } },
  ],
};
