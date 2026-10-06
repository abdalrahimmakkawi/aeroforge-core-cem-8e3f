/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  transpilePackages: ["three"],
  async rewrites() {
    const api = process.env.AEROFORGE_API_ORIGIN || "http://127.0.0.1:5000";
    return [
      { source: "/api/:path*", destination: `${api}/api/:path*` },
      { source: "/outputs/:path*", destination: `${api}/outputs/:path*` },
    ];
  },
};

export default nextConfig;
