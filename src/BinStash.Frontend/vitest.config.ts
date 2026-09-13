import { fileURLToPath, URL } from 'node:url'

import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

import { sfcFs } from './vite.config.ts'

/**
 * Separate from `vite.config.ts` on purpose: the dev config pulls in Tailwind and mkcert,
 * neither of which a component test needs, and mkcert would have CI generating a local
 * certificate authority just to run unit tests.
 */
export default defineConfig({
  plugins: [vue({ script: { fs: sfcFs } })],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
    // JUnit XML so Jenkins can publish frontend results alongside the .NET ones. Kept out of a
    // `TestResults/` folder on purpose: the .NET stage's xUnit publisher globs `**/TestResults/*.xml`
    // case-insensitively on Windows and would try to parse this file with the wrong parser.
    reporters: ['default', 'junit'],
    outputFile: { junit: './junit.xml' },
    coverage: {
      provider: 'v8',
      // cobertura is what the Jenkins coverage publisher reads; text is for the console log.
      reporter: ['text', 'html', 'cobertura'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,vue}'],
      exclude: ['src/graphql/generated/**', 'src/components/ui/**', 'src/**/*.spec.ts'],
    },
  },
})
