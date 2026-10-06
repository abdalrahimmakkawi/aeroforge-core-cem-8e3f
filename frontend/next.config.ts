/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  transpilePackages: ["three"],
  allowedDevOrigins: ["127.0.0.1", "localhost"],
  async rewrites() {
    const api = process.env.AEROFORGE_API_ORIGIN || "http://127.0.0.1:5000";
    // Only proxy static STL outputs; /api/* is handled by App Router route handlers
    // so long-running /api/design is not killed by the rewrite proxy timeout.
    return [{ source: "/outputs/:path*", destination: `${api}/outputs/:path*` }];
  },
};

export default nextConfig;
