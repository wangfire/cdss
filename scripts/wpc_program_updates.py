# -*- coding: utf-8 -*-
"""Program.cs：CORS 可配置化 + UseForwardedHeaders + UseHSTS。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8").read()

# 1. CORS 段整体替换（旧块定位用首尾锚点）
start_marker = "// 前端管理应用跨域"
end_marker = ".AllowCredentials();\n    }));"
si = c.index(start_marker)
ei = c.index(end_marker, si) + len(end_marker)
old_block = c[si:ei]
new_block = """// 前端管理应用跨域（开发环境前端运行在 3001+，生产走反代同源则无需 CORS）。
// 生产：Security:CorsOrigins 配置允许的源列表；同源部署时留空 = 仅同源请求（无跨域）。
var corsOrigins = builder.Configuration.GetSection("Security:CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("admin", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(origin =>
                    origin.StartsWith("http://localhost:")
                    || origin.StartsWith("http://127.0.0.1:"))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        // 生产且未配置 origins：不加允许源（同源请求不受 CORS 约束）。
    }));"""
c = c[:si] + new_block + c[ei:]

# 2. app = builder.Build() 后插入 ForwardedHeaders + HSTS + DataProtection
build_anchor = "var app = builder.Build();"
inject = """var app = builder.Build();

// 反向代理转发头（WP-C）：仅信任 KnownProxies 配置的代理地址，
// 使 X-Forwarded-For / X-Forwarded-Proto 可信（登录限流按真实客户端 IP 分区）。
var forwardedOptions = new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
foreach (var proxy in builder.Configuration.GetSection("Security:ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
{
    if (System.Net.IPAddress.TryParse(proxy, out var ip))
    {
        forwardedOptions.KnownProxies.Add(ip);
    }
}
app.UseForwardedHeaders(forwardedOptions);

// DataProtection 密钥环持久化（WP-D）：容器重建不掉线。
// 生产挂载 /var/dpkeys 卷；开发/测试用默认行为。
var dpPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dpPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
        .SetApplicationName("HospitalAi");
}
"""
assert build_anchor in c, "build anchor"
c = c.replace(build_anchor, inject, 1)

# 3. HSTS：Production 且 UseCors 之前
cors_use = 'app.UseCors("admin");'
hsts = '''// HSTS（WP-C）：生产强制 HTTPS（反向代理终结 TLS，应用侧只发头）。
if (app.Environment.IsProduction())
{
    app.UseHsts();
}

app.UseCors("admin");'''
assert cors_use in c, "usecors anchor"
c = c.replace(cors_use, hsts, 1)

open(p, "w", encoding="utf-8-sig").write(c)
print("program.cs updated: cors + forwarded headers + dataprotection + hsts")
