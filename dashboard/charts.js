// Small dependency-free chart kit (SVG + HTML). Every chart lives in a card with a title, a
// legend when there is more than one series, a hover/focus tooltip, and a "Table" toggle that
// swaps the chart for an equivalent table (tooltips enhance, never gate).
// Colors come from CSS custom properties (--viz-*) defined in styles.css. A series marked
// `hatch: true` is drawn in yellow/black hazard stripes (used for "not passed"), so pass/fail
// never depends on hue alone.

const SVG = "http://www.w3.org/2000/svg";

function h(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === "class") node.className = v;
    else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v === true ? "" : v);
  }
  for (const c of children.flat()) if (c != null) node.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return node;
}

function s(tag, attrs = {}) {
  const node = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) if (v != null) node.setAttribute(k, v);
  return node;
}

const fmtInt = (n) => Number(n).toLocaleString();

/**
 * Card shell: title, optional subtitle and legend, chart body, table twin.
 * Returns { card, body, tooltip } -- the caller renders into body.
 */
export function chartCard({ title, subtitle, legend, table, empty }) {
  const body = h("div", { class: "viz-body" });
  const tableWrap = h("div", { class: "viz-table table-wrap", hidden: true });
  const tooltip = h("div", { class: "viz-tip", role: "status", hidden: true });
  const toggle = h("button", { class: "link viz-toggle", type: "button", "aria-pressed": "false" }, "Table");
  toggle.addEventListener("click", () => {
    const showTable = tableWrap.hidden;
    tableWrap.hidden = !showTable;
    body.hidden = showTable;
    toggle.textContent = showTable ? "Chart" : "Table";
    toggle.setAttribute("aria-pressed", String(showTable));
    if (showTable && !tableWrap.firstChild && table) tableWrap.append(buildTable(table));
  });
  const head = h("div", { class: "viz-head" },
    h("div", {}, h("h3", {}, title), subtitle ? h("p", { class: "muted viz-sub" }, subtitle) : null),
    table && !empty ? toggle : null);
  const card = h("figure", { class: "card viz-card" }, head, legend && !empty ? renderLegend(legend) : null, body, tableWrap, tooltip);
  if (empty) body.append(h("p", { class: "empty" }, empty));
  return { card, body, tooltip };
}

function renderLegend(items) {
  return h("div", { class: "viz-legend" }, items.map((it) =>
    h("span", { class: "viz-legend-item" }, h("span", { class: it.hatch ? "viz-swatch hatch" : "viz-swatch", style: `background:var(${it.color})` }), it.label)));
}

function buildTable({ columns, rows }) {
  return h("table", {},
    h("thead", {}, h("tr", {}, columns.map((c, i) => h("th", { class: i ? "num" : null }, c)))),
    h("tbody", {}, rows.map((r) => h("tr", {}, r.map((v, i) => h("td", { class: i ? "num" : null }, v))))));
}

// Axis max for counts: 4 equal whole-number steps (1, 2, 5, 10, 20, 50... per step), so every
// tick label is an integer and the steps are even.
function niceMax(v) {
  const raw = Math.max(1, v / 4);
  const pow = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw && Number.isInteger(s)) || Math.ceil(raw);
  return step * 4;
}

function placeTip(tooltip, card, x, y) {
  tooltip.hidden = false;
  const cw = card.clientWidth;
  const tw = tooltip.offsetWidth;
  tooltip.style.left = `${Math.max(8, Math.min(cw - tw - 8, x - tw / 2))}px`;
  tooltip.style.top = `${Math.max(8, y - tooltip.offsetHeight - 10)}px`;
}

function tipContent(tooltip, title, rows) {
  tooltip.replaceChildren(
    h("div", { class: "viz-tip-title" }, title),
    ...rows.map((r) => h("div", { class: "viz-tip-row" },
      r.color ? h("span", { class: r.hatch ? "viz-key hatch" : "viz-key", style: `background:var(${r.color})` }) : null,
      h("strong", {}, r.value), " ", h("span", { class: "muted" }, r.label))));
}

