"use client";

import { useMemo, useState, type ReactNode } from "react";
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
  metrics: Record<string, number>;
  compile_ms: number;
  kernel_mode: string;
};

const PRESETS = [
  { id: "aerospike", label: "Aerospike" },
  { id: "regenerative", label: "Regenerative bell" },
  { id: "swirler", label: "Injector swirler" },
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
      text: "Workbench ready. NVIDIA NIM orchestrates geometry; PicoGK compiles voxels to STL.",
    },
  ]);
  const [result, setResult] = useState<DesignResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const stlUrl = useMemo(() => {
    if (!result?.stl_url) return null;
    return `${API_BASE}${result.stl_url}?v=${result.job_id}`;
  }, [result]);

  async function runDesign() {
    setBusy(true);
    setError(null);
    setChat((c) => [...c, { role: "user", text: prompt }]);
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
        throw new Error(raw.slice(0, 220) || res.statusText);
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
          text: `${data.reasoning}\n\nKernel ${data.kernel_mode} · ${Math.round(data.compile_ms)} ms · ${(data.metrics.stl_bytes / 1024).toFixed(0)} KB STL`,
        },
      ]);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg);
      setChat((c) => [...c, { role: "system", text: `Failed: ${msg}` }]);
    } finally {
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

  const thermo = result?.thermo;
  const metrics = result?.metrics;

  return (
    <div className="min-h-screen text-[var(--ink)]">
      <header className="border-b border-[var(--line)] bg-[var(--surface)]/90 backdrop-blur-sm">
        <div className="mx-auto flex max-w-[1600px] items-center justify-between gap-4 px-5 py-4 md:px-8">
          <div className="flex items-center gap-3 min-w-0">
            <div className="grid h-10 w-10 shrink-0 place-items-center rounded-md border border-[var(--line)] bg-[var(--surface-2)] text-[var(--accent)]">
              <Rocket className="h-5 w-5" strokeWidth={1.5} />
            </div>
            <div className="min-w-0">
              <h1 className="font-display text-2xl leading-none tracking-tight text-[var(--ink)] md:text-[1.75rem]">
                AeroForge
              </h1>
              <p className="mt-1 truncate text-xs text-[var(--muted)]">
                Computational propulsion workbench
              </p>
            </div>
          </div>
          <div className="hidden items-center gap-2 text-[11px] font-mono text-[var(--muted)] sm:flex">
            <span className="rounded border border-[var(--line)] bg-[var(--surface-2)] px-2 py-1">NIM</span>
            <span className="rounded border border-[var(--line)] bg-[var(--surface-2)] px-2 py-1">PicoGK</span>
            <span className="rounded border border-[var(--accent)]/30 bg-[var(--accent-soft)] px-2 py-1 text-[var(--accent)]">
              Local
            </span>
          </div>
        </div>
      </header>

      <main className="mx-auto grid max-w-[1600px] gap-4 p-4 md:p-6 xl:grid-cols-2 xl:grid-rows-2 xl:h-[calc(100vh-88px)] xl:min-h-[720px]">
        {/* Pane 1 — Console */}
        <section className="flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--line)] bg-[var(--surface)]">
          <PaneHead icon={<Terminal className="h-3.5 w-3.5" />} title="Command console" />
          <div className="flex flex-wrap gap-2 border-b border-[var(--line)] px-4 py-3">
            {PRESETS.map((p) => (
              <SegButton key={p.id} active={preset === p.id} onClick={() => setPreset(p.id)}>
                {p.label}
              </SegButton>
            ))}
          </div>
          <div className="min-h-[120px] flex-1 space-y-2 overflow-y-auto px-4 py-3">
            {chat.map((m, i) => (
              <div
                key={i}
                className={cn(
                  "rounded-md px-3 py-2 text-sm leading-relaxed",
                  m.role === "user" && "border border-[var(--line)] bg-[var(--surface-2)]",
                  m.role === "agent" &&
                    "border border-[var(--accent)]/20 bg-[var(--accent-soft)]/60 text-[var(--ink-soft)] whitespace-pre-wrap",
                  m.role === "system" && "font-mono text-xs text-[var(--muted)]"
                )}
              >
                {m.text}
              </div>
            ))}
          </div>
          <div className="space-y-3 border-t border-[var(--line)] p-4">
            <label className="block">
              <span className="mb-1.5 flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-[0.12em] text-[var(--muted)]">
                <PenLine className="h-3 w-3" />
                Design brief
              </span>
              <textarea
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                rows={3}
                className="w-full resize-none rounded-md border border-[var(--line)] bg-[var(--surface-2)] px-3 py-2 text-sm leading-relaxed text-[var(--ink)] outline-none focus:border-[var(--accent)]/40 focus:ring-2 focus:ring-[var(--accent)]/15"
              />
            </label>
            <Button onClick={runDesign} disabled={busy || !prompt.trim()} className="w-full">
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Box className="h-4 w-4" />}
              {busy ? "Compiling voxels…" : "Forge design"}
            </Button>
            {error && <p className="font-mono text-xs text-[var(--danger)]">{error}</p>}
          </div>
        </section>

        {/* Pane 2 — Viewport */}
        <section className="flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--line)] bg-[var(--surface)]">
          <PaneHead
            icon={<Orbit className="h-3.5 w-3.5" />}
            title="Viewport"
            right={
              <div className="flex gap-1">
                {(["solid", "wireframe", "thermal"] as ViewMode[]).map((m) => (
                  <SegButton key={m} active={viewMode === m} onClick={() => setViewMode(m)} compact>
                    {m}
                  </SegButton>
                ))}
              </div>
            }
          />
          <div className="flex min-h-0 flex-1 flex-col gap-3 p-4">
            <div className="min-h-[260px] flex-1">
              <Viewport3D stlUrl={stlUrl} mode={viewMode} sliceZ={sliceZ} />
            </div>
            <div className="space-y-1.5">
              <div className="flex justify-between text-[11px] font-mono text-[var(--muted)]">
                <span>Section cut · Z</span>
                <span>{sliceZ.toFixed(0)} mm</span>
              </div>
              <Slider
                value={[sliceZ]}
                min={-80}
                max={80}
                step={1}
                onValueChange={(v) => setSliceZ(v[0] ?? 0)}
              />
            </div>
          </div>
        </section>

        {/* Pane 3 — Thermo */}
        <section className="flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--line)] bg-[var(--surface)]">
          <PaneHead icon={<SlidersHorizontal className="h-3.5 w-3.5" />} title="Thermodynamics" />
          <div className="flex flex-1 flex-col gap-5 p-4">
            <Field label={`Thrust · ${thrust.toFixed(0)} kN`}>
              <Slider
                value={[thrust]}
                min={5}
                max={100}
                step={1}
                onValueChange={(v) => setThrust(v[0] ?? 20)}
              />
            </Field>
            <Field label={`Chamber pressure · ${pc.toFixed(1)} MPa`}>
              <Slider
                value={[pc]}
                min={1}
                max={12}
                step={0.1}
                onValueChange={(v) => setPc(v[0] ?? 3.5)}
              />
            </Field>
            <div>
              <p className="mb-2 text-[11px] font-medium uppercase tracking-[0.12em] text-[var(--muted)]">
                Propellant
              </p>
              <div className="flex flex-wrap gap-2">
                {FUELS.map((f) => (
                  <SegButton
                    key={f.id}
                    active={propellant === f.id}
                    onClick={() => setPropellant(f.id)}
                  >
                    {f.label}
                  </SegButton>
                ))}
              </div>
            </div>
            <div className="mt-auto grid grid-cols-2 gap-2">
              <Stat label="Isp" value={thermo ? `${fmt(thermo.isp_s, 0)} s` : "—"} />
              <Stat label="ε" value={thermo ? fmt(thermo.expansion_ratio, 2) : "—"} />
              <Stat label="Cf" value={thermo ? fmt(thermo.cf, 3) : "—"} />
              <Stat label="c*" value={thermo ? `${fmt(thermo.c_star_m_s, 0)} m/s` : "—"} />
            </div>
          </div>
        </section>

        {/* Pane 4 — Code & metrics */}
        <section className="flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--line)] bg-[var(--surface)]">
          <PaneHead icon={<Box className="h-3.5 w-3.5" />} title="Geometry & export" />
          <div className="grid grid-cols-2 gap-2 px-4 pt-4">
            <Stat
              label="At"
              value={thermo ? `${fmt(Number(thermo.throat_area_m2) * 1e4, 2)} cm²` : "—"}
            />
            <Stat
              label="Mass"
              value={metrics ? `${fmt(metrics.est_mass_kg_inconel718, 2)} kg` : "—"}
            />
            <Stat
              label="Throat Ø"
              value={thermo ? `${fmt(Number(thermo.throat_radius_mm) * 2, 1)} mm` : "—"}
            />
            <Stat label="Compile" value={result ? `${Math.round(result.compile_ms)} ms` : "—"} />
          </div>
          <div className="min-h-0 flex-1 px-4 py-3">
            <pre className="h-full overflow-auto rounded-md border border-[var(--line)] bg-[var(--surface-2)] p-3 font-mono text-[11px] leading-relaxed text-[var(--ink-soft)]">
              {result?.picogk_csharp || "// PicoGK C# appears after Forge design"}
            </pre>
          </div>
          <div className="border-t border-[var(--line)] p-4">
            <Button variant="outline" className="w-full" disabled={!result} onClick={downloadStl}>
              <Download className="h-4 w-4" />
              Download printable STL
            </Button>
          </div>
        </section>
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
    <div className="flex items-center justify-between gap-3 border-b border-[var(--line)] px-4 py-3">
      <div className="flex items-center gap-2 text-[11px] font-medium uppercase tracking-[0.14em] text-[var(--ink-soft)]">
        <span className="text-[var(--accent)]">{icon}</span>
        {title}
      </div>
      {right}
    </div>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="space-y-2">
      <p className="text-[11px] font-medium uppercase tracking-[0.12em] text-[var(--muted)]">{label}</p>
      {children}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-[var(--line)] bg-[var(--surface-2)] px-3 py-2.5">
      <p className="text-[10px] font-medium uppercase tracking-[0.14em] text-[var(--muted)]">{label}</p>
      <p className="mt-1 font-mono text-[15px] tabular-nums tracking-tight text-[var(--ink)]">{value}</p>
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
        "rounded-md border font-mono transition-colors",
        compact ? "px-2 py-1 text-[10px] uppercase tracking-wider" : "px-2.5 py-1.5 text-xs",
        active
          ? "border-[var(--accent)]/40 bg-[var(--accent-soft)] text-[var(--accent)]"
          : "border-[var(--line)] text-[var(--muted)] hover:border-[var(--line-strong)] hover:text-[var(--ink-soft)]"
      )}
    >
      {children}
    </button>
  );
}
