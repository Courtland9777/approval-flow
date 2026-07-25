import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': process.env.APPROVALFLOW_PROXY_TARGET ?? 'http://127.0.0.1:5080',
      '/openapi': process.env.APPROVALFLOW_PROXY_TARGET ?? 'http://127.0.0.1:5080',
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    restoreMocks: true,
    exclude: ['e2e/**', 'node_modules/**'],
  },
})
