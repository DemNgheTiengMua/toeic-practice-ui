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
  packageName?: string;
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
  certificates: CertificateOptionDto[];
  available?: CertificateOptionDto[];
  creditBalance: number;
  examinedSkills?: ModalityCode[];
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

export interface ProfileDto {
  fullName: string;
  phone?: string | null;
  timeZoneId: string;
}

export interface OrderDto {
  orderId: number;
  orderCode: string;
  paymentNo?: number;
  packageId: number;
  packageName: string;
  credits: number;
  amount: number;
  status: 'pending' | 'reconciling' | 'paid' | 'failed' | 'expired' | string;
  gateway: 'payos' | 'mock' | string;
  gatewayTxnRef?: string | null;
  gatewayErrorCode?: string | null;
  createdAt: string;
  paidAt?: string | null;
  checkoutUrl?: string | null;
}

export interface CreditLedgerEntryDto {
  txnId: number;
  reason: string;
  delta: number;
  note?: string | null;
  createdAt: string;
  orderCode?: string | null;
}

export interface WalletDto {
  balance: number;
  creditsPurchased: number;
  creditsUsed: number;
  amountPurchased: number;
  paidOrderCount: number;
  orders: OrderDto[];
  ledger: CreditLedgerEntryDto[];
}

export interface CheckoutPreviewDto {
  packageId: number;
  packageName: string;
  credits: number;
  price: number;
  currentBalance: number;
  balanceAfter: number;
  openOrder?: OrderDto | null;
}

export interface CreateOrderResponseDto {
  orderId: number;
  orderCode: string;
  paymentNo: number;
  amount: number;
  gateway: string;
  checkoutUrl?: string | null;
  status: string;
}

export interface PaymentResultDto {
  orderId: number;
  orderCode: string;
  status: string;
  packageName: string;
  credits: number;
  amount: number;
  gateway: string;
  createdAt: string;
  paidAt?: string | null;
  currentBalance: number;
  gatewayErrorCode?: string | null;
  message?: string | null;
  checkoutUrl?: string | null;
  packageId: number;
}

export interface MentorSummaryDto {
  mentorId: number;
  userId?: number | null;
  displayName: string;
  bio?: string | null;
  certId: number;
  certCode: string;
  certName: string;
  skillId?: number | null;
  skillCode?: string | null;
  skillName?: string | null;
  modality?: string | null;
  type: 'human' | 'ai' | string;
  pricePerSlot: number;
  priceCredits: number;
  headline?: string | null;
  expertise?: string | null;
  sessionMinutes: number;
  isActive: boolean;
  avgRating?: number | null;
  reviewCount: number;
}

export interface MentorSlotDto {
  slotId: number;
  mentorId: number;
  startAt: string;
  endAt: string;
  status: 'open' | 'booked' | 'closed' | string;
}

export interface MentorReviewDto {
  reviewId: number;
  rating: number;
  text?: string | null;
  userDisplayName: string;
  createdAt: string;
}

export interface MentorDetailDto {
  mentor: MentorSummaryDto;
  availableSlots: MentorSlotDto[];
  reviews: MentorReviewDto[];
}

export interface MentorSkillStandingDto {
  skillId?: number | null;
  modality: string;
  skillCode?: string | null;
  skillName?: string | null;
  status: 'measured' | 'not_measured' | 'not_examined' | string;
  levelCode?: string | null;
  positionInBand?: number | null;
  isStale: boolean;
}

export interface MentorLearnerStandingDto {
  certId: number;
  certCode: string;
  hasNoMeasurement: boolean;
  overallLevelCode?: string | null;
  limitingSkillId?: number | null;
  limitingSkillCode?: string | null;
  skills: MentorSkillStandingDto[];
}

export interface MentorBookingDto {
  bookingId: number;
  userId: number;
  mentorId: number;
  mentorName: string;
  mentorHeadline?: string | null;
  mentorType: 'human' | 'ai' | string;
  slotId?: number | null;
  startAt?: string | null;
  endAt?: string | null;
  status: 'pending' | 'confirmed' | 'done' | 'cancelled' | string;
  meetLink?: string | null;
  focusArea?: string | null;
  priceCredits: number;
  cancelledAt?: string | null;
  createdAt: string;
  attemptId?: number | null;
  redirectUrl?: string | null;
  review?: MentorReviewDto | null;
  standing?: MentorLearnerStandingDto | null;
}

export interface CancelBookingResultDto {
  bookingId: number;
  status: string;
  cancelledAt: string;
  refundedCredits: number;
}

export interface LearnerComplaintDto {
  complaintId: number;
  type: 'mentor_no_show' | 'mentor_quality' | 'grading_issue' | 'technical_error' | 'payment_issue' | string;
  reason: string;
  status: 'open' | 'reviewing' | 'resolved' | string;
  resolution?: string | null;
  resolvedAt?: string | null;
  resolvedByGrantTxnId?: number | null;
  grantedCredits?: number | null;
  createdAt: string;
  bookingId?: number | null;
  attemptId?: number | null;
  orderId?: number | null;
  subjectSummary?: string | null;
}

export interface EligibleBookingSubjectDto {
  bookingId: number;
  mentorName: string;
  scheduledAt?: string | null;
  status: string;
}

export interface EligibleAttemptSubjectDto {
  attemptId: number;
  examTitle: string;
  startedAt: string;
  status: string;
  totalScore?: number | null;
}

export interface EligibleOrderSubjectDto {
  orderId: number;
  orderCode: string;
  packageName: string;
  amount: number;
  createdAt: string;
  status: string;
}

export interface EligibleSubjectsDto {
  bookings: EligibleBookingSubjectDto[];
  attempts: EligibleAttemptSubjectDto[];
  orders: EligibleOrderSubjectDto[];
}

export interface CreateComplaintRequest {
  type: string;
  reason: string;
  bookingId?: number | null;
  attemptId?: number | null;
  orderId?: number | null;
}

export interface AttemptOptionDto {
  optionId: number;
  label: string;
  text: string;
}

export interface AttemptQuestionDto {
  questionId: number;
  displayOrder: number;
  totalQuestions?: number;
  sectionId: number;
  skillId?: number;
  sectionCode: string;
  sectionName: string;
  skillCode: string;
  skillName: string;
  modality: 'listening' | 'reading' | 'speaking' | 'writing' | string;
  stem: string;
  imagePath?: string | null;
  imageAlt?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  transcript?: string | null;
  passage?: string | null;
  options: AttemptOptionDto[];
  selectedOptionId?: number | null;
  isFlagged: boolean;
  questionType?: string | null;
  minWords?: number | null;
  maxWords?: number | null;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  responseText?: string | null;
  responseAudioPath?: string | null;
  responseDurationMs?: number | null;
  draftSavedAt?: string | null;
  skillDurationMinutes?: number | null;
  skillDisplayOrder?: number;
  selectedOptionIds?: number[] | null;
  maxSelections?: number | null;
  allowNumber?: boolean;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  layoutType?: string | null;
  requirePassage?: boolean;
  navigationMode?: string | null;
  toolsConfigJson?: string | null;
  hideOptionText?: boolean;
  optionCount?: number;
  clusterSize?: number;
  requireImage?: boolean;
  unitNumber?: number;
  groupId?: number | null;
  timerPolicyJson?: string | null;
  // Optional legacy/UI helper fields
  allowReplay?: boolean;
  maxReplays?: number | null;
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
  skillDisplayOrder: number;
  startOffsetSeconds: number;
  durationSeconds: number;
  lockNavigationOneWay?: boolean;
}

