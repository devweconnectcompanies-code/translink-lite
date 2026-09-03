import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      input: {
        popup: "index.html",
        offscreen: "offscreen.html",
        serviceWorker: "src/background/serviceWorker.ts",
        audioWorklet: "src/offscreen/audioWorklet.ts",
      },
      output: {
        entryFileNames: (chunkInfo) =>
          chunkInfo.name === "serviceWorker" || chunkInfo.name === "audioWorklet"
            ? chunkInfo.name === "serviceWorker"
              ? "service-worker.js"
              : "audio-worklet.js"
            : "assets/[name]-[hash].js",
        chunkFileNames: "assets/[name]-[hash].js",
        assetFileNames: "assets/[name]-[hash][extname]",
      },
    },
  },
});
