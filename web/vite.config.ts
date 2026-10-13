import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const api = "http://localhost:5258";

export default defineConfig({
  plugins: [react()],
  build: { outDir: "../src/DocuMind.Api/wwwroot", emptyOutDir: true },
  server: { proxy: { "/auth": api, "/chat": api, "/agent": api, "/documents": api } },
});
