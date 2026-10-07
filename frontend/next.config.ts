/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  transpilePackages: ["three"],
  allowedDevOrigins: ["127.0.0.1", "localhost"],
  // STLs are written into frontend/public/outputs by the API.
  // Serve them as Next static files (no rewrite proxy) so the viewport
  // can download the preview mesh without an extra hop.
};

export default nextConfig;
