/**
 * TypeScript DTOs matching Contracts/Admin and Contracts/Scoring 1:1
 */

export interface AdminCertificateSummaryDto {
  certId: number;
  code: string;
  name: string;
  description: string | null;
  scoreAggregationRule: string;
  presetCode: string | null;
  isActive: boolean;
  skillsCount: number;
  sectionsCount: number;
  examsCount: number;
}

export interface SkillBlueprintDetailDto {
  skillId: number;
  certId: number;
  code: string;
  name: string;
  modality: string;
  displayOrder: number;
  durationMinutes: number | null;
  sections: SectionBlueprintDetailDto[];
  minScaledScore?: number | null;
  maxScaledScore?: number | null;
  scoreStep?: number | null;
}

export interface SectionBlueprintDetailDto {
  sectionId: number;
  skillId: number;
  certId: number;
  code: string;
  name: string;
  displayOrder: number;
  scoreWeight: number;
  navigationMode: string;
  timerPolicyJson: string | null;
  layoutType: string;
  optionCount: number;
  clusterSize: number;
  unitCount: number;
  hideOptionText: boolean;
  requirePassage: boolean;
  requireAudio: boolean;
  requireImage: boolean;
  requiredKeywordCount: number;
  expectedQuestionCount: number;
  allowedComponentTypesJson: string | null;
  toolsConfigJson: string | null;
}

export interface BandDto {
  bandId: number;
  certId: number;
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  displayOrder: number;
}

export interface GradingCriterionDto {
  criterionId: number;
  skillCode: string;
  code: string;
  name: string;
  maxScore: number;
  weight: number;
  displayOrder: number;
  rubricDescription: string | null;
}

export interface CertificateBlueprintDetailDto {
  certId: number;
  code: string;
  name: string;
  description: string | null;
  scoreAggregationRule: string;
  presetCode: string | null;
  isActive: boolean;
  skills: SkillBlueprintDetailDto[];
  bands: BandDto[];
  criteria?: GradingCriterionDto[] | null;
}

export interface SectionBlueprintPresetDto {
  code: string;
  name: string;
  displayOrder: number;
  scoreWeight: number;
  navigationMode: string;
  timerPolicyJson: string | null;
  layoutType: string;
  optionCount: number;
  clusterSize: number;
  unitCount: number;
  hideOptionText: boolean;
  requirePassage: boolean;
  requireAudio: boolean;
  requireImage: boolean;
  requiredKeywordCount: number;
  expectedQuestionCount: number;
  allowedComponentTypesJson: string | null;
  toolsConfigJson: string | null;
}

export interface SkillBlueprintPresetDto {
  code: string;
  name: string;
  modality: string;
  displayOrder: number;
  durationMinutes: number | null;
  sections: SectionBlueprintPresetDto[];
}

export interface CertificateBlueprintPresetDto {
  presetCode: string;
  name: string;
  description: string;
  scoreAggregationRule: string;
  skills: SkillBlueprintPresetDto[];
}

export interface SaveSectionWizardDto {
  sectionId?: number;
  code: string;
  name: string;
  displayOrder: number;
  scoreWeight: number;
  navigationMode: string;
  timerPolicyJson?: string | null;
  layoutType: string;
  optionCount: number;
  clusterSize: number;
  unitCount: number;
  hideOptionText: boolean;
  requirePassage: boolean;
  requireAudio: boolean;
  requireImage: boolean;
  requiredKeywordCount: number;
  expectedQuestionCount: number;
  allowedComponentTypesJson?: string | null;
  toolsConfigJson?: string | null;
}

export interface SaveSkillWizardDto {
  skillId?: number;
  code: string;
  name: string;
  modality: string;
  displayOrder: number;
  durationMinutes?: number | null;
  sections: SaveSectionWizardDto[];
}

export interface SaveScoreBandWizardDto {
  bandId?: number | null;
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  displayOrder: number;
}

export interface SaveGradingCriterionWizardDto {
  criterionId?: number;
  skillCode: string;
  code: string;
  name: string;
  maxScore: number;
  weight: number;
  displayOrder: number;
  rubricDescription?: string | null;
}

