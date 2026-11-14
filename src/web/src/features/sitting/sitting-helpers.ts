import type {
  AttemptPhaseDto,
  AttemptQuestionDto,
  NavigatorItemDto,
  PlacementPreflightViewDto,
  PlacementScopeCode,
} from '@/lib/api';

export interface QuestionOrderReconciliation {
  resolvedOrder: number;
  wasReconciled: boolean;
  reason: 'ok' | 'clamped' | 'locked' | 'one_way';
}

export interface ReconcileQuestionOptions {
  navigator: NavigatorItemDto[];
  currentDisplayOrder: number;
  totalQuestions: number;
  oneWayForwardOnly: boolean;
}

/**
 * SIT-01: Reconcile a requested `?q=` question display order against bounds,
 * locked navigator cells, and one-way forward navigation constraints.
 */
export function reconcileRequestedQuestionOrder(
  requestedOrder: number | null | undefined,
  options: ReconcileQuestionOptions,
): QuestionOrderReconciliation {
  const { navigator, currentDisplayOrder, totalQuestions, oneWayForwardOnly } = options;
  if (!totalQuestions || totalQuestions < 1) {
    return { resolvedOrder: currentDisplayOrder || 1, wasReconciled: false, reason: 'ok' };
  }

  if (requestedOrder == null || Number.isNaN(requestedOrder)) {
    return { resolvedOrder: currentDisplayOrder, wasReconciled: false, reason: 'ok' };
  }

  let clamped = Math.trunc(requestedOrder);
  let wasClamped = false;
  if (clamped < 1) {
    clamped = 1;
    wasClamped = true;
  } else if (clamped > totalQuestions) {
    clamped = totalQuestions;
    wasClamped = true;
  }

  if (oneWayForwardOnly && clamped < currentDisplayOrder) {
    return {
      resolvedOrder: currentDisplayOrder,
      wasReconciled: true,
      reason: 'one_way',
    };
  }

  const targetNav = navigator.find((item) => item.displayOrder === clamped);
  if (targetNav?.isLocked) {
    return {
      resolvedOrder: currentDisplayOrder,
      wasReconciled: true,
      reason: 'locked',
    };
  }

  return {
    resolvedOrder: clamped,
    wasReconciled: wasClamped,
    reason: wasClamped ? 'clamped' : 'ok',
  };
}

/**
 * Determine whether backward navigation is disabled for the current question/phase.
 */
export function canNavigateBackInSitting(
  currentQuestion: AttemptQuestionDto | null,
  navigator: NavigatorItemDto[],
  phases?: AttemptPhaseDto[] | null,
): boolean {
  if (!currentQuestion || currentQuestion.displayOrder <= 1) {
    return false;
  }

  if (
    currentQuestion.navigationMode === 'AUDIO_DRIVEN' ||
    currentQuestion.navigationMode === 'SYSTEM_PACED_AUTO'
  ) {
    return false;
  }

  const activePhase = phases?.find(
    (p) => p.skillCode.toLowerCase() === currentQuestion.skillCode.toLowerCase(),
  );
  if (activePhase?.lockNavigationOneWay) {
    return false;
  }

  const prevNav = navigator.find((n) => n.displayOrder === currentQuestion.displayOrder - 1);
  if (prevNav?.isLocked) {
    return false;
  }

  return true;
}

import { computePerSectionUnanswered } from '@/lib/sitting-queue';

export interface SectionSubmitSummary {
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  answered: number;
  total: number;
  unanswered: number;
}

/**
 * PLC-18: Summarize answered / total / unanswered counts per section for the Submit modal.
 */
export function buildSectionSubmitSummary(
  navigator: NavigatorItemDto[],
  questions: AttemptQuestionDto[],
): SectionSubmitSummary[] {
  return computePerSectionUnanswered(navigator, questions).map((item) => ({
    sectionId: item.sectionId,
    sectionCode: item.sectionCode,
    sectionName: item.sectionName,
    answered: item.totalCount - item.unansweredCount,
    total: item.totalCount,
    unanswered: item.unansweredCount,
  }));
}

export type NormalizedSkillStatus =
  | 'measured'
  | 'estimated_from_one_task'
  | 'grading'
  | 'not_measured'
  | 'exempt_accommodated';

export function normalizePlacementSkillStatus(status?: string | null): NormalizedSkillStatus {
  switch ((status ?? '').trim().toLowerCase()) {
    case 'notmeasured':
    case 'not_measured':
      return 'not_measured';
    case 'estimatedfromonetask':
    case 'estimated_from_one_task':
      return 'estimated_from_one_task';
    case 'grading':
      return 'grading';
    case 'exemptaccommodated':
    case 'exempt_accommodated':
      return 'exempt_accommodated';
    case 'measured':
    default:
      return 'measured';
  }
}

