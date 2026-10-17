import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite-plus";

const BACKEND_URL = "http://localhost:8080";

export default defineConfig({
  fmt: {
    ignorePatterns: ["dist/**"],
  },
  lint: {
    plugins: ["oxc", "typescript", "unicorn", "react"],
    // Close to typescript-eslint's strictTypeChecked: pedantic carries the no-unsafe-* rules.
    categories: {
      correctness: "error",
      suspicious: "error",
      pedantic: "error",
    },
    rules: {
      "typescript/no-explicit-any": "error",
      "typescript/no-non-null-assertion": "error",
      // The react-jsx transform needs no React import.
      "react/react-in-jsx-scope": "off",
      // Library types such as RouterHistory are mutable, so this rule cannot be satisfied.
      "typescript/prefer-readonly-parameter-types": "off",
    },
    options: { typeAware: true, typeCheck: true },
    env: { browser: true },
    ignorePatterns: ["dist/**"],
  },
  plugins: [react(), tailwindcss()],
  server: {
    proxy: { "/api": BACKEND_URL },
  },
  test: {
    environment: "jsdom",
  },
});
