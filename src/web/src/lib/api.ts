/**
 * Typed API Client for Extra Efficiency (`src/WebAPI` via same-origin `/api` proxy).
 * Authenticates via the `ee_access_token` HttpOnly cookie (ADR-0015).
 */

export class ApiClientError extends Error {
  public readonly status: number;
  public readonly code: string;
  public readonly title: string;
  public readonly detail?: string;

  constructor(status: number, code: string, title: string, detail?: string) {
    super(detail || title || `API error ${status} (${code})`);
    this.name = 'ApiClientError';
    this.status = status;
    this.code = code;
    this.title = title;
    this.detail = detail;
  }
}

let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  const headers = new Headers(options.headers);
  if (
    options.body &&
    !(options.body instanceof FormData) &&
    !headers.has('Content-Type')
  ) {
    headers.set('Content-Type', 'application/json');
  }
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  const response = await fetch(normalizedPath, {
    ...options,
    headers,
    credentials: 'include',
  });

  if (!response.ok) {
    let code = 'unexpected_error';
    let title = response.statusText || 'Request failed';
    let detail: string | undefined;

    try {
      const problem = await response.json();
      if (problem && typeof problem === 'object') {
        code = problem.code || problem.error || code;
        title = problem.title || problem.message || title;
        detail = problem.detail || problem.message;
      }
    } catch {
      // Response body was not JSON
    }

    if (
      response.status === 401 &&
      unauthorizedHandler &&
      !normalizedPath.startsWith('/api/auth/')
    ) {
      unauthorizedHandler();
    }

    throw new ApiClientError(response.status, code, title, detail);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

// ============================================================================
// Shared DTO Types
// ============================================================================

export interface AuthUserDto {
  userId: number;
  email: string;
  fullName: string;
  role: string;
  emailVerified: boolean;
  permissions?: string[];
  held?: string[];
}

export interface AuthTokenResponse {
  token: string;
  expiresAt: string;
  user: AuthUserDto;
}

export interface CertificateOptionDto {
  code: string;
  name: string;
  sectionNoun?: string;
}

export interface CertificateSectionDto {
  code: string;
  name: string;
  expectedQuestionCount: number;
  sectionId?: number;
  navigationMode?: string;
  scoreWeight?: number;
  displayOrder?: number;
}

export interface CertificateSkillDto {
  code: string;
  name: string;
  modality: 'listening' | 'reading' | 'speaking' | 'writing' | string;
  durationMinutes?: number | null;
  sections: CertificateSectionDto[];
  skillId?: number;
  displayOrder?: number;
}

export interface CertificateBandDto {
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
}

export interface CertificateDto {
  code: string;
  name: string;
  description?: string | null;
  skills: CertificateSkillDto[];
  bands: CertificateBandDto[];
  certId?: number;
  scoreAggregationRule?: string;
  presetCode?: string | null;
  isActive?: boolean;
}

export interface PackageDto {
  packageId: number;
  name: string;
  credits: number;
  price: number;
  isActive: boolean;
  isFeatured: boolean;
  features?: string | null;
  displayOrder: number;
  ordersSold?: number;
  totalRevenue?: number;
  featureList?: string[];
}

export interface LearnerContextDto {
  active: CertificateOptionDto;
  certificates?: CertificateOptionDto[];
  available?: CertificateOptionDto[];
  creditBalance: number;
  examinedSkills?: string[] | null;
  examDate?: string | null;
}

export interface NextActionDto {
  eyebrow: string;
  title: string;
  description: string;
  actionLabel: string;
  actionUrl: string;
  severity: 'info' | 'warning' | 'danger' | 'success' | string;
  cascadeStep: string;
  metaLabel?: string | null;
}

export interface GoalStandingDto {
  currentBandCode?: string | null;
  currentBandName?: string | null;
  currentScore?: number | null;
  currentSource?: string | null;
  targetBandCode?: string | null;
  targetBandName?: string | null;
  targetMinScore?: number | null;
  targetDate?: string | null;
  weeklyStudyHours?: number | null;
  status?: 'feasible' | 'stretch' | 'unrealistic' | string | null;
  completedPathSteps: number;
  totalPathSteps: number;
  currentWeekNumber?: number | null;
  weekPlannedMinutes: number;
  weekCompletedMinutes: number;
  scoreDisplayFactor: number;
  requiredTotalHours?: number | null;
  overallLevelCode?: string | null;
  limitingSkillCode?: string | null;
  limitingSkillName?: string | null;
  hasNoMeasurement: boolean;
}

export interface SkillCategoryDto {
  code: string;
  name: string;
  accuracyPercent: number;
  answered: number;
  levelCode: string;
}

export interface SkillCardDto {
  skillId: number;
  code: string;
  name: string;
  modality: string;
  latestScaledScore?: number | null;
  maxScaledScore?: number | null;
  weakestTopics: SkillCategoryDto[];
  scoreDisplayFactor: number;
  levelCode?: string | null;
  positionInBand?: number | null;
  measuredAt?: string | null;
  isStale: boolean;
}

export interface RecentAttemptDto {
  attemptId: number;
  examTitle: string;
  status: string;
  startedAt: string;
  submittedAt?: string | null;
  totalScaledScore?: number | null;
  bandCode?: string | null;
  bandName?: string | null;
}

export interface RecentPracticeDto {
  sessionId: number;
  skillName: string;
  status: string;
  questionCount: number;
  correctCount: number;
  startedAt: string;
  completedAt?: string | null;
}

export interface LearnerDashboardDto {
  certificate: CertificateOptionDto;
  nextAction: NextActionDto;
  goal: GoalStandingDto;
  skills: SkillCardDto[];
  attemptCount: number;
  practiceCount: number;
  unfinishedAttempt?: RecentAttemptDto | null;
  unfinishedPractice?: RecentPracticeDto | null;
}

export interface CefrBandInfoDto {
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  isReached: boolean;
  isCurrent: boolean;
  isTarget?: boolean;
}

export interface TargetBandOptionDto {
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  description?: string | null;
  isCurrent: boolean;
  isTarget: boolean;
}

export interface GoalStatusDto {
  currentBandCode?: string | null;
  targetBandCode: string;
  targetDate?: string | null;
  weeklyStudyHours?: number | null;
  requiredHours?: number | null;
  availableHours?: number | null;
  status?: 'feasible' | 'stretch' | 'unrealistic' | string | null;
  suggestedWeeklyHours?: number | null;
  earliestFeasibleDate?: string | null;
}

export interface TargetBandViewDto {
  activeCert: CertificateOptionDto;
  currentBandCode?: string | null;
  currentTotalScore?: number | null;
  currentBandName?: string | null;
  targetBandCode?: string | null;
  cefrBands: CefrBandInfoDto[];
  availableBands: TargetBandOptionDto[];
  goal?: GoalStatusDto | null;
}

export interface SetTargetBandResultDto {
  userId: number;
  certId: number;
  targetBandCode: string;
  setAt: string;
}

export interface SetGoalRequest {
  currentBandCode?: string | null;
  targetBandCode: string;
  targetDate?: string | null;
  weeklyStudyHours?: number | null;
}

export interface SetGoalResultDto {
  userId: number;
  certId: number;
  targetBandCode: string;
  setAt: string;
  goal: GoalStatusDto;
}

export interface CalibrationProposalDto {
  proposedBandCode: string;
  proposedBandName: string;
  observedAccuracyPct: number;
  validAnswerCount: number;
  requiredAccuracyPct: number;
  evidence: string;
}

export interface CalibrationViewDto {
  placedBandCode: string;
  placedBandName?: string | null;
  validAnswerCount: number;
  requiredValidAnswerCount: number;
  excludedRushedCount: number;
  observedAccuracyPct?: number | null;
  reason: string;
  proposal?: CalibrationProposalDto | null;
}

export interface CalibrationDecisionResultDto {
  decision: string;
  placedBandCode: string;
  proposedBandCode: string;
  pathAdjusted: boolean;
}

export interface PathStepDto {
  stepId: number;
  weekNumber: number;
  weekSequence: number;
  stepType: 'lesson' | 'practice' | 'mock_exam' | string;
  route: 'item_selector' | 'lesson_reader' | 'attempt_engine' | string;
  title: string;
  status: 'locked' | 'available' | 'in_progress' | 'done' | string;
  skillCode: string;
  skillName: string;
  modality: string;
  topicCode: string;
  topicName: string;
  targetCefr: string;
  sizingTier: 'mastered' | 'developing' | 'low' | string;
  plannedItemCount?: number | null;
  estimatedMinutes?: number | null;
  score?: number | null;
  actualAccuracyPct?: number | null;
  targetAccuracyPct: number;
  canStart: boolean;
  startedAt?: string | null;
  completedAt?: string | null;
  refLessonId?: number | null;
  refExamId?: number | null;
}

export interface LearningPathWeekSummaryDto {
  weekNumber: number;
  plannedSteps: number;
  completedSteps: number;
  plannedMinutes: number;
  completedMinutes: number;
  isOnTrack: boolean;
  targetAccuracyPct: number;
  actualAccuracyPct?: number | null;
  steps: PathStepDto[];
}

export interface LearningPathSummaryDto {
  hasActivePath: boolean;
  activeCert: CertificateOptionDto;
  learnerPathId?: number | null;
  fromBandCode?: string | null;
  toBandCode?: string | null;
  fromBandName?: string | null;
  toBandName?: string | null;
  currentBandCode?: string | null;
  currentScore?: number | null;
  targetBandCode?: string | null;
  targetScore?: number | null;
  status?: 'active' | 'completed' | 'reset' | string | null;
  startedAt?: string | null;
  totalSteps: number;
  completedSteps: number;
  totalEstimatedMinutes: number;
  remainingMinutes: number;
  cefrBands: CefrBandInfoDto[];
  openSteps: PathStepDto[];
  nextStepId?: number | null;
  weeks?: LearningPathWeekSummaryDto[] | null;
  skillLevels?: Record<string, string> | null;
}

export interface StartStepResultDto {
  stepId: number;
  stepType: string;
  route: 'item_selector' | 'lesson_reader' | 'attempt_engine' | string;
  redirectUrl: string;
  sessionId?: number | null;
  attemptId?: number | null;
  substitutedFromTopicId?: number | null;
}

export interface ResetLearningPathResultDto {
  resetCount: number;
  newPathId?: number | null;
  summary?: LearningPathSummaryDto | null;
}

export interface LessonCardDto {
  cardId: number;
  cardType: 'rule' | 'pitfall' | 'example' | 'micro_check' | string;
  title: string;
  contentMarkdown: string;
  questionPrompt?: string | null;
  optionA?: string | null;
  optionB?: string | null;
  optionC?: string | null;
  optionD?: string | null;
  correctOption?: string | null;
  explanation?: string | null;
  displayOrder: number;
  rationale?: string | null;
  module: 'primary' | 'remedial' | string;
}

export interface LessonDetailDto {
  lessonId: number;
  title: string;
  summary?: string | null;
  skillId: number;
  categoryId?: number | null;
  estimatedMinutes: number;
  cards: LessonCardDto[];
  topicId: number;
  topicCode: string;
  topicName: string;
  cefrLevel: string;
  module: 'primary' | 'remedial' | string;
  hasRemedialModule: boolean;
}

export interface LessonProgressDto {
  cardsCompleted: number;
  totalCards: number;
  isCompleted: boolean;
  lastViewedCardId?: number | null;
  firstOpenedAt?: string | null;
  completedAt?: string | null;
  updatedAt?: string | null;
  resumeCardIndex: number;
  module: 'primary' | 'remedial' | string;
}

export interface MicroCheckResultDto {
  isCorrect: boolean;
  correctOption: string;
  explanation?: string | null;
  categoryId?: number | null;
}

export interface SectionAccuracyDto {
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  skillCode: string;
  skillName: string;
  answeredCount: number;
  correctCount: number;
  accuracyPercentage: number;
}

export interface TopicAccuracyDto {
  categoryId: number;
  topicCode: string;
  topicName: string;
  skillCode: string;
  skillName: string;
  answeredCount: number;
  correctCount: number;
  accuracyPercentage: number;
  levelCode?: string | null;
  meetsFloor: boolean;
  isWeakest: boolean;
}

export interface RecommendationItemDto {
  recId: number;
  text: string;
  severity: 'major' | 'minor' | 'info' | string;
  targetBandCode?: string | null;
  skillName?: string | null;
  createdAt: string;
  isResolved: boolean;
}

export interface ResolveRecommendationResultDto {
  recId: number;
  isResolved: boolean;
}

export interface WeaknessAnalysisDto {
  certificate: CertificateOptionDto;
  hasData: boolean;
  gradedExamCount: number;
  completedPracticeCount: number;
  totalAnswered: number;
  totalCorrect: number;
  overallAccuracyPercentage: number;
  sections: SectionAccuracyDto[];
  frequentlyMissedTopics: TopicAccuracyDto[];
  recommendations: RecommendationItemDto[];
  priorityPracticeSection?: SectionAccuracyDto | null;
  topics: TopicAccuracyDto[];
  weakestTopic?: TopicAccuracyDto | null;
}

export interface RecommendationDto {
  recId: number;
  insightId?: number | null;
  practiceSessionId?: number | null;
  attemptId?: number | null;
  text: string;
  severity: 'major' | 'minor' | 'info' | string;
  targetBandCode?: string | null;
  skillName?: string | null;
  isResolved: boolean;
  createdAt: string;
}

export interface AiInsightDto {
  insightId: number;
  userId: number;
  certId: number;
  scope: 'dashboard' | 'path' | 'practice' | 'attempt' | string;
  practiceSessionId?: number | null;
  attemptId?: number | null;
  narrative?: string | null;
  trend?: 'improving' | 'flat' | 'declining' | string | null;
  status: 'pending' | 'done' | 'failed' | string;
  failureReason?: string | null;
  model?: string | null;
  promptTokens?: number | null;
  completionTokens?: number | null;
  sourceFingerprint: string;
  createdAt: string;
  completedAt?: string | null;
  recommendations: RecommendationDto[];
  learnerPathId?: number | null;
  formattedNarrative?: string | null;
}

export interface AttemptOptionDto {
  optionId: number;
  label: string;
  text: string;
}

export interface AttemptQuestionDto {
  questionId: number;
  displayOrder: number;
  sectionId: number;
  skillId: number;
  sectionCode: string;
  sectionName: string;
  skillCode: string;
  skillName: string;
  modality: 'Listening' | 'Reading' | 'Speaking' | 'Writing' | string;
  stem: string;
  passage?: string | null;
  imagePath?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  groupId?: number | null;
  questionType?: string | null;
  options: AttemptOptionDto[];
  selectedOptionId?: number | null;
  isFlagged: boolean;
  responseText?: string | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  allowReplay?: boolean;
  maxReplays?: number | null;
  hideOptionText?: boolean;
  optionCount?: number;
  hasSpeakingAudio?: boolean;
  speakingAudioPath?: string | null;
}

export interface NavigatorItemDto {
  questionId: number;
  displayOrder: number;
  sectionId: number;
  sectionCode: string;
  modality: string;
  isAnswered: boolean;
  isFlagged: boolean;
  isCurrent: boolean;
  isLocked?: boolean;
}

export interface AttemptPhaseDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  displayOrder: number;
  startOffsetSeconds: number;
  durationSeconds: number;
  lockNavigationOneWay?: boolean;
}

