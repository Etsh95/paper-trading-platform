import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const backend = process.env.API_PROXY_TARGET ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  build: {
    // Never inline fonts as data: URIs; the Content Security Policy only allows fonts from 'self'.
    assetsInlineLimit: (file) => (/\.(woff2?|ttf|otf)$/.test(file) ? false : undefined),
    rolldownOptions: {
      output: {
        // Libraries change far less often than app code: separate, long-cacheable chunks mean a deploy only
        // invalidates the (small) app chunk. Higher priority wins when a module matches several groups.
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|scheduler|react-router)[\\/]/, priority: 30 },
            { name: 'motion', test: /node_modules[\\/](motion|framer-motion|motion-dom|motion-utils)[\\/]/, priority: 20 },
            { name: 'realtime', test: /node_modules[\\/]@microsoft[\\/]signalr[\\/]/, priority: 20 },
            { name: 'vendor', test: /node_modules[\\/]/, priority: 10 },
          ],
        },
      },
    },
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
