export type AttemptStatus = 'in_progress' | 'submitted' | 'graded' | 'abandoned';
export type AiGradingStatus = 'pending' | 'queued' | 'processing' | 'done' | 'failed';
export type FreeResponseStatus = 'draft' | 'pending_ai' | 'graded';

export interface SpeakingPriorAttempt {
  attemptId: number;
  status: string;
  startedAt: string;
  submittedAt?: string | null;
  canReview: boolean;
  reopenPath?: string | null;
}

export interface SpeakingCatalogExam {
  examId: number;
  name: string;
  description?: string | null;
  durationMinutes: number;
  creditCost: number;
  activeAttemptId?: number | null;
  latestGradedAttemptId?: number | null;
  latestGradedAt?: string | null;
}

export interface SpeakingCatalog {
  certificate: {
    code: string;
    name: string;
    sectionNoun?: string;
  };
  creditBalance: number;
  exams: SpeakingCatalogExam[];
}

export interface SpeakingSectionInfo {
  sectionId: number;
  code: string;
  name: string;
  expectedQuestionCount: number;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  displayOrder: number;
}

export interface SpeakingInstructions {
  examId: number;
  certId: number;
  certCode: string;
  examName: string;
  description?: string | null;
  durationMinutes: number;
  questionCount: number;
  creditCost: number;
  creditBalance: number;
  activeAttemptId?: number | null;
  sections: SpeakingSectionInfo[];
  attempts?: SpeakingPriorAttempt[];
}

export interface SpeakingQuestionNav {
  questionId: number;
  displayOrder: number;
  sectionName: string;
  hasAudio: boolean;
  status: FreeResponseStatus;
}

export interface SpeakingQuestion {
  questionId: number;
  displayOrder: number;
  stem: string;
  prepSeconds?: number | null;
  responseSeconds?: number | null;
  hint?: string | null;
  sectionCode: string;
  sectionName: string;
  groupImagePath?: string | null;
  groupImageAlt?: string | null;
  groupAudioPath?: string | null;
  audioPath?: string | null;
  durationMs?: number | null;
  responseText?: string | null;
  draftSavedAt?: string | null;
  status: FreeResponseStatus;
  readAloudText?: string | null;
}

export interface SpeakingTake {
  attemptId: number;
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  status: AttemptStatus;
  startedAt: string;
  expiresAt?: string | null;
  remainingSeconds?: number | null;
  currentQuestionId: number;
  currentQuestionIndex: number;
  totalQuestions: number;
  savedCount: number;
  question: SpeakingQuestion;
  questions: SpeakingQuestionNav[];
}

export interface SpeakingGradingItem {
  freeResponseId: number;
  questionId: number;
  displayOrder: number;
  status: AiGradingStatus;
  attemptCount: number;
  failureReason?: string | null;
}

export interface SpeakingGradingPending {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  attemptStatus: AttemptStatus | 'queued' | 'processing' | 'failed';
  totalResponses: number;
  gradedResponses: number;
  failedResponses: number;
  pendingResponses: number;
  canRetry: boolean;
  failureReason?: string | null;
  items: SpeakingGradingItem[];
}

export interface SpeakingCriterionScore {
  criterionId: number;
  code: string;
  name: string;
  score: number;
  maxScore: number;
  comment?: string | null;
}

export interface SpeakingQuestionReviewItem {
  questionId: number;
  displayOrder: number;
  sectionName: string;
  stem: string;
  audioPath?: string | null;
  durationMs?: number | null;
  transcript?: string | null;
  totalScore: number;
  maxScore: number;
  criteria: SpeakingCriterionScore[];
}

export interface SpeakingReview {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  submittedAt?: string | null;
  totalQuestions: number;
  answeredQuestions: number;
  totalScore: number;
  maxScore: number;
  scaledScore?: number | null;
  questions: SpeakingQuestionReviewItem[];
  lowestScoredQuestion?: SpeakingQuestionReviewItem | null;
  estimatedBandCode?: string | null;
  estimatedBandName?: string | null;
  scaledScoreMax?: number | null;
  scoreDisplayFactor: number;
  gradedQuestions: number;
}

/**
 * Formats a scaled/stored speaking score using the server's ScoreDisplayFactor
 * (e.g., IELTS tenths: 75 with factor 10 -> "7.5").
 */
export function formatSpeakingDisplayScore(value: number, factor = 1): string {
  const safeFactor = factor > 0 ? factor : 1;
  const scaled = value / safeFactor;
  if (safeFactor === 1) {
    return Number.isInteger(scaled) ? String(scaled) : scaled.toFixed(1).replace(/\.0$/, '');
  }
  return scaled.toFixed(1).replace(/\.0$/, '');
}

export function formatSpeakingAudioUrl(attemptId: number, questionId: number, fallbackPath?: string | null): string | null {
  if (fallbackPath && fallbackPath.trim().length > 0) {
    return `/api/learner/speaking/attempts/${attemptId}/questions/${questionId}/audio`;
  }
  return null;
}