export interface PlacementTakeViewDto {
  placementId: number;
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  status: string;
  startedAt: string;
  expiresAt: string;
  remainingSeconds: number;
  totalQuestions: number;
  answeredCount: number;
  flaggedCount: number;
  currentQuestion: AttemptQuestionDto | null;
  navigator: NavigatorItemDto[];
  isAutoSubmitted: boolean;
  questions: AttemptQuestionDto[];
  phases?: AttemptPhaseDto[];
  activeSkillId?: number | null;
  activeUnitNumber?: number;
  activeGroupId?: number | null;
  phaseDeadlineAt?: string | null;
  navigationMode?: string | null;
}

export interface AttemptTakeDto {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  status: string;
  startedAt: string;
  expiresAt: string;
  remainingSeconds: number;
  totalQuestions: number;
  answeredCount: number;
  flaggedCount: number;
  currentQuestion: AttemptQuestionDto | null;
  navigator: NavigatorItemDto[];
  isAutoSubmitted: boolean;
  questions: AttemptQuestionDto[];
  phases: AttemptPhaseDto[];
  activeSkillId?: number | null;
  activeUnitNumber: number;
  activeGroupId?: number | null;
  phaseDeadlineAt?: string | null;
  authorizationCode?: string | null;
  navigationMode?: string | null;
}