// Yellow/black diagonal stripes as an SVG pattern, unique per chart.
let hatchSeq = 0;
function hatchPattern(svg) {
  const id = `viz-hatch-${++hatchSeq}`;
  const pattern = s("pattern", { id, width: 8, height: 8, patternUnits: "userSpaceOnUse", patternTransform: "rotate(45)" });
  pattern.append(s("rect", { width: 8, height: 8, fill: "var(--bg)" }), s("rect", { width: 4, height: 8, fill: "var(--viz-fail)" }));
  const defs = s("defs");
  defs.append(pattern);
  svg.append(defs);
  return `url(#${id})`;
}

/**
 * Vertical (optionally stacked) column chart.
 * buckets: [{ label, tipTitle, values: { seriesKey: number } }]
 * series:  [{ key, label, color: "--viz-..." }]  (bottom -> top)
 * marker:  optional { index, label } -- a vertical rule before bucket `index` (e.g. a pass mark)
 */
export function columnChart(body, card, tooltip, { buckets, series, height = 200, marker, yTitle }) {
  const render = () => {
    const W = Math.max(280, body.clientWidth);
    const M = { top: 12, right: 8, bottom: 26, left: 36 };
    const plotW = W - M.left - M.right;
    const plotH = height;
    const totals = buckets.map((b) => series.reduce((n, sr) => n + (b.values[sr.key] || 0), 0));
    const yMax = niceMax(Math.max(1, ...totals));
    const band = plotW / buckets.length;
    const barW = Math.max(3, Math.min(24, band * 0.62));
    const y = (v) => M.top + plotH - (v / yMax) * plotH;

    const svg = s("svg", { viewBox: `0 0 ${W} ${plotH + M.top + M.bottom}`, width: "100%", role: "img", "aria-label": yTitle || "chart" });
    const hatchFill = series.some((sr) => sr.hatch) ? hatchPattern(svg) : null;
    const fillOf = (sr) => (sr.hatch ? hatchFill : `var(${sr.color})`);
    // gridlines + y ticks
    for (let i = 0; i <= 4; i++) {
      const v = (yMax / 4) * i;
      const gy = y(v);
      svg.append(s("line", { x1: M.left, x2: W - M.right, y1: gy, y2: gy, class: i === 0 ? "viz-axis" : "viz-grid" }));
      const t = s("text", { x: M.left - 6, y: gy + 4, class: "viz-tick", "text-anchor": "end" });
      t.textContent = fmtInt(Math.round(v));
      svg.append(t);
    }
    // x labels, thinned so they never collide
    const every = Math.max(1, Math.ceil(buckets.length / Math.max(1, Math.floor(plotW / 64))));
    buckets.forEach((b, i) => {
      if (i % every !== 0) return;
      const t = s("text", { x: M.left + band * i + band / 2, y: M.top + plotH + 18, class: "viz-tick", "text-anchor": "middle" });
      t.textContent = b.label;
      svg.append(t);
    });
    // bars
    const groups = [];
    buckets.forEach((b, i) => {
      const g = s("g", { class: "viz-col" });
      const x = M.left + band * i + (band - barW) / 2;
      let acc = 0;
      const drawn = series.map((sr) => ({ sr, v: b.values[sr.key] || 0 })).filter((d) => d.v > 0);
      drawn.forEach((d, j) => {
        const y0 = y(acc), y1 = y(acc + d.v);
        acc += d.v;
        const gap = j > 0 ? 2 : 0; // 2px ground gap between stacked segments
        const hh = Math.max(0, y0 - y1 - gap);
        if (hh <= 0) return;
        g.append(s("rect", { x, y: y1, width: barW, height: hh, fill: fillOf(d.sr),
          stroke: d.sr.hatch ? "var(--viz-fail)" : null, "stroke-width": d.sr.hatch ? 1 : null }));
      });
      // hit target = the whole band, taller than the mark
      const hit = s("rect", { x: M.left + band * i, y: M.top, width: band, height: plotH, class: "viz-hit", tabindex: "0",
        "aria-label": `${b.tipTitle || b.label}: ${series.map((sr) => `${sr.label} ${b.values[sr.key] || 0}`).join(", ")}` });
      const show = () => {
        groups.forEach((gg, k) => gg.classList.toggle("dim", k !== i));
        tipContent(tooltip, b.tipTitle || b.label, [...series].reverse().map((sr) => ({ color: sr.color, hatch: sr.hatch, value: fmtInt(b.values[sr.key] || 0), label: sr.label })));
        const sr = svg.getBoundingClientRect(), cr = card.getBoundingClientRect(), k = sr.width / W;
        placeTip(tooltip, card, sr.left - cr.left + (M.left + band * i + band / 2) * k, sr.top - cr.top + y(acc) * k);
      };
      const hide = () => { tooltip.hidden = true; groups.forEach((gg) => gg.classList.remove("dim")); };
      hit.addEventListener("pointerenter", show);
      hit.addEventListener("pointerleave", hide);
      hit.addEventListener("focus", show);
      hit.addEventListener("blur", hide);
      g.append(hit);
      groups.push(g);
      svg.append(g);
    });
    if (marker) {
      const mx = M.left + band * marker.index;
      svg.append(s("line", { x1: mx, x2: mx, y1: M.top, y2: M.top + plotH, class: "viz-marker" }));
      const t = s("text", { x: mx + 4, y: M.top + 10, class: "viz-tick viz-marker-label" });
      t.textContent = marker.label;
      svg.append(t);
    }
    body.replaceChildren(svg);
  };
  render();
  observeWidth(body, render);
}

