import { NextRequest, NextResponse } from "next/server";
import http from "node:http";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const maxDuration = 600;

const API_HOST = process.env.AEROFORGE_API_HOST || "127.0.0.1";
const API_PORT = Number(process.env.AEROFORGE_API_PORT || 5000);

function postDesign(body: string): Promise<{ status: number; body: string; contentType: string }> {
  return new Promise((resolve, reject) => {
    const req = http.request(
      {
        host: API_HOST,
        port: API_PORT,
        path: "/api/design",
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Content-Length": Buffer.byteLength(body),
        },
        timeout: 600_000,
      },
      (res) => {
        const chunks: Buffer[] = [];
        res.on("data", (c) => chunks.push(c));
        res.on("end", () => {
          resolve({
            status: res.statusCode || 502,
            body: Buffer.concat(chunks).toString("utf8"),
            contentType: res.headers["content-type"] || "application/json",
          });
        });
      }
    );
    req.on("timeout", () => {
      req.destroy(new Error("Upstream design timed out"));
    });
    req.on("error", reject);
    req.write(body);
    req.end();
  });
}

export async function POST(req: NextRequest) {
  try {
    const body = await req.text();
    const upstream = await postDesign(body);
    return new NextResponse(upstream.body, {
      status: upstream.status,
      headers: { "Content-Type": upstream.contentType },
    });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return NextResponse.json(
      { detail: { message: `Upstream design failed: ${message}` } },
      { status: 502 }
    );
  }
}
