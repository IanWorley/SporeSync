import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

const BACKEND_URL = "http://localhost:8080";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: { "/api": BACKEND_URL },
  },
  test: {
    environment: "jsdom",
  },
});
