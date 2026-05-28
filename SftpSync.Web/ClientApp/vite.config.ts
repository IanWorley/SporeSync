import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const backendUrl = process.env.ASPNETCORE_HTTPS_PORT
  ? `https://localhost:${process.env.ASPNETCORE_HTTPS_PORT}`
  : process.env.ASPNETCORE_URLS?.split(";")[0] ?? "http://localhost:5000";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: backendUrl,
        changeOrigin: true,
        secure: false
      },
      "/hubs": {
        target: backendUrl,
        changeOrigin: true,
        secure: false,
        ws: true
      },
      "/openapi": {
        target: backendUrl,
        changeOrigin: true,
        secure: false
      },
      "/scalar": {
        target: backendUrl,
        changeOrigin: true,
        secure: false
      }
    }
  },
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true
  }
});
