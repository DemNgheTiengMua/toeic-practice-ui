import '../../sitting/__tests__/test-setup';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MockExamReviewPage } from '../MockExamReviewPage';
import { MockExamHistoryPage } from '../MockExamHistoryPage';
import {
  api,
  type AttemptReviewDto,
  type ExamHistoryDto,
} from '@/lib/api';

const mockNavigate = vi.fn();
let mockParams: Record<string, string> = { attemptId: '99' };

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
        review: vi.fn(),
        history: vi.fn(),
      },
    },
  };
});

function makeReview(overrides: Partial<AttemptReviewDto> = {}): AttemptReviewDto {
  return {
    attemptId: 99,
    examId: 101,
    examName: 'TOEIC Full Mock Test 1',
    certId: 1,
    certCode: 'TOEIC',
    certName: 'TOEIC Listening & Reading',
    status: 'graded',
    startedAt: '2026-11-13T10:00:00Z',
    submittedAt: '2026-11-13T11:45:00Z',
    durationSeconds: 6300,
    totalScore: 840,
    bandCode: 'gold',
    bandName: 'Gold (860+)',
    skillScores: [],
    totalQuestions: 2,
    answeredCount: 2,
    correctCount: 1,
    incorrectCount: 1,
    blankCount: 0,
    flaggedCount: 1,
    questions: [
      {
        questionId: 2001,
        displayOrder: 1,
        totalQuestions: 2,
        sectionId: 1,
        sectionCode: 'Part 1',
        sectionName: 'Photographs',
        skillCode: 'listening',
        skillName: 'Listening',
        modality: 'listening',
        stem: 'Where is the meeting taking place?',
        imagePath: null,
        imageAlt: null,
        audioPath: null,
        audioStartMs: null,
        audioEndMs: null,
        transcript: 'The meeting is held in Conference Room B on the second floor.',
        passage: null,
        explanation: 'Option A is directly confirmed by the audio recording.',
        options: [
          { optionId: 11, label: 'A', text: 'In Conference Room B', isCorrect: true, rationale: 'Confirmed by speaker.' },
          { optionId: 12, label: 'B', text: 'In the cafeteria', isCorrect: false, rationale: 'No cafeteria was mentioned.' },
        ],
        selectedOptionId: 11,
        isCorrect: true,
        isFlagged: false,
      },
      {
        questionId: 2002,
        displayOrder: 2,
        totalQuestions: 2,
        sectionId: 2,
        sectionCode: 'Part 5',
        sectionName: 'Incomplete Sentences',
        skillCode: 'reading',
        skillName: 'Reading',
        modality: 'reading',
        stem: 'Ms. Sato submitted her report ______ than expected.',
        imagePath: null,
        imageAlt: null,
        audioPath: null,
        audioStartMs: null,
        audioEndMs: null,
        transcript: null,
        passage: null,
        explanation: 'Comparative form earlier is required before than.',
        options: [
          { optionId: 21, label: 'A', text: 'early', isCorrect: false },
          { optionId: 22, label: 'B', text: 'earlier', isCorrect: true },
        ],
        selectedOptionId: 21,
        isCorrect: false,
        isFlagged: true,
      },
    ],
    navigator: [
      {
        questionId: 2001,
        displayOrder: 1,
        sectionId: 1,
        sectionCode: 'Part 1',
        modality: 'listening',
        isAnswered: true,
        isFlagged: false,
        isCorrect: true,
      },
      {
        questionId: 2002,
        displayOrder: 2,
        sectionId: 2,
        sectionCode: 'Part 5',
        modality: 'reading',
        isAnswered: true,
        isFlagged: true,
        isCorrect: false,
      },
    ],
    ...overrides,
  };
}

