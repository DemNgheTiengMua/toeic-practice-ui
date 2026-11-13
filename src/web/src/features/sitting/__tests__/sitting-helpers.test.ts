import { describe, it, expect } from 'vitest';
import {
  reconcileRequestedQuestionOrder,
  canNavigateBackInSitting,
  buildSectionSubmitSummary,
  normalizePlacementSkillStatus,
  formatPlacementScore,
  classifyQuestionKind,
  needsLockBoundaryServerFetch,
  computePlacementScopeOffers,
} from '../sitting-helpers';
import type {
  AttemptPhaseDto,
  AttemptQuestionDto,
  NavigatorItemDto,
  PlacementPreflightViewDto,
} from '@/lib/api';

function makeNav(overrides: Partial<NavigatorItemDto> & { displayOrder: number }): NavigatorItemDto {
  return {
    questionId: overrides.displayOrder * 10,
    sectionId: 1,
    sectionCode: 'SEC-1',
    modality: 'reading',
    isAnswered: false,
    isFlagged: false,
    isCurrent: false,
    isLocked: false,
    ...overrides,
  };
}

function makeQuestion(
  overrides: Partial<AttemptQuestionDto> & { displayOrder: number },
): AttemptQuestionDto {
  return {
    questionId: overrides.displayOrder * 10,
    totalQuestions: 5,
    sectionId: 1,
    sectionCode: 'SEC-1',
    sectionName: 'Reading Comprehension',
    skillCode: 'read',
    skillName: 'Reading',
    modality: 'reading',
    stem: 'Question stem',
    options: [],
    isFlagged: false,
    questionType: 'mcq',
    ...overrides,
  };
}

