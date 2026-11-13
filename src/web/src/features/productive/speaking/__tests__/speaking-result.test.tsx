import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { SpeakingResultPage } from '../SpeakingResultPage';
import { api } from '@/lib/api';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: { to: string; children: React.ReactNode; [k: string]: unknown }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));

vi.mock('@/lib/api', () => ({
  api: {
    speaking: {
      pending: vi.fn(),
      retry: vi.fn(),
      review: vi.fn(),
    },
  },
}));

function makePending(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    attemptId: 404,
    examId: 11,
    examName: 'IELTS Speaking Set 1',
    certCode: 'IELTS',
    attemptStatus: 'queued',
    totalResponses: 2,
    gradedResponses: 1,
    failedResponses: 0,
    pendingResponses: 1,
    canRetry: false,
    failureReason: null,
    items: [
      { freeResponseId: 1, questionId: 901, displayOrder: 1, status: 'done', attemptCount: 1, failureReason: null },
      { freeResponseId: 2, questionId: 902, displayOrder: 2, status: 'processing', attemptCount: 1, failureReason: null },
    ],
    ...overrides,
  };
}

function makeReview(scoreDisplayFactor = 10) {
  return {
    attemptId: 404,
    examId: 11,
    examName: 'IELTS Speaking Set 1',
    certCode: 'IELTS',
    submittedAt: '2026-11-13T10:15:00Z',
    totalQuestions: 2,
    answeredQuestions: 1,
    totalScore: 22,
    maxScore: 30,
    scaledScore: scoreDisplayFactor === 10 ? 75 : null,
    scaledScoreMax: scoreDisplayFactor === 10 ? 90 : null,
    scoreDisplayFactor,
    estimatedBandCode: 'C1',
    estimatedBandName: 'Effective Operational Proficiency',
    gradedQuestions: 1,
    lowestScoredQuestion: {
      questionId: 901,
      displayOrder: 1,
      sectionName: 'Part 2: Cue card',
      stem: 'Describe a memorable trip.',
      audioPath: 'audio/404/901.webm',
      durationMs: 28000,
      transcript: 'Last summer I visited Da Nang with my family.',
      totalScore: 22,
      maxScore: 30,
      criteria: [],
    },
    questions: [
      {
        questionId: 901,
        displayOrder: 1,
        sectionName: 'Part 2: Cue card',
        stem: 'Describe a memorable trip.',
        audioPath: 'audio/404/901.webm',
        durationMs: 28000,
        transcript: 'Last summer I visited Da Nang with my family.',
        totalScore: 22,
        maxScore: 30,
        criteria: [
          {
            criterionId: 1,
            code: 'pronunciation',
            name: 'Pronunciation & intonation',
            score: 8,
            maxScore: 10,
            comment: 'Clear consonant endings and natural sentence stress.',
          },
          {
            criterionId: 2,
            code: 'fluency',
            name: 'Fluency & coherence',
            score: 7,
            maxScore: 10,
            comment: '',
          },
        ],
      },
    ],
  };
}

describe('SpeakingResultPage (/speaking/result/$attemptId)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('polls queued/processing grading until graded then renders tenths scaled score, criteria, and single-task caveat', async () => {
    vi.mocked(api.speaking.pending)
      .mockResolvedValueOnce(makePending({ attemptStatus: 'queued' }))
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'graded',
          gradedResponses: 2,
          pendingResponses: 0,
          items: [
            { freeResponseId: 1, questionId: 901, displayOrder: 1, status: 'done', attemptCount: 1, failureReason: null },
            { freeResponseId: 2, questionId: 902, displayOrder: 2, status: 'done', attemptCount: 1, failureReason: null },
          ],
        }),
      );
    vi.mocked(api.speaking.review).mockResolvedValue(makeReview(10));

    render(<SpeakingResultPage attemptId={404} pollIntervalMs={20} />);

    expect(await screen.findByText(/AI grading in progress/i)).toBeTruthy();

    await waitFor(() => {
      expect(api.speaking.review).toHaveBeenCalledWith(404);
      expect(screen.getByText('7.5')).toBeTruthy();
    });

    expect(screen.queryByText(/\b75\b/)).toBeNull();
    expect(screen.getByText(/Pronunciation & intonation/i)).toBeTruthy();
    expect(screen.getByText(/No note was recorded for this criterion on this grading\./i)).toBeTruthy();
    expect(screen.getByText(/Estimated from one task/i)).toBeTruthy();
    expect(screen.getByText(/Last summer I visited Da Nang with my family\./i)).toBeTruthy();
  });

  it('surfaces retryable failure alert and resumes polling after free retry', async () => {
    vi.mocked(api.speaking.pending)
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'submitted',
          failedResponses: 1,
          pendingResponses: 0,
          canRetry: true,
          failureReason: 'Speech evaluator timed out.',
        }),
      )
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'graded',
          gradedResponses: 2,
          failedResponses: 0,
          pendingResponses: 0,
          canRetry: false,
        }),
      );
    vi.mocked(api.speaking.retry).mockResolvedValue(undefined);
    vi.mocked(api.speaking.review).mockResolvedValue(makeReview(1));

    render(<SpeakingResultPage attemptId={404} pollIntervalMs={20} />);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/Speech evaluator timed out/i);

    fireEvent.click(screen.getByRole('button', { name: /Retry grading/i }));

    await waitFor(() => {
      expect(api.speaking.retry).toHaveBeenCalledWith(404);
      expect(api.speaking.review).toHaveBeenCalledWith(404);
    });
  });
});