export type PlacementScopeCode = 'all_skills' | 'listening_and_reading';

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
  phases?: AttemptPhaseDto[] | null;
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
  phases?: AttemptPhaseDto[] | null;
  activeSkillId?: number | null;
  activeUnitNumber: number;
  activeGroupId?: number | null;
  phaseDeadlineAt?: string | null;
  isSkillLocked?: boolean;
  authorizationCode?: string | null;
  lastAudioOffsetMs?: number;
  candidateNotesJson?: string | null;
  navigationMode?: string | null;
}

export interface SaveAnswerResultDto {
  questionId: number;
  selectedOptionId?: number | null;
  isFlagged: boolean;
  remainingSeconds: number;
  isAutoSubmitted: boolean;
}

export interface MockExamItemDto {
  examId: number;
  certId: number;
  certCode: string;
  name: string;
  description?: string | null;
  durationMinutes: number;
  questionCount?: number | null;
  sourceName?: string | null;
  creditCost: number;
  activeAttemptId?: number | null;
  activeAttemptExpiresAt?: string | null;
  completedAttemptScore?: number | null;
  completedAttemptBand?: string | null;
}

export interface MockExamListViewDto {
  certificate: CertificateOptionDto;
  creditBalance: number;
  exams: MockExamItemDto[];
}

export interface MockExamSectionInfoDto {
  sectionId: number;
  skillCode: string;
  skillName: string;
  modality: ModalityCode | string;
  sectionCode: string;
  sectionName: string;
  expectedQuestionCount: number;
  questionCount: number;
  durationMinutes: number;
  displayOrder: number;
}

export interface MockExamInstructionsDto {
  examId: number;
  certId: number;
  certCode: string;
  examName: string;
  description?: string | null;
  durationMinutes: number;
  questionCount?: number | null;
  creditCost: number;
  creditBalance: number;
  activeAttemptId?: number | null;
  sections: MockExamSectionInfoDto[];
}

export interface StartMockExamResponseDto {
  attemptId: number;
  examId: number;
  status: string;
  startedAt: string;
  expiresAt: string;
  remainingSeconds: number;
  isAutoSubmitted: boolean;
}

export interface SubmitAttemptResponseDto {
  attemptId: number;
  status: string;
  totalScore: number;
  bandCode?: string | null;
  bandName?: string | null;
  submittedAt: string;
  isAutoSubmitted: boolean;
}

export interface AttemptSkillScoreDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  modality: ModalityCode | string;
  rawScore: number;
  scaledScore: number;
  totalQuestions: number;
}

export interface AttemptResultDto {
  attemptId: number;
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  certName: string;
  status: string;
  startedAt: string;
  submittedAt: string;
  durationSeconds: number;
  isAutoSubmitted: boolean;
  totalScore: number;
  bandCode?: string | null;
  bandName?: string | null;
  skillScores: AttemptSkillScoreDto[];
  previousAttemptScore?: number | null;
  scoreDelta?: number | null;
  freeResponseCount?: number;
  gradedFreeResponseCount?: number;
  failedFreeResponseCount?: number;
  scoreDisplayFactor?: number;
}

export interface AttemptReviewOptionDto {
  optionId: number;
  label: string;
  text: string;
  isCorrect: boolean;
  rationale?: string | null;
}

export interface AttemptReviewRubricScoreDto {
  criterionCode: string;
  criterionName: string;
  score: number;
  maxScore: number;
  feedback?: string | null;
}

export interface AttemptReviewQuestionDto {
  questionId: number;
  displayOrder: number;
  totalQuestions: number;
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  skillCode: string;
  skillName: string;
  modality: ModalityCode | string;
  stem: string;
  imagePath?: string | null;
  imageAlt?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  transcript?: string | null;
  passage?: string | null;
  explanation?: string | null;
  options: AttemptReviewOptionDto[];
  selectedOptionId?: number | null;
  isCorrect?: boolean | null;
  isFlagged: boolean;
  responseText?: string | null;
  responseAudioPath?: string | null;
  aiScore?: number | null;
  aiFeedback?: string | null;
  rubricScores?: AttemptReviewRubricScoreDto[] | null;
}

export interface ReviewNavigatorItemDto {
  questionId: number;
  displayOrder: number;
  sectionId: number;
  sectionCode: string;
  modality: ModalityCode | string;
  isAnswered: boolean;
  isFlagged: boolean;
  isCorrect?: boolean | null;
}

export interface AttemptReviewDto {
  attemptId: number;
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  certName: string;
  status: string;
  startedAt: string;
  submittedAt: string;
  durationSeconds: number;
  totalScore: number;
  bandCode?: string | null;
  bandName?: string | null;
  skillScores: AttemptSkillScoreDto[];
  totalQuestions: number;
  answeredCount: number;
  correctCount: number;
  incorrectCount: number;
  blankCount: number;
  flaggedCount: number;
  questions: AttemptReviewQuestionDto[];
  navigator: ReviewNavigatorItemDto[];
}

export interface ScoreBandItemDto {
  code: string;
  name: string;
  minTotal: number;
  maxTotal: number;
  displayOrder: number;
}

export interface ExamHistoryItemDto {
  attemptId: number;
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  startedAt: string;
  submittedAt: string;
  totalScore: number;
  bandCode?: string | null;
  bandName?: string | null;
  skillScores: AttemptSkillScoreDto[];
  status: string;
}

export interface MockSkillProjectionDto {
  skillId: number;
  scaledScore: number;
  awardedLevelCode?: string | null;
  positionInBand?: number | null;
  outcome: 'projected' | 'uncertified' | 'display_ceiling' | 'cannot_judge' | string;
  cannotJudgeReason?: string | null;
}

export interface MockResultProjectionDto {
  attemptId: number;
  certId: number;
  certificateScore: number;
  bandCode?: string | null;
  bandName?: string | null;
  scoreDisplayFactor: number;
  skills: MockSkillProjectionDto[];
  weakestProjectedLevelCode?: string | null;
  weakestProjectedSkillId?: number | null;
  storedOverallLevelCode?: string | null;
  storedLimitingSkillCode?: string | null;
  hasNoStoredMeasurement: boolean;
  differingSkillIds: number[];
  syncOffered: boolean;
  historical: boolean;
  cannotJudgeReason?: string | null;
  cutScoreTableVersion?: number | null;
}

export interface MockSyncSkillDto {
  skillId: number;
  skillCode: string;
  levelCode: string;
  positionInBand: number;
  source: string;
}

export interface MockSyncResultDto {
  certId: number;
  overallLevelCode?: string | null;
  limitingSkillId?: number | null;
  limitingSkillCode?: string | null;
  skills: MockSyncSkillDto[];
}

