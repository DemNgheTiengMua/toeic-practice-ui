import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { WritingStudioPage } from '../WritingStudioPage';
import { api } from '@/lib/api';

const mockNavigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  Link: ({
    to,
    params,
    search,
    children,
    ...props
  }: {
    to: string;
    params?: Record<string, string>;
    search?: Record<string, unknown>;
    children: React.ReactNode;
    [k: string]: unknown;
  }) => {
    let resolved = params
      ? Object.entries(params).reduce((acc, [k, v]) => acc.replace(`$${k}`, v), to)
      : to;
    if (search) {
      const q = new URLSearchParams();
      for (const [k, v] of Object.entries(search)) {
        if (v !== undefined) q.set(k, String(v));
      }
      const qs = q.toString();
      if (qs) resolved += `?${qs}`;
    }
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
      catalog: vi.fn(),
      instructions: vi.fn(),
      start: vi.fn(),
    },
  },
}));

function makeCatalog(creditBalance = 6) {
  return {
    certificate: { code: 'IELTS', name: 'IELTS Academic', sectionNoun: 'Task' },
    creditBalance,
    exams: [
      {
        examId: 21,
        name: 'IELTS Writing Task 1 & 2',
        description: 'Academic chart report and argumentative essay.',
        durationMinutes: 60,
        creditCost: 2,
        activeAttemptId: null,
        latestGradedAttemptId: 91,
        latestGradedAt: '2026-11-11T08:00:00Z',
      },
    ],
  };
}

function makeInstructions(creditBalance = 6, creditCost = 2) {
  return {
    examId: 21,
    certId: 1,
    certCode: 'IELTS',
    examName: 'IELTS Writing Task 1 & 2',
    description: 'Academic chart report and argumentative essay.',
    durationMinutes: 60,
    questionCount: 2,
    creditCost,
    creditBalance,
    activeAttemptId: null,
    sections: [
      {
        sectionId: 201,
        code: 'T1',
        name: 'Task 1: Chart analysis',
        expectedQuestionCount: 1,
        responseSeconds: 1200,
        displayOrder: 1,
      },
      {
        sectionId: 202,
        code: 'T2',
        name: 'Task 2: Essay',
        expectedQuestionCount: 1,
        responseSeconds: 2400,
        displayOrder: 2,
      },
    ],
    attempts: [
      {
        attemptId: 91,
        status: 'graded',
        startedAt: '2026-11-11T07:00:00Z',
        submittedAt: '2026-11-11T07:55:00Z',
        canReview: true,
        reopenPath: '/writing/review/91',
      },
      {
        attemptId: 92,
        status: 'in_progress',
        startedAt: '2026-11-12T07:00:00Z',
        submittedAt: null,
        canReview: false,
        reopenPath: '/writing/take/92',
      },
    ],
  };
}

describe('WritingStudioPage (/writing)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('renders writing catalog, instructions, and prior result link then starts writing session', async () => {
    vi.mocked(api.writing.catalog).mockResolvedValue(makeCatalog(6));
    vi.mocked(api.writing.instructions).mockResolvedValue(makeInstructions(6, 2));
    vi.mocked(api.writing.start).mockResolvedValue({
      attemptId: 505,
    });

    render(<WritingStudioPage />);

    expect(await screen.findByText('IELTS Writing Task 1 & 2')).toBeTruthy();
    expect(await screen.findByText(/Task 1: Chart analysis/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /View result/i }).getAttribute('href')).toBe('/writing/result/91');
    expect(screen.getByText('Graded')).toBeTruthy();
    expect(screen.getByText('In progress')).toBeTruthy();
    expect(screen.queryByText('in_progress')).toBeNull();

    const startBtn = screen.getByRole('button', { name: /Start writing session/i }) as HTMLButtonElement;
    expect(startBtn.disabled).toBe(false);
    fireEvent.click(startBtn);

    await waitFor(() => {
      expect(api.writing.start).toHaveBeenCalledWith(21);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/writing/practice/$questionId',
        params: { questionId: '505' },
      });
    });
  });

  it('blocks start with checkout and pricing links when credits are insufficient', async () => {
    vi.mocked(api.writing.catalog).mockResolvedValue(makeCatalog(0));
    vi.mocked(api.writing.instructions).mockResolvedValue(makeInstructions(0, 2));

    render(<WritingStudioPage />);

    expect(await screen.findByText(/Not enough credits to start/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Top up wallet/i }).getAttribute('href')).toBe(
      '/checkout?credits=1&returnUrl=%2Fpractice%2Fwriting',
    );
    expect(screen.getByRole('link', { name: /View credit pricing/i }).getAttribute('href')).toBe('/pricing');
    const startBtn = screen.getByRole('button', { name: /Start writing session/i }) as HTMLButtonElement;
    expect(startBtn.disabled).toBe(true);
    expect(api.writing.start).not.toHaveBeenCalled();
  });
});
