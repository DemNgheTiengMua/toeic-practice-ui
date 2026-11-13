import { ApiClientError, PracticeCellMasteryValue } from '@/lib/api';

export type CanonicalMasteryState = 'Untested' | 'Learning' | 'Reviewing' | 'Mastered';

export interface QuestionAddressLike {
  questionId?: number | null;
  itemVersionId?: number | null;
  address?: number | null;
}

export interface SaveTargetLike {
  isSaved: boolean;
  savedQuestionId?: number | null;
  questionId?: number | null;
  itemVersionId?: number | null;
}

export type SaveTogglePlan =
  | { action: 'unsave'; savedQuestionId: number }
  | { action: 'save_question'; questionId: number }
  | { action: 'save_item_version'; itemVersionId: number };

export interface SavedNoteKey {
  savedQuestionId?: number | null;
  questionId?: number | null;
  itemVersionId?: number | null;
}

const NOTE_STORAGE_PREFIX = 'ee_saved_question_note:';

/**
 * Resolves the opaque practice question handle expected by WebAPI answer/hint endpoints:
 * legacy section drills use `questionId`; pool drills have `questionId == null` and use `itemVersionId`.
 */
export function getPracticeQuestionAddress(question: QuestionAddressLike): number {
  return question.questionId ?? question.itemVersionId ?? question.address ?? 0;
}

/**
 * Resolves the exact WebAPI endpoint action for saving or removing a bookmark.
 * Pool items (`questionId == null`) and legacy questions use separate POST endpoints; unsave always uses `savedQuestionId`.
 */
export function buildSaveTogglePlan(item: SaveTargetLike): SaveTogglePlan | null {
  if (item.isSaved) {
    return item.savedQuestionId != null
      ? { action: 'unsave', savedQuestionId: item.savedQuestionId }
      : null;
  }
  if (item.questionId != null) {
    return { action: 'save_question', questionId: item.questionId };
  }
  if (item.itemVersionId != null) {
    return { action: 'save_item_version', itemVersionId: item.itemVersionId };
  }
  return null;
}

/**
 * Enforces the StartPracticeRequest invariant: supply either `sectionId` or `(topicId, cefrLevelCode)`, never both.
 */
export function buildStartSessionPayload(params: {
  sectionId?: number | null;
  topicId?: number | null;
  cefrLevelCode?: string | null;
}): { sectionId?: number; topicId?: number; cefrLevelCode?: string } {
  const hasSection = params.sectionId != null;
  const hasCell = params.topicId != null && Boolean(params.cefrLevelCode);
  if (hasSection === hasCell) {
    throw new Error('Specify either a sectionId or a topic/CEFR cell, not both.');
  }
  if (hasSection) {
    return { sectionId: params.sectionId! };
  }
  return { topicId: params.topicId!, cefrLevelCode: params.cefrLevelCode! };
}

export function normalizeMasteryState(value: PracticeCellMasteryValue | undefined | null): CanonicalMasteryState {
  if (value === 1 || String(value).toLowerCase() === 'learning') return 'Learning';
  if (value === 2 || String(value).toLowerCase() === 'reviewing') return 'Reviewing';
  if (value === 3 || String(value).toLowerCase() === 'mastered') return 'Mastered';
  return 'Untested';
}

export function formatLocalReopenTime(isoString?: string | null): string | null {
  if (!isoString) return null;
  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) return null;
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(date);
}

function getStorageKeys(key: SavedNoteKey): string[] {
  const keys: string[] = [];
  if (key.savedQuestionId != null) keys.push(`${NOTE_STORAGE_PREFIX}sq:${key.savedQuestionId}`);
  if (key.itemVersionId != null) keys.push(`${NOTE_STORAGE_PREFIX}iv:${key.itemVersionId}`);
  if (key.questionId != null) keys.push(`${NOTE_STORAGE_PREFIX}q:${key.questionId}`);
  return keys;
}

function getLocalStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' && window.localStorage ? window.localStorage : null;
  } catch {
    return null;
  }
}

export function readSavedNote(key: SavedNoteKey): string {
  const storage = getLocalStorage();
  if (!storage) return '';
  for (const storageKey of getStorageKeys(key)) {
    const val = storage.getItem(storageKey);
    if (val != null && val.trim().length > 0) {
      return val;
    }
  }
  return '';
}

export function writeSavedNote(key: SavedNoteKey, note: string): string {
  const trimmed = note.trim();
  const storage = getLocalStorage();
  if (!storage) return trimmed;
  for (const storageKey of getStorageKeys(key)) {
    if (trimmed) {
      storage.setItem(storageKey, trimmed);
    } else {
      storage.removeItem(storageKey);
    }
  }
  return trimmed;
}

export function removeSavedNote(key: SavedNoteKey): void {
  const storage = getLocalStorage();
  if (!storage) return;
  for (const storageKey of getStorageKeys(key)) {
    storage.removeItem(storageKey);
  }
}

export function parseAcceptedAnswers(acceptedAnswersJson?: string | null): string[] {
  if (!acceptedAnswersJson) return [];
  try {
    const parsed = JSON.parse(acceptedAnswersJson);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .map((entry) => {
        if (Array.isArray(entry)) {
          return entry.map(String).filter(Boolean).join(' / ');
        }
        return entry != null ? String(entry) : '';
      })
      .filter((s) => s.length > 0);
  } catch {
    return [];
  }
}

export function parseSelectedOptionIds(input: {
  selectedOptionIds?: number[] | null;
  selectedOptionIdsJson?: string | null;
}): number[] {
  if (Array.isArray(input.selectedOptionIds) && input.selectedOptionIds.length > 0) {
    return input.selectedOptionIds;
  }
  if (!input.selectedOptionIdsJson) return [];
  try {
    const parsed = JSON.parse(input.selectedOptionIdsJson);
    return Array.isArray(parsed)
      ? parsed.map((id) => Number(id)).filter((n) => Number.isFinite(n))
      : [];
  } catch {
    return [];
  }
}

export function formatMsTime(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '00:00';
  const totalSeconds = Math.floor(ms / 1000);
  const mins = Math.floor(totalSeconds / 60);
  const secs = totalSeconds % 60;
  return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
}

export function getPracticeErrorMessage(err: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (err instanceof ApiClientError) {
    return err.detail || err.title || fallback;
  }
  if (err instanceof Error && err.message) {
    return err.message;
  }
  return fallback;
}
