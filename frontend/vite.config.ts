import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const backend = process.env.API_PROXY_TARGET ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // Never inline fonts as data: URIs; the Content Security Policy only allows fonts from 'self'.
    assetsInlineLimit: (file) => (/\.(woff2?|ttf|otf)$/.test(file) ? false : undefined),
  },
  server: {
    port: 5173,
    // In dev the API is reached through the Vite proxy, so the browser sees one origin (no CORS needed).
    proxy: {
      '/api': backend,
      '/hubs': { target: backend, ws: true },
    },
  },
})
