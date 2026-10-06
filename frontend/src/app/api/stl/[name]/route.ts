import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

const API = process.env.AEROFORGE_API_ORIGIN || "http://127.0.0.1:5000";

export async function GET(
  _req: NextRequest,
  ctx: { params: Promise<{ name: string }> }
) {
  const { name } = await ctx.params;
  const safe = name.replace(/[^a-zA-Z0-9._-]/g, "");
  const res = await fetch(`${API}/api/stl/${safe}`, { cache: "no-store" });
  if (!res.ok) {
    return NextResponse.json({ detail: "STL not found" }, { status: res.status });
  }
  const buf = await res.arrayBuffer();
  return new NextResponse(buf, {
    status: 200,
    headers: {
      "Content-Type": "model/stl",
      "Content-Disposition": `attachment; filename="${safe}"`,
    },
  });
}