export interface SaveAnswerResultDto {
  questionId: number;
  selectedOptionId?: number | null;
  isFlagged: boolean;
  remainingSeconds: number;
  isAutoSubmitted: boolean;
}

export interface PlacementResumeSkillDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  isDone: boolean;
  isActive: boolean;
  answeredCount: number;
  totalQuestions: number;
}

export interface PlacementResumeViewDto {
  placementId: number;
  attemptId: number;
  examName: string;
  certCode: string;
  startedAt: string;
  expiresAt: string;
  skills: PlacementResumeSkillDto[];
  activeSkillId?: number | null;
  activeSkillName?: string | null;
  activeStage: number;
  totalStages: number;
  syncedAnswerCount: number;
  totalQuestions: number;
  lastQuestionId?: number | null;
  lastQuestionDisplayOrder?: number | null;
}

export interface PlacementSkillResultInfoDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  status: 'measured' | 'grading' | 'not_measured' | string;
  level?: string | null;
  scaledScore?: number | null;
  measuredAt?: string | null;
  reusedFromPrior: boolean;
  failureReason?: string | null;
}

export interface PlacementResultViewDto {
  placementId: number;
  attemptId: number;
  certId: number;
  certCode: string;
  examName: string;
  takenAt: string;
  durationMinutes: number;
  totalQuestions: number;
  totalScore: number;
  resultBandCode?: string | null;
  resultBandName?: string | null;
  cefrBands: Array<{
    bandId: number;
    code: string;
    name: string;
    minTotal: number;
    maxTotal: number;
    isCurrent: boolean;
    isReached: boolean;
  }>;
  skillScores: Array<{
    skillId: number;
    skillCode: string;
    skillName: string;
    rawScore: number;
    maxQuestions: number;
    scaledScore: number;
    maxScaledScore: number;
  }>;
  weakerSkillName?: string | null;
  bandNote?: string | null;
  isGraded: boolean;
  skillResults?: PlacementSkillResultInfoDto[];
}