export interface ExamHistoryDto {
  certificate: CertificateOptionDto;
  totalAttempts: number;
  bestScore?: number | null;
  averageScore?: number | null;
  bestScoreBandCode?: string | null;
  bestScoreBandName?: string | null;
  cefrBands: ScoreBandItemDto[];
  attempts: ExamHistoryItemDto[];
}

export interface PlacementResumeSkillDto {
  skillId: number;
  code: string;
  name: string;
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
  activeStage?: number | null;
  totalStages: number;
  syncedAnswerCount: number;
  totalQuestions: number;
  lastQuestionId?: number | null;
}

export interface PlacementSkillResultInfoDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  status: string;
  levelCode?: string | null;
  positionInBand?: number | null;
  measuredAt?: string | null;
  reusedFromPrior: boolean;
  failureReason?: string | null;
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

export interface PlacementSkillScoreInfoDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  rawScore: number;
  totalQuestions: number;
  scaledScore: number;
  bandCode?: string | null;
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
  resultBandCode: string;
  resultBandName: string;
  cefrBands: CefrBandInfoDto[];
  skillScores: PlacementSkillScoreInfoDto[];
  weakerSkillName?: string | null;
  bandNote?: string | null;
  isAutoSubmitted?: boolean;
  freeResponseCount?: number;
  gradedFreeResponseCount?: number;
  failedFreeResponseCount?: number;
  scoreDisplayFactor?: number;
  isGraded: boolean;
  skillResults?: PlacementSkillResultInfoDto[] | null;
  scope?: PlacementScopeCode | string;
}

export interface PlacementGradingStatusDto {
  placementId: number;
  attemptId: number;
  isGraded: boolean;
  freeResponseCount: number;
  gradedFreeResponseCount: number;
  failedFreeResponseCount: number;
  totalScore?: number | null;
  bandCode?: string | null;
  bandName?: string | null;
  skillScores: PlacementSkillScoreInfoDto[];
  stageDescriptor: string;
}

export interface PreflightStatusDto {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  certName: string;
  durationMinutes: number;
  requiresPreflight: boolean;
  requiresAudio: boolean;
  requiresMicrophone: boolean;
  isVerified: boolean;
  verifiedAt?: string | null;
  audioUrls: string[];
}

export type ModalityCode = 'listening' | 'reading' | 'speaking' | 'writing' | string;
export type PracticeStatusCode = 'in_progress' | 'finished' | 'abandoned' | string;
export type QuestionSourceCode = 'all' | 'wrong_before' | 'unseen' | string;
export type PracticeCellMasteryValue =
  | 0
  | 1
  | 2
  | 3
  | 'Untested'
  | 'Learning'
  | 'Reviewing'
  | 'Mastered'
  | string;
export type SavedQuestionSourceCode = 'manual' | 'incorrect' | string;

export interface PracticeSectionDto {
  sectionId: number;
  skillCode: string;
  skillName: string;
  modality: ModalityCode;
  code: string;
  name: string;
  optionCount?: number | null;
  expectedQuestionCount: number;
  availableQuestionCount: number;
  accuracyPercentage?: number | null;
  answeredCount: number;
  displayOrder: number;
  totalCategoryCount: number;
  categoriesInCooldown: number;
  nextReviewDueAt?: string | null;
}

export interface PracticeSessionDto {
  sessionId: number;
  sectionId?: number | null;
  sectionCode: string;
  sectionName: string;
  modality: ModalityCode;
  questionCount: number;
  status: PracticeStatusCode;
  correctCount: number;
  questionSource: QuestionSourceCode;
  startedAt: string;
  finishedAt?: string | null;
  topicId?: number | null;
  cefrLevelCode?: string | null;
  isShort?: boolean;
}

export interface PracticeCatalogDto {
  certificate: CertificateOptionDto;
  listeningSections: PracticeSectionDto[];
  readingSections: PracticeSectionDto[];
  otherSections: PracticeSectionDto[];
  weakestSection?: PracticeSectionDto | null;
  unfinishedSession?: PracticeSessionDto | null;
  speakingSections?: PracticeSectionDto[] | null;
  speakingPracticeExamId?: number | null;
}

export interface PracticeCellDto {
  topicId: number;
  topicCode: string;
  topicName: string;
  modality: ModalityCode;
  levelCode: string;
  mastery: PracticeCellMasteryValue;
  isPrioritised: boolean;
  reopensAt?: string | null;
  isResting: boolean;
  isRestingNow?: boolean;
}

export interface PrioritisedCoordinateDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  modality: ModalityCode;
  topicId: number;
  topicCode: string;
  topicName: string;
  levelCode: string;
  reason: string;
}

export interface IncompleteStepDto {
  stepId: number;
  weekNumber: number;
  title: string;
  topicCode: string;
  estimatedMinutes: number;
}

export interface FreeDrillStateDto {
  isUnlocked: boolean;
  incompleteSteps: IncompleteStepDto[];
  remainingMinutes: number;
  priorities: PrioritisedCoordinateDto[];
  cellUsedToday: boolean;
  dailyCeilingReached: boolean;
  answeredToday: number;
  dailyItemCeiling: number;
  lockNotice?: string | null;
}

export interface PracticeOptionDto {
  optionId: number;
  label: string;
  text: string;
}

export interface PracticeQuestionDto {
  sessionId: number;
  sectionName: string;
  questionId?: number | null;
  displayOrder: number;
  totalQuestions: number;
  stem: string;
  categoryName?: string | null;
  hint?: string | null;
  hintUsed: boolean;
  options: PracticeOptionDto[];
  selectedOptionId?: number | null;
  isCorrect?: boolean | null;
  correctOptionId?: number | null;
  explanation?: string | null;
  transcript?: string | null;
  passage?: string | null;
  audioPath?: string | null;
  imagePath?: string | null;
  imageAlt?: string | null;
  isSaved: boolean;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  questionType: string;
  maxSelections?: number | null;
  allowNumber: boolean;
  acceptedAnswersJson?: string | null;
  requiredKeywords?: string | null;
  minWords?: number | null;
  maxWords?: number | null;
  responseText?: string | null;
  selectedOptionIds?: number[] | null;
  selectedOptionIdsJson?: string | null;
  correctOptionIds?: number[] | null;
  itemVersionId?: number | null;
  savedQuestionId?: number | null;
  cefrLevelCode?: string | null;
  address?: number;
}

export interface PracticeNavigatorItemDto {
  questionId?: number | null;
  displayOrder: number;
  isAnswered: boolean;
  isCorrect?: boolean | null;
  isSaved: boolean;
  isCurrent: boolean;
  itemVersionId?: number | null;
  savedQuestionId?: number | null;
  address?: number;
}

export interface PracticeSessionTakeDto {
  session: PracticeSessionDto;
  question: PracticeQuestionDto;
  questions: PracticeQuestionDto[];
  navigator: PracticeNavigatorItemDto[];
}

export interface RevealHintResponseDto {
  hint?: string | null;
  hintUsed: boolean;
}

