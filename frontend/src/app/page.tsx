"use client";

import dynamic from "next/dynamic";

const Studio = dynamic(() => import("@/components/Studio"), {
  ssr: false,
  loading: () => (
    <div className="flex min-h-screen items-center justify-center bg-[var(--bg)] text-[var(--muted)]">
      <p className="font-mono text-sm tracking-wide">Loading AeroForge…</p>
    </div>
  ),
});

export default function Page() {
  return <Studio />;
}