export interface SaveCertificateWizardRequest {
  certId?: number | null;
  code: string;
  name: string;
  description?: string | null;
  scoreAggregationRule: string;
  presetCode?: string | null;
  isActive: boolean;
  skills: SaveSkillWizardDto[];
  minScaledScore?: number | null;
  maxScaledScore?: number | null;
  scoreStep?: number | null;
  bands?: SaveScoreBandWizardDto[] | null;
  criteria?: SaveGradingCriterionWizardDto[] | null;
}

export interface SaveCertificateWizardResultDto {
  certId: number;
  code: string;
  name: string;
  skillsCount: number;
  sectionsCount: number;
}

// --- Scoring & Score Conversion ---

export interface ScoreScaleRowDto {
  scaleId: number | null;
  skillId: number;
  skillCode: string;
  skillName: string;
  rawScore: number;
  scaledScore: number;
}

export interface SkillScoreScaleGroupDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  maxQuestions: number;
  scales: ScoreScaleRowDto[];
}

export interface ScoreBandConfigDto {
  bandId: number;
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  displayOrder: number;
}

export interface CertificateOptionDto {
  certId: number;
  code: string;
  name: string;
}

export interface AdminScoreConversionDto {
  certId: number;
  certCode: string;
  certName: string;
  certificates: CertificateOptionDto[];
  examId: number | null;
  examName: string | null;
  skillScales: SkillScoreScaleGroupDto[];
  bands: ScoreBandConfigDto[];
}

export interface UpdateScoreScaleEntryDto {
  skillId: number;
  rawScore: number;
  scaledScore: number;
}

export interface UpdateScoreBandEntryDto {
  bandId?: number | null;
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  displayOrder: number;
}

export interface UpdateScoreConversionRequest {
  examId?: number | null;
  scales: UpdateScoreScaleEntryDto[];
  bands: UpdateScoreBandEntryDto[];
}

export interface SectionScoreInputDto {
  sectionId?: number | null;
  sectionCode?: string | null;
  skillId?: number | null;
  rawScore: number;
  scoreWeight?: number;
  maxQuestions?: number | null;
}

export interface SkillScoreInputDto {
  skillId?: number | null;
  skillCode?: string | null;
  rawScore?: number | null;
  sections?: SectionScoreInputDto[] | null;
}

export interface ScorePreviewRequestDto {
  certId?: number | null;
  certCode?: string | null;
  scoreAggregationRule: string;
  skills: SkillScoreInputDto[];
  customScales?: UpdateScoreScaleEntryDto[] | null;
  customBands?: UpdateScoreBandEntryDto[] | null;
}

export interface SectionScorePreviewResultDto {
  sectionId: number | null;
  sectionCode: string | null;
  rawScore: number;
  scoreWeight: number;
}

export interface SkillScorePreviewResultDto {
  skillId: number | null;
  skillCode: string | null;
  rawScore: number;
  scaledScore: number;
  sections?: SectionScorePreviewResultDto[] | null;
}

export interface ScorePreviewResultDto {
  scoreAggregationRule: string;
  skillScores: SkillScorePreviewResultDto[];
  totalScore: number;
  bandCode: string | null;
  bandName: string | null;
}

// --- Rubric Studio ---

export interface RubricCellDto {
  band: number; // 0..6
  descriptor: string;
  binaryGates: string;
}

export interface RubricAnchorDto {
  band: number; // 2, 3, or 4
  responseText: string;
}

export interface RubricCriterionDto {
  criterionId: number;
  code: string;
  name: string;
  maxScore: number;
  weight: number;
  displayOrder: number;
  cells: RubricCellDto[];
  anchors: RubricAnchorDto[];
}

export interface RubricLadderPointDto {
  band: number;
  composite: number;
  isVerboseTrap: boolean;
}

export interface RubricLadderDto {
  outcome: string; // accepted, not_monotonic, separation, trap_ceiling, not_plotted
  accepted: boolean;
  reason: string | null;
  responsibleCriteria: string[];
  points: RubricLadderPointDto[];
  ranAt: string | null;
}

export interface RubricStudioDto {
  certId: number;
  certCode: string;
  skillId: number;
  skillCode: string;
  skillName: string;
  modality: string;
  isLocked: boolean;
  lockedAt: string | null;
  criteria: RubricCriterionDto[];
  ladder: RubricLadderDto | null;
}

