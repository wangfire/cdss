import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

// CAP 院知识库全量灌库：建医院 → ICD-10/ICD-9 字典 → 规则/同义词 → icd_index 52 批 → tumor seed。
// 用法: node scripts/bootstrap_cap_full.mjs [apiBase]
const settings = JSON.parse(fs.readFileSync('src/HospitalAi.Api/appsettings.Development.json', 'utf8'));
const key = settings.JwtAuth.Key;
const api = process.argv[2] ?? 'http://127.0.0.1:5081';

const b64 = (v) => Buffer.from(typeof v === 'string' ? v : JSON.stringify(v)).toString('base64url');
function mintJwt(hospitalId, role = 'ADMIN') {
    const now = Math.floor(Date.now() / 1000);
    const header = b64({ alg: 'HS256', typ: 'JWT' });
    const payload = b64({
        sub: 'bootstrap-cap', nameid: 'bootstrap-cap',
        iss: 'hospitalai', aud: 'hospitalai-admin',
        iat: now, nbf: now - 30, exp: now + 7200,
        hospitalId,
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': role,
    });
    const sig = crypto.createHmac('sha256', key).update(`${header}.${payload}`).digest('base64url');
    return `${header}.${payload}.${sig}`;
}

async function post(path, token, hospitalId, body, timeoutMs = 900000) {
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json; charset=utf-8' };
    if (hospitalId) headers['X-Hospital-Id'] = hospitalId;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const resp = await fetch(`${api}${path}`, { method: 'POST', headers, body, signal: controller.signal });
        const text = await resp.text();
        if (!resp.ok) throw new Error(`${path} -> ${resp.status} ${text.slice(0, 300)}`);
        return JSON.parse(text);
    } finally { clearTimeout(timer); }
}

async function get(path, token, hospitalId, timeoutMs = 900000) {
    const headers = { Authorization: `Bearer ${token}` };
    if (hospitalId) headers['X-Hospital-Id'] = hospitalId;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const resp = await fetch(`${api}${path}`, { headers, signal: controller.signal });
        const text = await resp.text();
        if (!resp.ok) throw new Error(`${path} -> ${resp.status} ${text.slice(0, 300)}`);
        return JSON.parse(text);
    } finally { clearTimeout(timer); }
}

// 1. 建医院（复用固定 code 幂等）
const bootstrapToken = mintJwt(crypto.randomUUID());
const hosp = await post('/api/v1/hospitals', bootstrapToken, null,
    JSON.stringify({ code: 'ICD10CAP-DE', name: 'ICD10抽样测试医院-DE' }));
const hospitalId = hosp.data.id;
console.log('[1] 医院 hospitalId =', hospitalId);

const token = mintJwt(hospitalId);

// 2. 字典
const r10 = await post('/api/v1/code-systems/import', token, hospitalId,
    fs.readFileSync('data/knowledge/icd10-2026.json', 'utf8'));
console.log('[2] ICD-10 imported =', r10.data?.importedCount);
const r9 = await post('/api/v1/code-systems/import', token, hospitalId,
    fs.readFileSync('data/knowledge/icd9cm3-2026.json', 'utf8'));
console.log('[2] ICD-9-CM-3 imported =', r9.data?.importedCount);

// 3. 规则/同义词
const rr = await post('/api/v1/coding-rules/import', token, hospitalId,
    fs.readFileSync('data/knowledge/coding-rules-2026.json', 'utf8'));
console.log('[3] synonyms =', rr.data?.synonymCount, 'rules =', rr.data?.ruleCount);

// 4. icd_index 52 批
const batchDir = 'data/knowledge/icd-index-batches';
const batches = fs.readdirSync(batchDir).filter(f => f.endsWith('.json')).sort();
let idxTotal = 0;
for (const [i, file] of batches.entries()) {
    const body = fs.readFileSync(path.join(batchDir, file), 'utf8');
    const r = await post('/api/v1/icd-index/import', token, hospitalId, body);
    idxTotal += r.data?.importedCount ?? 0;
    if ((i + 1) % 10 === 0 || i === batches.length - 1) {
        console.log(`[4] icd-index 批次 ${i + 1}/${batches.length} 累计 imported = ${idxTotal}`);
    }
}

// 5. tumor_index seed（直接 SQL 脚本走 API 无端点——检查有无端点，无则跳过提示）
console.log('DONE hospitalId=' + hospitalId);
fs.writeFileSync('logs/cap_full_hospital.json', JSON.stringify({ hospitalId, code: 'ICD10CAP-DE' }, null, 2));
