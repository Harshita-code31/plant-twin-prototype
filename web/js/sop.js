// The plant's SOPs, read from the same files the SOP copilot answers from (web/copilot/sops/),
// so lessons, drills and the copilot always quote the same sections.

let library = null;
let loading = null;

export function loadSops() {
  if (library) return Promise.resolve(library);
  if (!loading) {
    loading = fetch("copilot/sops/index.json")
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error("SOP index unavailable"))))
      .then((names) => Promise.all(names.map((n) => fetch(`copilot/sops/${encodeURIComponent(n)}`).then((r) => r.json()))))
      .then((docs) => {
        library = docs;
        return library;
      })
      .catch((err) => {
        console.warn("SOPs could not be loaded:", err);
        loading = null;
        return [];
      });
  }
  return loading;
}

/** The section of an SOP, or null if the SOPs are not loaded yet or the section does not exist. */
export function sopSection(sopId, number) {
  const doc = library?.find((d) => d.id === sopId);
  const section = doc?.sections.find((s) => String(s.number) === String(number));
  return section ? { doc, section } : null;
}

export function sopDoc(sopId) {
  return library?.find((d) => d.id === sopId) || null;
}
