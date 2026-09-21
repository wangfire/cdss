# CDSS 智能编码系统 — 交付说明（前端框架集成 & 认证）

> 面向接手开发者（中文团队）。本文档覆盖：前端框架集成、业务模块裁剪、
> 前后端契约对齐、后端统一响应信封与最小认证端点。
> 涉及目录：`web-ui/`（前端 monorepo）、`src/HospitalAi.Api/`（后端）。

---

## 1. 前端框架集成

### 1.1 做了什么
- 将 `tabtab-ui` pnpm monorepo 复制到 `D:\CDSS智能编码系统\web-ui\`（与 `src/`、`tests/` 平级，
  **不包含 `.git`**，后续自行 `git init` 为独立仓库）。
- 保留 `apps/admin`（主管理端）；删除 `apps/playground`。
- 根 `package.json` 去掉 `dev:playground`；`internal/dev-cli/index.mjs` 的 `apps` 数组只保留 `admin`。
- `pnpm install` 通过；`pnpm --filter @tabtab/admin typecheck` 通过。

### 1.2 业务模块裁剪
前端只保留**框架外壳**（布局、登录、仪表盘首页、主题设置、错误页），删除了：
- 路由模块：`users`、`analytics`、`settings`（业务）、`collect`（采集）——保留 `dashboard`。
- 视图：`Users.vue`、`users/`、`analytics/`、`Settings.vue`、`settings/`、`Collect/`——
  保留 `Dashboard.vue`、`Login.vue`、`error/*`。
- API：`api/index.ts` 只保留 `authApi`（login/me/logout/changePassword），
  路径统一为 `/api/v1/auth/*`。
- i18n：`menu.ts` 裁剪到核心 key；`settings.ts` 重建为仅主题面板版本
  （`ThemeSettings.vue` 依赖的 key 全保留，原 collect 段删除）。

---

## 2. 前后端契约对齐

### 2.1 CORS
后端 `Program.cs` 注册 `admin` 策略，允许 `http://localhost:3001` / `http://127.0.0.1:3001`，
`UseCors("admin")` 放在 `ExceptionHandlingMiddleware` 之前。

### 2.2 响应信封统一
前端 `http.ts` 期望 `{ code, message, data }`，`code===0` 为成功。
后端原先返回裸 JSON / 字符串 code 的 `ErrorResponse`，现已统一：

- 新增 `src/HospitalAi.Api/ApiEnvelope.cs`：
  ```csharp
  Results.Ok(ApiEnvelope.Create(data))   // → { "code":0, "message":"ok", "data": ... }
  ```
- **所有 `/api/v1/*` 业务端点的成功响应**（`Results.Ok` / `Results.Created` / 原 `NoContent`）
  已改为 `ApiEnvelope.Create(...)`。
- `ExceptionHandlingMiddleware` 输出的错误体保持 `ErrorResponse(code, message, traceId, data)`，
  其中 `data` 恒为 `null`；前端按 HTTP 状态 + `message` 处理业务错误。
- **健康端点**（`/api/v1/health`、`/health/live`、`/health/ready`）保持裸 `HealthResponse`，
  不套信封（基础设施探针）。
- `/workbench` HTML 页面内嵌 JS 已改为读 `envelope.data`。

### 2.3 API 路径
前端 `api/index.ts` 全部为 `/api/v1/*`；`http.ts` `BASE_URL` 仍为 `http://localhost:5080`。

---

## 3. 后端最小认证端点（真密码 + JWT）

### 3.1 数据模型
- `app_user` 表新增列：`password_hash nvarchar(512) NOT NULL DEFAULT ''`、
  `last_login_at datetimeoffset(7) NULL`（手写迁移 `20260919000000_AppUserPasswordHash`）。
- `AppUserRecord` 增加 `PasswordHash`（非空、默认 `""`）与 `LastLoginAt`。
- `SqlServerSecurityService.CreateUserAsync` 创建用户时写入默认初始密码
  `Code + "123"` 的 PBKDF2 哈希（生产接入 SSO/目录后应停用）。

### 3.2 密码哈希
`SqlServerAuthService.HashPassword` / `VerifyPassword`（公共静态方法）：
PBKDF2-SHA256，10 万轮，16 字节盐，格式 `v1$<saltB64>$<hashB64>`。

### 3.3 JWT
- `Microsoft.IdentityModel.JsonWebTokens` / `System.IdentityModel.Tokens.Jwt` **7.7.1**
  （离线缓存可用版本；8.x 在本机 NuGet 缓存缺失）。
- HS256，声明含 `sub`(userId) / `hospitalId` / `username` / `jti`，
  默认 120 分钟有效期。
- 配置节 `JwtAuth`（`appsettings.json` / `appsettings.Development.json`）：
  `Key` / `Issuer` / `Audience` / `ClockSkewSeconds` / `TokenLifetimeMinutes`。
  通过 `ConfigJwtOptionsProvider`（实现 `IJwtOptionsProvider`）绑定。
- 密钥**必须替换**为 ≥32 字节随机串（当前为占位符）。

### 3.4 端点（`Program.cs`，均套 `ApiEnvelope`）
| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/v1/auth/login` | 公开；body `LoginRequest{userName,password,hospitalId}` → `LoginResponse` |
| GET  | `/api/v1/auth/me` | 需 Bearer；返回 `CurrentUserResponse` |
| POST | `/api/v1/auth/logout` | 无状态 JWT，仅返回 ok（客户端清 token） |
| POST | `/api/v1/auth/change-password` | 需 Bearer；`ChangePasswordRequest` |

### 3.5 认证主体解析
`RequestContextMiddleware` 现在会解析 `Authorization: Bearer <jwt>`：
- 校验签名 / 有效期（用 `JwtAuth:Key`）后，用令牌 `sub` / `hospitalId` 声明
  **覆盖** `RequestContext.UserId` / `HospitalId`，并构建已认证 `ClaimsPrincipal`。
- 校验失败（无效/过期）回退到旧的 `X-User-Id` / `X-Hospital-Id` 头认证或匿名。
- 保留头认证以兼容旧测试与 `/workbench` 页面。

### 3.6 前端配套
- `api/types.ts` `LoginRequest` 增加 `hospitalId: string`。
- `stores/user.ts` `login(userName, password, hospitalId)`，登录成功把 `hospitalId`
  存 `STORAGE_KEYS.HOSPITAL`。
- `api/http.ts` `buildHeaders` 在带鉴权时自动注入 `X-Hospital-Id`（取自本地存储）。
- `views/Login.vue` 增加“医院 ID”输入框（GUID 校验），默认账号提示改为
  `admin / admin123`。
- i18n `login.ts`（zh/en）补充 `hospitalId` / `hospitalIdPlaceholder` / `hospitalIdInvalid`，
  并修正演示账号文案。

---

## 4. 验证状态与已知限制

### 4.1 已验证
- 前端：`pnpm install`、`pnpm --filter @tabtab/admin typecheck` 均通过。
- 后端：全部改动**静态审阅**通过（见 §4.3 编译限制）。

### 4.2 未验证 / 需本地执行
- 后端编译、迁移应用、运行测试（本机 .NET SDK 损坏，见 §4.3）。
- 数据库 `password_hash` 迁移需执行：`dotnet ef database update` 或
  `Database:AutoMigrate=true` 启动。
- 登录/改密端到端：需先有一个 `app_user`（带 `password_hash`）记录。
  可用 `POST /api/v1/users` 建用户（初始密码 `Code+"123"`）再登录。

### 4.3 本机 .NET SDK 损坏（需修复）
`dotnet restore/build` 报 `MSB4276`：
`SDK "Microsoft.NET.SDK.WorkloadAutoImportPropsLocator" 解析失败，
C:\Program Files\dotnet\sdk\10.0.401\Sdks\... 目录不存在`。
这是 SDK 10.0.401 安装不完整（与本仓库代码无关）。**修复**：
```powershell
# 重新安装 .NET SDK 10
winget install Microsoft.Dotnet.SDK.10
# 或
winget upgrade Microsoft.Dotnet.SDK.10
```
修复后执行：
```powershell
dotnet build 'D:\CDSS智能编码系统\src\HospitalAi.Api\HospitalAi.Api.csproj'
dotnet test 'D:\CDSS智能编码系统\tests\HospitalAi.Api.Tests'
```

### 4.4 测试调整
- `tests/HospitalAi.Api.Tests/ApiSmokeTests.cs`：健康端点读裸 `HealthResponse`；
  新增 `Auth_Login_缺少字段` 验证登录 400 校验。
- `ApiObservabilityTests.cs`：成功响应改为经 `ReadEnvelopeDataAsync<T>` 读 `envelope.data`
  （业务端点现已套信封）。

---

## 5. 待办 / 后续（Open Items）

| # | 项 | 说明 |
|---|---|---|
| 1 | 替换 `JwtAuth:Key` | 当前为占位符，生产/演示前务必换成 ≥32 字节随机串 |
| 2 | 创建演示管理员 | 需要一个 `app_user` 才能登录；可 `POST /api/v1/users`（`Code=admin`，
     初始密码 `admin123`）+ `POST /api/v1/users/{id}/roles/{roleId}` 授权 |
| 3 | `/me`、`/change-password` 的鉴权策略 | 现依赖中间件解析 Bearer 填充 `RequestContext`；
     如需强制 401（无/无效 token 直接拒绝），给这两个端点加
     `RequireAuthorization("authenticated-user")` |
| 4 | 角色矩阵 / 权限码 | 第一阶段 `permissions = role codes`；医院权限方案确定后收敛 |
| 5 | 登录锁定 / 限流 | 未实现；防暴力破解需另加 |
| 6 | 修复 .NET SDK | 见 §4.3，否则后端无法编译/测试 |
| 7 | `web-ui` 独立成仓库 | 目前无 `.git`；建议 `git init` 并加 `.gitignore` |
| 8 | 健康端点信封策略 | 现保持裸响应；若统一套信封需同步改 `ApiSmokeTests` 健康用例 |

---

## 6. 关键文件索引

| 文件 | 说明 |
|---|---|
| `web-ui/apps/admin/src/api/{index,types,http}.ts` | 前端 API 客户端（authApi + 信封解析 + 头注入） |
| `web-ui/apps/admin/src/stores/user.ts` | 登录/用户 store（hospitalId 持久化） |
| `web-ui/apps/admin/src/views/Login.vue` | 登录页（新增医院 ID 输入） |
| `src/HospitalAi.Api/ApiEnvelope.cs` | 统一成功响应信封 |
| `src/HospitalAi.Api/Program.cs` | CORS + 4 个 auth 端点 + 信封包装 |
| `src/HospitalAi.Api/RequestContextMiddleware.cs` | Bearer JWT 解析 → 请求上下文 |
| `src/HospitalAi.Api/ExceptionHandlingMiddleware.cs` | 统一错误体 `ErrorResponse` |
| `src/HospitalAi.Contracts/Security/AuthContracts.cs` | Login/Me/ChangePassword 契约 |
| `src/HospitalAi.Application/Security/IAuthService.cs` | 认证服务契约 |
| `src/HospitalAi.Infrastructure/Security/SqlServerAuthService.cs` | PBKDF2 + JWT 实现 |
| `src/HospitalAi.Infrastructure/Security/{JwtAuthOptions,ConfigJwtOptionsProvider,IJwtOptionsProvider}.cs` | JWT 配置绑定 |
| `src/HospitalAi.Infrastructure/SqlServer/{PersistenceRecords.cs, HospitalAiDbContext.cs}` | `app_user` 新列映射 |
| `src/HospitalAi.Infrastructure/SqlServer/Migrations/20260919000000_AppUserPasswordHash.cs` | 迁移（+Designer + Snapshot 已同步） |
