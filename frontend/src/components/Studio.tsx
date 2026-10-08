"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  Box,
  Download,
  Loader2,
  Orbit,
  PenLine,
  Rocket,
  SlidersHorizontal,
  Terminal,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Slider } from "@/components/ui/slider";
import { Viewport3D, type ViewMode } from "@/components/Viewport3D";
import { API_BASE, cn } from "@/lib/utils";
import { sizeEngine, type PropellantId, type SizedThermo } from "@/lib/thermo";

/** Browser/proxy often dies around 60s; Core forges should finish well under this. */
const DESIGN_TIMEOUT_MS = 120_000;

type ChatItem = { role: "user" | "agent" | "system"; text: string };

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
  stl_preview_url?: string;
  metrics: Record<string, number>;
  compile_ms: number;
  kernel_mode: string;
};

const PRESETS = [
  {
    id: "aerospike",
    label: "Aerospike",
    prompt: "Design a truncated aerospike thruster with conformal spiral cooling passages.",
  },
  {
    id: "regenerative",
    label: "Regenerative bell",
    prompt:
      "Design a regeneratively cooled Rao bell rocket nozzle with gyroid cooling jacket and propellant manifolds.",
  },
  {
    id: "swirler",
    label: "Injector swirler",
    prompt: "Design a coaxial swirl injector faceplate with LOX/fuel plenums and film-cooling orifices.",
  },
] as const;

const FUELS = [
  { id: "lox_methane", label: "LOX / LCH₄" },
  { id: "lox_rp1", label: "LOX / RP-1" },
  { id: "lox_lh2", label: "LOX / LH₂" },
] as const;