describe('sitting-helpers', () => {
  describe('SIT-01 reconcileRequestedQuestionOrder', () => {
    const navigator: NavigatorItemDto[] = [
      makeNav({ displayOrder: 1, sectionCode: 'LIS-1', modality: 'listening', isLocked: true }),
      makeNav({ displayOrder: 2, sectionCode: 'REA-1', modality: 'reading', isCurrent: true, isLocked: false }),
      makeNav({ displayOrder: 3, sectionCode: 'REA-1', modality: 'reading', isLocked: false }),
      makeNav({ displayOrder: 4, sectionCode: 'WRT-1', modality: 'writing', isLocked: true }),
    ];

    it('clamps out-of-bounds negative/zero/over-total ?q= values', () => {
      const low = reconcileRequestedQuestionOrder(-3, {
        navigator: navigator.map((n) => ({ ...n, isLocked: false })),
        currentDisplayOrder: 2,
        totalQuestions: 4,
        oneWayForwardOnly: false,
      });
      expect(low.resolvedOrder).toBe(1);
      expect(low.wasReconciled).toBe(true);

      const high = reconcileRequestedQuestionOrder(99, {
        navigator: navigator.map((n) => ({ ...n, isLocked: false })),
        currentDisplayOrder: 2,
        totalQuestions: 4,
        oneWayForwardOnly: false,
      });
      expect(high.resolvedOrder).toBe(4);
      expect(high.wasReconciled).toBe(true);
    });

    it('reconciles a locked ?q= target back to the current unlocked question', () => {
      const result = reconcileRequestedQuestionOrder(4, {
        navigator,
        currentDisplayOrder: 2,
        totalQuestions: 4,
        oneWayForwardOnly: false,
      });
      expect(result.resolvedOrder).toBe(2);
      expect(result.wasReconciled).toBe(true);
      expect(result.reason).toBe('locked');
    });

    it('prevents backward ?q= tampering when oneWayForwardOnly is active', () => {
      const navUnlocked = navigator.map((n) => ({ ...n, isLocked: false }));
      const result = reconcileRequestedQuestionOrder(1, {
        navigator: navUnlocked,
        currentDisplayOrder: 2,
        totalQuestions: 4,
        oneWayForwardOnly: true,
      });
      expect(result.resolvedOrder).toBe(2);
      expect(result.wasReconciled).toBe(true);
      expect(result.reason).toBe('one_way');
    });
  });

  describe('canNavigateBackInSitting', () => {
    it('returns false on question 1 or when phase/navigationMode locks backward navigation', () => {
      const phases: AttemptPhaseDto[] = [
        {
          skillId: 1,
          skillCode: 'lis',
          skillName: 'Listening',
          skillDisplayOrder: 1,
          startOffsetSeconds: 0,
          durationSeconds: 600,
          lockNavigationOneWay: true,
        },
      ];
      expect(
        canNavigateBackInSitting(
          makeQuestion({ displayOrder: 2, skillCode: 'lis' }),
          [makeNav({ displayOrder: 1, isLocked: false }), makeNav({ displayOrder: 2, isCurrent: true })],
          phases,
        ),
      ).toBe(false);

      expect(
        canNavigateBackInSitting(
          makeQuestion({ displayOrder: 2, skillCode: 'read', navigationMode: 'SYSTEM_PACED_AUTO' }),
          [makeNav({ displayOrder: 1, isLocked: false }), makeNav({ displayOrder: 2, isCurrent: true })],
          [],
        ),
      ).toBe(false);
    });

    it('returns false if previous question is locked, true otherwise', () => {
      expect(
        canNavigateBackInSitting(
          makeQuestion({ displayOrder: 2, skillCode: 'read' }),
          [
            makeNav({ displayOrder: 1, isLocked: true }),
            makeNav({ displayOrder: 2, isCurrent: true, isLocked: false }),
          ],
          [],
        ),
      ).toBe(false);

      expect(
        canNavigateBackInSitting(
          makeQuestion({ displayOrder: 2, skillCode: 'read' }),
          [
            makeNav({ displayOrder: 1, isLocked: false }),
            makeNav({ displayOrder: 2, isCurrent: true, isLocked: false }),
          ],
          [],
        ),
      ).toBe(true);
    });
  });

  describe('PLC-18 buildSectionSubmitSummary', () => {
    it('computes per-section answered, total, and unanswered counts cleanly', () => {
      const questions: AttemptQuestionDto[] = [
        makeQuestion({ displayOrder: 1, sectionId: 10, sectionCode: 'LIS', sectionName: 'Listening' }),
        makeQuestion({ displayOrder: 2, sectionId: 10, sectionCode: 'LIS', sectionName: 'Listening' }),
        makeQuestion({ displayOrder: 3, sectionId: 20, sectionCode: 'REA', sectionName: 'Reading' }),
      ];
      const navigator: NavigatorItemDto[] = [
        makeNav({ displayOrder: 1, sectionId: 10, sectionCode: 'LIS', isAnswered: true }),
        makeNav({ displayOrder: 2, sectionId: 10, sectionCode: 'LIS', isAnswered: false }),
        makeNav({ displayOrder: 3, sectionId: 20, sectionCode: 'REA', isAnswered: true }),
      ];

      const summary = buildSectionSubmitSummary(navigator, questions);
      expect(summary).toEqual([
        { sectionId: 10, sectionCode: 'LIS', sectionName: 'Listening', answered: 1, total: 2, unanswered: 1 },
        { sectionId: 20, sectionCode: 'REA', sectionName: 'Reading', answered: 1, total: 1, unanswered: 0 },
      ]);
    });
  });

  describe('normalizePlacementSkillStatus & formatPlacementScore', () => {
    it('normalizes PascalCase and snake_case backend statuses uniformly', () => {
      expect(normalizePlacementSkillStatus('NotMeasured')).toBe('not_measured');
      expect(normalizePlacementSkillStatus('not_measured')).toBe('not_measured');
      expect(normalizePlacementSkillStatus('EstimatedFromOneTask')).toBe('estimated_from_one_task');
      expect(normalizePlacementSkillStatus('estimated_from_one_task')).toBe('estimated_from_one_task');
      expect(normalizePlacementSkillStatus('Grading')).toBe('grading');
      expect(normalizePlacementSkillStatus('ExemptAccommodated')).toBe('exempt_accommodated');
      expect(normalizePlacementSkillStatus('Measured')).toBe('measured');
    });

    it('scales scores by scoreDisplayFactor and refuses 0 for unmeasured state', () => {
      expect(formatPlacementScore(65, 10)).toBe('6.5');
      expect(formatPlacementScore(70, 10)).toBe('7.0');
      expect(formatPlacementScore(450, 1)).toBe('450');
      expect(formatPlacementScore(null, 10)).toBeNull();
    });
  });

  describe('classifyQuestionKind & needsLockBoundaryServerFetch', () => {
    it('distinguishes speaking, writing, mcq_multi, and mcq correctly', () => {
      expect(classifyQuestionKind(makeQuestion({ displayOrder: 1, questionType: 'free_response', modality: 'speaking' }))).toBe('speaking');
      expect(classifyQuestionKind(makeQuestion({ displayOrder: 2, questionType: 'free_response', modality: 'writing' }))).toBe('writing');
      expect(classifyQuestionKind(makeQuestion({ displayOrder: 3, questionType: 'writing', modality: 'reading' }))).toBe('writing');
      expect(classifyQuestionKind(makeQuestion({ displayOrder: 4, questionType: 'mcq', maxSelections: 2 }))).toBe('mcq_multi');
      expect(classifyQuestionKind(makeQuestion({ displayOrder: 5, questionType: 'mcq' }))).toBe('mcq');
    });

    it('detects when advancing crosses a locked skill or HYBRID_GROUP_LOCKED boundary', () => {
      const current = makeQuestion({ displayOrder: 3, skillCode: 'wrt', groupId: 100, navigationMode: 'HYBRID_GROUP_LOCKED' });
      const nextSameGroup = makeQuestion({ displayOrder: 4, skillCode: 'wrt', groupId: 100, navigationMode: 'HYBRID_GROUP_LOCKED' });
      const nextGroup = makeQuestion({ displayOrder: 5, skillCode: 'wrt', groupId: 200, navigationMode: 'HYBRID_GROUP_LOCKED' });
      const nav: NavigatorItemDto[] = [
        makeNav({ displayOrder: 3 }),
        makeNav({ displayOrder: 4, isLocked: false }),
        makeNav({ displayOrder: 5, isLocked: true }),
      ];

      expect(needsLockBoundaryServerFetch(current, nextSameGroup, nav)).toBe(false);
      expect(needsLockBoundaryServerFetch(current, nextGroup, nav)).toBe(true);
    });
  });

  describe('computePlacementScopeOffers', () => {
    it('computes all_skills (5 cr) vs listening_and_reading (3 cr) scope offers and affordability', () => {
      const preflight: PlacementPreflightViewDto = {
        certId: 1,
        certCode: 'IELTS',
        examId: 10,
        offers: [
          {
            skillId: 1,
            skillCode: 'listening',
            skillName: 'Listening',
            modality: 'listening',
            requiresMicrophone: false,
            requiresAudio: true,
            levelCode: 'B1',
            positionInBand: 60,
            measuredAt: '2026-10-01T00:00:00Z',
            expiresAt: '2026-12-01T00:00:00Z',
            canReuse: true,
            isAged: false,
          },
          {
            skillId: 2,
            skillCode: 'reading',
            skillName: 'Reading',
            modality: 'reading',
            requiresMicrophone: false,
            requiresAudio: false,
            levelCode: 'B1',
            positionInBand: 55,
            measuredAt: '2026-10-01T00:00:00Z',
            expiresAt: '2026-12-01T00:00:00Z',
            canReuse: true,
            isAged: false,
          },
          {
            skillId: 3,
            skillCode: 'writing',
            skillName: 'Writing',
            modality: 'writing',
            requiresMicrophone: false,
            requiresAudio: false,
            levelCode: null,
            positionInBand: null,
            measuredAt: null,
            expiresAt: null,
            canReuse: false,
            isAged: false,
          },
          {
            skillId: 4,
            skillCode: 'speaking',
            skillName: 'Speaking',
            modality: 'speaking',
            requiresMicrophone: true,
            requiresAudio: false,
            levelCode: null,
            positionInBand: null,
            measuredAt: null,
            expiresAt: null,
            canReuse: false,
            isAged: false,
          },
        ],
        cooldownUntil: null,
        cooldownWaived: false,
        hasReusableSkills: true,
        isStartable: true,
        requiresMicrophone: true,
        requiresAudio: true,
        isRetake: true,
        allSkillsRetakeCredits: 5,
        listeningAndReadingRetakeCredits: 3,
        creditBalance: 4,
      };

      const scopes = computePlacementScopeOffers(preflight);
      expect(scopes).toHaveLength(2);
      const full = scopes.find((s) => s.scope === 'all_skills')!;
      const lr = scopes.find((s) => s.scope === 'listening_and_reading')!;
      expect(full.creditCost).toBe(5);
      expect(full.canAfford).toBe(false);
      expect(lr.creditCost).toBe(3);
      expect(lr.canAfford).toBe(true);
      expect(lr.excludedSkills).toEqual(['Writing', 'Speaking']);
    });
  });
});
