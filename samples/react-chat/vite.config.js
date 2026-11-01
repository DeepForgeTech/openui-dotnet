import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  resolve: {
    dedupe: ["ai", "react", "react-dom", "zod", "zustand"],
  },
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      // The ASP.NET Core API; OPENUI_API_PORT moves it, the same variable the API reads.
      "/api": `http://localhost:${process.env.OPENUI_API_PORT ?? 5280}`,
    },
  },
});
