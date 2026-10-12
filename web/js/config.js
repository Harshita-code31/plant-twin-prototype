// Settings a plant changes without touching the code.
export const CONFIG = {
  // Link to the SOP copilot. Leave empty until it is deployed; the "Ask SOP" buttons then show as coming soon.
  copilotUrl: "",

  // Same pass rule as the AbhyasAR Unity app (Assets/ScriptsQuiz/TrainingScoring.cs).
  passMark: 80,

  // Plant target for time-to-competency, in days, drawn as a line on the competency map.
  targetDays: 45,

  unit: { tag: "K-101", name: { en: "Gas compression unit", hi: "गैस कंप्रेशन यूनिट" } },

  mentors: [
    { id: "m1", name: "Anil Sharma", role: { en: "Shift supervisor", hi: "शिफ्ट सुपरवाइज़र" } },
    { id: "m2", name: "Priya Nair", role: { en: "Senior panel operator", hi: "सीनियर पैनल ऑपरेटर" } },
  ],
};
