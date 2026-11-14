import '../../sitting/__tests__/test-setup';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MockExamCatalogPage } from '../MockExamCatalogPage';
import { MockExamConfirmPage } from '../MockExamConfirmPage';
import {
  api,
  type MockExamInstructionsDto,
  type MockExamListViewDto,
} from '@/lib/api';

const mockNavigate = vi.fn();
let mockParams: Record<string, string> = {};

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  useParams: () => mockParams,
  useSearch: () => ({}),
  Link: ({
    to,
    params,
    children,
    className,
  }: {
    to: string;
    params?: Record<string, string>;
    children: ReactNode;
    className?: string;
  }) => {
    let href = to;
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        href = href.replace(`$${k}`, v);
      }
    }
    return (
      <a href={href} className={className}>
        {children}
      </a>
    );
  },
}));

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return {
    ...actual,
    api: {
      ...actual.api,
      exams: {
        ...actual.api.exams,
        list: vi.fn(),
        instructions: vi.fn(),
        start: vi.fn(),
      },
    },
  };
});

function makeCatalog(overrides: Partial<MockExamListViewDto> = {}): MockExamListViewDto {
  return {
    certificate: {
      code: 'TOEIC',
      name: 'TOEIC Listening & Reading',
    },
    creditBalance: 3,
    exams: [
      {
        examId: 101,
        certId: 1,
        certCode: 'TOEIC',
        name: 'TOEIC Full Mock Test 1',
        description: 'Complete mock exam with authentic timing.',
        durationMinutes: 120,
        questionCount: 200,
        sourceName: 'ETS Official 2026',
        creditCost: 1,
        activeAttemptId: null,
        activeAttemptExpiresAt: null,
        completedAttemptScore: null,
        completedAttemptBand: null,
      },
      {
        examId: 102,
        certId: 1,
        certCode: 'TOEIC',
        name: 'TOEIC Full Mock Test 2',
        description: 'Second full practice paper.',
        durationMinutes: 120,
        questionCount: 200,
        sourceName: 'ETS Official 2026',
        creditCost: 1,
        activeAttemptId: 555,
        activeAttemptExpiresAt: '2026-11-13T12:00:00Z',
        completedAttemptScore: 850,
        completedAttemptBand: 'Gold',
      },
    ],
    ...overrides,
  };
}

function makeInstructions(
  overrides: Partial<MockExamInstructionsDto> = {},
): MockExamInstructionsDto {
  return {
    examId: 101,
    certId: 1,
    certCode: 'TOEIC',
    examName: 'TOEIC Full Mock Test 1',
    description: 'Complete mock exam with authentic timing.',
    durationMinutes: 120,
    questionCount: 200,
    creditCost: 1,
    creditBalance: 3,
    activeAttemptId: null,
    sections: [
      {
        sectionId: 1,
        skillCode: 'listening',
        skillName: 'Listening',
        modality: 'listening',
        sectionCode: 'Part 1',
        sectionName: 'Photographs',
        expectedQuestionCount: 6,
        questionCount: 6,
        durationMinutes: 10,
        displayOrder: 1,
      },
      {
        sectionId: 2,
        skillCode: 'reading',
        skillName: 'Reading',
        modality: 'reading',
        sectionCode: 'Part 5',
        sectionName: 'Incomplete Sentences',
        expectedQuestionCount: 30,
        questionCount: 30,
        durationMinutes: 20,
        displayOrder: 2,
      },
    ],
    ...overrides,
  };
}

describe('MockExamCatalogPage & MockExamConfirmPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockParams = {};
  });

  it('renders catalog with credit balance, exam items, in-progress attempts, and out-of-credit banner', async () => {
    vi.mocked(api.exams.list).mockResolvedValue(makeCatalog());

    render(<MockExamCatalogPage />);

    expect(screen.getByText('Loading mock exams...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('TOEIC Full Mock Test 2')).toBeInTheDocument();
    expect(screen.getByText('In progress')).toBeInTheDocument();
    expect(screen.getByText(/Resume sitting/i)).toBeInTheDocument();
    expect(screen.getByText(/Take exam/i)).toBeInTheDocument();

    // Clicking "Take exam" navigates to confirm page
    fireEvent.click(screen.getByText(/Take exam/i));
    expect(mockNavigate).toHaveBeenCalledWith({
      to: '/exams/$examId',
      params: { examId: '101' },
    });
  });

  it('shows out of credits alert and blocks full exam start when balance is zero', async () => {
    vi.mocked(api.exams.list).mockResolvedValue(
      makeCatalog({
        creditBalance: 0,
      }),
    );

    render(<MockExamCatalogPage />);

    await waitFor(() => {
      expect(screen.getByText('You are out of credits')).toBeInTheDocument();
    });

    expect(screen.getByText('0')).toBeInTheDocument();
    const topUpBtn = screen.getByRole('button', { name: /top up wallet/i });
    expect(topUpBtn).toBeInTheDocument();
    fireEvent.click(topUpBtn);
    expect(mockNavigate).toHaveBeenCalledWith({
      to: '/checkout',
      search: { credits: 1, returnUrl: '/exams' },
    });
    expect(screen.getByText(/Practice free/i)).toBeInTheDocument();
  });

  it('renders confirmation page with credit check placard and blueprints', async () => {
    mockParams = { examId: '101' };
    vi.mocked(api.exams.instructions).mockResolvedValue(makeInstructions());

    render(<MockExamConfirmPage />);

    expect(screen.getByText('Loading exam details...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Before you begin: TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    expect(screen.getAllByText(/credits available/i).length).toBeGreaterThanOrEqual(1);

    // Section table
    expect(screen.getByText('Photographs')).toBeInTheDocument();
    expect(screen.getByText('Incomplete Sentences')).toBeInTheDocument();
  });

  it('allows starting full exam and navigates to sitting room', async () => {
    mockParams = { examId: '101' };
    vi.mocked(api.exams.instructions).mockResolvedValue(makeInstructions());
    vi.mocked(api.exams.start).mockResolvedValue({
      attemptId: 999,
      examId: 101,
      status: 'in_progress',
      startedAt: '2026-11-13T10:00:00Z',
      expiresAt: '2026-11-13T12:00:00Z',
      remainingSeconds: 7200,
      isAutoSubmitted: false,
    });

    render(<MockExamConfirmPage />);

    await waitFor(() => {
      expect(screen.getByText('Start mock exam')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Start mock exam'));

    await waitFor(() => {
      expect(api.exams.start).toHaveBeenCalledWith(101);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/exams/take/$attemptId',
        params: { attemptId: '999' },
      });
    });
  });

  it('allows choosing single section free practice option', async () => {
    mockParams = { examId: '101' };
    vi.mocked(api.exams.instructions).mockResolvedValue(
      makeInstructions({
        creditBalance: 0, // insufficient for full exam
      }),
    );

    render(<MockExamConfirmPage />);

    await waitFor(() => {
      expect(screen.getByText('Insufficient credits')).toBeInTheDocument();
    });

    // Select single section practice
    fireEvent.click(screen.getByText('Single section practice'));

    expect(screen.getByText('Choose section to practice')).toBeInTheDocument();
    expect(screen.getByText('Continue to free practice')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Continue to free practice'));
    expect(mockNavigate).toHaveBeenCalledWith({
      to: '/practice',
    });
  });
});