export interface RubricLadderResponseDto {
  band: number;
  responseText: string;
  isVerboseTrap: boolean;
}

export interface RunRubricLadderRequest {
  certId: number;
  skillId: number;
  responses: RubricLadderResponseDto[];
}

export interface SaveRubricRequest {
  certId: number;
  skillId: number;
  criteria: RubricCriterionDto[];
}

// --- Exams Authoring Studio ---

export interface AdminCertificateOptionDto {
  certId: number;
  code: string;
  name: string;
}

export interface AdminExamListItemDto {
  examId: number;
  certId: number;
  certCode: string;
  certName: string;
  name: string;
  status: string; // draft, published, withdrawn, hidden
  purpose: string; // full_mock, mini_mock, placement, diagnostic, practice, speaking_practice, writing_practice
  durationMinutes: number;
  questionCount: number | null;
  actualQuestionCount: number;
  audioStatus: string;
  finishedAttemptCount: number;
  updatedAt: string | null;
  createdAt: string;
  sourceName?: string | null;
}

export interface AdminExamListSummaryDto {
  exams: AdminExamListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  certificates: AdminCertificateOptionDto[];
}

export interface AdminExamSectionProgressDto {
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  modality: string;
  questionCount: number;
  expectedQuestionCount: number;
  hasAudio: boolean;
  isComplete: boolean;
  statusText: string;
}

export interface AdminExamSectionAudioDto {
  examId: number;
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  audioPath: string;
  durationSec: number;
  sizeBytes: number;
  uploadedAt: string;
}

export interface AdminExamDetailDto {
  examId: number;
  certId: number;
  certCode: string;
  certName: string;
  name: string;
  status: string;
  durationMinutes: number;
  purpose: string;
  description: string | null;
  sourceName: string | null;
  internalNote: string | null;
  questionCount: number | null;
  actualQuestionCount: number;
  finishedAttemptCount: number;
  createdAt: string;
  updatedAt: string | null;
  audios: AdminExamSectionAudioDto[];
  sectionProgress: AdminExamSectionProgressDto[];
}

export interface CreateAdminExamRequest {
  certId: number;
  name: string;
  durationMinutes: number;
  purpose: string;
  description?: string | null;
  sourceName?: string | null;
  internalNote?: string | null;
  questionCount?: number | null;
}

export interface UpdateAdminExamRequest {
  certId: number;
  name: string;
  durationMinutes: number;
  purpose: string;
  description?: string | null;
  sourceName?: string | null;
  internalNote?: string | null;
  questionCount?: number | null;
}

export interface SaveExamSectionAudioRequest {
  sectionId: number;
  audioPath: string;
  durationSec: number;
  sizeBytes: number;
}

export interface ExamAuditIssueDto {
  code: string;
  severity: 'error' | 'warning';
  message: string;
  sectionId?: number | null;
  sectionCode?: string | null;
  groupId?: number | null;
  questionId?: number | null;
  elementTarget?: string | null;
}

export interface ExamInvariantDto {
  code: string;
  title: string;
  satisfied: boolean;
  deficitCount: number;
  firstElementTarget: string | null;
}

export interface ExamAuditResultDto {
  canPublish: boolean;
  totalQuestions: number;
  expectedQuestions: number;
  blockingErrorCount: number;
  warningCount: number;
  issues: ExamAuditIssueDto[];
  missingItems: string[];
  invariants?: ExamInvariantDto[] | null;
}

export interface AdminPublishExamResultDto {
  success: boolean;
  message: string;
  missingItems: string[];
}

export interface ExamStudioQuestionOptionDto {
  optionId: number;
  questionId: number;
  label: string;
  text: string;
  isCorrect: boolean;
  rationale: string | null;
}

export interface ExamStudioCriterionDto {
  criterionId: number;
  code: string;
  name: string;
  description: string | null;
  maxScore: number;
  skillId?: number;
}

