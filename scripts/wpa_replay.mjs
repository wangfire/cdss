import crypto from 'node:crypto';
import fs from 'node:fs';
const settings = JSON.parse(fs.readFileSync('src/HospitalAi.Api/appsettings.Development.json', 'utf8'));
const key = settings.JwtAuth.Key;
const hospitalId = '6b427cb5-a6ba-4a8e-9b36-311c1102f20b';
const api = process.env.API_BASE || 'http://127.0.0.1:5081';
const b64 = (v) => Buffer.from(typeof v === 'string' ? v : JSON.stringify(v)).toString('base64url');
const now = Math.floor(Date.now() / 1000);
const header = b64({ alg: 'HS256', typ: 'JWT' });
const payload = b64({ sub: 'wpa-replay', nameid: 'wpa-replay', iss: 'hospitalai', aud: 'hospitalai-admin', iat: now, nbf: now - 30, exp: now + 7200, hospitalId, 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'ADMIN', permission: 'knowledge.icd.import' });
const sig = crypto.createHmac('sha256', key).update(`${header}.${payload}`).digest('base64url');
const token = `${header}.${payload}.${sig}`;
for (const file of ['data/knowledge/icd-index-batches-extra/batch-9001.json', 'data/knowledge/icd-index-batches-extra/batch-9002.json']) {
    const body = fs.readFileSync(file, 'utf8');
    const resp = await fetch(`${api}/api/v1/icd-index/import`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'X-Hospital-Id': hospitalId, 'Content-Type': 'application/json' }, body });
    const text = await resp.text();
    console.log(resp.status, file, text.slice(0, 120));
}