/**
 * Horizontal bars, one row per item (HTML, so long labels wrap instead of colliding).
 * rows: [{ label, value (0..max), valueText, tip: [..lines] }]
 */
export function barList(body, card, tooltip, { rows, max = 1, color = "--viz-pass", hatch = false }) {
  const list = h("div", { class: "viz-bars" }, rows.map((r) => {
    const pct = max ? Math.max(0, Math.min(1, r.value / max)) : 0;
    const row = h("div", { class: "viz-bar-row", tabindex: "0", "aria-label": `${r.label}: ${r.valueText}` },
      h("div", { class: "viz-bar-label" }, r.label),
      h("div", { class: "viz-bar-track" },
        h("div", { class: hatch ? "viz-bar hatch" : "viz-bar", style: `width:${(pct * 100).toFixed(1)}%;background:var(${r.color || color})` })),
      h("div", { class: "viz-bar-value" }, r.valueText));
    const show = () => {
      tipContent(tooltip, r.label, (r.tip || []).map((t) => ({ value: t.value, label: t.label })));
      const rr = row.getBoundingClientRect(), cr = card.getBoundingClientRect();
      placeTip(tooltip, card, rr.left - cr.left + rr.width * 0.6, rr.top - cr.top);
    };
    const hide = () => (tooltip.hidden = true);
    if (r.tip && r.tip.length) {
      row.addEventListener("pointerenter", show);
      row.addEventListener("pointerleave", hide);
      row.addEventListener("focus", show);
      row.addEventListener("blur", hide);
    }
    return row;
  }));
  body.replaceChildren(list);
}

/** Progress meter: fill on a lighter track of the same hue. */
export function meter({ label, value, max, valueText, sub }) {
  const pct = max ? Math.min(1, value / max) : 0;
  return h("div", { class: "viz-meter", role: "meter", "aria-valuemin": "0", "aria-valuemax": String(max), "aria-valuenow": String(value), "aria-label": label },
    h("div", { class: "viz-meter-head" }, h("span", {}, label), h("strong", {}, valueText)),
    h("div", { class: "viz-meter-track" }, h("div", { class: "viz-meter-fill", style: `width:${(pct * 100).toFixed(1)}%` })),
    sub ? h("div", { class: "muted viz-meter-sub" }, sub) : null);
}

const observers = new WeakMap();
function observeWidth(node, fn) {
  if (observers.has(node)) observers.get(node).disconnect();
  let last = node.clientWidth;
  let raf = 0;
  const ro = new ResizeObserver(() => {
    if (Math.abs(node.clientWidth - last) < 8) return;
    last = node.clientWidth;
    cancelAnimationFrame(raf);
    raf = requestAnimationFrame(fn);
  });
  ro.observe(node);
  observers.set(node, ro);
}