export interface GradedAnswerDto {
  isCorrect?: boolean | null;
  selectedOptionId?: number | null;
  correctOptionId?: number | null;
  explanation?: string | null;
  transcript?: string | null;
  isSessionFinished: boolean;
  nextQuestionDisplayOrder?: number | null;
  isSaved: boolean;
  acceptedAnswersJson?: string | null;
  correctOptionIds?: number[] | null;
  responseText?: string | null;
  selectedOptionIdsJson?: string | null;
  savedQuestionId?: number | null;
  cefrLevelCode?: string | null;
}

export interface QuestionOutcomeDto {
  displayOrder: number;
  questionId?: number | null;
  categoryName?: string | null;
  stem: string;
  selectedOptionLabel?: string | null;
  selectedOptionText?: string | null;
  correctOptionLabel?: string | null;
  correctOptionText?: string | null;
  isCorrect?: boolean | null;
  hintUsed: boolean;
  explanation?: string | null;
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  transcript?: string | null;
  questionType: string;
  responseText?: string | null;
  selectedOptionIdsJson?: string | null;
  acceptedAnswersJson?: string | null;
  itemVersionId?: number | null;
}

export interface PracticeSummaryDto {
  sessionId: number;
  sectionCode: string;
  sectionName: string;
  modality: ModalityCode;
  startedAt: string;
  finishedAt?: string | null;
  totalQuestions: number;
  answeredCount: number;
  correctCount: number;
  incorrectCount: number;
  skippedCount: number;
  accuracyPercentage: number;
  hintUsedCount: number;
  correctWithHintCount: number;
  incorrectWithHintCount: number;
  questions: QuestionOutcomeDto[];
}

export interface RecommendationDto {
  recId: number;
  insightId?: number | null;
  practiceSessionId?: number | null;
  attemptId?: number | null;
  text: string;
  severity: string;
  targetBandCode?: string | null;
  skillName?: string | null;
  isResolved: boolean;
  createdAt: string;
}

