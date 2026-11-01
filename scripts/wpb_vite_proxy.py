# -*- coding: utf-8 -*-
"""vite.config.ts：加 /api 代理到后端（dev server 下同源请求转发）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\vite.config.ts"
c = open(p, encoding="utf-8").read()

old = """  server: {
    port: 3001,
    host: true,
  },
})"""
new = """  server: {
    port: 3001,
    host: true,
    // /api 同源代理到后端（WP-B 前端生产化）：开发环境前端 fetch('/api/...') 转发到 ASP.NET Core，
    // 生产环境由 nginx 承担同样的反代角色（deploy/nginx/cdss.conf）。
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET || 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})"""
assert old in c, "server anchor"
c = c.replace(old, new, 1)

open(p, "w", encoding="utf-8").write(c)
print("vite proxy added")
