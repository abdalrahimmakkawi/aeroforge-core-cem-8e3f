"use client";

import { useMemo, useState, type ReactNode } from "react";
import { motion } from "framer-motion";
import { Download, Flame, Loader2, Rocket, Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Slider } from "@/components/ui/slider";
import { Viewport3D, type ViewMode } from "@/components/Viewport3D";
import { API_BASE, cn } from "@/lib/utils";

type ChatItem = { role: "user" | "agent" | "system"; text: string; ts: number };

type DesignResult = {
  ok: boolean;
  job_id: string;
  reasoning: string;
  model: string;
  thermo: Record<string, number | string>;
  preset: string;
  geometry: Record<string, number>;
  picogk_csharp: string;
  stl_url: string;
  metrics: Record<string, number>;
  compile_ms: number;
  kernel_mode: string;
};

const PRESETS = [
  { id: "aerospike", label: "Aerospike" },
  { id: "regenerative", label: "Regenerative Bell" },
  { id: "swirler", label: "Injector Swirler" },
] as const;

const FUELS = [
  { id: "lox_methane", label: "LOX / LCH4" },
  { id: "lox_rp1", label: "LOX / RP-1" },
  { id: "lox_lh2", label: "LOX / LH2" },
] as const;

export default function StudioPage() {
  const [prompt, setPrompt] = useState(
    "Design a 20 kN MethaLOX aerospike with internal spiral cooling channels"
  );
  const [thrust, setThrust] = useState(20);
  const [pc, setPc] = useState(3.5);
  const [propellant, setPropellant] = useState<(typeof FUELS)[number]["id"]>("lox_methane");
  const [preset, setPreset] = useState<(typeof PRESETS)[number]["id"]>("aerospike");
  const [viewMode, setViewMode] = useState<ViewMode>("solid");
  const [sliceZ, setSliceZ] = useState(0);
  const [busy, setBusy] = useState(false);
  const [chat, setChat] = useState<ChatItem[]>([
    {
      role: "system",
      text: "AeroForge online. NVIDIA NIM + PicoGK voxel kernel standing by.",
      ts: Date.now(),
    },
  ]);
  const [result, setResult] = useState<DesignResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const stlUrl = useMemo(() => {
    if (!result?.stl_url) return null;
    const bust = result.job_id;
    return `${API_BASE}${result.stl_url}?v=${bust}`;
  }, [result]);

  async function runDesign() {
    setBusy(true);
    setError(null);
    setChat((c) => [...c, { role: "user", text: prompt, ts: Date.now() }]);
    try {
      const res = await fetch(`${API_BASE}/api/design`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          prompt,
          thrust_kn: thrust,
          chamber_pressure_mpa: pc,
          propellant,
          preset,
          use_ai_script: false,
        }),
      });
      const raw = await res.text();
      let data: DesignResult & { detail?: unknown };
      try {
        data = JSON.parse(raw);
      } catch {
        throw new Error(raw.slice(0, 200) || res.statusText);
      }
      if (!res.ok) {
        const detail = data?.detail;
        const msg =
          typeof detail === "string"
            ? detail
            : detail && typeof detail === "object" && "message" in detail
              ? String((detail as { message: string }).message)
              : res.statusText;
        throw new Error(msg);
      }
      setResult(data);
      setChat((c) => [
        ...c,
        {
          role: "agent",
          text: `${data.reasoning}\n\nKernel ${data.kernel_mode} · ${Math.round(data.compile_ms)} ms · STL ${(data.metrics.stl_bytes / 1024).toFixed(0)} KB`,
          ts: Date.now(),
        },
      ]);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg);
      setChat((c) => [...c, { role: "system", text: `Error: ${msg}`, ts: Date.now() }]);
    } finally {
      setBusy(false);
    }
  }

  const thermo = result?.thermo;
  const metrics = result?.metrics;

  return (
    <div className="min-h-screen bg-[#070b12] text-slate-100">
      <header className="border-b border-slate-800/80 px-5 py-3 flex items-center justify-between bg-[#0b1220]/90 backdrop-blur">
        <div className="flex items-center gap-3">
          <div className="h-9 w-9 rounded-lg bg-cyan-500/15 border border-cyan-400/30 grid place-items-center shadow-[0_0_24px_rgba(34,211,238,0.25)]">
            <Rocket className="h-5 w-5 text-cyan-300" />
          </div>
          <div>
            <p className="font-[family-name:var(--font-display)] text-lg tracking-[0.18em] text-cyan-100">AEROFORGE</p>
            <p className="text-[11px] text-slate-400 font-mono">AI Computational Propulsion Workbench</p>
          </div>
        </div>
        <div className="hidden md:flex items-center gap-2 text-[11px] font-mono text-slate-400">
          <span className="px-2 py-1 rounded border border-slate-700 bg-slate-900/60">NIM</span>
          <span className="px-2 py-1 rounded border border-slate-700 bg-slate-900/60">PicoGK</span>
          <span className="px-2 py-1 rounded border border-cyan-500/30 text-cyan-300 bg-cyan-500/10">LOCAL</span>
        </div>
      </header>

      <main className="grid grid-cols-1 xl:grid-cols-2 gap-3 p-3 h-[calc(100vh-64px)]">
        {/* Pane 1 */}
        <motion.section
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          className="rounded-xl border border-slate-800 bg-[#0c1422]/90 flex flex-col min-h-0"
        >
          <PaneTitle icon={<Sparkles className="h-3.5 w-3.5" />} title="AI Command Console" />
          <div className="px-4 pb-3 flex gap-2 flex-wrap">
            {PRESETS.map((p) => (
              <button
                key={p.id}
                onClick={() => setPreset(p.id)}
                className={cn(
                  "text-[11px] font-mono px-2.5 py-1 rounded-full border transition",
                  preset === p.id
                    ? "border-cyan-400/50 bg-cyan-500/15 text-cyan-200"
                    : "border-slate-700 text-slate-400 hover:border-slate-500"
                )}
              >
                {p.label}
              </button>
            ))}
          </div>
          <div className="flex-1 overflow-y-auto px-4 space-y-2 min-h-[140px]">
            {chat.map((m, i) => (
              <div
                key={i}
                className={cn(
                  "rounded-lg px-3 py-2 text-sm leading-relaxed",
                  m.role === "user" && "bg-slate-800/70 border border-slate-700",
                  m.role === "agent" && "bg-cyan-950/40 border border-cyan-500/20 text-cyan-50 whitespace-pre-wrap",
                  m.role === "system" && "bg-slate-900/50 border border-slate-800 text-slate-400 font-mono text-xs"
                )}
              >
                {m.text}
              </div>
            ))}
          </div>
          <div className="p-4 border-t border-slate-800 space-y-2">
            <textarea
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={3}
              className="w-full rounded-lg bg-slate-950/80 border border-slate-700 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-cyan-400/40 resize-none font-mono"
            />
            <Button onClick={runDesign} disabled={busy} className="w-full">
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Flame className="h-4 w-4" />}
              {busy ? "Compiling voxels…" : "Forge Design"}
            </Button>
            {error && <p className="text-xs text-rose-400 font-mono">{error}</p>}
          </div>
        </motion.section>

        {/* Pane 2 */}
        <motion.section
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.05 }}
          className="rounded-xl border border-slate-800 bg-[#0c1422]/90 flex flex-col min-h-0"
        >
          <PaneTitle title="Interactive 3D Viewport" right={
            <div className="flex gap-1">
              {(["solid", "wireframe", "thermal"] as ViewMode[]).map((m) => (
                <button
                  key={m}
                  onClick={() => setViewMode(m)}
                  className={cn(
                    "text-[10px] uppercase tracking-wider px-2 py-1 rounded border font-mono",
                    viewMode === m ? "border-cyan-400/50 text-cyan-200 bg-cyan-500/10" : "border-slate-700 text-slate-500"
                  )}
                >
                  {m}
                </button>
              ))}
            </div>
          } />
          <div className="flex-1 min-h-[280px] px-3 pb-2">
            <Viewport3D stlUrl={stlUrl} mode={viewMode} sliceZ={sliceZ} />
          </div>
          <div className="px-4 pb-4 space-y-1">
            <div className="flex justify-between text-[11px] font-mono text-slate-400">
              <span>Section cut Z</span>
              <span>{sliceZ.toFixed(0)} mm</span>
            </div>
            <Slider value={[sliceZ]} min={-80} max={80} step={1} onValueChange={(v) => setSliceZ(v[0] ?? 0)} />
          </div>
        </motion.section>

        {/* Pane 3 */}
        <motion.section
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="rounded-xl border border-slate-800 bg-[#0c1422]/90 flex flex-col"
        >
          <PaneTitle title="Thermodynamic Controls" />
          <div className="p-4 space-y-5">
            <Control label={`Thrust  ${thrust.toFixed(0)} kN`}>
              <Slider value={[thrust]} min={5} max={100} step={1} onValueChange={(v) => setThrust(v[0] ?? 20)} />
            </Control>
            <Control label={`Chamber pressure  ${pc.toFixed(1)} MPa`}>
              <Slider value={[pc]} min={1} max={12} step={0.1} onValueChange={(v) => setPc(v[0] ?? 3.5)} />
            </Control>
            <div>
              <p className="text-[11px] font-mono text-slate-400 mb-2">Propellant</p>
              <div className="flex flex-wrap gap-2">
                {FUELS.map((f) => (
                  <button
                    key={f.id}
                    onClick={() => setPropellant(f.id)}
                    className={cn(
                      "text-xs px-3 py-1.5 rounded-md border font-mono",
                      propellant === f.id
                        ? "border-cyan-400/50 bg-cyan-500/15 text-cyan-100"
                        : "border-slate-700 text-slate-400"
                    )}
                  >
                    {f.label}
                  </button>
                ))}
              </div>
            </div>
            <div className="grid grid-cols-2 gap-2 pt-2">
              <Metric label="Isp" value={thermo ? `${Number(thermo.isp_s).toFixed(0)} s` : "—"} />
              <Metric label="ε" value={thermo ? Number(thermo.expansion_ratio).toFixed(2) : "—"} />
              <Metric label="Cf" value={thermo ? Number(thermo.cf).toFixed(3) : "—"} />
              <Metric label="c*" value={thermo ? `${Number(thermo.c_star_m_s).toFixed(0)} m/s` : "—"} />
            </div>
          </div>
        </motion.section>

        {/* Pane 4 */}
        <motion.section
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.15 }}
          className="rounded-xl border border-slate-800 bg-[#0c1422]/90 flex flex-col min-h-0"
        >
          <PaneTitle title="PicoGK Code & Metrics" />
          <div className="px-4 grid grid-cols-2 gap-2 mb-2">
            <Metric label="A_t" value={thermo ? `${(Number(thermo.throat_area_m2) * 1e4).toFixed(2)} cm²` : "—"} />
            <Metric label="Mass (Inconel)" value={metrics ? `${Number(metrics.est_mass_kg_inconel718).toFixed(2)} kg` : "—"} />
            <Metric label="Throat Ø" value={thermo ? `${(Number(thermo.throat_radius_mm) * 2).toFixed(1)} mm` : "—"} />
            <Metric label="Compile" value={result ? `${Math.round(result.compile_ms)} ms` : "—"} />
          </div>
          <div className="flex-1 min-h-0 px-4 pb-2">
            <pre className="h-full overflow-auto rounded-lg bg-slate-950/90 border border-slate-800 p-3 text-[11px] leading-relaxed text-cyan-100/90 font-mono">
              {result?.picogk_csharp || "// PicoGK C# will appear here after Forge Design"}
            </pre>
          </div>
          <div className="p-4 border-t border-slate-800">
            <Button
              variant="outline"
              className="w-full"
              disabled={!result}
              onClick={() => {
                if (!result) return;
                window.open(`${API_BASE}${result.stl_url}`, "_blank");
              }}
            >
              <Download className="h-4 w-4" />
              Download 3D-printable STL
            </Button>
          </div>
        </motion.section>
      </main>
    </div>
  );
}

function PaneTitle({
  title,
  icon,
  right,
}: {
  title: string;
  icon?: ReactNode;
  right?: ReactNode;
}) {
  return (
    <div className="flex items-center justify-between px-4 py-3 border-b border-slate-800/80">
      <div className="flex items-center gap-2 text-xs uppercase tracking-[0.16em] text-slate-300 font-mono">
        {icon}
        {title}
      </div>
      {right}
    </div>
  );
}

function Control({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="space-y-2">
      <p className="text-[11px] font-mono text-slate-400">{label}</p>
      {children}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950/50 px-3 py-2">
      <p className="text-[10px] uppercase tracking-wider text-slate-500 font-mono">{label}</p>
      <p className="text-sm text-cyan-100 font-mono mt-0.5">{value}</p>
    </div>
  );
}