export interface AiInsightDto {
  insightId: number;
  userId: number;
  certId: number;
  scope: string;
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

export interface SavedQuestionOptionDto {
  optionId: number;
  label: string;
  text: string;
  isCorrect: boolean;
}

export interface SavedQuestionItemDto {
  savedQuestionId: number;
  questionId?: number | null;
  itemId?: number | null;
  itemVersionId?: number | null;
  isRetiredDefective: boolean;
  source: SavedQuestionSourceCode;
  savedAt: string;
  sectionId?: number | null;
  sectionCode?: string | null;
  sectionName?: string | null;
  topicCode?: string | null;
  topicName?: string | null;
  categoryId?: number | null;
  categoryName?: string | null;
  stem: string;
  passage?: string | null;
  imagePath?: string | null;
  audioPath?: string | null;
  hint?: string | null;
  explanation?: string | null;
  transcript?: string | null;
  options: SavedQuestionOptionDto[];
  scopeCode?: string;
  scopeName?: string;
}

export interface SavedQuestionSectionFilterDto {
  sectionId: number;
  code: string;
  name: string;
  count: number;
}

export interface SavedQuestionCategoryFilterDto {
  categoryId: number;
  name: string;
  count: number;
}

export interface SavedQuestionsViewDto {
  certificate: CertificateOptionDto;
  totalSavedCount: number;
  manualSavedCount: number;
  incorrectSavedCount: number;
  questions: SavedQuestionItemDto[];
  sections: SavedQuestionSectionFilterDto[];
  categories: SavedQuestionCategoryFilterDto[];
}

export interface SaveQuestionResultDto {
  isSaved: boolean;
  source?: SavedQuestionSourceCode | null;
  savedAt?: string | null;
  savedQuestionId?: number | null;
}

export interface PathStepDto {
  stepId: number;
  weekNumber: number;
  weekSequence: number;
  stepType: string;
  route: string;
  title: string;
  status: string;
  skillCode: string;
  skillName: string;
  modality: ModalityCode;
  topicCode: string;
  topicName: string;
  targetCefr: string;
  sizingTier: string;
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

export interface StartStepResultDto {
  stepId: number;
  stepType: string;
  route: string;
  redirectUrl: string;
  sessionId?: number | null;
  attemptId?: number | null;
  substitutedFromTopicId?: number | null;
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

export interface PlacementSkillOfferDto {
  skillId: number;
  skillCode: string;
  skillName: string;
  modality: string;
  requiresMicrophone: boolean;
  requiresAudio: boolean;
  levelCode?: string | null;
  positionInBand?: number | null;
  measuredAt?: string | null;
  expiresAt?: string | null;
  canReuse: boolean;
  isAged: boolean;
}

export interface PlacementPreflightViewDto {
  certId: number;
  certCode: string;
  examId?: number | null;
  offers: PlacementSkillOfferDto[];
  cooldownUntil?: string | null;
  cooldownWaived: boolean;
  hasReusableSkills: boolean;
  isStartable: boolean;
  requiresMicrophone: boolean;
  requiresAudio: boolean;
  isRetake?: boolean;
  allSkillsRetakeCredits?: number;
  listeningAndReadingRetakeCredits?: number;
  creditBalance?: number | null;
}

export interface PlacementCatalogItemDto {
  examId: number;
  certId: number;
  certCode: string;
  name: string;
  description?: string | null;
  durationMinutes: number;
  questionCount: number;
  isRecommended: boolean;
  activePlacementId?: number | null;
}

export interface PlacementCatalogViewDto {
  activeCert: CertificateOptionDto;
  currentBandCode?: string | null;
  currentBandName?: string | null;
  currentTotalScore?: number | null;
  exams: PlacementCatalogItemDto[];
  activePlacementId?: number | null;
  activeAttemptId?: number | null;
  isRetake?: boolean;
  allSkillsRetakeCredits?: number;
  listeningAndReadingRetakeCredits?: number;
  creditBalance?: number | null;
}

export interface PlacementTestInfoDto {
  placementId: number;
  attemptId: number;
  examId: number;
  status: string;
  startedAt: string;
  expiresAt: string;
  remainingSeconds: number;
  isAutoSubmitted: boolean;
  scope?: PlacementScopeCode | string;
  creditsCharged?: number;
}

// ============================================================================
// Admin Finance & Operations DTOs (#304)
// ============================================================================

export type OrderStatusDto = 'pending' | 'reconciling' | 'paid' | 'failed' | 'expired';
export type PaymentGatewayDto = 'payos' | 'mock';
export type PaymentEventSourceDto = 'webhook' | 'status_query' | 'return_redirect' | 'cancel';
export type GatewayStatusDto = 'pending' | 'paid' | 'failed' | 'expired' | 'cancelled';

export interface AdminOrderDto {
  orderId: number;
  orderCode: string;
  paymentNo: number;
  userId: number;
  userEmail: string;
  userFullName?: string | null;
  packageId: number;
  packageName: string;
  credits: number;
  amount: number;
  status: OrderStatusDto;
  gateway: PaymentGatewayDto;
  gatewayTxnRef?: string | null;
  gatewayErrorCode?: string | null;
  createdAt: string;
  paidAt?: string | null;
}

export interface AdminPaymentEventDto {
  eventId: number;
  orderId: number;
  source: PaymentEventSourceDto;
  reportedStatus?: GatewayStatusDto | null;
  signatureValid: boolean;
  wasApplied: boolean;
  note?: string | null;
  payload?: string | null;
  createdAt: string;
}

export interface AdminOrderDetailDto {
  order: AdminOrderDto;
  events: AdminPaymentEventDto[];
}

export interface AdminOrdersSummaryDto {
  orders: AdminOrderDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  paidThisMonthCount: number;
  paidThisMonthAmount: number;
  reconcilingCount: number;
  failedOrExpiredCount: number;
}

export type RefundMethodDto = 'money' | 'credit';
export type RefundStatusDto = 'requested' | 'approved' | 'settled' | 'rejected';

export interface AdminRefundListItemDto {
  refundId: number;
  userId: number;
  learnerName: string;
  learnerEmail: string;
  method: RefundMethodDto;
  status: RefundStatusDto;
  creditCount: number;
  reason: string;
  orderId?: number | null;
  orderCode?: string | null;
  createdAt: string;
  decidedAt?: string | null;
  amount: number;
  settledAt?: string | null;
}

export interface AdminRefundDetailDto {
  refundId: number;
  userId: number;
  learnerName: string;
  learnerEmail: string;
  method: RefundMethodDto;
  status: RefundStatusDto;
  creditCount: number;
  reason: string;
  orderId?: number | null;
  orderCode?: string | null;
  complaintId?: number | null;
  createdAt: string;
  requestedByUserId: number;
  requestedByUserName?: string | null;
  decidedByUserId?: number | null;
  decidedByUserName?: string | null;
  decidedAt?: string | null;
  creditTxnId?: number | null;
  grantedCredits?: number | null;
  amount: number;
  orderPaidAmount?: number | null;
  settledByUserId?: number | null;
  settledByUserName?: string | null;
  settledAt?: string | null;
}

export interface AdminRefundsSummaryDto {
  refunds: AdminRefundListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  requestedCount: number;
  approvedCount: number;
  rejectedCount: number;
  creditsRefundedTotal: number;
}

export interface RequestRefundRequest {
  userId: number;
  method: RefundMethodDto;
  creditCount: number;
  reason: string;
  orderId?: number | null;
  complaintId?: number | null;
  amount?: number | null;
}

export interface RejectRefundRequest {
  reason: string;
}

export interface AdminPackagesSummaryDto {
  packages: PackageDto[];
  packagesOnSale: number;
  featuredCount: number;
  totalOrdersSold: number;
  totalRevenue: number;
}

export interface CreatePackageRequest {
  name: string;
  credits: number;
  price: number;
  isActive?: boolean;
  isFeatured?: boolean;
  features?: string | null;
  displayOrder?: number;
}

export interface UpdatePackageRequest {
  name: string;
  credits: number;
  price: number;
  isActive: boolean;
  isFeatured: boolean;
  features?: string | null;
  displayOrder?: number;
}

export type ComplaintTypeDto =
  | 'mentor_no_show'
  | 'mentor_quality'
  | 'grading_issue'
  | 'technical_error'
  | 'payment_issue';

export type ComplaintStatusDto = 'open' | 'reviewing' | 'resolved';

export interface AdminComplaintListItemDto {
  complaintId: number;
  userId: number;
  learnerName: string;
  learnerEmail: string;
  type: ComplaintTypeDto;
  subjectType: string;
  subjectId: number;
  subjectSummary: string;
  status: ComplaintStatusDto;
  createdAt: string;
  resolvedAt?: string | null;
}

export interface AdminComplaintDetailDto {
  complaintId: number;
  userId: number;
  learnerName: string;
  learnerEmail: string;
  type: ComplaintTypeDto;
  reason: string;
  status: ComplaintStatusDto;
  subjectType: string;
  subjectId: number;
  subjectSummary: string;
  subjectDetails?: string | null;
  createdAt: string;
  resolution?: string | null;
  internalNotes?: string | null;
  resolvedByUserId?: number | null;
  resolvedByUserName?: string | null;
  resolvedAt?: string | null;
  resolvedByGrantTxnId?: number | null;
  grantedCredits?: number | null;
}

export interface AdminComplaintsSummaryDto {
  complaints: AdminComplaintListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  pendingCount: number;
  resolvedCount: number;
  oldestPendingCreatedAt?: string | null;
  oldestPendingId?: number | null;
  oldestPendingType?: ComplaintTypeDto | null;
}

export interface ResolveComplaintRequest {
  resolution: string;
  internalNotes: string;
  grantCredits?: number;
}

export interface UpdateComplaintStatusRequest {
  status: ComplaintStatusDto;
}

export type MentorTypeDto = 'human' | 'ai';
export type SlotStatusDto = 'open' | 'booked' | 'closed';

export interface AdminMentorListItemDto {
  mentorId: number;
  userId?: number | null;
  displayName: string;
  type: MentorTypeDto;
  certId: number;
  certCode: string;
  skillId?: number | null;
  skillName?: string | null;
  pricePerSlot: number;
  priceCredits: number;
  sessionMinutes: number;
  avgRating?: number | null;
  reviewCount: number;
  totalSlotsCount: number;
  openSlotsCount: number;
  isActive: boolean;
}

export interface AdminMentorSlotDto {
  slotId: number;
  mentorId: number;
  startAt: string;
  endAt: string;
  status: SlotStatusDto;
}

export interface CreateMentorProfileRequest {
  userId?: number | null;
  displayName: string;
  bio?: string | null;
  certId: number;
  skillId?: number | null;
  type: MentorTypeDto;
  pricePerSlot: number;
  priceCredits: number;
  headline?: string | null;
  expertise?: string | null;
  sessionMinutes: number;
  isActive: boolean;
}

export interface UpdateMentorProfileRequest {
  userId?: number | null;
  displayName: string;
  bio?: string | null;
  certId: number;
  skillId?: number | null;
  pricePerSlot: number;
  priceCredits: number;
  headline?: string | null;
  expertise?: string | null;
  sessionMinutes: number;
  isActive: boolean;
}

export interface CreateMentorSlotRequest {
  localStartAt: string;
  localEndAt?: string | null;
  timeZoneId?: string | null;
}

export interface MentorSkillOptionDto {
  skillId: number;
  code: string;
  name: string;
  modality: string;
}

export interface MentorCertificateOptionDto {
  certId: number;
  code: string;
  name: string;
  skills: MentorSkillOptionDto[];
}

// ============================================================================
// Admin & Audit DTO Types (#302)
// ============================================================================

export interface AdminNeedsAttentionDto {
  draftExamsCount: number;
  stalePendingOrdersCount: number;
  openComplaintsCount: number;
}

export interface AdminMostAttemptedExamDto {
  examId: number;
  examName: string;
  attemptsCount: number;
  averageScore?: number | null;
}

export interface AdminDashboardDto {
  activeLearnersCount: number;
  newLearners7DaysCount: number;
  creditsUsedThisMonth: number;
  creditsRefundedThisMonth: number;
  creditsPerDayAverage: number;
  revenueTodayVnd: number;
  revenueThisMonthVnd: number;
  paidOrdersThisMonthCount: number;
  mostAttemptedExams: AdminMostAttemptedExamDto[];
  needsAttention: AdminNeedsAttentionDto;
}

export interface DashboardRevision {
  paidOrdersCount: number;
  paidOrdersAmount: number;
  complaintsOpen: number;
  complaintsReviewing: number;
  complaintsResolved: number;
  refundsRequested: number;
  refundsApproved: number;
  refundsSettled: number;
  refundsRejected: number;
  settledMoneyRefundsCount: number;
  settledMoneyRefundsAmount: number;
  token?: string;
}

export interface AdminUserDto {
  userId: number;
  email: string;
  fullName: string;
  phone?: string | null;
  creditsLeft: number;
  examsTaken: number;
  bestScore?: number | null;
  lastLoginAt?: string | null;
  isLocked: boolean;
  emailVerifiedAt?: string | null;
  createdAt: string;
}

export interface AdminUsersSummaryDto {
  users: AdminUserDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  activeCount: number;
  lockedCount: number;
  notActivatedCount: number;
}

export interface AdminPlacementHistoryItemDto {
  placementId: number;
  certCode: string;
  examName: string;
  status: string;
  scope: string;
  paid: boolean;
  creditsCharged: number;
  totalScore?: number | null;
  resultBandCode?: string | null;
  takenAt: string;
}

export interface AdminUserDetailDto {
  user: AdminUserDto;
  placements: AdminPlacementHistoryItemDto[];
}

export interface AdjustCreditsRequest {
  delta: number;
  reason: string;
}

export interface AdjustCreditsResultDto {
  txnId: number;
  userId: number;
  newBalance: number;
  delta: number;
  reason: string;
}

export interface AuditSampleDto {
  sampleItemId: number;
  itemId: number;
  stem: string;
  optionA: string;
  optionB: string;
  optionC: string;
  optionD: string;
  solvedLabel?: string | null;
  solvedAt?: string | null;
  keyRevealedAt?: string | null;
  keyRevealed: boolean;
  verdict?: string | null;
}

export interface AuditStratumDto {
  stratumId: number;
  batchId: number;
  topicCode: string;
  cefrLevel: string;
  modality: string;
  itemCount: number;
  sampleSize: number;
  sampled: number;
  unsampled: number;
  status: string;
  quarantineReason?: string | null;
  decidedAt?: string | null;
  items: AuditSampleDto[];
}

export interface AuditQueueDto {
  batchId: number;
  sourceName: string;
  status: string;
  strata: AuditStratumDto[];
  sampledTotal: number;
  unsampledTotal: number;
}

export interface AuditKeyRevealDto {
  sampleItemId: number;
  itemId: number;
  keyLabel: string;
  explanation?: string | null;
  solvedLabel?: string | null;
  revealedAt: string;
}

export interface AuditDecisionDto {
  stratumId: number;
  status: string;
  quarantineReason?: string | null;
  sampledPromoted: number;
  unsampledPromoted: number;
  quarantined: number;
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
    context: async () => {
      const ctx = await apiFetch<LearnerContextDto>('/api/learner/context');
      const certs = ctx.available ?? ctx.certificates ?? [];
      return {
        ...ctx,
        available: certs,
        certificates: ctx.certificates ?? certs,
      };
    },
    switchCertificate: (code: string) =>
      apiFetch<void>('/api/learner/active-certificate', {
        method: 'PUT',
        body: JSON.stringify({ code }),
      }),
    dashboard: () => apiFetch<LearnerDashboardDto>('/api/learner/dashboard'),
    profile: () => apiFetch<ProfileDto>('/api/profile'),
    updateProfile: (body: { fullName: string; phone?: string | null; timeZoneId: string }) =>
      apiFetch<ProfileDto>('/api/profile', {
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
    catalog: () => apiFetch<PlacementCatalogViewDto>('/api/learner/placement'),
    preflightGate: () => apiFetch<PlacementPreflightViewDto>('/api/learner/placement/preflight'),
    start: (body: {
      examId: number;
      reuseValidSkills?: boolean;
      scope?: PlacementScopeCode | string;
      idempotencyKey?: string;
    }) =>
      apiFetch<PlacementTestInfoDto>('/api/learner/placement/start', {
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
      body: {
        selectedOptionId?: number | null;
        isFlagged: boolean;
        responseText?: string | null;
        selectedOptionIdsJson?: string | null;
      },
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
      apiFetch<{ freeResponseId: number; questionId: number; savedAt: string; wordCount: number }>(
        `/api/learner/placement/take/${placementId}/draft`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      ),
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
      body: { questionId: number; sessionToken: string; capturedMs: number },
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
      apiFetch<PlacementGradingStatusDto>(`/api/learner/placement/result/${placementId}/status`),
    retryGrading: (placementId: number) =>
      apiFetch<{ placementId: number; attemptId: number; requeuedCount: number }>(
        `/api/learner/placement/result/${placementId}/retry`,
        {
          method: 'POST',
        },
      ),
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
    list: () => apiFetch<MockExamListViewDto>('/api/learner/exams'),
    instructions: (examId: number) =>
      apiFetch<MockExamInstructionsDto>(`/api/learner/exams/${examId}/instructions`),
    start: (examId: number) =>
      apiFetch<StartMockExamResponseDto>(`/api/learner/exams/${examId}/start`, { method: 'POST' }),
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
      apiFetch<{ freeResponseId?: number; questionId?: number; savedAt?: string; wordCount?: number }>(
        `/api/learner/attempts/${attemptId}/draft`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      ),
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
      apiFetch<SubmitAttemptResponseDto>(`/api/learner/attempts/${attemptId}/submit`, { method: 'POST' }),
    cancel: (attemptId: number) =>
      apiFetch<void>(`/api/learner/attempts/${attemptId}/cancel`, { method: 'POST' }),
    resumeByCode: (authorizationCode: string) =>
      apiFetch<{ attemptId: number }>('/api/learner/attempts/resume', {
        method: 'POST',
        body: JSON.stringify({ authorizationCode }),
      }),
    result: (attemptId: number) =>
      apiFetch<AttemptResultDto>(`/api/learner/attempts/${attemptId}/result`),
    projection: (attemptId: number) =>
      apiFetch<MockResultProjectionDto>(`/api/learner/attempts/${attemptId}/projection`),
    syncProjection: (attemptId: number) =>
      apiFetch<MockSyncResultDto>(`/api/learner/attempts/${attemptId}/projection/sync`, {
        method: 'POST',
      }),
    review: (attemptId: number) =>
      apiFetch<AttemptReviewDto>(`/api/learner/attempts/${attemptId}/review`),
    history: () => apiFetch<ExamHistoryDto>('/api/learner/exams/history'),
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
    catalog: () => apiFetch<PracticeCatalogDto>('/api/learner/practice/catalog'),
    matrix: () => apiFetch<PracticeCellDto[]>('/api/learner/practice/matrix'),
    freeDrill: (topicId?: number, cefrLevelCode?: string) => {
      const params = new URLSearchParams();
      if (topicId != null) params.set('topicId', String(topicId));
      if (cefrLevelCode) params.set('cefrLevelCode', cefrLevelCode);
      const q = params.toString();
      return apiFetch<FreeDrillStateDto>(`/api/learner/practice/free-drill${q ? `?${q}` : ''}`);
    },
    startSession: (body: {
      sectionId?: number | null;
      topicId?: number | null;
      cefrLevelCode?: string | null;
      contentTier?: string | null;
      questionCount?: number;
      skillId?: number | null;
      learnerPathStepId?: number | null;
    }) =>
      apiFetch<PracticeSessionDto>('/api/learner/practice/sessions', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    sessionTake: (sessionId: number, q?: number | null) =>
      apiFetch<PracticeSessionTakeDto>(
        q != null
          ? `/api/learner/practice/sessions/${sessionId}/take?q=${q}`
          : `/api/learner/practice/sessions/${sessionId}/take`,
      ),
    revealHint: (sessionId: number, questionId: number) =>
      apiFetch<RevealHintResponseDto>(
        `/api/learner/practice/sessions/${sessionId}/questions/${questionId}/hint`,
        {
          method: 'POST',
        },
      ),
    submitAnswer: (
      sessionId: number,
      questionId: number,
      body: {
        selectedOptionId?: number | null;
        responseText?: string | null;
        selectedOptionIdsJson?: string | null;
        selectedOptionIds?: number[] | null;
      },
    ) =>
      apiFetch<GradedAnswerDto>(
        `/api/learner/practice/sessions/${sessionId}/questions/${questionId}/answer`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      ),
    summary: (sessionId: number) =>
      apiFetch<PracticeSummaryDto>(`/api/learner/practice/sessions/${sessionId}/summary`),
    insight: (sessionId: number) =>
      apiFetch<AiInsightDto>(`/api/learner/insights/practice/${sessionId}`),
    retryInsight: (sessionId: number) =>
      apiFetch<void>(`/api/learner/insights/practice/${sessionId}/retry`, {
        method: 'POST',
      }),
    savedQuestions: (params?: { source?: string; sectionId?: number; categoryId?: number }) => {
      const sp = new URLSearchParams();
      if (params?.source) sp.set('source', params.source);
      if (params?.sectionId != null) sp.set('sectionId', String(params.sectionId));
      if (params?.categoryId != null) sp.set('categoryId', String(params.categoryId));
      const q = sp.toString();
      return apiFetch<SavedQuestionsViewDto>(`/api/learner/saved-questions${q ? `?${q}` : ''}`);
    },
    savedQuestion: (savedQuestionId: number) =>
      apiFetch<SavedQuestionItemDto>(`/api/learner/saved-questions/${savedQuestionId}`),
    saveQuestion: (questionId: number, source = 'manual') =>
      apiFetch<SaveQuestionResultDto>(`/api/learner/saved-questions/questions/${questionId}`, {
        method: 'POST',
        body: JSON.stringify({ source }),
      }),
    saveItemVersion: (itemVersionId: number, source = 'manual') =>
      apiFetch<SaveQuestionResultDto>(
        `/api/learner/saved-questions/item-versions/${itemVersionId}`,
        {
          method: 'POST',
          body: JSON.stringify({ source }),
        },
      ),
    unsaveQuestion: (savedQuestionId: number) =>
      apiFetch<SaveQuestionResultDto>(`/api/learner/saved-questions/${savedQuestionId}`, {
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
    wallet: () => apiFetch<WalletDto>('/api/learner/wallet'),
    checkoutPreview: (packageId: number) =>
      apiFetch<CheckoutPreviewDto>(`/api/checkout/preview?packageId=${packageId}`),
    createOrder: (packageId: number) =>
      apiFetch<CreateOrderResponseDto>('/api/checkout/orders', {
        method: 'POST',
        body: JSON.stringify({ packageId }),
      }),
    cancelOrder: (orderCode: string) =>
      apiFetch<{ message: string }>('/api/checkout/cancel', {
        method: 'POST',
        body: JSON.stringify({ orderCode }),
      }),
    handleReturn: (orderCode: string) =>
      apiFetch<PaymentResultDto>('/api/checkout/return', {
        method: 'POST',
        body: JSON.stringify({ orderCode }),
      }),
    paymentResult: (orderCode: string) =>
      apiFetch<PaymentResultDto>(`/api/checkout/orders/${encodeURIComponent(orderCode)}/result`),
    mockGatewayAction: (body: {
      orderCode: string;
      action: 'success' | 'failed' | 'pending' | 'expired' | string;
      errorCode?: string;
    }) =>
      apiFetch<PaymentResultDto>('/api/mock-gateway/action', {
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
      return apiFetch<MentorSummaryDto[]>(`/api/mentors${q ? `?${q}` : ''}`);
    },
    detail: (mentorId: number) => apiFetch<MentorDetailDto>(`/api/mentors/${mentorId}`),
    book: (
      mentorId: number,
      body: { slotId?: number | null; focusArea?: string | null; examId?: number | null },
    ) =>
      apiFetch<MentorBookingDto>(`/api/mentors/${mentorId}/book`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    bookings: () => apiFetch<MentorBookingDto[]>('/api/mentors/bookings'),
    bookingDetail: (bookingId: number) =>
      apiFetch<MentorBookingDto>(`/api/mentors/bookings/${bookingId}`),
    cancelBooking: (bookingId: number) =>
      apiFetch<CancelBookingResultDto>(`/api/mentors/bookings/${bookingId}/cancel`, {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    rateBooking: (bookingId: number, body: { rating: number; text?: string | null }) =>
      apiFetch<MentorReviewDto>(`/api/mentors/bookings/${bookingId}/rate`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  support: {
    complaints: () => apiFetch<LearnerComplaintDto[]>('/api/learner/complaints'),
    eligibleSubjects: () => apiFetch<EligibleSubjectsDto>('/api/learner/complaints/eligible-subjects'),
    fileComplaint: (body: CreateComplaintRequest) =>
      apiFetch<LearnerComplaintDto>('/api/learner/complaints', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },

  admin: {
    dashboard: () => apiFetch<AdminDashboardDto>('/api/admin/dashboard'),
    dashboardRevision: () => apiFetch<DashboardRevision>('/api/admin/dashboard/revision'),
    auditQueue: (batchId: number) => apiFetch<AuditQueueDto>(`/api/admin/audit/batches/${batchId}`),
    auditDraw: (batchId: number) =>
      apiFetch<AuditQueueDto>(`/api/admin/audit/batches/${batchId}/draw`, { method: 'POST' }),
    auditSolve: (sampleItemId: number, label: string) =>
      apiFetch<void>(`/api/admin/audit/samples/${sampleItemId}/solve`, {
        method: 'POST',
        body: JSON.stringify({ label }),
      }),
    auditReveal: (sampleItemId: number) =>
      apiFetch<AuditKeyRevealDto>(`/api/admin/audit/samples/${sampleItemId}/reveal`, {
        method: 'POST',
      }),
    auditVerdict: (sampleItemId: number, verdict: string) =>
      apiFetch<void>(`/api/admin/audit/samples/${sampleItemId}/verdict`, {
        method: 'POST',
        body: JSON.stringify({ verdict }),
      }),
    auditDecide: (stratumId: number) =>
      apiFetch<AuditDecisionDto>(`/api/admin/audit/strata/${stratumId}/decide`, {
        method: 'POST',
      }),
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
    packages: () => apiFetch<AdminPackagesSummaryDto>('/api/admin/packages'),
    createPackage: (body: CreatePackageRequest) =>
      apiFetch<PackageDto>('/api/admin/packages', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    updatePackage: (packageId: number, body: UpdatePackageRequest) =>
      apiFetch<PackageDto>(`/api/admin/packages/${packageId}`, {
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
      return apiFetch<AdminUsersSummaryDto>(`/api/admin/users?${sp.toString()}`);
    },
    userDetail: (userId: number) => apiFetch<AdminUserDetailDto>(`/api/admin/users/${userId}`),
    lockUser: (userId: number) =>
      apiFetch<void>(`/api/admin/users/${userId}/lock`, { method: 'POST' }),
    unlockUser: (userId: number) =>
      apiFetch<void>(`/api/admin/users/${userId}/unlock`, { method: 'POST' }),
    resendUserActivation: (userId: number) =>
      apiFetch<void>(`/api/admin/users/${userId}/resend-activation`, { method: 'POST' }),
    adjustCredits: (userId: number, body: AdjustCreditsRequest) =>
      apiFetch<AdjustCreditsResultDto>(`/api/admin/users/${userId}/adjust-credits`, {
        method: 'POST',
        body: JSON.stringify({
          delta: body.delta,
          reason: body.reason,
        }),
      }),
    orders: (params?: { q?: string; status?: string; gateway?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      if (params?.gateway) sp.set('gateway', params.gateway);
      return apiFetch<AdminOrdersSummaryDto>(`/api/admin/orders?${sp.toString()}`);
    },
    orderDetail: (orderId: number) =>
      apiFetch<AdminOrderDetailDto>(`/api/admin/orders/${orderId}`),
    reconcileOrder: (orderId: number) =>
      apiFetch<AdminOrderDto>(`/api/admin/orders/${orderId}/reconcile`, { method: 'POST' }),
    refunds: (params?: { q?: string; status?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      return apiFetch<AdminRefundsSummaryDto>(`/api/admin/refunds?${sp.toString()}`);
    },
    refundDetail: (refundId: number) =>
      apiFetch<AdminRefundDetailDto>(`/api/admin/refunds/${refundId}`),
    requestRefund: (body: RequestRefundRequest) =>
      apiFetch<AdminRefundDetailDto>('/api/admin/refunds', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    approveRefund: (refundId: number) =>
      apiFetch<AdminRefundDetailDto>(`/api/admin/refunds/${refundId}/approve`, { method: 'POST' }),
    rejectRefund: (refundId: number, reason: string) =>
      apiFetch<AdminRefundDetailDto>(`/api/admin/refunds/${refundId}/reject`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    settleRefund: (refundId: number) =>
      apiFetch<AdminRefundDetailDto>(`/api/admin/refunds/${refundId}/settle`, { method: 'POST' }),
    exportRefundsCsv: async (from?: string, to?: string): Promise<{ blob: Blob; filename: string }> => {
      const sp = new URLSearchParams();
      if (from) sp.set('from', from);
      if (to) sp.set('to', to);
      const query = sp.toString() ? `?${sp.toString()}` : '';
      const response = await fetch(`/api/admin/refunds/export${query}`, {
        credentials: 'include',
      });
      if (!response.ok) {
        let code = 'unexpected_error';
        let title = response.statusText || 'Export failed';
        let detail: string | undefined;
        try {
          const problem = await response.json();
          if (problem && typeof problem === 'object') {
            code = problem.code || problem.error || code;
            title = problem.title || problem.message || title;
            detail = problem.detail || problem.message;
          }
        } catch {}
        throw new ApiClientError(response.status, code, title, detail);
      }
      const disposition = response.headers.get('content-disposition');
      let filename = `refunds-${from || 'today'}-to-${to || 'today'}.csv`;
      if (disposition) {
        const match = disposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
        if (match && match[1]) {
          filename = match[1].replace(/['"]/g, '').trim();
        }
      }
      const blob = await response.blob();
      return { blob, filename };
    },
    complaints: (params?: { q?: string; status?: string; page?: number; pageSize?: number }) => {
      const sp = new URLSearchParams();
      sp.set('page', String(params?.page ?? 1));
      sp.set('pageSize', String(params?.pageSize ?? 25));
      if (params?.q) sp.set('q', params.q);
      if (params?.status) sp.set('status', params.status);
      return apiFetch<AdminComplaintsSummaryDto>(`/api/admin/complaints?${sp.toString()}`);
    },
    complaintDetail: (complaintId: number) =>
      apiFetch<AdminComplaintDetailDto>(`/api/admin/complaints/${complaintId}`),
    updateComplaintStatus: (complaintId: number, status: ComplaintStatusDto) =>
      apiFetch<AdminComplaintDetailDto>(`/api/admin/complaints/${complaintId}/status`, {
        method: 'POST',
        body: JSON.stringify({ status }),
      }),
    resolveComplaint: (
      complaintId: number,
      body: ResolveComplaintRequest | { resolution: string; adminNotes?: string; internalNotes?: string; grantCredits?: number },
    ) => {
      const internalNotes =
        'internalNotes' in body && body.internalNotes
          ? body.internalNotes
          : 'adminNotes' in body && body.adminNotes
            ? body.adminNotes
            : '';
      return apiFetch<AdminComplaintDetailDto>(`/api/admin/complaints/${complaintId}/resolve`, {
        method: 'POST',
        body: JSON.stringify({
          resolution: body.resolution,
          internalNotes,
          grantCredits: body.grantCredits ?? 0,
        }),
      });
    },
    mentors: () => apiFetch<AdminMentorListItemDto[]>('/api/admin/mentors'),
    mentorOptions: () => apiFetch<MentorCertificateOptionDto[]>('/api/admin/mentors/options'),
    mentorDetail: (mentorId: number) => apiFetch<MentorSummaryDto>(`/api/admin/mentors/${mentorId}`),
    createMentor: (body: CreateMentorProfileRequest) =>
      apiFetch<MentorSummaryDto>('/api/admin/mentors', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    updateMentor: (mentorId: number, body: UpdateMentorProfileRequest) =>
      apiFetch<MentorSummaryDto>(`/api/admin/mentors/${mentorId}`, {
        method: 'PUT',
        body: JSON.stringify(body),
      }),
    mentorSlots: (mentorId: number) =>
      apiFetch<AdminMentorSlotDto[]>(`/api/admin/mentors/${mentorId}/slots`),
    createMentorSlot: (mentorId: number, body: CreateMentorSlotRequest) =>
      apiFetch<AdminMentorSlotDto>(`/api/admin/mentors/${mentorId}/slots`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    closeMentorSlot: (mentorId: number, slotId: number) =>
      apiFetch<void>(`/api/admin/mentors/${mentorId}/slots/${slotId}/close`, {
        method: 'POST',
      }),
  },
};

