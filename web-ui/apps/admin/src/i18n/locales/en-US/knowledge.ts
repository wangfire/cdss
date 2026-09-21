/**
 * English knowledge translations
 */
export default {
  // Page title
  title: 'Knowledge Base',
  subtitle: 'Manage ICD code dictionaries, synonyms, and coding rules',

  // Code system import
  codeSystemImport: 'Code System Import',
  codeSystemImportDesc: 'Import ICD-10 disease codes or ICD-9-CM-3 procedure codes',
  codeSystem: 'Code System',
  version: 'Version',
  icd10: 'ICD-10 Disease Codes',
  icd9cm3: 'ICD-9-CM-3 Procedure Codes',
  importCodes: 'Import Codes',
  importedCount: 'Imported',
  updatedCount: 'Updated',
  importSuccess: 'Code system imported successfully',
  importFailed: 'Code system import failed',

  // Rules import
  rulesImport: 'Rules & Synonyms Import',
  rulesImportDesc: 'Import medical term synonyms and coding validation rules',
  synonyms: 'Synonyms',
  rules: 'Coding Rules',
  importRules: 'Import Rules',
  synonymCount: 'Synonym Count',
  ruleCount: 'Rule Count',
  rulesImportSuccess: 'Rules imported successfully',
  rulesImportFailed: 'Rules import failed',

  // Knowledge stats
  knowledgeStats: 'Knowledge Base Stats',
  totalCodes: 'Total Codes',
  enabledCodes: 'Enabled',
  totalSynonyms: 'Total Synonyms',
  totalRules: 'Total Rules',

  // Form
  codeSystemPlaceholder: 'Select code system',
  versionPlaceholder: 'Enter version',
  fileUpload: 'Upload File',
  fileUploadDesc: 'Supports JSON format files',
  selectFile: 'Select File',
  noFileSelected: 'No file selected',

  // Sample data
  loadSampleData: 'Load Sample Data',
  sampleDataLoaded: 'Sample data loaded',

  // Data browsing
  browseData: 'Browse Data',
  codeSystems: 'Code Systems',
  medicalCodes: 'Medical Codes',
  termSynonyms: 'Term Synonyms',
  codingRulesTab: 'Coding Rules',

  // Code system browsing
  codeColumn: 'Code',
  nameColumn: 'Name',
  versionColumn: 'Version',
  createdAtColumn: 'Created At',
  noCodeSystems: 'No code systems yet. Import some first.',

  // Medical code browsing
  codeTitle: 'Code Title',
  codeSystemColumn: 'Code System',
  searchTextColumn: 'Search Text',
  statusColumn: 'Status',
  enabled: 'Enabled',
  disabled: 'Disabled',
  searchPlaceholder: 'Search code, title, or search text',
  filterByCodeSystem: 'Filter by code system',
  allCodeSystems: 'All Systems',
  totalRecords: '{total} records',

  // Synonym browsing
  termColumn: 'Term',
  normalizedTermColumn: 'Normalized Term',
  entityTypeColumn: 'Entity Type',
  relatedCodeColumn: 'Related Code',
  searchSynonymPlaceholder: 'Search term or normalized term',

  // Rule browsing
  ruleCodeColumn: 'Rule Code',
  codePatternColumn: 'Code Pattern',
  ruleTypeColumn: 'Rule Type',
  severityColumn: 'Severity',
  messageColumn: 'Message',
  filterByRuleCodeSystem: 'Filter by system',

  // Pagination
  pagination: 'Page {page} / {totalPages}',
  previousPage: 'Previous',
  nextPage: 'Next',

  // Loading states
  loading: 'Loading...',
  loadFailed: 'Failed to load data',
}
