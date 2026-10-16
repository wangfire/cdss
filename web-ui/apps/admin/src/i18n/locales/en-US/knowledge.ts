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
  totalSynonyms: 'Synonym Groups',
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
  icdIndexTab: 'ICD Index',

  // ICD index browsing
  seqNoColumn: 'Seq No',
  rawNameColumn: 'Raw Index Name',
  rawNameEnColumn: 'Raw Index English Name',
  indexNameColumn: 'Index Name',
  indexCodeColumn: 'Index Code',
  indexLevelColumn: 'Index Level',
  parentNodeColumn: 'Parent Node',
  fullIndexNameColumn: 'Full Index Name',
  leadWordStatusColumn: 'Lead Word Status',
  leadWordValid: 'Valid',
  leadWordInvalid: 'Invalid',
  searchIcdIndexPlaceholder: 'Search index name, full name, or code',
  filterByLeadWordStatus: 'Filter by lead word status',
  allLeadWordStatus: 'All statuses',
  noIcdIndex: 'No data',
  expandNode: 'Expand children',
  collapseNode: 'Collapse children',
  icdIndexRootProgress: 'Root indexes loaded {loaded} / {total}',
  loadMoreRoots: 'Load more root indexes',

  // CM3 (procedure) index browsing
  cm3IndexTab: 'CM3 Index',
  searchCm3IndexPlaceholder: 'Search procedure index name, full name, or code',
  noCm3Index: 'No data',
  cm3IndexRootProgress: 'Root procedure indexes loaded {loaded} / {total}',

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

  // Synonym browsing (icd_term_synonym)
  variantTermsColumn: 'Variant Terms',
  normalizedTermColumn: 'Canonical Term',
  entityTypeColumn: 'Term Channel',
  termTypeDiagnosis: 'Diagnosis',
  termTypeProcedure: 'Procedure',
  searchSynonymPlaceholder: 'Search variant or canonical term',
  equivalenceGroupColumn: 'Equivalence group',
  equivalenceHint: 'Any two terms in a group are interchangeable (including the canonical term); each row is one equivalence edge.',
  createSynonymGroup: 'New synonym group',
  editSynonymGroup: 'Edit synonym group',
  canonicalTermPlaceholder: 'Canonical term, e.g. premature contraction',
  variantTermPlaceholder: 'Equivalent term, e.g. early beat',
  addVariantTerm: 'Add equivalent term',
  actionsColumn: 'Actions',
  edit: 'Edit',
  delete: 'Delete',
  save: 'Save',
  synonymSaved: 'Synonym saved',
  synonymDeleted: 'Synonym group deleted',
  saveFailed: 'Failed to save',
  deleteFailed: 'Failed to delete',
  deleteGroupConfirm: 'Delete the whole equivalence group "{canonical}" ({count} rows)?',
  canonicalTermRequired: 'Canonical term is required',
  atLeastOneVariantRequired: 'At least one equivalent term is required',
  variantEqualsCanonical: 'Equivalent term must differ from the canonical term',
  duplicateVariantTerm: 'Equivalent terms must be unique',

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