export interface PreflightStatusDto {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  certName: string;
  durationMinutes: number;
  requiresListening: boolean;
  requiresSpeaking: boolean;
  requiresPreflight: boolean;
  isVerified: boolean;
  verifiedAt?: string | null;
  sampleAudioUrl?: string | null;
  rules: string[];
}

export interface PlacementPreflightScopeOfferDto {
  scope: string;
  title: string;
  includedSkills: string[];
  excludedSkills: string[];
  creditCost: number;
  isFree: boolean;
  canAfford: boolean;
}

export interface PlacementPreflightViewDto {
  certId: number;
  certCode: string;
  certName: string;
  examId: number;
  examName: string;
  isRetake: boolean;
  isInCooldown: boolean;
  cooldownEndsAt?: string | null;
  creditBalance: number;
  validPriorSkills: Array<{
    skillCode: string;
    skillName: string;
    level: string;
    measuredAt: string;
  }>;
  offers: PlacementPreflightScopeOfferDto[];
}

// ============================================================================
// Domain API Surface
// ============================================================================

export const api = {
  auth: {
    me: () => apiFetch<AuthUserDto>('/api/auth/me'),
    signIn: (body: { email: string; password: string }) =>
      apiFetch<AuthTokenResponse>('/api/auth/sign-in', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    demoSignIn: () =>
      apiFetch<AuthTokenResponse>('/api/auth/demo-sign-in', {
        method: 'POST',
      }),
    register: (body: {
      fullName: string;
      email: string;
      phone?: string;
      password: string;
      termsAccepted: boolean;
    }) =>
      apiFetch<AuthTokenResponse>('/api/auth/register', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    signOut: () =>
      apiFetch<void>('/api/auth/sign-out', {
        method: 'POST',
      }),
    verifyEmail: (token: string) =>
      apiFetch<void>('/api/auth/verify-email', {
        method: 'POST',
        body: JSON.stringify({ token }),
      }),
    resendActivation: () =>
      apiFetch<void>('/api/auth/resend-activation', {
        method: 'POST',
      }),
    forgotPassword: (email: string) =>
      apiFetch<void>('/api/auth/forgot-password', {
        method: 'POST',
        body: JSON.stringify({ email }),
      }),
    resetPassword: (body: { token: string; newPassword: string }) =>
      apiFetch<void>('/api/auth/reset-password', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  catalog: {
    certificates: () => apiFetch<CertificateDto[]>('/api/certificates'),
    packages: () => apiFetch<PackageDto[]>('/api/packages'),
  },

  learner: {
    context: () => apiFetch<LearnerContextDto>('/api/learner/context'),
    switchCertificate: (code: string) =>
      apiFetch<void>('/api/learner/active-certificate', {
        method: 'PUT',
        body: JSON.stringify({ code }),
      }),
    dashboard: () => apiFetch<LearnerDashboardDto>('/api/learner/dashboard'),
    profile: () => apiFetch<any>('/api/profile'),
    updateProfile: (body: { fullName: string; phone?: string | null; timeZoneId: string }) =>
      apiFetch<any>('/api/profile', {
        method: 'PUT',
        body: JSON.stringify(body),
      }),
    changePassword: (body: { currentPassword: string; newPassword: string }) =>
      apiFetch<void>('/api/profile/password', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  placement: {
    catalog: () => apiFetch<any>('/api/learner/placement'),
    preflightGate: () => apiFetch<PlacementPreflightViewDto>('/api/learner/placement/preflight'),
    start: (body: {
      examId: number;
      reuseValidSkills?: boolean;
      scope?: string;
      idempotencyKey?: string;
    }) =>
      apiFetch<{ placementId: number; attemptId: number }>('/api/learner/placement/start', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    preflightStatus: (placementId: number) =>
      apiFetch<PreflightStatusDto>(`/api/learner/placement/take/${placementId}/preflight`),
    take: (placementId: number, q?: number | null) =>
      apiFetch<PlacementTakeViewDto>(
        q != null
          ? `/api/learner/placement/take/${placementId}?q=${q}`
          : `/api/learner/placement/take/${placementId}`,
      ),
    saveAnswer: (
      placementId: number,
      questionId: number,
      body: { selectedOptionId?: number | null; isFlagged: boolean; responseText?: string | null },
    ) =>
      apiFetch<SaveAnswerResultDto>(
        `/api/learner/placement/take/${placementId}/answers/${questionId}`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      ),
    saveDraft: (
      placementId: number,
      body: { questionId: number; responseText: string },
    ) =>
      apiFetch<{ savedAt: string }>(`/api/learner/placement/take/${placementId}/draft`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    submit: (placementId: number, isAutoSubmit = false) =>
      apiFetch<PlacementResultViewDto>(`/api/learner/placement/take/${placementId}/submit`, {
        method: 'POST',
        body: JSON.stringify({ isAutoSubmit }),
      }),
    resumeInfo: (placementId: number) =>
      apiFetch<PlacementResumeViewDto>(`/api/learner/placement/take/${placementId}/resume`),
    abandon: (placementId: number) =>
      apiFetch<void>(`/api/learner/placement/take/${placementId}/abandon`, {
        method: 'POST',
      }),
    recordInterruption: (
      placementId: number,
      body: { questionId: number; sessionToken: string; reason?: string },
    ) =>
      apiFetch<{ interruptions: number; outcome: string; replacementQuestionId?: number | null }>(
        `/api/learner/placement/take/${placementId}/recording-interruptions`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      ),
    result: (placementId: number) =>
      apiFetch<PlacementResultViewDto>(`/api/learner/placement/result/${placementId}`),
    gradingStatus: (placementId: number) =>
      apiFetch<any>(`/api/learner/placement/result/${placementId}/status`),
    retryGrading: (placementId: number) =>
      apiFetch<{ requeuedCount: number }>(`/api/learner/placement/result/${placementId}/retry`, {
        method: 'POST',
      }),
    targetBand: () => apiFetch<TargetBandViewDto>('/api/learner/target-band'),
    setTargetBand: (targetBandCode: string) =>
      apiFetch<SetTargetBandResultDto>('/api/learner/target-band', {
        method: 'POST',
        body: JSON.stringify({ targetBandCode }),
      }),
    setGoal: (body: SetGoalRequest) =>
      apiFetch<SetGoalResultDto>('/api/learner/goal', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    calibration: () => apiFetch<CalibrationViewDto>('/api/learner/placement/calibration'),
    decideCalibration: (adjust: boolean) =>
      apiFetch<CalibrationDecisionResultDto>('/api/learner/placement/calibration/decision', {
        method: 'POST',
        body: JSON.stringify({ adjust }),
      }),
  },

  exams: {
    list: () => apiFetch<any>('/api/learner/exams'),
    instructions: (examId: number) =>
      apiFetch<any>(`/api/learner/exams/${examId}/instructions`),
    start: (examId: number) =>
      apiFetch<any>(`/api/learner/exams/${examId}/start`, { method: 'POST' }),
    preflightStatus: (attemptId: number) =>
      apiFetch<PreflightStatusDto>(`/api/learner/attempts/${attemptId}/preflight`),
    verifyPreflight: (attemptId: number) =>
      apiFetch<PreflightStatusDto>(`/api/learner/attempts/${attemptId}/preflight/verify`, {
        method: 'POST',
      }),
    take: (attemptId: number, q?: number | null) =>
      apiFetch<AttemptTakeDto>(
        q != null ? `/api/learner/attempts/${attemptId}?q=${q}` : `/api/learner/attempts/${attemptId}`,
      ),
    saveAnswer: (
      attemptId: number,
      questionId: number,
      body: { selectedOptionId?: number | null; isFlagged: boolean; responseText?: string | null },
    ) =>
      apiFetch<SaveAnswerResultDto>(`/api/learner/attempts/${attemptId}/answers/${questionId}`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    saveDraft: (attemptId: number, body: { questionId: number; responseText: string }) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/draft`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    sync: (attemptId: number, body: any) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/sync`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    transitionSkill: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/transition/skill`, {
        method: 'POST',
      }),
    transitionUnit: (attemptId: number, targetUnitNumber: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/transition/unit`, {
        method: 'POST',
        body: JSON.stringify({ targetUnitNumber }),
      }),
    transitionGroup: (attemptId: number, targetGroupId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/transition/group`, {
        method: 'POST',
        body: JSON.stringify({ targetGroupId }),
      }),
    submit: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/submit`, { method: 'POST' }),
    cancel: (attemptId: number) =>
      apiFetch<void>(`/api/learner/attempts/${attemptId}/cancel`, { method: 'POST' }),
    resumeByCode: (authorizationCode: string) =>
      apiFetch<{ attemptId: number }>('/api/learner/attempts/resume', {
        method: 'POST',
        body: JSON.stringify({ authorizationCode }),
      }),
    result: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/result`),
    projection: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/projection`),
    syncProjection: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/projection/sync`, {
        method: 'POST',
      }),
    review: (attemptId: number) =>
      apiFetch<any>(`/api/learner/attempts/${attemptId}/review`),
    history: () => apiFetch<any>('/api/learner/exams/history'),
    insight: (attemptId: number) =>
      apiFetch<any>(`/api/learner/insights/exam/${attemptId}`),
    retryInsight: (attemptId: number) =>
      apiFetch<void>(`/api/learner/insights/exam/${attemptId}/retry`, { method: 'POST' }),
  },

  speaking: {
    catalog: () => apiFetch<any>('/api/learner/skill-practice/speaking'),
    instructions: (examId: number) =>
      apiFetch<any>(`/api/learner/speaking/${examId}/instructions`),
    start: (examId: number) =>
      apiFetch<{ attemptId: number }>(`/api/learner/speaking/${examId}/start`, {
        method: 'POST',
      }),
    take: (attemptId: number, questionId?: number | null) =>
      apiFetch<any>(
        questionId != null
          ? `/api/learner/speaking/attempts/${attemptId}?questionId=${questionId}`
          : `/api/learner/speaking/attempts/${attemptId}`,
      ),
    uploadAudio: (
      attemptId: number,
      questionId: number,
      blob: Blob,
      fileName: string,
      durationMs?: number,
    ) => {
      const form = new FormData();
      form.append('audioFile', blob, fileName);
      if (durationMs != null) {
        form.append('durationMs', String(Math.round(durationMs)));
      }
      return apiFetch<any>(
        `/api/learner/speaking/attempts/${attemptId}/questions/${questionId}/audio`,
        {
          method: 'POST',
          body: form,
        },
      );
    },
    submit: (attemptId: number) =>
      apiFetch<any>(`/api/learner/speaking/attempts/${attemptId}/submit`, {
        method: 'POST',
      }),
    pending: (attemptId: number) =>
      apiFetch<any>(`/api/learner/speaking/attempts/${attemptId}/pending`),
    retry: (attemptId: number) =>
      apiFetch<void>(`/api/learner/speaking/attempts/${attemptId}/retry`, {
        method: 'POST',
      }),
    review: (attemptId: number) =>
      apiFetch<any>(`/api/learner/speaking/attempts/${attemptId}/review`),
  },

  writing: {
    catalog: () => apiFetch<any>('/api/learner/skill-practice/writing'),
    instructions: (examId: number) =>
      apiFetch<any>(`/api/learner/writing/${examId}/instructions`),
    start: (examId: number) =>
      apiFetch<{ attemptId: number }>(`/api/learner/writing/${examId}/start`, {
        method: 'POST',
      }),
    take: (attemptId: number, questionId?: number | null) =>
      apiFetch<any>(
        questionId != null
          ? `/api/learner/writing/attempts/${attemptId}?questionId=${questionId}`
          : `/api/learner/writing/attempts/${attemptId}`,
      ),
    saveDraft: (attemptId: number, body: { questionId: number; responseText: string }) =>
      apiFetch<any>(`/api/learner/writing/attempts/${attemptId}/draft`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    submit: (attemptId: number) =>
      apiFetch<any>(`/api/learner/writing/attempts/${attemptId}/submit`, {
        method: 'POST',
      }),
    pending: (attemptId: number) =>
      apiFetch<any>(`/api/learner/writing/attempts/${attemptId}/pending`),
    retry: (attemptId: number) =>
      apiFetch<void>(`/api/learner/writing/attempts/${attemptId}/retry`, {
        method: 'POST',
      }),
    review: (attemptId: number) =>
      apiFetch<any>(`/api/learner/writing/attempts/${attemptId}/review`),
  },

  practice: {
    catalog: () => apiFetch<any>('/api/learner/practice/catalog'),
    matrix: () => apiFetch<any[]>('/api/learner/practice/matrix'),
    freeDrill: (topicId?: number, cefrLevelCode?: string) => {
      const params = new URLSearchParams();
      if (topicId != null) params.set('topicId', String(topicId));
      if (cefrLevelCode) params.set('cefrLevelCode', cefrLevelCode);
      const q = params.toString();
      return apiFetch<any>(`/api/learner/practice/free-drill${q ? `?${q}` : ''}`);
    },
    startSession: (body: {
      sectionId?: number | null;
      questionCount?: number;
      topicId?: number | null;
      cefrLevelCode?: string | null;
      skillId?: number | null;
      learnerPathStepId?: number | null;
    }) =>
      apiFetch<any>('/api/learner/practice/sessions', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    sessionTake: (sessionId: number, q?: number | null) =>
      apiFetch<any>(
        q != null
          ? `/api/learner/practice/sessions/${sessionId}/take?q=${q}`
          : `/api/learner/practice/sessions/${sessionId}/take`,
      ),
    revealHint: (sessionId: number, questionId: number) =>
      apiFetch<any>(`/api/learner/practice/sessions/${sessionId}/questions/${questionId}/hint`, {
        method: 'POST',
      }),
    submitAnswer: (
      sessionId: number,
      questionId: number,
      body: { selectedOptionId?: number | null; responseText?: string | null },
    ) =>
      apiFetch<any>(`/api/learner/practice/sessions/${sessionId}/questions/${questionId}/answer`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    summary: (sessionId: number) =>
      apiFetch<any>(`/api/learner/practice/sessions/${sessionId}/summary`),
    savedQuestions: (params?: { source?: string; sectionId?: number; categoryId?: number }) => {
      const sp = new URLSearchParams();
      if (params?.source) sp.set('source', params.source);
      if (params?.sectionId != null) sp.set('sectionId', String(params.sectionId));
      if (params?.categoryId != null) sp.set('categoryId', String(params.categoryId));
      const q = sp.toString();
      return apiFetch<any>(`/api/learner/saved-questions${q ? `?${q}` : ''}`);
    },
    saveQuestion: (questionId: number, source = 'manual') =>
      apiFetch<any>(`/api/learner/saved-questions/questions/${questionId}`, {
        method: 'POST',
        body: JSON.stringify({ source }),
      }),
    saveItemVersion: (itemVersionId: number, source = 'manual') =>
      apiFetch<any>(`/api/learner/saved-questions/item-versions/${itemVersionId}`, {
        method: 'POST',
        body: JSON.stringify({ source }),
      }),
    unsaveQuestion: (savedQuestionId: number) =>
      apiFetch<any>(`/api/learner/saved-questions/${savedQuestionId}`, {
        method: 'DELETE',
      }),
  },

  learningPath: {
    get: () => apiFetch<LearningPathSummaryDto>('/api/learner/learning-path'),
    step: (stepId: number) => apiFetch<PathStepDto>(`/api/learner/learning-path/steps/${stepId}`),
    generate: (body?: { targetBandCode?: string; archiveExisting?: boolean }) =>
      apiFetch<LearningPathSummaryDto>('/api/learner/learning-path/generate', {
        method: 'POST',
        body: JSON.stringify(body ?? {}),
      }),
    startStep: (stepId: number) =>
      apiFetch<StartStepResultDto>(`/api/learner/learning-path/steps/${stepId}/start`, {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    reset: () =>
      apiFetch<ResetLearningPathResultDto>('/api/learner/learning-path/reset', {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    lesson: (lessonId: number, module = 'primary') =>
      apiFetch<LessonDetailDto>(`/api/learner/lessons/${lessonId}?module=${encodeURIComponent(module)}`),
    lessonByCategory: (categoryId: number, module = 'primary') =>
      apiFetch<LessonDetailDto>(
        `/api/learner/lessons/by-category/${categoryId}?module=${encodeURIComponent(module)}`,
      ),
    lessonForCell: (params: {
      skillId: number;
      topicId: number;
      level: string;
      module?: string;
    }) => {
      const sp = new URLSearchParams({
        skillId: String(params.skillId),
        topicId: String(params.topicId),
        level: params.level,
        module: params.module ?? 'primary',
      });
      return apiFetch<LessonDetailDto>(`/api/learner/lessons/cell?${sp.toString()}`);
    },
    lessonProgress: (lessonId: number, module = 'primary') =>
      apiFetch<LessonProgressDto>(
        `/api/learner/lessons/${lessonId}/progress?module=${encodeURIComponent(module)}`,
      ),
    recordCardView: (lessonId: number, cardId: number) =>
      apiFetch<void>(`/api/learner/lessons/${lessonId}/cards/${cardId}/view`, {
        method: 'POST',
      }),
    submitMicroCheck: (lessonId: number, cardId: number, selectedOption: string) =>
      apiFetch<MicroCheckResultDto>(`/api/learner/lessons/${lessonId}/cards/${cardId}/micro-check`, {
        method: 'POST',
        body: JSON.stringify({ selectedOption }),
      }),
    weaknessAnalysis: () => apiFetch<WeaknessAnalysisDto>('/api/learner/weakness-analysis'),
    resolveRecommendation: (recId: number) =>
      apiFetch<ResolveRecommendationResultDto>(`/api/learner/recommendations/${recId}/resolve`, {
        method: 'POST',
      }),
  },

  insights: {
    dashboard: (certId?: number) =>
      apiFetch<AiInsightDto>(
        certId != null ? `/api/learner/insights/dashboard?certId=${certId}` : '/api/learner/insights/dashboard',
      ),
    retryDashboard: (certId?: number) =>
      apiFetch<void>(
        certId != null
          ? `/api/learner/insights/dashboard/retry?certId=${certId}`
          : '/api/learner/insights/dashboard/retry',
        { method: 'POST' },
      ),
    path: () => apiFetch<AiInsightDto>('/api/learner/insights/path'),
    retryPath: () =>
      apiFetch<void>('/api/learner/insights/path/retry', {
        method: 'POST',
      }),
    retryById: (insightId: number) =>
      apiFetch<void>(`/api/learner/insights/${insightId}/retry`, {
        method: 'POST',
      }),
  },

  finance: {
    wallet: () => apiFetch<any>('/api/learner/wallet'),
    checkoutPreview: (packageId: number) =>
      apiFetch<any>(`/api/checkout/preview?packageId=${packageId}`),
    createOrder: (packageId: number) =>
      apiFetch<any>('/api/checkout/orders', {
        method: 'POST',
        body: JSON.stringify({ packageId }),
      }),
    cancelOrder: (orderCode: string) =>
      apiFetch<void>('/api/checkout/cancel', {
        method: 'POST',
        body: JSON.stringify({ orderCode }),
      }),
    handleReturn: (orderCode: string) =>
      apiFetch<any>('/api/checkout/return', {
        method: 'POST',
        body: JSON.stringify({ orderCode }),
      }),
    paymentResult: (orderCode: string) =>
      apiFetch<any>(`/api/checkout/orders/${encodeURIComponent(orderCode)}/result`),
    mockGatewayAction: (body: { orderCode: string; action: string; errorCode?: string }) =>
      apiFetch<any>('/api/mock-gateway/action', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  mentors: {
    list: (certCode?: string, skill?: string) => {
      const sp = new URLSearchParams();
      if (certCode) sp.set('certCode', certCode);
      if (skill) sp.set('skill', skill);
      const q = sp.toString();
      return apiFetch<any[]>(`/api/mentors${q ? `?${q}` : ''}`);
    },
    detail: (mentorId: number) => apiFetch<any>(`/api/mentors/${mentorId}`),
    book: (
      mentorId: number,
      body: { slotId?: number | null; focusArea?: string | null; examId?: number | null },
    ) =>
      apiFetch<any>(`/api/mentors/${mentorId}/book`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    bookings: () => apiFetch<any[]>('/api/mentors/bookings'),
    bookingDetail: (bookingId: number) =>
      apiFetch<any>(`/api/mentors/bookings/${bookingId}`),
    cancelBooking: (bookingId: number) =>
      apiFetch<any>(`/api/mentors/bookings/${bookingId}/cancel`, {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    rateBooking: (bookingId: number, body: { rating: number; text?: string | null }) =>
      apiFetch<any>(`/api/mentors/bookings/${bookingId}/rate`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  support: {
    complaints: () => apiFetch<any[]>('/api/learner/complaints'),
    eligibleSubjects: () => apiFetch<any>('/api/learner/complaints/eligible-subjects'),
    fileComplaint: (body: any) =>
      apiFetch<any>('/api/learner/complaints', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  admin: {
    dashboard: () => apiFetch<any>('/api/admin/dashboard'),
    dashboardRevision: () => apiFetch<any>('/api/admin/dashboard/revision'),
    ingestionBatches: () => apiFetch<any>('/api/admin/ingestion'),
    ingestionBatch: (batchId: number, rule?: string) =>
      apiFetch<any>(
        rule
          ? `/api/admin/ingestion/${batchId}?rule=${encodeURIComponent(rule)}`
          : `/api/admin/ingestion/${batchId}`,
      ),
    clearWorkbenchItem: (batchId: number, itemId: number) =>
      apiFetch<void>(`/api/admin/ingestion/${batchId}/items/${itemId}/clear`, {
        method: 'POST',
      }),
    discardWorkbenchRecord: (batchId: number, line: number, reason?: string) =>
      apiFetch<void>(`/api/admin/ingestion/${batchId}/records/${line}/discard`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    coverage: () => apiFetch<any>('/api/admin/coverage'),
    exams: (params?: { q?: string; status?: string; certId?: number; purpose?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      if (params?.certId != null) sp.set('certId', String(params.certId));
      if (params?.purpose) sp.set('purpose', params.purpose);
      return apiFetch<any>(`/api/admin/exams?${sp.toString()}`);
    },
    examDetail: (examId: number) => apiFetch<any>(`/api/admin/exams/${examId}`),
    examAudit: (examId: number) => apiFetch<any>(`/api/admin/exams/${examId}/audit`),
    examAssembly: (examId: number) => apiFetch<any>(`/api/admin/exams/${examId}/assembly`),
    examStudio: (examId: number) => apiFetch<any>(`/api/admin/exams/${examId}/studio`),
    publishExam: (examId: number) =>
      apiFetch<any>(`/api/admin/exams/${examId}/publish`, {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    withdrawExam: (examId: number) =>
      apiFetch<any>(`/api/admin/exams/${examId}/withdraw`, {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    createExam: (body: any) =>
      apiFetch<any>('/api/admin/exams', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    certificates: () => apiFetch<any[]>('/api/admin/certificates'),
    certificateBlueprint: (certId: number) =>
      apiFetch<any>(`/api/admin/certificates/${certId}/blueprint`),
    packages: () => apiFetch<any>('/api/admin/packages'),
    createPackage: (body: any) =>
      apiFetch<any>('/api/admin/packages', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    updatePackage: (packageId: number, body: any) =>
      apiFetch<any>(`/api/admin/packages/${packageId}`, {
        method: 'PUT',
        body: JSON.stringify(body),
      }),
    deactivatePackage: (packageId: number) =>
      apiFetch<void>(`/api/admin/packages/${packageId}/deactivate`, {
        method: 'POST',
      }),
    lessons: () => apiFetch<any>('/api/admin/lessons'),
    users: (params?: { q?: string; status?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      return apiFetch<any>(`/api/admin/users?${sp.toString()}`);
    },
    userDetail: (userId: number) => apiFetch<any>(`/api/admin/users/${userId}`),
    lockUser: (userId: number) =>
      apiFetch<any>(`/api/admin/users/${userId}/lock`, { method: 'POST' }),
    unlockUser: (userId: number) =>
      apiFetch<any>(`/api/admin/users/${userId}/unlock`, { method: 'POST' }),
    adjustCredits: (userId: number, body: { amount: number; reason: string }) =>
      apiFetch<any>(`/api/admin/users/${userId}/adjust-credits`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    orders: (params?: { q?: string; status?: string; gateway?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      if (params?.gateway) sp.set('gateway', params.gateway);
      return apiFetch<any>(`/api/admin/orders?${sp.toString()}`);
    },
    reconcileOrder: (orderId: number) =>
      apiFetch<any>(`/api/admin/orders/${orderId}/reconcile`, { method: 'POST' }),
    refunds: (params?: { q?: string; status?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      return apiFetch<any>(`/api/admin/refunds?${sp.toString()}`);
    },
    requestRefund: (body: any) =>
      apiFetch<any>('/api/admin/refunds', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    approveRefund: (refundId: number) =>
      apiFetch<any>(`/api/admin/refunds/${refundId}/approve`, { method: 'POST' }),
    rejectRefund: (refundId: number, reason: string) =>
      apiFetch<any>(`/api/admin/refunds/${refundId}/reject`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    settleRefund: (refundId: number) =>
      apiFetch<any>(`/api/admin/refunds/${refundId}/settle`, { method: 'POST' }),
    complaints: (params?: { q?: string; status?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      return apiFetch<any>(`/api/admin/complaints?${sp.toString()}`);
    },
    resolveComplaint: (complaintId: number, body: { resolution: string; adminNotes: string }) =>
      apiFetch<any>(`/api/admin/complaints/${complaintId}/resolve`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    mentors: () => apiFetch<any[]>('/api/admin/mentors'),
  },
};
