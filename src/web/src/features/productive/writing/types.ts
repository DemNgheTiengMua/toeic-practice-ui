export type WritingAttemptStatus = 'in_progress' | 'submitted' | 'graded' | 'abandoned';
export type WritingAiGradingStatus = 'pending' | 'queued' | 'processing' | 'done' | 'failed';
export type WritingFreeResponseStatus = 'draft' | 'pending_ai' | 'graded';

export interface WritingPriorAttempt {
  attemptId: number;
  status: string;
  startedAt: string;
  submittedAt?: string | null;
  canReview: boolean;
  reopenPath?: string | null;
}

export interface WritingCatalogExam {
  examId: number;
  name: string;
  description?: string | null;
  durationMinutes: number;
  creditCost: number;
  activeAttemptId?: number | null;
  latestGradedAttemptId?: number | null;
  latestGradedAt?: string | null;
}

export interface WritingCatalog {
  certificate: {
    code: string;
    name: string;
    sectionNoun?: string;
  };
  creditBalance: number;
  exams: WritingCatalogExam[];
}

export interface WritingSectionInfo {
  sectionId: number;
  code: string;
  name: string;
  expectedQuestionCount: number;
  responseSeconds?: number | null;
  displayOrder: number;
}

export interface WritingInstructions {
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
  sections: WritingSectionInfo[];
  attempts?: WritingPriorAttempt[];
}

export interface WritingQuestionNav {
  questionId: number;
  displayOrder: number;
  sectionCode: string;
  sectionName: string;
  isAnswered: boolean;
  isCurrent: boolean;
}

export interface WritingQuestion {
  questionId: number;
  displayOrder: number;
  stem: string;
  minWords?: number | null;
  maxWords?: number | null;
  responseSeconds?: number | null;
  hint?: string | null;
  sectionCode: string;
  sectionName: string;
  groupImagePath?: string | null;
  groupImageAlt?: string | null;
  responseText?: string | null;
  draftSavedAt?: string | null;
  status: WritingFreeResponseStatus;
}

export interface WritingTake {
  attemptId: number;
  examId: number;
  examName: string;
  certId: number;
  certCode: string;
  status: WritingAttemptStatus;
  startedAt: string;
  expiresAt?: string | null;
  remainingSeconds?: number | null;
  currentQuestionId: number;
  currentQuestionIndex: number;
  totalQuestions: number;
  question: WritingQuestion;
  questions: WritingQuestionNav[];
}

export interface WritingGradingItem {
  freeResponseId: number;
  questionId: number;
  displayOrder: number;
  status: WritingAiGradingStatus;
  attemptCount: number;
  failureReason?: string | null;
}

export interface WritingGradingPending {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  attemptStatus: WritingAttemptStatus | 'queued' | 'processing' | 'failed';
  totalResponses: number;
  gradedResponses: number;
  failedResponses: number;
  pendingResponses: number;
  canRetry: boolean;
  failureReason?: string | null;
  items: WritingGradingItem[];
}

export interface WritingCriterionScore {
  criterionId: number;
  code: string;
  name: string;
  score: number;
  maxScore: number;
  comment?: string | null;
}

export interface WritingErrorSpan {
  originalSnippet: string;
  correctedSnippet: string;
  category: string;
  explanationVi?: string | null;
  explanation?: string | null;
}

export interface WritingQuestionReviewItem {
  questionId: number;
  displayOrder: number;
  sectionName: string;
  stem: string;
  responseText?: string | null;
  wordCount: number;
  totalScore: number;
  maxScore: number;
  criteria: WritingCriterionScore[];
  errorSpans?: WritingErrorSpan[] | null;
  priorityFix?: string | null;
  modelRewrite?: string | null;
  annotatedFeedback?: string | null;
}

export interface WritingReview {
  attemptId: number;
  examId: number;
  examName: string;
  certCode: string;
  submittedAt?: string | null;
  totalQuestions: number;
  answeredQuestions: number;
  totalScore: number;
  maxScore: number;
  questions: WritingQuestionReviewItem[];
  estimatedBandCode?: string | null;
  estimatedBandName?: string | null;
  gradedQuestions: number;
}

const WRITING_ERROR_CATEGORY_LABELS: Record<string, string> = {
  grammar: 'Grammar',
  vocabulary: 'Vocabulary',
  spelling: 'Spelling',
  punctuation: 'Punctuation',
  cohesion: 'Cohesion',
  task: 'Task response',
  other: 'Other',
};

export function resolveWritingErrorCategoryLabel(category?: string | null): string {
  if (!category) return 'Error not classified';
  const normalized = category.trim().toLowerCase();
  return WRITING_ERROR_CATEGORY_LABELS[normalized] ?? 'Error not classified';
}

const WRITING_ATTEMPT_STATUS_LABELS: Record<string, string> = {
  in_progress: 'In progress',
  submitted: 'Submitted',
  graded: 'Graded',
  abandoned: 'Abandoned',
  queued: 'Queued',
  processing: 'Processing',
  failed: 'Failed',
};

const WRITING_GRADING_STATUS_LABELS: Record<string, string> = {
  pending: 'Pending',
  queued: 'Queued',
  processing: 'Processing',
  done: 'Done',
  failed: 'Failed',
};

export function formatWritingAttemptStatusLabel(status?: string | null): string {
  if (!status) return 'Unknown';
  const normalized = status.trim().toLowerCase();
  if (WRITING_ATTEMPT_STATUS_LABELS[normalized]) {
    return WRITING_ATTEMPT_STATUS_LABELS[normalized];
  }
  const spaced = normalized.replace(/_/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export function formatWritingGradingStatusLabel(status?: string | null): string {
  if (!status) return 'Unknown';
  const normalized = status.trim().toLowerCase();
  if (WRITING_GRADING_STATUS_LABELS[normalized]) {
    return WRITING_GRADING_STATUS_LABELS[normalized];
  }
  const spaced = normalized.replace(/_/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
