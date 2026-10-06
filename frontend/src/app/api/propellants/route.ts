import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

const API = process.env.AEROFORGE_API_ORIGIN || "http://127.0.0.1:5000";

export async function GET() {
  const res = await fetch(`${API}/api/propellants`, { cache: "no-store" });
  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}
