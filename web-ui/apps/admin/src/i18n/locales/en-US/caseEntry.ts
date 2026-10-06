/**
 * English case entry translations
 */
export default {
  title: 'Case Entry',
  subtitle: 'Enter a single case with diagnoses / procedures; AI coding runs asynchronously after submission',

  basicInfo: 'Basic Info',
  patientName: 'Name',
  patientNamePlaceholder: 'Patient name',
  medicalRecordNo: 'MRN',
  medicalRecordNoPlaceholder: 'Medical record number (unique in hospital)',
  admissionCount: 'Admission No.',
  admissionCountPlaceholder: 'Auto-increment if empty',
  admissionAt: 'Admission Date',
  dischargeAt: 'Discharge Date',

  clinical: 'Diagnoses & Procedures',
  onePerLine: 'One per line',
  admissionDiagnoses: 'Admission Diagnoses',
  dischargeDiagnoses: 'Discharge Diagnoses',
  principalHint: 'One per line; the first becomes the principal diagnosis',
  procedures: 'Procedures',
  additionalDocument: 'Extra Document Text (optional)',
  additionalDocumentPlaceholder: 'Free text (e.g. admission note) used as supplementary evidence…',

  submit: 'Submit & Run AI Recommendation',
  submitting: 'Submitting…',
  submitTimeoutHint: 'Accepted. AI coding is running — do not close the page',

  acceptedTitle: 'Case Accepted',
  processing: 'AI is generating coding recommendations…',
  taskFailed: 'AI coding failed, check the run log in the Workbench',
  taskTimeout: 'Timed out waiting for AI coding, check the Workbench later',

  successTitle: 'Case Created',
  inputCount: 'Diagnosis Inputs',
  recCount: 'AI Recommendations',
  visitCount: 'Admission No.',
  pipeline: 'Pipeline',
  inputsLabel: 'Generated diagnosis inputs',
  goWorkbench: 'Review in Workbench',
  another: 'Enter Another Case',

  recPanelTitle: 'AI Recommendations',
  recPanelEmpty: 'Fill in the left and middle panels and submit; recommended codes appear here directly',
  recPanelRunning: 'Pipeline running, results will appear automatically…',
  recPanelLoading: 'Loading recommendations…',
  recPanelNone: 'No recommendations produced',
  recFetchFailed: 'Failed to load recommendations, check the Workbench',
  principal: 'Principal',

  nameRequired: 'Patient name is required',
  recordNoRequired: 'Medical record number is required',
  admissionDateRequired: 'Admission date is required',
  dischargeRequired: 'At least one discharge diagnosis is required',
  submitFailed: 'Submit failed',
}