export interface ExamStudioQuestionDto {
  questionId: number;
  groupId: number;
  examId: number;
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  skillId: number;
  skillCode: string;
  stem: string;
  questionType: string;
  explanation: string | null;
  categoryId: number | null;
  categoryCode: string | null;
  categoryName: string | null;
  prepSeconds: number | null;
  responseSeconds: number | null;
  minWords: number | null;
  maxWords: number | null;
  hint: string | null;
  displayOrder: number;
  updatedAt: string | null;
  options: ExamStudioQuestionOptionDto[];
  criteria: ExamStudioCriterionDto[];
  hasAnswers: boolean;
  maxSelections?: number | null;
  allowNumber?: boolean;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  sampleAnswerOrImageDesc?: string | null;
  referenceKind?: string | null;
  cefrLevelCode?: string | null;
}

export interface ExamStudioGroupDto {
  groupId: number;
  examId: number;
  certId: number;
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  modality: string;
  passage: string | null;
  audioPath: string | null;
  audioStartMs: number | null;
  audioEndMs: number | null;
  imagePath: string | null;
  imageAlt: string | null;
  transcript: string | null;
  prepSeconds: number | null;
  responseSeconds: number | null;
  displayOrder: number;
  questions: ExamStudioQuestionDto[];
  unitNumber?: number;
}

export interface ExamStudioSectionDto {
  sectionId: number;
  certId: number;
  code: string;
  name: string;
  modality: string;
  expectedQuestionCount: number;
  displayOrder: number;
  currentQuestionCount: number;
  hasAudio: boolean;
  masterAudioPath: string | null;
  masterAudioDurationSec: number | null;
  groups: ExamStudioGroupDto[];
  optionCount?: number;
  clusterSize?: number;
  unitCount?: number;
  hideOptionText?: boolean;
  requirePassage?: boolean;
  requireAudio?: boolean;
  requireImage?: boolean;
  requiredKeywordCount?: number;
  allowedComponentTypesJson?: string | null;
  layoutType?: string;
  navigationMode?: string;
  timerPolicyJson?: string | null;
  toolsConfigJson?: string | null;
  skillId?: number;
  skillCode?: string | null;
  skillName?: string | null;
}

export interface AdminSectionOptionDto {
  sectionId: number;
  code: string;
  name: string;
  modality: string;
  expectedQuestionCount: number;
  displayOrder: number;
}

export interface AdminQuestionCategoryOptionDto {
  categoryId: number;
  certId: number;
  code: string;
  name: string;
}

export interface AdminExamQuestionsDto {
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  groups: ExamStudioGroupDto[];
  availableSections: AdminSectionOptionDto[];
  availableCategories: AdminQuestionCategoryOptionDto[];
}

export interface ExamStudioDetailDto {
  examId: number;
  certId: number;
  certCode: string;
  certName: string;
  name: string;
  status: string;
  purpose: string;
  durationMinutes: number;
  description: string | null;
  sourceName: string | null;
  internalNote: string | null;
  questionCount: number | null;
  actualQuestionCount: number;
  finishedAttemptCount: number;
  createdAt: string;
  updatedAt: string | null;
  sections: ExamStudioSectionDto[];
  availableCategories: AdminQuestionCategoryOptionDto[];
  availableCriteria: ExamStudioCriterionDto[];
  audit: ExamAuditResultDto;
}

export interface ExamAssemblyPositionDto {
  displayOrder: number;
  isDeclared: boolean;
  itemVersionId: number | null;
  topicCode: string | null;
  cefrLevel: string | null;
  unitNumber: number | null;
}

export interface ExamAssemblyCandidateDto {
  itemId: number;
  itemVersionId: number;
  versionNumber: number;
  topicCode: string;
  cefrLevel: string;
  itemType: string;
  contentTier: string;
  stem: string;
}

export interface ExamAssemblySectionDto {
  sectionId: number;
  code: string;
  name: string;
  modality: string;
  displayOrder: number;
  declaredCount: number;
  boundCount: number;
  supplyCount: number;
  shortfall: number;
  positions: ExamAssemblyPositionDto[];
  candidates: ExamAssemblyCandidateDto[];
  candidateTotal: number;
}

export interface ExamAssemblyDto {
  examId: number;
  examName: string;
  certCode: string;
  contentTier: string;
  durationMinutes: number;
  finishedAttemptCount: number;
  sections: ExamAssemblySectionDto[];
}

