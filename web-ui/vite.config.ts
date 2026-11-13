import { defineConfig } from 'vite-plus'

export default defineConfig({
  test: {
    globals: true,
    include: ['**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}'],
    exclude: ['**/node_modules/**', '**/dist/**'],
  },

  fmt: {
    semi: false,
    singleQuote: true,
  },

  run: {
    tasks: {
      buildAll: {
        command: 'vite build',
      },
    },
  },
})
