import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import ui from '@tabtab/ui/vite'
import path from 'node:path'

export default defineConfig({
  plugins: [
    vue(),
    ui(),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3001,
    host: true,
    // /api 同源代理到后端（WP-B 前端生产化）：开发环境前端 fetch('/api/...') 转发到 ASP.NET Core，
    // 生产环境由 nginx 承担同样的反代角色（deploy/nginx/cdss.conf）。
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET || 'http://localhost:5081',
        changeOrigin: true,
      },
    },
  },
})