export interface BindPoolItemRequest {
  sectionId: number;
  itemVersionId: number;
  displayOrder: number;
  unitNumber?: number;
}

export interface ExamItemBindingDto {
  examItemId: number;
  examId: number;
  sectionId: number;
  certId: number;
  itemId: number;
  itemVersionId: number;
  modality: string;
  displayOrder: number;
  unitNumber: number;
}

export interface CreateExamClusterRequest {
  sectionId: number;
  passage?: string | null;
  transcript?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  questionCount?: number;
  unitNumber?: number;
  cefrLevelCode?: string | null;
}

export interface ReorderSectionItemEntry {
  groupId: number | null;
  questionId: number | null;
  displayOrder: number;
}

export interface ReorderSectionItemsRequest {
  sectionId: number;
  items: ReorderSectionItemEntry[];
}

export interface QuickIngestParsedOptionDto {
  label: string;
  text: string;
  isCorrect: boolean;
  rationale?: string | null;
}

export interface QuickIngestIssueDto {
  lineNumber: number;
  severity: 'error' | 'warning';
  message: string;
  questionIndex?: number | null;
}

export interface QuickIngestParsedQuestionDto {
  index: number;
  startLineNumber: number;
  endLineNumber: number;
  stem: string;
  explanation: string | null;
  categoryCode: string | null;
  options: QuickIngestParsedOptionDto[];
  issues: QuickIngestIssueDto[];
  isValid: boolean;
  questionType?: string;
  passage?: string | null;
  transcript?: string | null;
  maxWords?: number | null;
  maxSelections?: number | null;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  hint?: string | null;
  groupNumber?: number | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  minWords?: number | null;
  allowNumber?: boolean;
  sampleAnswerOrImageDesc?: string | null;
  referenceKind?: string | null;
  cefrLevel?: string | null;
  topicCode?: string | null;
  itemType?: string | null;
  placementEligible?: boolean;
  evidenceStart?: number | null;
  evidenceEnd?: number | null;
  evidenceUnit?: string | null;
  evidenceQuote?: string | null;
}

export interface QuickIngestParseRequest {
  rawText: string;
  sectionId?: number | null;
  certId?: number | null;
}

export interface QuickIngestParseResultDto {
  success: boolean;
  totalQuestions: number;
  validQuestions: number;
  errorCount: number;
  questions: QuickIngestParsedQuestionDto[];
  globalIssues: QuickIngestIssueDto[];
}

export interface QuickIngestCommitRequest {
  sectionId: number;
  mode: 'append' | 'replace';
  questions: QuickIngestParsedQuestionDto[];
  cefrLevel?: string | null;
  topicCode?: string | null;
}

export interface QuickIngestCommitResultDto {
  success: boolean;
  questionsCommitted: number;
  groupsCreated: number;
  message: string;
  poolItemsWritten?: number;
  poolStimuliWritten?: number;
  poolItemsBound?: number;
}

export interface QuickIngestSampleTemplateDto {
  template: string;
  formatDescription: string;
}

export interface AdminQuestionOptionInput {
  optionId?: number | null;
  label: string;
  text: string;
  isCorrect: boolean;
  rationale?: string | null;
}

export interface CreateQuestionRequest {
  groupId: number;
  stem: string;
  questionType: string;
  explanation?: string | null;
  categoryId?: number | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  minWords?: number | null;
  maxWords?: number | null;
  hint?: string | null;
  displayOrder?: number;
  options?: AdminQuestionOptionInput[] | null;
  criterionIds?: number[] | null;
  maxSelections?: number | null;
  allowNumber?: boolean;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  sampleAnswerOrImageDesc?: string | null;
  referenceKind?: string | null;
  cefrLevelCode?: string | null;
}

export interface UpdateQuestionRequest {
  groupId?: number | null;
  stem: string;
  questionType: string;
  explanation?: string | null;
  categoryId?: number | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  minWords?: number | null;
  maxWords?: number | null;
  hint?: string | null;
  displayOrder?: number;
  options?: AdminQuestionOptionInput[] | null;
  criterionIds?: number[] | null;
  maxSelections?: number | null;
  allowNumber?: boolean;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  sampleAnswerOrImageDesc?: string | null;
  referenceKind?: string | null;
  cefrLevelCode?: string | null;
}