function fmt(n: number | string | undefined, digits = 2): string {
  if (n === undefined || n === null || n === "") return "—";
  const v = typeof n === "number" ? n : Number(n);
  if (!Number.isFinite(v)) return "—";
  return v.toLocaleString("en-US", {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
}

export default function Studio() {
  const [prompt, setPrompt] = useState(PRESETS[1].prompt);
  const [thrust, setThrust] = useState(50);
  const [pc, setPc] = useState(6.0);
  const [propellant, setPropellant] = useState<(typeof FUELS)[number]["id"]>("lox_methane");
  const [preset, setPreset] = useState<(typeof PRESETS)[number]["id"]>("regenerative");
  const [viewMode, setViewMode] = useState<ViewMode>("solid");
  const [sliceZ, setSliceZ] = useState(80);
  const [busy, setBusy] = useState(false);
  const [busyElapsed, setBusyElapsed] = useState(0);
  const [chat, setChat] = useState<ChatItem[]>([
    {
      role: "system",
      text: "Workbench ready. Set thrust / Pc / propellant / family, then Forge — Algorithm X sizes any engine class from those inputs (not a fixed 50 kN design).",
    },
  ]);
  const [result, setResult] = useState<DesignResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [meshStale, setMeshStale] = useState(false);
  const logRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Viewport loads the lightweight preview; Download STL uses the full mesh.
  const stlUrl = useMemo(() => {
    if (!result) return null;
    const path = result.stl_preview_url || result.stl_url;
    if (!path) return null;
    return `${API_BASE}${path}?v=${result.job_id}`;
  }, [result]);

  // Instant client-side sizing — bars always track sliders (API sync is optional).
  const liveThermo = useMemo(
    () =>
      sizeEngine({
        thrustKn: thrust,
        chamberPressureMpa: pc,
        propellant: propellant as PropellantId,
      }),
    [thrust, pc, propellant]
  );
  // Live slider thermo always wins; forged thermo is only a snapshot of the last mesh.
  const thermo: SizedThermo | Record<string, number | string> = liveThermo;
  const metrics = result?.metrics;

  useEffect(() => {
    if (!busy) {
      setBusyElapsed(0);
      return;
    }
    const t0 = Date.now();
    const id = window.setInterval(() => setBusyElapsed(Math.round((Date.now() - t0) / 1000)), 250);
    return () => window.clearInterval(id);
  }, [busy]);

  // Mark prior forge as stale when thermo inputs change so users re-forge for a new mesh.
  useEffect(() => {
    if (result) setMeshStale(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only when slider inputs change
  }, [thrust, pc, propellant, preset]);

  // Optional API sync (keeps backend / client formulas aligned when server is up).
  useEffect(() => {
    const ctrl = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const qs = new URLSearchParams({
          thrust_kn: String(thrust),
          chamber_pressure_mpa: String(pc),
          propellant,
        });
        await fetch(`${API_BASE}/api/thermo?${qs}`, { signal: ctrl.signal });
      } catch {
        /* client-side thermo already drives the UI */
      }
    }, 200);
    return () => {
      ctrl.abort();
      window.clearTimeout(timer);
    };
  }, [thrust, pc, propellant]);

  function selectPreset(id: (typeof PRESETS)[number]["id"]) {
    setPreset(id);
    const p = PRESETS.find((x) => x.id === id);
    if (p) setPrompt(p.prompt);
  }

  async function runDesign() {
    if (busy) return;
    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;
    const timer = window.setTimeout(() => ac.abort(), DESIGN_TIMEOUT_MS);

    setBusy(true);
    setError(null);
    setResult(null);
    setChat((c) => [...c, { role: "user", text: prompt }]);
    requestAnimationFrame(() => {
      logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: "smooth" });
    });
    try {
      const res = await fetch(`${API_BASE}/api/design`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        signal: ac.signal,
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
        throw new Error(raw.slice(0, 220) || res.statusText || "Invalid JSON from design API");
      }
      if (!res.ok) {
        const detail = data?.detail;
        const msg =
          typeof detail === "string"
            ? detail
            : detail && typeof detail === "object" && "message" in detail
              ? String((detail as { message: string }).message)
              : res.statusText;
        throw new Error(msg || `Design failed (${res.status})`);
      }
      setResult(data);
      setMeshStale(false);
      const kb = (data.metrics?.stl_bytes ?? 0) / 1024;
      const pkb = (data.metrics?.stl_preview_bytes ?? 0) / 1024;
      const family = data.preset || preset;
      setChat((c) => [
        ...c,
        {
          role: "agent",
          text: `${data.reasoning}\n\nFamily ${family} · kernel ${data.kernel_mode} (${data.model}) · ${Math.round(data.compile_ms)} ms · full ${kb.toFixed(0)} KB · viewport ${pkb > 0 ? pkb.toFixed(0) + " KB" : "n/a"}`,
        },
      ]);
      requestAnimationFrame(() => {
        logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: "smooth" });
      });
    } catch (e) {
      let msg = e instanceof Error ? e.message : String(e);
      if (e instanceof DOMException && e.name === "AbortError") {
        msg = `Design timed out after ${DESIGN_TIMEOUT_MS / 1000}s. Try again — Core forges usually finish in a few seconds.`;
      } else if (/failed to fetch|networkerror|load failed/i.test(msg)) {
        msg =
          "Failed to reach the design API (network/proxy). The previous hang was usually a 60s NIM wait — Core now skips that. Retry Forge design.";
      }
      setError(msg);
      setChat((c) => [...c, { role: "system", text: `Failed: ${msg}` }]);
    } finally {
      window.clearTimeout(timer);
      setBusy(false);
    }
  }

  function downloadStl() {
    if (!result) return;
    const a = document.createElement("a");
    a.href = `${API_BASE}${result.stl_url}`;
    a.download = `aeroforge_${result.job_id}.stl`;
    a.rel = "noopener";
    document.body.appendChild(a);
    a.click();
    a.remove();
  }

  return (
    <div className="min-h-screen text-[var(--ink)]">
      <header className="border-b border-[var(--line)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-[1680px] items-end justify-between gap-6 px-6 py-6 md:px-10">
          <div className="flex items-end gap-4">
            <div className="grid h-11 w-11 place-items-center rounded-lg border border-[var(--line)] bg-[var(--bg-deep)] text-[var(--accent)]">
              <Rocket className="h-5 w-5" strokeWidth={1.5} />
            </div>
            <div>
              <p className="font-display text-[2.15rem] leading-none tracking-[-0.03em]">AeroForge</p>
              <p className="mt-2 text-sm text-[var(--muted)]">Computational propulsion workbench</p>
            </div>
          </div>
          <div className="hidden items-center gap-2 pb-1 text-xs text-[var(--muted)] sm:flex">
            <span className="rounded-md border border-[var(--line)] px-2.5 py-1">NIM</span>
            <span className="rounded-md border border-[var(--line)] px-2.5 py-1">PicoGK</span>
            <span className="rounded-md border border-[var(--accent)]/25 bg-[var(--accent-soft)] px-2.5 py-1 text-[var(--accent)]">
              Local
            </span>
          </div>
        </div>
      </header>

      <main className="mx-auto flex max-w-[1680px] flex-col gap-5 p-5 md:p-8 xl:h-[calc(100vh-112px)] xl:min-h-[720px] xl:flex-row">
        {/* Left rail */}
        <aside className="flex w-full shrink-0 flex-col gap-5 xl:w-[400px] xl:overflow-y-auto xl:pr-1">
          <section className="flex flex-col rounded-xl border border-[var(--line)] bg-[var(--surface)]">
            <PaneHead icon={<Terminal className="h-4 w-4" />} title="Command console" />
            <div className="flex flex-wrap gap-2 px-5 py-4">
              {PRESETS.map((p) => (
                <SegButton key={p.id} active={preset === p.id} onClick={() => selectPreset(p.id)}>
                  {p.label}
                </SegButton>
              ))}
            </div>
            <div ref={logRef} className="max-h-52 space-y-3 overflow-y-auto px-5 pb-2 xl:max-h-64">
              {chat.map((m, i) => (
                <div
                  key={i}
                  className={cn(
                    "rounded-lg px-3.5 py-3 text-[13px] leading-6",
                    m.role === "user" && "border border-[var(--line)] bg-[var(--surface-2)]",
                    m.role === "agent" &&
                      "max-h-40 overflow-y-auto border border-[var(--accent)]/15 bg-[var(--accent-soft)]/50 text-[var(--ink-soft)] whitespace-pre-wrap",
                    m.role === "system" && "text-[12px] text-[var(--muted)]"
                  )}
                >
                  {m.text}
                </div>
              ))}
            </div>
            <div className="space-y-3 border-t border-[var(--line)] p-5">
              <label className="block">
                <span className="mb-2 flex items-center gap-1.5 text-xs font-medium text-[var(--muted)]">
                  <PenLine className="h-3.5 w-3.5" />
                  Design brief
                </span>
                <textarea
                  value={prompt}
                  onChange={(e) => setPrompt(e.target.value)}
                  rows={3}
                  className="w-full resize-none rounded-lg border border-[var(--line)] bg-[var(--surface-2)] px-3.5 py-2.5 text-sm leading-6 outline-none focus:border-[var(--accent)]/35 focus:ring-2 focus:ring-[var(--accent)]/12"
                />
              </label>
              <Button onClick={runDesign} disabled={busy || !prompt.trim()} className="h-11 w-full">
                {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Box className="h-4 w-4" />}
                {busy ? `Compiling voxels… ${busyElapsed}s` : "Forge design"}
              </Button>
              {error && <p className="text-xs text-[var(--danger)]">{error}</p>}
            </div>
          </section>

          <section className="rounded-xl border border-[var(--line)] bg-[var(--surface)]">
            <PaneHead icon={<SlidersHorizontal className="h-4 w-4" />} title="Thermodynamics" />
            <div className="space-y-5 p-5">
              <Field label="Thrust" value={`${thrust.toFixed(0)} kN`}>
                <Slider value={[thrust]} min={5} max={100} step={1} onValueChange={(v) => setThrust(v[0] ?? 20)} />
              </Field>
              <Field label="Chamber pressure" value={`${pc.toFixed(1)} MPa`}>
                <Slider value={[pc]} min={1} max={12} step={0.1} onValueChange={(v) => setPc(v[0] ?? 3.5)} />
              </Field>
              <div>
                <p className="mb-2.5 text-xs font-medium text-[var(--muted)]">Propellant</p>
                <div className="flex flex-wrap gap-2">
                  {FUELS.map((f) => (
                    <SegButton key={f.id} active={propellant === f.id} onClick={() => setPropellant(f.id)}>
                      {f.label}
                    </SegButton>
                  ))}
                </div>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <Stat label="Isp" value={thermo ? `${fmt(thermo.isp_s, 0)} s` : "—"} />
                <Stat label="ε" value={thermo ? fmt(thermo.expansion_ratio, 2) : "—"} />
                <Stat label="Cf" value={thermo ? fmt(thermo.cf, 3) : "—"} />
                <Stat label="c*" value={thermo ? `${fmt(thermo.c_star_m_s, 0)} m/s` : "—"} />
                <Stat
                  label="Throat Ø"
                  value={
                    thermo?.throat_radius_mm != null
                      ? `${fmt(Number(thermo.throat_radius_mm) * 2, 1)} mm`
                      : "—"
                  }
                />
                <Stat
                  label="Length"
                  value={thermo?.length_mm != null ? `${fmt(thermo.length_mm, 0)} mm` : "—"}
                />
              </div>
              <p className="text-[11px] leading-5 text-[var(--muted)]">
                Isp / ε / throat / length update as you drag. Click{" "}
                <span className="font-medium text-[var(--ink-soft)]">Forge design</span> to compile a
                new mesh for this thrust · Pc · propellant ·{" "}
                {PRESETS.find((p) => p.id === preset)?.label ?? preset}.
                {meshStale && result ? (
                  <span className="mt-1 block text-[var(--accent)]">
                    Inputs changed — viewport mesh is from the previous forge.
                  </span>
                ) : null}
              </p>
            </div>
          </section>
        </aside>

        {/* Main stage */}
        <div className="flex min-h-0 min-w-0 flex-1 flex-col gap-5">
          <section className="flex min-h-[360px] flex-1 flex-col overflow-hidden rounded-xl border border-[var(--line)] bg-[var(--surface)]">
            <PaneHead
              icon={<Orbit className="h-4 w-4" />}
              title="Viewport"
              right={
                <div className="flex gap-1.5">
                  {(["solid", "wireframe", "thermal"] as ViewMode[]).map((m) => (
                    <SegButton key={m} active={viewMode === m} onClick={() => setViewMode(m)} compact>
                      {m}
                    </SegButton>
                  ))}
                </div>
              }
            />
            <div className="flex min-h-0 flex-1 flex-col gap-4 p-5">
              <div className="min-h-[280px] flex-1">
                <Viewport3D stlUrl={stlUrl} mode={viewMode} sliceZ={sliceZ} />
              </div>
              <div className="space-y-2">
                <div className="flex justify-between text-xs text-[var(--muted)]">
                  <span>Section cut · Z (ends = off)</span>
                  <span className="font-mono tabular-nums">
                    {Math.abs(sliceZ) >= 78 ? "off" : `${sliceZ.toFixed(0)} mm`}
                  </span>
                </div>
                <Slider value={[sliceZ]} min={-80} max={80} step={1} onValueChange={(v) => setSliceZ(v[0] ?? 80)} />
              </div>
            </div>
          </section>

          <section className="shrink-0 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
            <PaneHead icon={<Box className="h-4 w-4" />} title="Geometry & export" />
            <div className="grid gap-5 p-5 lg:grid-cols-[1fr_220px]">
              <div className="min-w-0 space-y-4">
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <Stat label="At" value={thermo ? `${fmt(Number(thermo.throat_area_m2) * 1e4, 2)} cm²` : "—"} />
                  <Stat label="Mass" value={metrics ? `${fmt(metrics.est_mass_kg_inconel718, 2)} kg` : "—"} />
                  <Stat
                    label="Throat Ø"
                    value={thermo ? `${fmt(Number(thermo.throat_radius_mm) * 2, 1)} mm` : "—"}
                  />
                  <Stat label="Compile" value={result ? `${Math.round(result.compile_ms)} ms` : "—"} />
                </div>
                <pre className="max-h-36 overflow-auto rounded-lg border border-[var(--line)] bg-[var(--surface-2)] p-4 font-mono text-[12px] leading-6 text-[var(--ink-soft)]">
                  {result?.picogk_csharp || "// PicoGK C# appears after Forge design"}
                </pre>
              </div>
              <div className="flex flex-col justify-end">
                <Button
                  variant="outline"
                  className="h-11 w-full"
                  disabled={!result}
                  onClick={downloadStl}
                >
                  <Download className="h-4 w-4" />
                  Download STL
                </Button>
              </div>
            </div>
          </section>
        </div>
      </main>
    </div>
  );
}

