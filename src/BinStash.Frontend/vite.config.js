import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import mkcert from 'vite-plugin-mkcert'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig({
  define: {
    'process.env': process.env
  },
  plugins: [vue(), mkcert()],
  build: {
    outDir: '../BinStash.Server/wwwroot',
    commonjsOptions: {
      transformMixedEsModules: true,
    }
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    allowedHosts: ['localhost', '100.104.156.72'],
    port: 8080,
    proxy: {
      '/api': {
        target: 'https://localhost:7117',
        changeOrigin: true,
        secure: false
      },
      '/health': {
        target: 'https://localhost:7117',
        changeOrigin: true,
        secure: false
      },
      '/graphql': {
        target: 'https://localhost:7117',
        changeOrigin: true,
        secure: false,
        ws: true
      },
    }
  }
})
