// Klemme15 Live: a phone dashboard for `klemme15 serve`. It only reads the JSON the headless server already offers
// (/live.json, /faults, /health, /info); it never sends anything to the car.
import { createRoot } from "react-dom/client";
import { useEffect, useMemo, useRef, useState } from "react";

type Value = { value: number | null; unit: string; name: string };
type Live = { time: string; values: Record<string, Value> };
type Health = { ok: boolean; reads: number; errors: number; lastError: string | null; lastUpdate: string };
type Info = { car?: string; interval?: number; version?: string };
type Fault = { code: string; state: string; Description?: string | null; description?: string | null; priority: string; advice?: string };
type Prefs = { imperial: boolean; theme: "auto" | "dark" | "light" };

const HISTORY = 120; // samples kept per value for the sparklines

function loadPrefs(): Prefs {
  try {
    const raw = localStorage.getItem("klemme15.prefs");
    if (raw) return { imperial: false, theme: "auto", ...JSON.parse(raw) };
  } catch { /* private mode: defaults */ }
  return { imperial: navigator.language === "en-US", theme: "auto" };
}

function savePrefs(p: Prefs) {
  try { localStorage.setItem("klemme15.prefs", JSON.stringify(p)); } catch { /* ignore */ }
}

/** Converts a value to the user's units. */
function convert(v: number, unit: string, imperial: boolean): [number, string] {
  if (!imperial) return [v, unit];
  if (unit === "°C") return [v * 9 / 5 + 32, "°F"];
  if (unit === "km/h") return [v * 0.621371, "mph"];
  if (unit === "kPa") return [v * 0.145038, "psi"];
  return [v, unit];
}

function fmt(v: number | null | undefined, unit = ""): string {
  if (v === null || v === undefined || Number.isNaN(v)) return "–";
  const digits = Math.abs(v) >= 100 || unit === "rpm" ? 0 : 1;
  return v.toFixed(digits);
}

async function getJson<T>(url: string): Promise<T> {
  const r = await fetch(url, { cache: "no-store" });
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  return r.json();
}

type Level = "ok" | "warn" | "bad";
type Alert = { level: Level; text: string };

/** Rules of thumb for an E-series petrol engine; the same ones the desktop app explains. */
function alerts(values: Record<string, Value>): Alert[] {
  const list: Alert[] = [];
  const n = (id: string) => values[id]?.value ?? null;
  const rpm = n("rpm"), coolant = n("coolant"), volt = n("voltage");
  if (coolant !== null && coolant >= 118) list.push({ level: "bad", text: `Coolant ${coolant.toFixed(0)} °C: overheating. Stop safely and let it cool.` });
  else if (coolant !== null && coolant >= 112) list.push({ level: "warn", text: `Coolant ${coolant.toFixed(0)} °C: hotter than these engines normally run.` });
  if (volt !== null && rpm !== null) {
    if (rpm > 500 && volt < 13.0) list.push({ level: "warn", text: `${volt.toFixed(1)} V with the engine running: the alternator may not be charging.` });
    else if (rpm > 500 && volt > 15.0) list.push({ level: "bad", text: `${volt.toFixed(1)} V: overcharging. Check the alternator regulator.` });
    else if (rpm <= 500 && volt < 12.0) list.push({ level: "warn", text: `Battery at ${volt.toFixed(1)} V: charge it before any coding or service work.` });
  }
  for (const bank of ["1", "2"]) {
    const s = n(`stft${bank}`), l = n(`ltft${bank}`);
    if (s !== null && l !== null && Math.abs(s + l) > 12 && rpm !== null && rpm > 500)
      list.push({ level: "warn", text: `Bank ${bank} fuel correction ${(s + l).toFixed(0)} %: run the fuel trim test in the app.` });
  }
  return list;
}