function PaneHead({
  title,
  icon,
  right,
}: {
  title: string;
  icon?: ReactNode;
  right?: ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-[var(--line)] px-5 py-3.5">
      <div className="flex items-center gap-2 text-sm font-medium tracking-tight text-[var(--ink)]">
        <span className="text-[var(--accent)]">{icon}</span>
        {title}
      </div>
      {right}
    </div>
  );
}

function Field({
  label,
  value,
  children,
}: {
  label: string;
  value: string;
  children: ReactNode;
}) {
  return (
    <div className="space-y-2.5">
      <div className="flex items-baseline justify-between gap-3">
        <p className="text-xs font-medium text-[var(--muted)]">{label}</p>
        <p className="font-mono text-sm tabular-nums text-[var(--ink)]">{value}</p>
      </div>
      {children}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-[var(--line)] bg-[var(--surface-2)] px-3.5 py-3">
      <p className="text-[11px] font-medium text-[var(--muted)]">{label}</p>
      <p className="mt-1.5 font-mono text-base tabular-nums tracking-tight text-[var(--ink)]">{value}</p>
    </div>
  );
}

function SegButton({
  active,
  onClick,
  children,
  compact,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
  compact?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "rounded-md border transition-colors",
        compact ? "px-2.5 py-1 text-[11px] capitalize" : "px-3 py-1.5 text-xs",
        active
          ? "border-[var(--accent)]/35 bg-[var(--accent-soft)] text-[var(--accent)]"
          : "border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:border-[var(--line-strong)] hover:text-[var(--ink-soft)]"
      )}
    >
      {children}
    </button>
  );
}