export interface CreateQuestionGroupRequest {
  sectionId: number;
  passage?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  imagePath?: string | null;
  imageAlt?: string | null;
  transcript?: string | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  displayOrder?: number;
  unitNumber?: number;
}

export interface UpdateQuestionGroupRequest {
  sectionId: number;
  passage?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  imagePath?: string | null;
  imageAlt?: string | null;
  transcript?: string | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  displayOrder?: number;
  unitNumber?: number;
}

// --- Question Bank, Coverage, Items, Stimulus & Productive Studio ---

export interface AdminCoverageCellDto {
  topicId: number;
  modality: string;
  topicCode: string;
  topicName: string;
  cefrLevel: string;
  targetItemCount: number;
  activeItemCount: number;
  placementEligibleItemCount: number;
  deficit: number;
}

export interface AdminCoverageMatrixDto {
  cells: AdminCoverageCellDto[];
}

export interface CreatePoolStimulusRequest {
  cefrLevelCode: string;
  passage?: string | null;
  audioPath?: string | null;
  transcript?: string | null;
  domainContext?: string;
  promptCopy?: string | null;
  imagePath?: string | null;
  imageAlt?: string | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
}

export interface CreatePoolItemRequest {
  modality: string;
  topicId: number;
  cefrLevelCode: string;
  itemType: string;
  stem: string;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  keyLabel?: string | null;
  explanation?: string | null;
  placementEligible?: boolean;
  evidenceStart?: number | null;
  evidenceEnd?: number | null;
  evidenceUnit?: string | null;
  evidenceQuote?: string | null;
  referenceText?: string | null;
  referenceKind?: string | null;
  rationaleA?: string | null;
  rationaleB?: string | null;
  rationaleC?: string | null;
  rationaleD?: string | null;
  stimulus?: CreatePoolStimulusRequest | null;
  stimulusId?: number | null;
  renderFormat?: string | null;
}

export interface UpdatePoolItemRequest {
  stem: string;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  keyLabel?: string | null;
  explanation?: string | null;
  evidenceStart?: number | null;
  evidenceEnd?: number | null;
  evidenceUnit?: string | null;
  evidenceQuote?: string | null;
  referenceText?: string | null;
  referenceKind?: string | null;
  rationaleA?: string | null;
  rationaleB?: string | null;
  rationaleC?: string | null;
  rationaleD?: string | null;
}

export interface PoolItemDto {
  itemId: number;
  itemVersionId: number;
  versionNumber: number;
  versionCount: number;
  modality: string;
  topicId: number;
  topicCode: string;
  cefrLevelCode: string;
  itemType: string;
  contentTier: string;
  lifecycleStatus: string;
  placementEligible: boolean;
  stimulusId: number | null;
  stem: string;
  optionA: string | null;
  optionB: string | null;
  optionC: string | null;
  optionD: string | null;
  keyLabel: string | null;
  explanation: string | null;
  evidenceStart: number | null;
  evidenceEnd: number | null;
  evidenceUnit: string | null;
  evidenceQuote: string | null;
  referenceText: string | null;
  referenceKind: string | null;
  createdAt: string;
  rationaleA?: string | null;
  rationaleB?: string | null;
  rationaleC?: string | null;
  rationaleD?: string | null;
}

export interface StimulusDraftChild {
  position: number;
  cefrLevel: string;
  topicCode: string;
  stem: string;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  keyLabel?: string | null;
  explanation?: string | null;
  rationaleA?: string | null;
  rationaleB?: string | null;
  rationaleC?: string | null;
  rationaleD?: string | null;
  evidenceStart?: number | null;
  evidenceEnd?: number | null;
  evidenceQuote?: string | null;
  placementEligible?: boolean;
}

export interface StimulusDraftRequest {
  modality: string;
  stimulusLevel: string;
  itemType: string;
  children: StimulusDraftChild[];
  passage?: string | null;
  transcript?: string | null;
  audioPath?: string | null;
  domainContext?: string;
}

export interface StimulusRefusalDto {
  rule: string;
  gate: string;
  message: string;
}

