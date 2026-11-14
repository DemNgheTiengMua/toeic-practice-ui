import { describe, expect, it, beforeEach } from 'vitest';
import {
  isLocalPath,
  resolveSafeReturnUrl,
  safeExternalHref,
  toBrowserMediaUrl,
  parseSafeInlineSegments,
  formatVnd,
  formatSignedVnd,
  formatTimerSeconds,
  countWords,
  STAFF_PERMISSIONS,
  canViewDashboard,
  canManageUsers,
  canManageCatalog,
  canManageBilling,
  canTriageSupport,
  VND_PER_CREDIT,
  creditsToVnd,
} from '../security';
import {
  computePerSectionUnanswered,
  enqueueOfflineMutation,
  dequeueOfflineMutation,
  readOfflineQueue,
  writeOfflineQueue,
  saveAudioProgress,
  readAudioProgressMap,
  formatSavePlacard,
  formatNotMeasuredReason,
} from '../sitting-queue';

describe('Security & Link-Injection Guards (Q12)', () => {
  it('allows only local paths and rejects open redirects', () => {
    expect(isLocalPath('/dashboard')).toBe(true);
    expect(isLocalPath('/placement/take/1?q=25')).toBe(true);
    expect(isLocalPath('//evil.com')).toBe(false);
    expect(isLocalPath('/\\evil.com')).toBe(false);
    expect(isLocalPath('https://evil.com/phish')).toBe(false);
    expect(isLocalPath('javascript:alert(1)')).toBe(false);
    expect(isLocalPath('')).toBe(false);
    expect(isLocalPath(null)).toBe(false);

    expect(resolveSafeReturnUrl('https://evil.com', '/dashboard')).toBe('/dashboard');
    expect(resolveSafeReturnUrl('//evil.com', '/dashboard')).toBe('/dashboard');
    expect(resolveSafeReturnUrl('/placement/take/1?q=25', '/dashboard')).toBe(
      '/placement/take/1?q=25',
    );
  });

  it('allows only http/https external URLs and blocks javascript/data/vbscript URLs', () => {
    expect(safeExternalHref('https://meet.google.com/abc-defg-hij')).toBe(
      'https://meet.google.com/abc-defg-hij',
    );
    expect(safeExternalHref('http://localhost:5000/room')).toBe('http://localhost:5000/room');
    expect(safeExternalHref('javascript:alert(document.cookie)')).toBeNull();
    expect(safeExternalHref('data:text/html,<script>alert(1)</script>')).toBeNull();
    expect(safeExternalHref('vbscript:msgbox(1)')).toBeNull();
    expect(safeExternalHref(null)).toBeNull();
  });

  it('normalizes media paths to /media/* for same-origin range streaming', () => {
    expect(toBrowserMediaUrl('imports/5/0123456789abcdef0123456789abcdef.mp3')).toBe(
      '/media/imports/5/0123456789abcdef0123456789abcdef.mp3',
    );
    expect(toBrowserMediaUrl('/api/media/exams/3/0123456789abcdef0123456789abcdef.jpg')).toBe(
      '/media/exams/3/0123456789abcdef0123456789abcdef.jpg',
    );
    expect(toBrowserMediaUrl('/media/exams/3/0123456789abcdef0123456789abcdef.mp3')).toBe(
      '/media/exams/3/0123456789abcdef0123456789abcdef.mp3',
    );
  });

  it('parses bold markers without executing HTML or corrupting Vietnamese diacritics', () => {
    const raw =
      "<script>alert('pwned')</script>**Đề thi thử TOEIC** và chọn đáp án đúng.";
    const segments = parseSafeInlineSegments(raw);
    expect(segments).toEqual([
      { type: 'text', value: "<script>alert('pwned')</script>" },
      { type: 'bold', value: 'Đề thi thử TOEIC' },
      { type: 'text', value: ' và chọn đáp án đúng.' },
    ]);
  });

  it('formats timers, VND currency, and word counts accurately', () => {
    expect(formatTimerSeconds(45)).toBe('00:45');
    expect(formatTimerSeconds(125)).toBe('02:05');
    expect(formatTimerSeconds(3661)).toBe('01:01:01');
    expect(countWords('what the fuck')).toBe(3);
    expect(countWords('   ')).toBe(0);
    expect(formatVnd(199000)).toContain('199.000');
    expect(formatSignedVnd(-250000)).toBe('\u2212250.000 \u20ab');
    expect(formatSignedVnd(250000)).toContain('250.000');
  });

  it('grants staff navigation solely from held capabilities (SEC-05)', () => {
    const learner = { role: 'learner', permissions: [] };
    const supportOnly = { role: 'staff', permissions: [STAFF_PERMISSIONS.SupportTriage] };
    const contentOnly = { role: 'staff', permissions: [STAFF_PERMISSIONS.ContentAuthor] };
    const fullAdmin = {
      role: 'admin',
      permissions: Object.values(STAFF_PERMISSIONS),
    };

    expect(canViewDashboard(learner)).toBe(false);
    expect(canTriageSupport(learner)).toBe(false);

    expect(canViewDashboard(supportOnly)).toBe(true);
    expect(canTriageSupport(supportOnly)).toBe(true);
    expect(canManageBilling(supportOnly)).toBe(true);
    expect(canManageUsers(supportOnly)).toBe(false);
    expect(canManageCatalog(supportOnly)).toBe(false);

    expect(canManageCatalog(contentOnly)).toBe(true);
    expect(canManageBilling(contentOnly)).toBe(false);

    expect(canManageUsers(fullAdmin)).toBe(true);
    expect(VND_PER_CREDIT).toBe(20000);
    expect(creditsToVnd(5)).toBe(100000);
  });
});

