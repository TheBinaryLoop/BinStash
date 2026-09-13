import fs from 'node:fs'
import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import mkcert from 'vite-plugin-mkcert'

const API_TARGET = process.env.BINSTASH_API ?? 'https://localhost:7117'

const proxy = {
  target: API_TARGET,
  changeOrigin: true,
  // The dev instance serves a self-signed certificate.
  secure: false,
}

/**
 * Vue's SFC compiler needs filesystem access to resolve prop types imported from another
 * module — `defineProps<DialogTriggerProps>()` in the shadcn-vue/reka-ui components. It
 * normally borrows TypeScript's `sys` for that, but TypeScript 7 no longer exposes it, so
 * the compiler reports "non-Node environment". Hand it a real fs instead of downgrading.
 */
const sfcFs = {
  fileExists: (file: string) => fs.existsSync(file),
  readFile: (file: string) => (fs.existsSync(file) ? fs.readFileSync(file, 'utf-8') : undefined),
  realpath: (file: string) => fs.realpathSync(file),
}

export default defineConfig({
  plugins: [vue({ script: { fs: sfcFs } }), tailwindcss(), mkcert()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    // The server serves the SPA from wwwroot; keep this contract.
    outDir: '../BinStash.Server/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: 8080,
    proxy: {
      '/api': proxy,
      '/health': proxy,
      '/graphql': { ...proxy, ws: true },
    },
  },
})
