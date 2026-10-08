import { NextRequest, NextResponse } from "next/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const API_ORIGIN = process.env.AEROFORGE_API_ORIGIN || "http://127.0.0.1:5000";

export async function GET(req: NextRequest) {
  const url = new URL(req.url);
  const upstream = `${API_ORIGIN}/api/thermo?${url.searchParams.toString()}`;
  try {
    const res = await fetch(upstream, { cache: "no-store" });
    const body = await res.text();
    return new NextResponse(body, {
      status: res.status,
      headers: { "Content-Type": res.headers.get("content-type") || "application/json" },
    });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return NextResponse.json({ ok: false, detail: message }, { status: 502 });
  }
}
