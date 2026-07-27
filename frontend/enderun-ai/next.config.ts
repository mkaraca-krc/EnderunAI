import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  experimental: {
    proxyClientMaxBodySize: "60mb",
  },
  async rewrites() {
    return {
      beforeFiles: [],
      afterFiles: [
        {
          source: "/api/:path*",
          destination: "/api/backend/:path*",
        },
      ],
      fallback: [],
    };
  },
};

export default nextConfig;
