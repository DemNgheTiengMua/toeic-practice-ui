import '../../sitting/__tests__/test-setup';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MockExamTakePage } from '../MockExamTakePage';
import { MockExamResultPage } from '../MockExamResultPage';
import {
  api,
  ApiClientError,
  type AttemptResultDto,
  type AttemptTakeDto,
  type PreflightStatusDto,
} from '@/lib/api';

const mockNavigate = vi.fn();
let mockParams: Record<string, string> = { attemptId: '88' };
let mockSearch: Record<string, unknown> = {};

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  useParams: () => mockParams,
  useSearch: () => mockSearch,
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
        take: vi.fn(),
        saveAnswer: vi.fn(),
        saveDraft: vi.fn(),
        submit: vi.fn(),
        result: vi.fn(),
        preflightStatus: vi.fn(),
        verifyPreflight: vi.fn(),
      },
    },
  };
});

function makeAttemptTake(overrides: Partial<AttemptTakeDto> = {}): AttemptTakeDto {
  return {
    attemptId: 88,
    examId: 101,
    examName: 'TOEIC Full Mock Test 1',
    certCode: 'TOEIC',
    status: 'in_progress',
    startedAt: '2026-11-13T10:00:00Z',
    expiresAt: '2026-11-13T12:00:00Z',
    remainingSeconds: 3600,
    totalQuestions: 2,
    answeredCount: 0,
    flaggedCount: 0,
    currentQuestion: {
      questionId: 1001,
      displayOrder: 1,
      sectionId: 1,
      sectionCode: 'Part 1',
      sectionName: 'Photographs',
      skillCode: 'listening',
      skillName: 'Listening',
      modality: 'listening',
      stem: 'What is shown in the image?',
      options: [
        { optionId: 1, label: 'A', text: 'A person is reading a book.' },
        { optionId: 2, label: 'B', text: 'A person is writing code.' },
      ],
      selectedOptionId: null,
      isFlagged: false,
    },
    navigator: [
      {
        questionId: 1001,
        displayOrder: 1,
        sectionId: 1,
        sectionCode: 'Part 1',
        modality: 'listening',
        isAnswered: false,
        isFlagged: false,
        isCurrent: true,
      },
      {
        questionId: 1002,
        displayOrder: 2,
        sectionId: 1,
        sectionCode: 'Part 1',
        modality: 'listening',
        isAnswered: false,
        isFlagged: false,
        isCurrent: false,
      },
    ],
    isAutoSubmitted: false,
    questions: [],
    activeUnitNumber: 1,
    ...overrides,
  };
}

function makeAttemptResult(overrides: Partial<AttemptResultDto> = {}): AttemptResultDto {
  return {
    attemptId: 88,
    examId: 101,
    examName: 'TOEIC Full Mock Test 1',
    certId: 1,
    certCode: 'TOEIC',
    certName: 'TOEIC Listening & Reading',
    status: 'graded',
    startedAt: '2026-11-13T10:00:00Z',
    submittedAt: '2026-11-13T11:45:00Z',
    durationSeconds: 6300,
    isAutoSubmitted: false,
    totalScore: 840,
    bandCode: 'gold',
    bandName: 'Gold (860+)',
    skillScores: [
      {
        skillId: 1,
        skillCode: 'listening',
        skillName: 'Listening',
        modality: 'listening',
        rawScore: 90,
        scaledScore: 430,
        totalQuestions: 100,
      },
      {
        skillId: 2,
        skillCode: 'reading',
        skillName: 'Reading',
        modality: 'reading',
        rawScore: 85,
        scaledScore: 410,
        totalQuestions: 100,
      },
    ],
    previousAttemptScore: 800,
    scoreDelta: 40,
    freeResponseCount: 0,
    gradedFreeResponseCount: 0,
    failedFreeResponseCount: 0,
    scoreDisplayFactor: 1,
    ...overrides,
  };
}