export interface LevelGaugeDto {
  label: string;
  value: string;
  target: string;
  inside: boolean | null;
}

export interface StimulusOverlapDto {
  firstPosition: number;
  secondPosition: number;
  overlap: number;
  overCeiling: boolean;
}

export interface StimulusGapDto {
  afterPosition: number;
  beforePosition: number;
  seconds: number;
  belowFloor: boolean;
}

export interface StimulusDraftAnalysisDto {
  refusals: StimulusRefusalDto[];
  unrunChecks: StimulusRefusalDto[];
  gauges: LevelGaugeDto[];
  levelVerdict: string;
  overlaps: StimulusOverlapDto[];
  gaps: StimulusGapDto[];
  fittingBand?: string | null;
  selectedBand?: string | null;
  acceptable?: boolean;
}

export interface StimulusStudioTopicDto {
  topicId: number;
  code: string;
  name: string;
}

export interface StimulusStudioQuotaDto {
  level: string;
  itemType: string;
  locked: number;
  minimum: number;
  maximum: number;
  allowedChildLevels: string[];
  minimumAtLevel: number;
}

export interface StimulusStudioOptionsDto {
  topics: StimulusStudioTopicDto[];
  levels: string[];
  itemTypes: string[];
  quotas: StimulusStudioQuotaDto[];
  spanUnit: string;
}

export interface StimulusBundleDto {
  stimulusId: number;
  stimulusLevel: string;
  children: PoolItemDto[];
}

export interface ProductiveSubTaskInput {
  itemType: string;
  prompt: string;
  minWords?: number | null;
  maxWords?: number | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  explanation?: string | null;
}

export interface ProductiveTaskDraftRequest {
  modality: string;
  level: string;
  subTasks: ProductiveSubTaskInput[];
  topicCode: string;
  promptCopy?: string | null;
  imagePath?: string | null;
  imageAlt?: string | null;
  domainContext?: string;
  placementEligible?: boolean;
}

export interface ProductiveRefusalDto {
  rule: string;
  gate: string;
  message: string;
}

export interface ProductiveWordRangeDto {
  part: number;
  floor: number;
  ceiling: number;
  placementScoped: boolean;
}

export interface ProductiveTaskAnalysisDto {
  refusals: ProductiveRefusalDto[];
  unrunChecks: ProductiveRefusalDto[];
  placementRecordingCap: number | null;
  placementWordRanges: ProductiveWordRangeDto[];
  practiceWordRanges: ProductiveWordRangeDto[];
  acceptable?: boolean;
}

export interface ProductiveStudioTopicDto {
  topicId: number;
  code: string;
  name: string;
}

export interface ProductiveTypeOptionDto {
  code: string;
  visual: string;
  minimumPrepSeconds: number | null;
  maximumPrepSeconds: number | null;
  minimumResponseSeconds: number | null;
  maximumResponseSeconds: number | null;
  minimumWords: number | null;
  maximumWords: number | null;
}

export interface ProductiveStudioOptionsDto {
  topics: ProductiveStudioTopicDto[];
  levels: string[];
  types: ProductiveTypeOptionDto[];
}

export interface ProductiveTaskBundleDto {
  stimulusId: number;
  level: string;
  parts: PoolItemDto[];
}

// --- Ingestion & Imports ---

export interface IngestionBatchSummaryDto {
  batchId: number;
  sourceName: string;
  status: string;
  uploadedBy: number;
  uploadedByEmail: string;
  createdAt: string;
  completedAt: string | null;
  records: number;
  accepted: number;
  discarded: number;
}

export interface IngestionBatchListDto {
  batches: IngestionBatchSummaryDto[];
}

export interface IngestionRefusalDto {
  rule: string;
  message: string;
}

export interface IngestionRecordDto {
  line: number;
  attempt: number;
  accepted: boolean;
  itemId: number | null;
  refusals: IngestionRefusalDto[];
  recordJson: string | null;
  discarded: boolean;
  note: string | null;
  message?: string;
  rule?: string | null;
}

export interface IngestionRuleGroupDto {
  rule: string;
  records: number;
}

export interface IngestionStagedItemDto {
  itemId: number;
  topicCode: string;
  cefrLevel: string;
  modality: string;
  lifecycleStatus: string;
  versionNumber: number;
}

