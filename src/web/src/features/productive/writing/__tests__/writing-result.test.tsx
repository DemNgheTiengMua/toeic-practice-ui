import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { WritingResultPage } from '../WritingResultPage';
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
    writing: {
      pending: vi.fn(),
      retry: vi.fn(),
      review: vi.fn(),
    },
  },
}));

function makePending(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    attemptId: 505,
    examId: 21,
    examName: 'IELTS Writing Task 1 & 2',
    certCode: 'IELTS',
    attemptStatus: 'processing',
    totalResponses: 1,
    gradedResponses: 0,
    failedResponses: 0,
    pendingResponses: 1,
    canRetry: false,
    failureReason: null,
    items: [
      { freeResponseId: 7, questionId: 1001, displayOrder: 1, status: 'processing', attemptCount: 1, failureReason: null },
    ],
    ...overrides,
  };
}

function makeReview() {
  return {
    attemptId: 505,
    examId: 21,
    examName: 'IELTS Writing Task 1 & 2',
    certCode: 'IELTS',
    submittedAt: '2026-11-13T11:05:00Z',
    totalQuestions: 2,
    answeredQuestions: 1,
    totalScore: 18,
    maxScore: 24,
    estimatedBandCode: 'B2',
    estimatedBandName: 'Upper Intermediate',
    gradedQuestions: 1,
    questions: [
      {
        questionId: 1001,
        displayOrder: 1,
        sectionName: 'Task 1: Chart analysis',
        stem: 'Summarize the main trends shown in the energy chart.',
        responseText: 'Solar output grow fast in 2024.',
        wordCount: 162,
        totalScore: 18,
        maxScore: 24,
        criteria: [
          {
            criterionId: 11,
            code: 'task_achievement',
            name: 'Task achievement',
            score: 5,
            maxScore: 6,
            comment: 'Covers **key peaks** clearly.',
          },
          {
            criterionId: 12,
            code: 'cohesion',
            name: 'Coherence & cohesion',
            score: 4,
            maxScore: 6,
            comment: '',
          },
        ],
        errorSpans: [
          {
            originalSnippet: 'Solar output grow',
            correctedSnippet: 'Solar output grew',
            category: 'grammar',
            explanationVi: 'Use **past simple** for 2024 data <script>alert(1)</script>.',
          },
          {
            originalSnippet: 'very much big',
            correctedSnippet: 'substantial',
            category: 'unrecognized_bucket',
            explanationVi: 'Academic register adjustment.',
          },
        ],
        priorityFix: 'Tighten **verb tense** consistency across historical dates.',
        modelRewrite: 'Solar generation expanded sharply throughout **2024**.',
      },
    ],
  };
}

describe('WritingResultPage (/writing/result/$attemptId)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('polls queued/processing grading then renders safe inline feedback, error categories, and single-task caveat', async () => {
    vi.mocked(api.writing.pending)
      .mockResolvedValueOnce(makePending({ attemptStatus: 'queued' }))
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'graded',
          gradedResponses: 1,
          pendingResponses: 0,
          items: [
            { freeResponseId: 7, questionId: 1001, displayOrder: 1, status: 'done', attemptCount: 1, failureReason: null },
          ],
        }),
      );
    vi.mocked(api.writing.review).mockResolvedValue(makeReview());

    const { container } = render(<WritingResultPage attemptId={505} pollIntervalMs={20} />);

    expect(await screen.findByText(/AI grading in progress/i)).toBeTruthy();
    expect(screen.getByText('Queued')).toBeTruthy();
    expect(screen.getByText('Processing')).toBeTruthy();

    await waitFor(() => {
      expect(api.writing.review).toHaveBeenCalledWith(505);
      expect(screen.getByText(/Task achievement/i)).toBeTruthy();
    });

    expect(screen.getByText('Grammar')).toBeTruthy();
    expect(screen.getByText('Error not classified')).toBeTruthy();
    expect(screen.getByText(/No note was recorded for this criterion on this grading\./i)).toBeTruthy();
    expect(screen.getByText(/Estimated from one task/i)).toBeTruthy();
    expect(screen.getByText(/verb tense/i).tagName).toBe('STRONG');
    expect(container.querySelector('script')).toBeNull();
    expect(screen.getByText(/<script>alert\(1\)<\/script>/i)).toBeTruthy();

    // HeroUI v3 ProgressBar for criteria & 3-column correction Table & inline highlighted errorSpan
    expect(screen.getAllByRole('progressbar').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole('grid', { name: /corrections/i })).toBeTruthy();
    expect(screen.getByText('Original')).toBeTruthy();
    expect(screen.getByText('Corrected')).toBeTruthy();
    expect(screen.getByText('Vietnamese Explanation')).toBeTruthy();
    const marks = container.querySelectorAll('mark');
    expect(marks.length).toBeGreaterThanOrEqual(1);
    expect(marks[0].textContent).toContain('Solar output grow');
  });

  it('surfaces failed grading alert and retries cleanly without extra charge', async () => {
    vi.mocked(api.writing.pending)
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'submitted',
          failedResponses: 1,
          pendingResponses: 0,
          canRetry: true,
          failureReason: 'LLM grader returned malformed rubric JSON.',
        }),
      )
      .mockResolvedValueOnce(
        makePending({
          attemptStatus: 'graded',
          gradedResponses: 1,
          failedResponses: 0,
          pendingResponses: 0,
          canRetry: false,
        }),
      );
    vi.mocked(api.writing.retry).mockResolvedValue(undefined);
    vi.mocked(api.writing.review).mockResolvedValue(makeReview());

    render(<WritingResultPage attemptId={505} pollIntervalMs={20} />);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/malformed rubric JSON/i);

    fireEvent.click(screen.getByRole('button', { name: /Retry grading/i }));

    await waitFor(() => {
      expect(api.writing.retry).toHaveBeenCalledWith(505);
      expect(api.writing.review).toHaveBeenCalledWith(505);
    });
  });
});