describe('Sitting Edge-Case State Helpers (PLC-12..PLC-19)', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('PLC-17: queues offline answers by questionId and formats honest offline placard', () => {
    enqueueOfflineMutation('placement', 9, {
      questionId: 101,
      selectedOptionId: 11,
      isFlagged: false,
    });
    expect(readOfflineQueue('placement', 9).map((i) => i.questionId)).toEqual([101]);

    const offlinePlacard = formatSavePlacard(false, 1, false, false);
    expect(offlinePlacard.text).toBe('Offline · 1 saved on this device');
    expect(offlinePlacard.tone).toBe('offline');

    const boundaryPlacard = formatSavePlacard(false, 1, false, true);
    expect(boundaryPlacard.text).toContain('Waiting for a connection');

    dequeueOfflineMutation('placement', 9, 101);
    expect(readOfflineQueue('placement', 9)).toEqual([]);

    const savedPlacard = formatSavePlacard(true, 0, false, false);
    expect(savedPlacard.text).toBe('Saved');
    expect(savedPlacard.tone).toBe('saved');
    writeOfflineQueue('placement', 9, []);
  });

  it('ADR-0010: persists single-play audio completion and monotonic currentTime per audioKey', () => {
    saveAudioProgress('placement', 9, 'shared-clip', { completed: false, currentTime: 4.2 });
    saveAudioProgress('placement', 9, 'shared-clip', { completed: true, currentTime: 1.0 });

    const map = readAudioProgressMap('placement', 9);
    expect(map['shared-clip']).toEqual({ completed: true, currentTime: 4.2 });
  });

  it('PLC-18: computes per-section unanswered counts in sitting order and keeps zero-count sections', () => {
    const sections = computePerSectionUnanswered(
      [
        {
          questionId: 10,
          displayOrder: 1,
          sectionId: 1,
          sectionCode: 'L1',
          modality: 'Listening',
          isAnswered: true,
          isFlagged: false,
          isCurrent: true,
        },
        {
          questionId: 11,
          displayOrder: 2,
          sectionId: 1,
          sectionCode: 'L1',
          modality: 'Listening',
          isAnswered: true,
          isFlagged: false,
          isCurrent: false,
        },
        {
          questionId: 20,
          displayOrder: 3,
          sectionId: 2,
          sectionCode: 'R1',
          modality: 'Reading',
          isAnswered: false,
          isFlagged: false,
          isCurrent: false,
        },
        {
          questionId: 21,
          displayOrder: 4,
          sectionId: 2,
          sectionCode: 'R1',
          modality: 'Reading',
          isAnswered: false,
          isFlagged: false,
          isCurrent: false,
        },
      ],
      [
        { questionId: 10, sectionId: 1, sectionCode: 'L1', sectionName: 'Listening section' },
        { questionId: 20, sectionId: 2, sectionCode: 'R1', sectionName: 'Reading section' },
      ],
    );

    expect(sections).toEqual([
      {
        sectionId: 1,
        sectionCode: 'L1',
        sectionName: 'Listening section',
        unansweredCount: 0,
        totalCount: 2,
      },
      {
        sectionId: 2,
        sectionCode: 'R1',
        sectionName: 'Reading section',
        unansweredCount: 2,
        totalCount: 2,
      },
    ]);
  });

  it('PLC-19: maps not-measured failureReason codes to learner copy', () => {
    expect(formatNotMeasuredReason('candidate_skipped')).toBe('You did not attempt this skill.');
    expect(formatNotMeasuredReason('inactivity_timeout')).toBe(
      'The sitting ended before this skill was reached.',
    );
    expect(formatNotMeasuredReason('ai_grading_failed')).toBe(
      'The grader could not score this task.',
    );
    expect(formatNotMeasuredReason(null)).toBe('Nothing was measured for this skill.');
  });
});
