/**
 * 知识库导入示例数据（领域数据，非 UI 文案——中文为 ICD 术语本身，不参与 i18n）
 */
export interface TermSynonymImportItem {
  term: string
  normalizedTerm: string
  entityType: string
  codeSystemCode: string
  code: string
}

export interface CodingRuleImportItem {
  ruleCode: string
  codeSystem: string
  codePattern: string
  ruleType: string
  severity: string
  message: string
  isEnabled: boolean
}

export const sampleSynonyms: TermSynonymImportItem[] = [
  { term: '感冒', normalizedTerm: '急性鼻咽炎', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'J00' },
  { term: '高血压', normalizedTerm: '特发性高血压', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'I10' },
  { term: '糖尿病', normalizedTerm: '非胰岛素依赖型糖尿病', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'E11' },
  { term: '阑尾炎手术', normalizedTerm: '阑尾切除术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '47.0' },
  { term: '搭桥手术', normalizedTerm: '冠状动脉旁路移植术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '36.1' },
]

export const sampleRules: CodingRuleImportItem[] = [
  { ruleCode: 'SZ-001', codeSystem: 'ICD-10', codePattern: 'J00-J06', ruleType: 'VALIDATION', severity: 'WARNING', message: '上呼吸道感染编码需确认病程', isEnabled: true },
  { ruleCode: 'SZ-002', codeSystem: 'ICD-10', codePattern: 'I10-I15', ruleType: 'VALIDATION', severity: 'ERROR', message: '高血压编码需区分原发性和继发性', isEnabled: true },
  { ruleCode: 'SZ-003', codeSystem: 'ICD-10', codePattern: 'E10-E14', ruleType: 'VALIDATION', severity: 'WARNING', message: '糖尿病编码需确认分型', isEnabled: true },
]
