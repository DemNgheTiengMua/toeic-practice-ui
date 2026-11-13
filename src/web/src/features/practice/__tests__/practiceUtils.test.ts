import { describe, expect, it, beforeEach } from 'vitest';
import {
  buildSaveTogglePlan,
  buildStartSessionPayload,
  formatLocalReopenTime,
  formatMsTime,
  getPracticeQuestionAddress,
  normalizeMasteryState,
  parseAcceptedAnswers,
  parseSelectedOptionIds,
  readSavedNote,
  removeSavedNote,
  writeSavedNote,
} from '../practiceUtils';

describe('practiceUtils', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('resolves question address preferring questionId then itemVersionId for pool drills', () => {
    expect(getPracticeQuestionAddress({ questionId: 42, itemVersionId: null })).toBe(42);
    expect(getPracticeQuestionAddress({ questionId: null, itemVersionId: 901 })).toBe(901);
    expect(getPracticeQuestionAddress({ questionId: 12, itemVersionId: 99 })).toBe(12);
    expect(getPracticeQuestionAddress({ questionId: null, itemVersionId: null, address: 77 })).toBe(77);
  });

  it('chooses the exact save or unsave endpoint target without mixing questionId and itemVersionId routes', () => {
    expect(
      buildSaveTogglePlan({ isSaved: true, savedQuestionId: 55, questionId: 10, itemVersionId: null }),
    ).toEqual({ action: 'unsave', savedQuestionId: 55 });

    expect(
      buildSaveTogglePlan({ isSaved: false, savedQuestionId: null, questionId: null, itemVersionId: 88 }),
    ).toEqual({ action: 'save_item_version', itemVersionId: 88 });

    expect(
      buildSaveTogglePlan({ isSaved: false, savedQuestionId: null, questionId: 14, itemVersionId: null }),
    ).toEqual({ action: 'save_question', questionId: 14 });

    expect(
      buildSaveTogglePlan({ isSaved: true, savedQuestionId: null, questionId: 14, itemVersionId: null }),
    ).toBeNull();
  });

  it('builds mutually exclusive StartPracticeRequest payloads', () => {
    expect(buildStartSessionPayload({ sectionId: 3 })).toEqual({ sectionId: 3 });
    expect(buildStartSessionPayload({ topicId: 7, cefrLevelCode: 'B1' })).toEqual({
      topicId: 7,
      cefrLevelCode: 'B1',
    });
    expect(() => buildStartSessionPayload({ sectionId: 3, topicId: 7, cefrLevelCode: 'B1' })).toThrow();
    expect(() => buildStartSessionPayload({})).toThrow();
  });

  it('normalizes both numeric and string PracticeCellMastery values', () => {
    expect(normalizeMasteryState(0)).toBe('Untested');
    expect(normalizeMasteryState(1)).toBe('Learning');
    expect(normalizeMasteryState(2)).toBe('Reviewing');
    expect(normalizeMasteryState(3)).toBe('Mastered');
    expect(normalizeMasteryState('learning')).toBe('Learning');
    expect(normalizeMasteryState('Mastered')).toBe('Mastered');
  });

  it('formats reopensAt in local time and rejects invalid/null timestamps', () => {
    expect(formatLocalReopenTime(null)).toBeNull();
    expect(formatLocalReopenTime('not-a-date')).toBeNull();
    const formatted = formatLocalReopenTime('2026-11-15T14:30:00Z');
    expect(formatted).toBeTruthy();
    expect(formatted).not.toContain('2026-11-15T14:30:00Z');
  });

  it('persists, reads, updates, and clears local personal notes for saved questions', () => {
    const key = { savedQuestionId: 123, itemVersionId: 456 };
    expect(readSavedNote(key)).toBe('');

    writeSavedNote(key, '  Remember prepositions with arrive in/at  ');
    expect(readSavedNote({ savedQuestionId: 123 })).toBe('Remember prepositions with arrive in/at');
    expect(readSavedNote({ itemVersionId: 456 })).toBe('Remember prepositions with arrive in/at');

    writeSavedNote(key, '   ');
    expect(readSavedNote(key)).toBe('');

    writeSavedNote(key, 'Keep for review');
    removeSavedNote(key);
    expect(readSavedNote(key)).toBe('');
  });

  it('parses acceptedAnswersJson and selectedOptionIdsJson safely', () => {
    expect(parseAcceptedAnswers('["budget","financial plan"]')).toEqual(['budget', 'financial plan']);
    expect(parseAcceptedAnswers('[["went","left"],["home"]]')).toEqual(['went / left', 'home']);
    expect(parseAcceptedAnswers('invalid')).toEqual([]);
    expect(parseSelectedOptionIds({ selectedOptionIds: [4, 7], selectedOptionIdsJson: null })).toEqual([4, 7]);
    expect(parseSelectedOptionIds({ selectedOptionIds: null, selectedOptionIdsJson: '[9, 11]' })).toEqual([9, 11]);
    expect(formatMsTime(65_000)).toBe('01:05');
  });
});
