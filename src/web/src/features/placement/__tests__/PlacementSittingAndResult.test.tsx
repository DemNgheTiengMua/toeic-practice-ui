import '../../sitting/__tests__/test-setup';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlacementResumePage } from '../PlacementResumePage';
import { PlacementTakePage } from '../PlacementTakePage';
import { PlacementResultPage } from '../PlacementResultPage';
import {
  api,
  type PlacementGradingStatusDto,
  type PlacementResultViewDto,
  type PlacementResumeViewDto,
  type PlacementTakeViewDto,
} from '@/lib/api';

const mockNavigate = vi.fn();
let mockParams: Record<string, string> = { attemptId: '42' };
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
      placement: {
        ...actual.api.placement,
        resumeInfo: vi.fn(),
        abandon: vi.fn(),
        take: vi.fn(),
        saveAnswer: vi.fn(),
        submit: vi.fn(),
        result: vi.fn(),
        gradingStatus: vi.fn(),
        retryGrading: vi.fn(),
        setTargetBand: vi.fn(),
      },
      speaking: {
        ...actual.api.speaking,
        uploadAudio: vi.fn(),
      },
    },
  };
});

function makeResume(): PlacementResumeViewDto {
  return {
    placementId: 42,
    attemptId: 420,
    examName: 'IELTS Placement',
    certCode: 'IELTS',
    startedAt: '2026-11-13T10:00:00Z',
    expiresAt: '2026-11-13T11:00:00Z',
    skills: [
      {
        skillId: 1,
        code: 'listening',
        name: 'Listening',
        isDone: true,
        isActive: false,
        answeredCount: 6,
        totalQuestions: 6,
      },
      {
        skillId: 2,
        code: 'reading',
        name: 'Reading',
        isDone: false,
        isActive: true,
        answeredCount: 2,
        totalQuestions: 6,
      },
    ],
    activeSkillId: 2,
    activeSkillName: 'Reading',
    activeStage: 2,
    totalStages: 4,
    syncedAnswerCount: 8,
    totalQuestions: 24,
    lastQuestionId: 108,
  };
}

function makeSimpleTake(): PlacementTakeViewDto {
  return {
    placementId: 42,
    attemptId: 420,
    examId: 10,
    examName: 'IELTS Placement',
    certCode: 'IELTS',
    status: 'in_progress',
    startedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    remainingSeconds: 900,
    totalQuestions: 1,
    answeredCount: 0,
    flaggedCount: 0,
    currentQuestion: {
      questionId: 101,
      displayOrder: 1,
      totalQuestions: 1,
      sectionId: 1,
      sectionCode: 'REA',
      sectionName: 'Reading',
      skillCode: 'reading',
      skillName: 'Reading',
      modality: 'reading',
      stem: 'Sample restored stem',
      options: [{ optionId: 1, label: 'A', text: 'Choice A' }],
      isFlagged: false,
      questionType: 'mcq',
    },
    questions: [],
    navigator: [
      {
        questionId: 101,
        displayOrder: 1,
        sectionId: 1,
        sectionCode: 'REA',
        modality: 'reading',
        isAnswered: false,
        isFlagged: false,
        isCurrent: true,
      },
    ],
    isAutoSubmitted: false,
  };
}

function makeResult(overrides: Partial<PlacementResultViewDto> = {}): PlacementResultViewDto {
  return {
    placementId: 42,
    attemptId: 420,
    certId: 1,
    certCode: 'IELTS',
    examName: 'IELTS Diagnostic Placement',
    takenAt: '2026-11-13T10:30:00Z',
    durationMinutes: 45,
    totalQuestions: 24,
    totalScore: 65,
    scoreDisplayFactor: 10,
    resultBandCode: 'B2',
    resultBandName: 'Upper Intermediate',
    cefrBands: [
      { code: 'A2', name: 'Elementary', minTotal: 30, maxTotal: 40, isReached: true, isCurrent: false },
      { code: 'B1', name: 'Intermediate', minTotal: 45, maxTotal: 55, isReached: true, isCurrent: false },
      { code: 'B2', name: 'Upper Intermediate', minTotal: 60, maxTotal: 75, isReached: true, isCurrent: true },
    ],
    skillScores: [
      { skillId: 1, skillCode: 'listening', skillName: 'Listening', rawScore: 6, totalQuestions: 6, scaledScore: 70, bandCode: 'B2' },
      { skillId: 2, skillCode: 'reading', skillName: 'Reading', rawScore: 5, totalQuestions: 6, scaledScore: 65, bandCode: 'B2' },
      { skillId: 3, skillCode: 'writing', skillName: 'Writing', rawScore: 0, totalQuestions: 1, scaledScore: 0, bandCode: null },
      { skillId: 4, skillCode: 'speaking', skillName: 'Speaking', rawScore: 0, totalQuestions: 1, scaledScore: 60, bandCode: 'B2' },
    ],
    skillResults: [
      {
        skillId: 1,
        skillCode: 'listening',
        skillName: 'Listening',
        status: 'measured',
        levelCode: 'B2',
        positionInBand: 70,
        measuredAt: '2026-10-01T00:00:00Z',
        reusedFromPrior: true,
        failureReason: null,
      },
      {
        skillId: 2,
        skillCode: 'reading',
        skillName: 'Reading',
        status: 'measured',
        levelCode: 'B2',
        positionInBand: 50,
        measuredAt: '2026-11-13T10:30:00Z',
        reusedFromPrior: false,
        failureReason: null,
      },
      {
        skillId: 3,
        skillCode: 'writing',
        skillName: 'Writing',
        status: 'NotMeasured',
        levelCode: null,
        positionInBand: null,
        measuredAt: null,
        reusedFromPrior: false,
        failureReason: 'ai_grading_failed',
      },
      {
        skillId: 4,
        skillCode: 'speaking',
        skillName: 'Speaking',
        status: 'EstimatedFromOneTask',
        levelCode: 'B2',
        positionInBand: 40,
        measuredAt: '2026-11-13T10:30:00Z',
        reusedFromPrior: false,
        failureReason: null,
      },
    ],
    isGraded: true,
    scope: 'all_skills',
    freeResponseCount: 2,
    gradedFreeResponseCount: 1,
    failedFreeResponseCount: 1,
    ...overrides,
  };
}

