import crypto from 'node:crypto';
import fs from 'node:fs';

// 灌 ES 索引：对指定医院调用 /api/v1/admin/knowledge/index/rebuild（ADMIN + rebuild 权限 JWT）。
const settings = JSON.parse(fs.readFileSync('src/HospitalAi.Api/appsettings.Development.json', 'utf8'));
const key = settings.JwtAuth.Key;
const hospitalId = process.argv[2] ?? 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
const api = process.env.API_BASE || 'http://127.0.0.1:5081';

const b64 = (v) => Buffer.from(typeof v === 'string' ? v : JSON.stringify(v)).toString('base64url');
const now = Math.floor(Date.now() / 1000);
const header = b64({ alg: 'HS256', typ: 'JWT' });
const payload = b64({
    sub: 'rebuild', nameid: 'rebuild', iss: 'hospitalai', aud: 'hospitalai-admin',
    iat: now, nbf: now - 30, exp: now + 7200, hospitalId,
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'ADMIN',
});
const sig = crypto.createHmac('sha256', key).update(`${header}.${payload}`).digest('base64url');
const token = `${header}.${payload}.${sig}`;

const resp = await fetch(`${api}/api/v1/admin/knowledge/index/rebuild`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'X-Hospital-Id': hospitalId, 'Content-Type': 'application/json' },
    body: '{}',
});
const text = await resp.text();
console.log(resp.status, text.slice(0, 400));
