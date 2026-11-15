import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent, act } from '@testing-library/react';
import { WritingPracticePage } from '../WritingPracticePage';
import { api } from '@/lib/api';

const mockNavigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  Link: ({
    to,
    params,
    children,
    ...props
  }: {
    to: string;
    params?: Record<string, string>;
    children: React.ReactNode;
    [k: string]: unknown;
  }) => {
    const resolved = params
      ? Object.entries(params).reduce((acc, [k, v]) => acc.replace(`$${k}`, v), to)
      : to;
    return (
      <a href={resolved} {...props}>
        {children}
      </a>
    );
  },
}));

vi.mock('@/lib/api', () => ({
  api: {
    writing: {
      take: vi.fn(),
      saveDraft: vi.fn(),
      submit: vi.fn(),
    },
  },
}));

function makeWritingTake(responseText = '') {
  return {
    attemptId: 505,
    examId: 21,
    examName: 'IELTS Writing Task 1 & 2',
    certId: 1,
    certCode: 'IELTS',
    status: 'in_progress' as const,
    startedAt: '2026-11-13T11:00:00Z',
    expiresAt: '2026-11-13T12:00:00Z',
    remainingSeconds: 3500,
    currentQuestionId: 1001,
    currentQuestionIndex: 0,
    totalQuestions: 2,
    question: {
      questionId: 1001,
      displayOrder: 1,
      stem: 'Summarize the main trends shown in the energy consumption chart.',
      minWords: 150,
      maxWords: 250,
      responseSeconds: 1200,
      hint: 'Compare the highest and lowest categories.',
      sectionCode: 'T1',
      sectionName: 'Task 1: Chart analysis',
      groupImagePath: '/media/writing/chart.png',
      groupImageAlt: 'Energy chart',
      responseText,
      draftSavedAt: null,
      status: 'draft' as const,
    },
    questions: [
      {
        questionId: 1001,
        displayOrder: 1,
        sectionCode: 'T1',
        sectionName: 'Task 1',
        isAnswered: Boolean(responseText),
        isCurrent: true,
      },
      {
        questionId: 1002,
        displayOrder: 2,
        sectionCode: 'T2',
        sectionName: 'Task 2',
        isAnswered: false,
        isCurrent: false,
      },
    ],
  };
}

describe('WritingPracticePage (/writing/practice/$questionId)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    if (!globalThis.CSS || typeof globalThis.CSS.escape !== 'function') {
      (globalThis as unknown as { CSS: { escape: (s: string) => string } }).CSS = {
        escape: (s: string) => s,
      };
    }
  });

  afterEach(() => {
    cleanup();
  });

  it('tracks live countWords, saves draft, warns about unanswered task on submit, and navigates to result', async () => {
    vi.mocked(api.writing.take).mockResolvedValue(makeWritingTake(''));
    vi.mocked(api.writing.saveDraft).mockResolvedValue({
      freeResponseId: 77,
      questionId: 1001,
      savedAt: '2026-11-13T11:02:00Z',
      wordCount: 4,
    });
    vi.mocked(api.writing.submit).mockResolvedValue({
      attemptId: 505,
      status: 'submitted',
      submittedAt: '2026-11-13T11:05:00Z',
    });

    render(<WritingPracticePage attemptId={505} />);

    expect(
      await screen.findByText('Summarize the main trends shown in the energy consumption chart.'),
    ).toBeTruthy();

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement;
    expect(textarea.getAttribute('spellcheck')).toBe('false');

    const validEssay = 'Solar output rose sharply across every measured region during the final quarter';
    fireEvent.change(textarea, { target: { value: validEssay } });

    expect(screen.getByText(/12 \/ 1,000 words/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /#1 \(12w\)/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /#2 Open/i })).toBeTruthy();

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));
    });

    await waitFor(() => {
      expect(api.writing.saveDraft).toHaveBeenCalledWith(505, {
        questionId: 1001,
        responseText: validEssay,
      });
    });

    fireEvent.click(screen.getByRole('button', { name: /Submit attempt/i }));

    expect(await screen.findByText(/Unanswered tasks remain/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /Confirm submit/i }));

    await waitFor(() => {
      expect(api.writing.submit).toHaveBeenCalledWith(505);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/writing/result/$attemptId',
        params: { attemptId: '505' },
      });
    });
  });

  it('displays live word/character bounds errors and disables submit when out of bounds', async () => {
    vi.mocked(api.writing.take).mockResolvedValue(makeWritingTake(''));

    render(<WritingPracticePage attemptId={505} />);
    await screen.findByText('Summarize the main trends shown in the energy consumption chart.');

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement;
    const submitBtn = screen.getByRole('button', { name: /Submit attempt/i });

    const isDisabled = (el: HTMLElement) =>
      (el as HTMLButtonElement).disabled || el.getAttribute('aria-disabled') === 'true';

    // Empty -> submit disabled
    expect(isDisabled(submitBtn)).toBe(true);

    // < 10 words -> warning/error message + submit disabled
    fireEvent.change(textarea, { target: { value: 'Only four words here' } });
    expect(screen.getByText(/4 \/ 1,000 words/i)).toBeTruthy();
    expect(screen.getByText(/Response must be at least 10 words/i)).toBeTruthy();
    expect(isDisabled(submitBtn)).toBe(true);

    // > 5,000 characters -> error message + submit disabled
    fireEvent.change(textarea, { target: { value: 'word '.repeat(1001).trim() } });
    expect(screen.getByText(/cannot exceed/i)).toBeTruthy();
    expect(isDisabled(submitBtn)).toBe(true);

    // Valid (10+ words, <= 5,000 chars) -> submit enabled
    fireEvent.change(textarea, {
      target: { value: 'One two three four five six seven eight nine ten eleven.' },
    });
    expect(isDisabled(submitBtn)).toBe(false);
  });
});