/**
 * Scale a stored integer score by `scoreDisplayFactor` (e.g. 10 -> "6.5", 1 -> "450").
 * Never turns null/unmeasured into "0".
 */
export function formatPlacementScore(
  storedScore: number | null | undefined,
  scoreDisplayFactor = 1,
): string | null {
  if (storedScore == null) return null;
  const factor = scoreDisplayFactor > 0 ? scoreDisplayFactor : 1;
  if (factor === 1) return String(storedScore);
  const decimals = factor >= 100 ? 2 : 1;
  return (storedScore / factor).toFixed(decimals);
}

export type SittingQuestionKind = 'speaking' | 'writing' | 'mcq_multi' | 'mcq';

export function classifyQuestionKind(question: AttemptQuestionDto): SittingQuestionKind {
  const qType = (question.questionType ?? 'mcq').toLowerCase();
  const modality = (question.modality ?? '').toLowerCase();

  if (qType === 'free_response' && modality === 'speaking') {
    return 'speaking';
  }
  if (qType === 'free_response' || qType === 'writing') {
    return 'writing';
  }
  if (qType === 'mcq_multi' || (question.maxSelections != null && question.maxSelections > 1)) {
    return 'mcq_multi';
  }
  return 'mcq';
}

/**
 * Detect whether navigating from `currentQuestion` to `targetQuestion` crosses a server-enforced
 * lock boundary (different skill or HYBRID_GROUP_LOCKED group boundary, or locked navigator cell).
 */
export function needsLockBoundaryServerFetch(
  currentQuestion: AttemptQuestionDto,
  targetQuestion: AttemptQuestionDto | undefined,
  navigator: NavigatorItemDto[],
): boolean {
  if (!targetQuestion) return true;
  const targetNav = navigator.find((n) => n.displayOrder === targetQuestion.displayOrder);
  if (targetNav?.isLocked) return true;

  if (
    currentQuestion.navigationMode === 'HYBRID_GROUP_LOCKED' &&
    currentQuestion.groupId != null &&
    targetQuestion.groupId !== currentQuestion.groupId
  ) {
    return true;
  }

  return false;
}

export interface DerivedScopeOffer {
  scope: PlacementScopeCode;
  title: string;
  description: string;
  includedSkills: string[];
  excludedSkills: string[];
  creditCost: number;
  isFree: boolean;
  canAfford: boolean;
}

/**
 * Derive placement scope choices (all_skills vs listening_and_reading) from PlacementPreflightViewDto.
 */
export function computePlacementScopeOffers(
  preflight: PlacementPreflightViewDto,
): DerivedScopeOffer[] {
  const isRetake = Boolean(preflight.isRetake);
  const allCost = isRetake ? (preflight.allSkillsRetakeCredits ?? 5) : 0;
  const lrCost = isRetake ? (preflight.listeningAndReadingRetakeCredits ?? 3) : 0;
  const balance = preflight.creditBalance ?? 0;

  const allSkillNames = preflight.offers.map((o) => o.skillName);
  const lrOffers = preflight.offers.filter((o) => {
    const mod = o.modality.toLowerCase();
    return mod === 'listening' || mod === 'reading';
  });
  const nonLrOffers = preflight.offers.filter((o) => {
    const mod = o.modality.toLowerCase();
    return mod !== 'listening' && mod !== 'reading';
  });

  const result: DerivedScopeOffer[] = [
    {
      scope: 'all_skills',
      title: 'Full four-skill placement',
      description: 'Measures every skill configured for this certificate and builds a complete profile.',
      includedSkills: allSkillNames,
      excludedSkills: [],
      creditCost: allCost,
      isFree: allCost === 0,
      canAfford: allCost === 0 || balance >= allCost,
    },
  ];

  if (lrOffers.length > 0 && nonLrOffers.length > 0) {
    result.push({
      scope: 'listening_and_reading',
      title: 'Listening + Reading only',
      description:
        'Objective skills only. Excluded skills remain unmeasured until you take a full sitting.',
      includedSkills: lrOffers.map((o) => o.skillName),
      excludedSkills: nonLrOffers.map((o) => o.skillName),
      creditCost: lrCost,
      isFree: lrCost === 0,
      canAfford: lrCost === 0 || balance >= lrCost,
    });
  }

  return result;
}