describe('PlacementResumePage, PlacementTakePage & PlacementResultPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockParams = { attemptId: '42' };
    mockSearch = {};
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('PLC-14 Resume: shows stage & synced counts without charging, resumes and abandons cleanly', async () => {
    vi.mocked(api.placement.resumeInfo).mockResolvedValueOnce(makeResume());
    vi.mocked(api.placement.abandon).mockResolvedValueOnce(undefined);

    render(<PlacementResumePage />);

    expect(await screen.findByText(/8 \/ 24/)).toBeInTheDocument();
    expect(screen.getByText(/Stage 2 of 4/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /resume placement/i })).toHaveAttribute(
      'href',
      '/placement/take/42',
    );

    fireEvent.click(screen.getByRole('button', { name: /abandon placement/i }));
    const dialog = await screen.findByRole('alertdialog');
    fireEvent.click(within(dialog).getByRole('button', { name: /confirm abandon/i }));

    await waitFor(() => {
      expect(api.placement.abandon).toHaveBeenCalledWith(42);
      expect(mockNavigate).toHaveBeenCalledWith({ to: '/placement' });
    });
  });

  it('PLC-14 Take: performs pure read on reload and submits to result route', async () => {
    mockSearch = { q: 1 };
    vi.mocked(api.placement.take).mockResolvedValueOnce(makeSimpleTake());
    vi.mocked(api.placement.submit).mockResolvedValueOnce(makeResult());

    render(<PlacementTakePage />);

    expect(await screen.findByText('Sample restored stem')).toBeInTheDocument();
    expect(api.placement.take).toHaveBeenCalledWith(42, 1);

    fireEvent.click(screen.getAllByRole('button', { name: /review & submit/i })[0]);
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: /submit answers now/i }));

    await waitFor(() => {
      expect(api.placement.submit).toHaveBeenCalledWith(42, false);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/placement/result/$attemptId',
        params: { attemptId: '42' },
      });
    });
  });

  it('SIT-03 Result: polls grading status every 3s while ungraded and refreshes result when complete', async () => {
    vi.useFakeTimers();
    const ungraded = makeResult({ isGraded: false });
    const graded = makeResult({ isGraded: true, failedFreeResponseCount: 0 });
    const statusPending: PlacementGradingStatusDto = {
      placementId: 42,
      attemptId: 420,
      isGraded: false,
      freeResponseCount: 2,
      gradedFreeResponseCount: 1,
      failedFreeResponseCount: 0,
      skillScores: [],
      stageDescriptor: 'Grading writing response',
    };
    const statusDone: PlacementGradingStatusDto = {
      ...statusPending,
      isGraded: true,
      gradedFreeResponseCount: 2,
      stageDescriptor: 'Complete',
    };

    vi.mocked(api.placement.result)
      .mockResolvedValueOnce(ungraded)
      .mockResolvedValueOnce(graded);
    vi.mocked(api.placement.gradingStatus)
      .mockResolvedValueOnce(statusPending)
      .mockResolvedValueOnce(statusDone);

    render(<PlacementResultPage />);

    // Flush initial effect microtasks
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(screen.getByText(/AI grading in progress/i)).toBeInTheDocument();

    await act(async () => {
      vi.advanceTimersByTime(3100);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(api.placement.gradingStatus).toHaveBeenCalledTimes(1);

    await act(async () => {
      vi.advanceTimersByTime(3100);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(api.placement.gradingStatus).toHaveBeenCalledTimes(2);
    expect(api.placement.result).toHaveBeenCalledTimes(2);
  });

  it('Result renders Unmeasured badge (never 0), Carried over, AI-estimated, 2-skill notice, and Retry grading', async () => {
    vi.mocked(api.placement.result).mockResolvedValueOnce(
      makeResult({
        scope: 'listening_and_reading',
        failedFreeResponseCount: 1,
      }),
    );
    vi.mocked(api.placement.retryGrading).mockResolvedValueOnce({
      placementId: 42,
      attemptId: 420,
      requeuedCount: 1,
    });
    vi.mocked(api.placement.result).mockResolvedValueOnce(makeResult({ isGraded: false }));

    render(<PlacementResultPage />);

    expect(await screen.findByText('Unmeasured')).toBeInTheDocument();
    expect(screen.getByText('Carried over')).toBeInTheDocument();
    expect(screen.getByText('AI-estimated')).toBeInTheDocument();
    expect(screen.getByText(/Two-skill scope/i)).toBeInTheDocument();
    expect(screen.getByText('6.5')).toBeInTheDocument();

    // Writing row is NotMeasured -> never displays raw/scaled "0" as its score
    const writingRow = screen.getByTestId('skill-result-writing');
    expect(within(writingRow).queryByText(/^0(\.0)?$/)).not.toBeInTheDocument();
    expect(within(writingRow).getByText(/could not score/i)).toBeInTheDocument();

    fireEvent.click(within(writingRow).getByRole('button', { name: /retry grading/i }));
    await waitFor(() => {
      expect(api.placement.retryGrading).toHaveBeenCalledWith(42);
    });
  });
});