export interface IngestionBatchDetailDto {
  batch: IngestionBatchSummaryDto;
  records: IngestionRecordDto[];
  ruleGroups: IngestionRuleGroupDto[];
  staged: IngestionStagedItemDto[];
}

export interface IngestionUploadResultDto {
  batchId: number;
  sourceName: string;
  skill: string;
  status: string;
  accepted: number;
  rejected: number;
  itemsWritten: number;
}

export interface AdminImportResultDto {
  batchId: number;
  status: string;
  examId: number | null;
  examName: string | null;
  questionCount: number;
  errorCount: number;
  warningCount: number;
}

// --- Micro-Lessons ---

export interface AdminLessonSummaryDto {
  lessonId: number;
  certId: number;
  certCode: string;
  skillId: number;
  skillCode: string;
  skillName: string;
  categoryId: number | null;
  categoryCode: string | null;
  categoryName: string | null;
  title: string;
  summary: string | null;
  estimatedMinutes: number;
  displayOrder: number;
  cardCount: number;
  createdAt: string;
  topicId?: number;
  topicCode?: string;
  topicName?: string;
  cefrLevel?: string;
}

export interface LessonCardDto {
  cardId: number;
  cardType: string;
  title: string;
  contentMarkdown: string;
  questionPrompt: string | null;
  optionA: string | null;
  optionB: string | null;
  optionC: string | null;
  optionD: string | null;
  correctOption: string | null;
  explanation: string | null;
  displayOrder: number;
  rationale?: string | null;
  module: string;
}

export interface AdminLessonDetailDto {
  lessonId: number;
  certId: number;
  certCode: string;
  skillId: number;
  skillCode: string;
  categoryId: number | null;
  categoryCode: string | null;
  title: string;
  summary: string | null;
  estimatedMinutes: number;
  displayOrder: number;
  createdAt: string;
  cards: LessonCardDto[];
  modality?: string;
  topicId?: number;
  topicCode?: string;
  cefrLevel?: string;
}

export interface AdminLessonSkillOptionDto {
  skillId: number;
  certId: number;
  code: string;
  name: string;
  modality: string;
}

export interface AdminLessonCategoryOptionDto {
  categoryId: number;
  certId: number;
  code: string;
  name: string;
}

export interface AdminLessonTopicOptionDto {
  topicId: number;
  modality: string;
  code: string;
  name: string;
}

export interface AdminLessonFormOptionsDto {
  certificates: AdminCertificateOptionDto[];
  skills: AdminLessonSkillOptionDto[];
  categories: AdminLessonCategoryOptionDto[];
  topics?: AdminLessonTopicOptionDto[] | null;
}

export interface AdminLessonCardUpsertDto {
  cardId?: number | null;
  cardType: string;
  title: string;
  contentMarkdown: string;
  questionPrompt?: string | null;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  correctOption?: string | null;
  explanation?: string | null;
  rationale?: string | null;
  displayOrder?: number;
  module?: string;
}

export interface AdminLessonUpsertDto {
  certId: number;
  skillId: number;
  categoryId?: number | null;
  title: string;
  summary?: string | null;
  estimatedMinutes: number;
  displayOrder: number;
  cards: AdminLessonCardUpsertDto[];
  modality?: string;
  topicId?: number;
  cefrLevel?: string;
}

export interface AdminLessonImportResultDto {
  createdCount: number;
  updatedCount: number;
  totalCardsImported: number;
  messages: string[];
}

export interface LessonManifestCardDto {
  cardType: string;
  title: string;
  contentMarkdown: string;
  questionPrompt?: string | null;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  correctOption?: string | null;
  explanation?: string | null;
  rationale?: string | null;
  module?: string;
}

export interface LessonManifestItemDto {
  certCode: string;
  skillCode: string;
  categoryCode?: string | null;
  title: string;
  summary?: string | null;
  estimatedMinutes: number;
  displayOrder: number;
  modality?: string;
  topicCode?: string;
  cefrLevel?: string;
  cards: LessonManifestCardDto[];
}

export interface LessonManifestDto {
  lessons: LessonManifestItemDto[];
}