function Sparkline({ data, color }: { data: number[]; color: string }) {
  if (data.length < 2) return <svg className="spark" viewBox="0 0 100 28" />;
  const min = Math.min(...data), max = Math.max(...data), span = max - min || 1;
  const pts = data.map((v, i) => `${(i / (HISTORY - 1)) * 100},${26 - ((v - min) / span) * 24}`).join(" ");
  return (
    <svg className="spark" viewBox="0 0 100 28" preserveAspectRatio="none" aria-hidden="true">
      <polyline points={pts} fill="none" stroke={color} strokeWidth="1.6" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

/** A 240° arc gauge. */
function Gauge({ label, value, unit, min, max, warn, bad }: { label: string; value: number | null; unit: string; min: number; max: number; warn?: number; bad?: number }) {
  const r = 42, c = 2 * Math.PI * r, sweep = (240 / 360) * c;
  const frac = value === null ? 0 : Math.max(0, Math.min(1, (value - min) / (max - min)));
  const level = value === null ? "" : bad !== undefined && value >= bad ? "bad" : warn !== undefined && value >= warn ? "warn" : "ok";
  return (
    <div className="gauge">
      <svg viewBox="0 0 100 100" role="img" aria-label={`${label} ${fmt(value, unit)} ${unit}`}>
        <circle cx="50" cy="50" r={r} className="track" strokeDasharray={`${sweep} ${c}`} transform="rotate(150 50 50)" />
        <circle cx="50" cy="50" r={r} className={`fill ${level}`} strokeDasharray={`${sweep * frac} ${c}`} transform="rotate(150 50 50)" />
        <text x="50" y="52" className="big">{fmt(value, unit)}</text>
        <text x="50" y="68" className="unit">{unit}</text>
      </svg>
      <div className="gauge-label">{label}</div>
    </div>
  );
}

function FaultsTab() {
  const [faults, setFaults] = useState<Fault[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const read = async () => {
    setBusy(true); setError(null);
    try {
      const data = await getJson<Fault[] | { error: string }>("/faults");
      if (Array.isArray(data)) setFaults(data); else setError(data.error);
    } catch (e) { setError(String(e)); }
    setBusy(false);
  };
  const order: Record<string, number> = { FixNow: 0, CheckIt: 1, Background: 2 };
  return (
    <section className="panel">
      <div className="row between">
        <h2>Engine faults</h2>
        <button onClick={read} disabled={busy}>{busy ? "Reading..." : "Read faults"}</button>
      </div>
      <p className="muted small">Reads the engine computer's fault memory. Clearing faults is done in the desktop app, which checks the battery first.</p>
      {error && <p className="alert bad">{error}</p>}
      {faults && faults.length === 0 && <p className="alert ok">No faults stored.</p>}
      {faults && [...faults].sort((a, b) => (order[a.priority] ?? 3) - (order[b.priority] ?? 3)).map((f, i) => (
        <div key={i} className={`fault ${f.priority === "FixNow" ? "bad" : f.priority === "CheckIt" ? "warn" : "muted-border"}`}>
          <div className="row between"><b className="mono">{f.code}</b><span className="small">{f.priority === "FixNow" ? "Fix now" : f.priority === "CheckIt" ? "Check it" : "Background"} · {f.state}</span></div>
          <div>{f.description ?? f.Description ?? "No description"}</div>
        </div>
      ))}
    </section>
  );
}

function App() {
  const [prefs, setPrefsState] = useState<Prefs>(loadPrefs);
  const [live, setLive] = useState<Live | null>(null);
  const [health, setHealth] = useState<Health | null>(null);
  const [info, setInfo] = useState<Info>({});
  const [offline, setOffline] = useState(false);
  const [tab, setTab] = useState<"live" | "faults" | "settings">("live");
  const history = useRef<Record<string, number[]>>({});
  const [, force] = useState(0);

  const setPrefs = (p: Prefs) => { setPrefsState(p); savePrefs(p); };
  useEffect(() => {
    if (prefs.theme === "auto") document.documentElement.removeAttribute("data-theme");
    else document.documentElement.setAttribute("data-theme", prefs.theme);
  }, [prefs.theme]);

  useEffect(() => { getJson<Info>("/info").then(setInfo).catch(() => {}); }, []);

  useEffect(() => {
    let stop = false;
    const interval = Math.max(500, (info.interval ?? 1) * 1000);
    const tick = async () => {
      try {
        const d = await getJson<Live>("/live.json");
        if (stop) return;
        for (const [id, v] of Object.entries(d.values)) {
          if (v.value === null) continue;
          const h = (history.current[id] ??= []);
          h.push(v.value);
          if (h.length > HISTORY) h.shift();
        }
        setLive(d); setOffline(false); force(x => x + 1);
      } catch { if (!stop) setOffline(true); }
      try { const h = await getJson<Health>("/health"); if (!stop) setHealth(h); } catch { /* shown as offline */ }
    };
    tick();
    const t = setInterval(tick, interval);
    return () => { stop = true; clearInterval(t); };
  }, [info.interval]);

  const values = live?.values ?? {};
  const age = live ? (Date.now() - new Date(live.time).getTime()) / 1000 : Infinity;
  const status: { level: Level; text: string } = offline
    ? { level: "bad", text: "Can't reach the Pi" }
    : !live || age > 10 ? { level: "warn", text: health?.lastError ? `No fresh data: ${health.lastError}` : "Waiting for the car..." }
    : { level: "ok", text: `Live · updated ${age < 2 ? "just now" : `${age.toFixed(0)} s ago`}` };
  const list = useMemo(() => alerts(values), [live]);

  const v = (id: string): [number | null, string] => {
    const x = values[id];
    if (!x || x.value === null) return [null, x?.unit ?? ""];
    return convert(x.value, x.unit, prefs.imperial);
  };
  const [rpm] = v("rpm"); const [speed, speedUnit] = v("speed"); const [coolant, coolUnit] = v("coolant"); const [volt] = v("voltage");
  const f = prefs.imperial;

  return (
    <>
      <style>{CSS}</style>
      <header className="row between">
        <div>
          <div className="brand">Klemme15 <span className="muted">Live</span></div>
          <div className="muted small">{info.car ?? "Headless telemetry"}</div>
        </div>
        <div className={`pill ${status.level}`}><span className="dot" />{status.text}</div>
      </header>

      <nav className="tabs" role="tablist">
        {(["live", "faults", "settings"] as const).map(t => (
          <button key={t} role="tab" aria-selected={tab === t} className={tab === t ? "on" : ""} onClick={() => setTab(t)}>
            {t === "live" ? "Live" : t === "faults" ? "Faults" : "Settings"}
          </button>
        ))}
      </nav>

      {tab === "live" && (
        <>
          {list.map((a, i) => <div key={i} className={`alert ${a.level}`}>{a.text}</div>)}
          <section className="gauges">
            <Gauge label="Engine speed" value={rpm} unit="rpm" min={0} max={7000} warn={6500} />
            <Gauge label="Coolant" value={coolant} unit={coolUnit || "°C"} min={f ? 100 : 40} max={f ? 266 : 130} warn={f ? 234 : 112} bad={f ? 244 : 118} />
            <Gauge label="Speed" value={speed} unit={speedUnit || "km/h"} min={0} max={f ? 160 : 260} />
            <Gauge label="Battery" value={volt} unit="V" min={10} max={15.5} warn={15.0} bad={15.3} />
          </section>
          <section className="tiles">
            {Object.entries(values).map(([id, x]) => {
              const [val, unit] = x.value === null ? [null, x.unit] : convert(x.value, x.unit, prefs.imperial);
              const hist = (history.current[id] ?? []).map(h => convert(h, x.unit, prefs.imperial)[0]);
              return (
                <div key={id} className="tile">
                  <div className="muted small">{x.name}</div>
                  <div className="value"><span className="mono">{fmt(val, unit)}</span> <span className="muted small">{unit}</span></div>
                  <Sparkline data={hist} color="var(--accent)" />
                </div>
              );
            })}
            {Object.keys(values).length === 0 && <p className="muted">No values yet. Is the ignition on and the cable plugged in?</p>}
          </section>
        </>
      )}

      {tab === "faults" && <FaultsTab />}

      {tab === "settings" && (
        <section className="panel">
          <h2>Settings</h2>
          <label className="row"><input type="checkbox" checked={prefs.imperial} onChange={e => setPrefs({ ...prefs, imperial: e.target.checked })} /> °F, mph and psi</label>
          <div className="row">
            Theme:
            {(["auto", "dark", "light"] as const).map(t => (
              <button key={t} className={prefs.theme === t ? "on" : ""} onClick={() => setPrefs({ ...prefs, theme: t })}>{t}</button>
            ))}
          </div>
          <p className="muted small">
            Data: <a href="/live.json">/live.json</a> · <a href="/health">/health</a> · <a href="/classic">simple page</a>.
            Reads {health?.reads ?? 0}, errors {health?.errors ?? 0}. {info.version ?? ""}
          </p>
          <p className="muted small">Tip: on a phone, "Add to Home Screen" makes this a full-screen app.</p>
        </section>
      )}
    </>
  );
}

const CSS = `
.row{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.between{justify-content:space-between}
header{margin-bottom:12px}.brand{font-weight:700;font-size:1.25rem;letter-spacing:.02em}
.muted{color:var(--muted)}.small{font-size:.82rem}.mono{font-variant-numeric:tabular-nums;font-feature-settings:"tnum"}
.pill{display:inline-flex;align-items:center;gap:8px;padding:6px 12px;border-radius:999px;background:var(--panel);border:1px solid var(--line);font-size:.85rem;max-width:100%}
.pill .dot{width:9px;height:9px;border-radius:50%;background:var(--muted);flex:none}
.pill.ok .dot{background:var(--ok)}.pill.warn .dot{background:var(--warn)}.pill.bad .dot{background:var(--bad)}
.tabs{display:flex;gap:6px;margin:6px 0 14px;overflow-x:auto}
button{font:inherit;color:var(--text);background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:8px 14px;cursor:pointer}
button.on,button[aria-selected=true]{background:var(--accent);border-color:var(--accent);color:#fff}
button:disabled{opacity:.6}
.alert{border-radius:10px;padding:10px 14px;margin:0 0 10px;border:1px solid var(--line);background:var(--panel)}
.alert.warn{border-color:var(--warn)}.alert.bad{border-color:var(--bad)}.alert.ok{border-color:var(--ok)}
.gauges{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-bottom:14px}
@media (min-width:720px){.gauges{grid-template-columns:repeat(4,minmax(0,1fr))}}
.gauge{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:8px 8px 10px;text-align:center}
.gauge svg{width:100%;max-width:180px;display:block;margin:0 auto}
.gauge .track,.gauge .fill{fill:none;stroke-width:8;stroke-linecap:round}
.gauge .track{stroke:var(--raised)}.gauge .fill{stroke:var(--accent);transition:stroke-dasharray .4s}
.gauge .fill.warn{stroke:var(--warn)}.gauge .fill.bad{stroke:var(--bad)}
.gauge .big{font-size:19px;font-weight:700;text-anchor:middle;fill:var(--text);font-variant-numeric:tabular-nums}
.gauge .unit{font-size:9px;text-anchor:middle;fill:var(--muted)}
.gauge-label{font-size:.85rem;color:var(--muted);margin-top:-6px}
.tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}
.tile{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:10px 12px;min-width:0}
.tile .value{font-size:1.5rem;font-weight:700;margin:2px 0}
.spark{width:100%;height:28px;display:block}
.panel{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:14px 16px}
.panel h2{font-size:1.05rem;margin:0}
.fault{border-left:4px solid var(--line);background:var(--raised);border-radius:8px;padding:8px 12px;margin-top:10px}
.fault.bad{border-left-color:var(--bad)}.fault.warn{border-left-color:var(--warn)}
a{color:var(--accent)}
input[type=checkbox]{width:20px;height:20px}
`;

createRoot(document.getElementById("root")!).render(<App />);
