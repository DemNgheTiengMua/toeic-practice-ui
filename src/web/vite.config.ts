import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';

const apiTarget = process.env.VITE_API_TARGET || 'http://localhost:5001';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5000,
    strictPort: true,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
      '/media': {
        target: apiTarget,
        changeOrigin: true,
        rewrite: (p) => `/api${p}`,
      },
    },
  },
  preview: {
    port: 5000,
    strictPort: true,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
      '/media': {
        target: apiTarget,
        changeOrigin: true,
        rewrite: (p) => `/api${p}`,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
});
