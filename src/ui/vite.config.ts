import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The UI is served by the host from a virtual folder mapping, so every asset
// reference must be relative -- there is no server root to be absolute against.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'chrome110',
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        // Three.js is large and changes rarely; keeping it separate makes the
        // app chunk small enough to parse quickly on cold start.
        manualChunks: { three: ['three'] },
      },
    },
  },
});
