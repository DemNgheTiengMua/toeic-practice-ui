/**
 * Sitting Edge-Case State Helpers (PLC-12..PLC-18, ADR-0010, ADR-0015)
 *
 * - Offline answer & draft queue in localStorage (`PLC-17`).
 * - Single-play audio progress & completion lock per `audioKey` (`ADR-0010`).
 * - Per-section unanswered calculation for submit confirmation modal (`PLC-18`).
 */

export type SittingKind = 'placement' | 'mock' | 'speaking' | 'writing';

export interface QueuedSittingMutation {
  questionId: number;
  selectedOptionId?: number | null;
  isFlagged?: boolean;
  responseText?: string | null;
  queuedAt: number;
}

export interface StoredAudioProgress {
  completed: boolean;
  currentTime: number;
}

export interface NavigatorCell {
  questionId: number;
  displayOrder: number;
  sectionId: number;
  sectionCode: string;
  sectionName?: string;
  modality: string;
  isAnswered: boolean;
  isFlagged: boolean;
  isCurrent: boolean;
  isLocked?: boolean;
}

export interface SectionUnansweredSummary {
  sectionId: number;
  sectionCode: string;
  sectionName: string;
  unansweredCount: number;
  totalCount: number;
}

function queueStorageKey(kind: SittingKind, id: number): string {
  return `ee_${kind}_queue_${id}`;
}

function audioStorageKey(kind: SittingKind, id: number): string {
  return `ee_${kind}_audio_${id}`;
}

export function readOfflineQueue(kind: SittingKind, id: number): QueuedSittingMutation[] {
  try {
    const raw = window.localStorage.getItem(queueStorageKey(kind, id));
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function writeOfflineQueue(
  kind: SittingKind,
  id: number,
  items: QueuedSittingMutation[],
): void {
  try {
    const key = queueStorageKey(kind, id);
    if (items.length === 0) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, JSON.stringify(items));
    }
  } catch {
    // Ignore storage quota errors
  }
}

export function enqueueOfflineMutation(
  kind: SittingKind,
  id: number,
  mutation: Omit<QueuedSittingMutation, 'queuedAt'>,
): QueuedSittingMutation[] {
  const existing = readOfflineQueue(kind, id);
  const next = existing.filter((item) => item.questionId !== mutation.questionId);
  next.push({ ...mutation, queuedAt: Date.now() });
  writeOfflineQueue(kind, id, next);
  return next;
}

export function dequeueOfflineMutation(
  kind: SittingKind,
  id: number,
  questionId: number,
): QueuedSittingMutation[] {
  const existing = readOfflineQueue(kind, id);
  const next = existing.filter((item) => item.questionId !== questionId);
  writeOfflineQueue(kind, id, next);
  return next;
}

export function readAudioProgressMap(
  kind: SittingKind,
  id: number,
): Record<string, StoredAudioProgress> {
  try {
    const raw = window.localStorage.getItem(audioStorageKey(kind, id));
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}

export function saveAudioProgress(
  kind: SittingKind,
  id: number,
  audioKey: string,
  progress: StoredAudioProgress,
): void {
  if (!audioKey) return;
  try {
    const map = readAudioProgressMap(kind, id);
    const prev = map[audioKey];
    map[audioKey] = {
      completed: Boolean(prev?.completed || progress.completed),
      currentTime: Math.max(prev?.currentTime ?? 0, progress.currentTime),
    };
    window.localStorage.setItem(audioStorageKey(kind, id), JSON.stringify(map));
  } catch {
    // Ignore storage errors
  }
}

/**
 * PLC-18: Computes per-section unanswered counts in sitting order.
 * Sections with 0 unanswered keep their entry with unansweredCount = 0 so the UI
 * can hide them cleanly without reshuffling section order.
 */
export function computePerSectionUnanswered(
  navigator: NavigatorCell[],
  questions?: Array<{
    questionId: number;
    sectionId: number;
    sectionCode: string;
    sectionName: string;
  }>,
): SectionUnansweredSummary[] {
  const sectionNameById = new Map<number, string>();
  if (questions) {
    for (const q of questions) {
      if (q.sectionName && !sectionNameById.has(q.sectionId)) {
        sectionNameById.set(q.sectionId, q.sectionName);
      }
    }
  }

  const order: number[] = [];
  const bySection = new Map<number, SectionUnansweredSummary>();

  for (const item of navigator) {
    let summary = bySection.get(item.sectionId);
    if (!summary) {
      order.push(item.sectionId);
      summary = {
        sectionId: item.sectionId,
        sectionCode: item.sectionCode,
        sectionName:
          item.sectionName ||
          sectionNameById.get(item.sectionId) ||
          item.sectionCode ||
          `Section ${item.sectionId}`,
        unansweredCount: 0,
        totalCount: 0,
      };
      bySection.set(item.sectionId, summary);
    }
    summary.totalCount += 1;
    if (!item.isAnswered) {
      summary.unansweredCount += 1;
    }
  }

  return order.map((id) => bySection.get(id)!);
}

/**
 * PLC-17: Formats the status placard text so an offline or failed save never claims "Saved".
 */
export function formatSavePlacard(
  isOnline: boolean,
  queuedCount: number,
  isSaving: boolean,
  waitingForBoundary: boolean,
): { text: string; tone: 'saved' | 'saving' | 'offline' | 'warning' } {
  if (waitingForBoundary && queuedCount > 0) {
    return {
      text: `Waiting for a connection · ${queuedCount} saved on this device`,
      tone: 'offline',
    };
  }
  if (!isOnline || queuedCount > 0) {
    return {
      text: `Offline · ${queuedCount} saved on this device`,
      tone: 'offline',
    };
  }
  if (isSaving) {
    return {
      text: 'Saving...',
      tone: 'saving',
    };
  }
  return {
    text: 'Saved',
    tone: 'saved',
  };
}

/**
 * PLC-19 / PLC-35: Maps placement skill failureReason codes to learner-facing copy.
 */
export function formatNotMeasuredReason(failureReason: string | null | undefined): string {
  switch (failureReason) {
    case 'candidate_skipped':
      return 'You did not attempt this skill.';
    case 'inactivity_timeout':
      return 'The sitting ended before this skill was reached.';
    case 'ai_grading_failed':
      return 'The grader could not score this task.';
    default:
      return 'Nothing was measured for this skill.';
  }
}