function makeHistory(overrides: Partial<ExamHistoryDto> = {}): ExamHistoryDto {
  return {
    certificate: {
      code: 'TOEIC',
      name: 'TOEIC Listening & Reading',
    },
    totalAttempts: 2,
    bestScore: 840,
    averageScore: 810,
    bestScoreBandCode: 'gold',
    bestScoreBandName: 'Gold',
    cefrBands: [
      { code: 'gold', name: 'Gold', minTotal: 860, maxTotal: 990, displayOrder: 1 },
      { code: 'blue', name: 'Blue', minTotal: 730, maxTotal: 855, displayOrder: 2 },
    ],
    attempts: [
      {
        attemptId: 99,
        examId: 101,
        examName: 'TOEIC Full Mock Test 1',
        certId: 1,
        certCode: 'TOEIC',
        startedAt: '2026-11-13T10:00:00Z',
        submittedAt: '2026-11-13T11:45:00Z',
        totalScore: 840,
        bandCode: 'gold',
        bandName: 'Gold',
        skillScores: [
          { skillId: 1, skillCode: 'listening', skillName: 'Listening', modality: 'listening', rawScore: 90, scaledScore: 430, totalQuestions: 100 },
          { skillId: 2, skillCode: 'reading', skillName: 'Reading', modality: 'reading', rawScore: 85, scaledScore: 410, totalQuestions: 100 },
        ],
        status: 'graded',
      },
    ],
    ...overrides,
  };
}

describe('MockExamReviewPage & MockExamHistoryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockParams = { attemptId: '99' };
  });

  it('renders detailed answer review with question cards, correct & incorrect badges, and explanations', async () => {
    vi.mocked(api.exams.review).mockResolvedValue(makeReview());

    render(<MockExamReviewPage />);

    expect(screen.getByText('Loading answer review...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Review — TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    // Summary counters
    expect(screen.getByText('1 correct')).toBeInTheDocument();
    expect(screen.getByText('1 incorrect')).toBeInTheDocument();
    expect(screen.getByText('1 flagged')).toBeInTheDocument();

    // Question 1: Correct answer
    expect(screen.getByText('Where is the meeting taking place?')).toBeInTheDocument();
    expect(screen.getByText('Your answer · Correct')).toBeInTheDocument();
    expect(screen.getByText('Option A is directly confirmed by the audio recording.')).toBeInTheDocument();

    // Question 2: Incorrect answer
    expect(screen.getByText('Ms. Sato submitted her report ______ than expected.')).toBeInTheDocument();
    expect(screen.getByText('Your answer')).toBeInTheDocument();
    expect(screen.getByText('Correct answer')).toBeInTheDocument();
    expect(screen.getByText('Comparative form earlier is required before than.')).toBeInTheDocument();

    // Navigator grid
    expect(screen.getByText('Question navigator')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '2' })).toBeInTheDocument();
  });

  it('filters questions by status (e.g. incorrect only)', async () => {
    vi.mocked(api.exams.review).mockResolvedValue(makeReview());

    render(<MockExamReviewPage />);

    await waitFor(() => {
      expect(screen.getByText('Review — TOEIC Full Mock Test 1')).toBeInTheDocument();
    });

    const statusSelect = screen.getByLabelText(/Status:/i);
    fireEvent.change(statusSelect, { target: { value: 'incorrect' } });

    // Question 2 (incorrect) is visible, Question 1 (correct) is hidden
    expect(screen.getByText('Ms. Sato submitted her report ______ than expected.')).toBeInTheDocument();
    expect(screen.queryByText('Where is the meeting taking place?')).not.toBeInTheDocument();
  });

  it('renders exam history with aggregate score stats and past attempts table', async () => {
    vi.mocked(api.exams.history).mockResolvedValue(makeHistory());

    render(<MockExamHistoryPage />);

    expect(screen.getByText('Loading exam history...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Exam history' })).toBeInTheDocument();
    });

    // Stats
    expect(screen.getByText('Exams taken')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('Best score')).toBeInTheDocument();
    expect(screen.getAllByText('840').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Average score')).toBeInTheDocument();
    expect(screen.getByText('810')).toBeInTheDocument();

    // Attempts table
    expect(screen.getByText('Past attempts (1)')).toBeInTheDocument();
    expect(screen.getByText('TOEIC Full Mock Test 1')).toBeInTheDocument();
    expect(screen.getByText('listening: 430')).toBeInTheDocument();
    expect(screen.getByText('reading: 410')).toBeInTheDocument();

    // Actions
    expect(screen.getByRole('button', { name: 'Result' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Review' }));
    expect(mockNavigate).toHaveBeenCalledWith({
      to: '/exams/review/$attemptId',
      params: { attemptId: '99' },
    });
  });
});