describe('MockExamTakePage & MockExamResultPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockParams = { attemptId: '88' };
    mockSearch = {};
  });

  it('renders sitting room in ExamSittingShell and navigates to result when submitted', async () => {
    vi.mocked(api.exams.take).mockResolvedValue(makeAttemptTake());
    vi.mocked(api.exams.submit).mockResolvedValue({
      attemptId: 88,
      status: 'graded',
      totalScore: 840,
      bandCode: 'gold',
      bandName: 'Gold',
      submittedAt: '2026-11-13T11:45:00Z',
      isAutoSubmitted: false,
    });

    render(<MockExamTakePage />);

    expect(screen.getByText('Loading mock exam sitting...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    expect(screen.getByText('What is shown in the image?')).toBeInTheDocument();
    expect(screen.getByText('A person is reading a book.')).toBeInTheDocument();

    // Submit button in sitting shell header
    const submitBtn = screen.getByRole('button', { name: /Submit/i });
    expect(submitBtn).toBeInTheDocument();

    fireEvent.click(submitBtn);

    // Confirm modal opens
    await waitFor(() => {
      expect(screen.getByText('Review & submit sitting')).toBeInTheDocument();
    });

    const confirmSubmit = screen.getByRole('button', { name: /Submit answers now/i });
    fireEvent.click(confirmSubmit);

    await waitFor(() => {
      expect(api.exams.submit).toHaveBeenCalledWith(88);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/exams/result/$attemptId',
        params: { attemptId: '88' },
      });
    });
  });

  it('handles preflight_required error with equipment check gate', async () => {
    const preflightErr = new ApiClientError(400, 'preflight_required', 'Equipment check required');
    vi.mocked(api.exams.take).mockRejectedValueOnce(preflightErr);
    vi.mocked(api.exams.preflightStatus).mockResolvedValue({
      attemptId: 88,
      examId: 101,
      examName: 'TOEIC Speaking Test',
      certCode: 'TOEIC',
      certName: 'TOEIC Speaking',
      durationMinutes: 20,
      requiresPreflight: true,
      requiresAudio: true,
      requiresMicrophone: true,
      isVerified: false,
      verifiedAt: null,
      audioUrls: [],
    });
    vi.mocked(api.exams.verifyPreflight).mockResolvedValue({} as any);

    render(<MockExamTakePage />);

    await waitFor(() => {
      expect(screen.getByText('Audio & equipment check')).toBeInTheDocument();
    });

    expect(screen.getByText('Sound & headphones')).toBeInTheDocument();
    expect(screen.getByText('Microphone check')).toBeInTheDocument();

    const confirmEquipment = screen.getByRole('button', {
      name: /Confirm equipment & start exam/i,
    });
    fireEvent.click(confirmEquipment);

    await waitFor(() => {
      expect(api.exams.verifyPreflight).toHaveBeenCalledWith(88);
    });
  });

  it('auto-submits the mock exam when countdown timer expires to zero', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.mocked(api.exams.take).mockResolvedValue(makeAttemptTake({ remainingSeconds: 2 }));
      vi.mocked(api.exams.submit).mockResolvedValue({
        attemptId: 88,
        status: 'graded',
        totalScore: 800,
        bandCode: 'blue',
        bandName: 'Blue',
        submittedAt: '2026-11-13T12:00:00Z',
        isAutoSubmitted: true,
      });

      render(<MockExamTakePage />);

      await waitFor(() => {
        expect(screen.getByText('TOEIC Full Mock Test 1')).toBeInTheDocument();
      });

      expect(api.exams.submit).not.toHaveBeenCalled();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(2500);
      });

      await waitFor(() => {
        expect(api.exams.submit).toHaveBeenCalledWith(88);
        expect(mockNavigate).toHaveBeenCalledWith({
          to: '/exams/result/$attemptId',
          params: { attemptId: '88' },
        });
      });
    } finally {
      vi.useRealTimers();
    }
  });

  it('renders score report, CEFR band, negative delta with minus sign, and skill breakdown', async () => {
    vi.mocked(api.exams.result).mockResolvedValue(
      makeAttemptResult({
        totalScore: 780,
        scoreDelta: -20,
        isAutoSubmitted: true,
      }),
    );

    render(<MockExamResultPage />);

    await waitFor(() => {
      expect(screen.getByText('Results — TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    // Score hero & minus sign
    expect(screen.getByText('780')).toBeInTheDocument();
    expect(screen.getByText('\u221220 vs previous attempt')).toBeInTheDocument();

    // Auto-submitted alert
    expect(screen.getByText('Time expired')).toBeInTheDocument();

    // Skills breakdown
    expect(screen.getByText('Listening')).toBeInTheDocument();
    expect(screen.getByText('430 pts')).toBeInTheDocument();
    expect(screen.getByText('Reading')).toBeInTheDocument();
    expect(screen.getByText('410 pts')).toBeInTheDocument();
  });
});
