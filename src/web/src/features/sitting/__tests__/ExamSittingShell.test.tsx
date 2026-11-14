import './test-setup';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act, within } from '@testing-library/react';
import { ExamSittingShell } from '../ExamSittingShell';
import type { AttemptQuestionDto, PlacementTakeViewDto, SaveAnswerResultDto } from '@/lib/api';
import { readOfflineQueue } from '@/lib/sitting-queue';

function makeTake(): PlacementTakeViewDto {
  const q1: AttemptQuestionDto = {
    questionId: 101,
    displayOrder: 1,
    totalQuestions: 3,
    sectionId: 1,
    sectionCode: 'REA-1',
    sectionName: 'Reading Comprehension',
    skillCode: 'reading',
    skillName: 'Reading',
    modality: 'reading',
    stem: 'Which city hosted the symposium?',
    options: [
      { optionId: 1001, label: 'A', text: 'London' },
      { optionId: 1002, label: 'B', text: 'Paris' },
    ],
    selectedOptionId: null,
    isFlagged: false,
    questionType: 'mcq',
  };

  const q2: AttemptQuestionDto = {
    questionId: 102,
    displayOrder: 2,
    totalQuestions: 3,
    sectionId: 2,
    sectionCode: 'WRT-1',
    sectionName: 'Writing Task',
    skillCode: 'writing',
    skillName: 'Writing',
    modality: 'writing',
    stem: 'Summarize the chart in at least 50 words.',
    options: [],
    selectedOptionId: null,
    isFlagged: false,
    questionType: 'free_response',
    minWords: 50,
    maxWords: 200,
    responseText: '',
  };

  const q3: AttemptQuestionDto = {
    questionId: 103,
    displayOrder: 3,
    totalQuestions: 3,
    sectionId: 2,
    sectionCode: 'WRT-2',
    sectionName: 'Writing Essay',
    skillCode: 'writing',
    skillName: 'Writing',
    modality: 'writing',
    stem: 'Locked writing essay question.',
    options: [],
    selectedOptionId: null,
    isFlagged: false,
    questionType: 'free_response',
  };

  return {
    placementId: 77,
    attemptId: 500,
    examId: 10,
    examName: 'IELTS Placement',
    certCode: 'IELTS',
    status: 'in_progress',
    startedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 1800_000).toISOString(),
    remainingSeconds: 1800,
    totalQuestions: 3,
    answeredCount: 0,
    flaggedCount: 0,
    currentQuestion: q1,
    questions: [q1, q2, q3],
    navigator: [
      {
        questionId: 101,
        displayOrder: 1,
        sectionId: 1,
        sectionCode: 'REA-1',
        modality: 'reading',
        isAnswered: false,
        isFlagged: false,
        isCurrent: true,
        isLocked: false,
      },
      {
        questionId: 102,
        displayOrder: 2,
        sectionId: 2,
        sectionCode: 'WRT-1',
        modality: 'writing',
        isAnswered: false,
        isFlagged: false,
        isCurrent: false,
        isLocked: false,
      },
      {
        questionId: 103,
        displayOrder: 3,
        sectionId: 2,
        sectionCode: 'WRT-2',
        modality: 'writing',
        isAnswered: false,
        isFlagged: false,
        isCurrent: false,
        isLocked: true,
      },
    ],
    isAutoSubmitted: false,
    phases: [
      {
        skillId: 1,
        skillCode: 'reading',
        skillName: 'Reading',
        skillDisplayOrder: 1,
        startOffsetSeconds: 0,
        durationSeconds: 900,
      },
      {
        skillId: 2,
        skillCode: 'writing',
        skillName: 'Writing',
        skillDisplayOrder: 2,
        startOffsetSeconds: 900,
        durationSeconds: 900,
      },
    ],
  };
}

describe('ExamSittingShell', () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('saves MCQ option selection, navigates cleanly without remounting ProgressBar, and syncs ?q=', async () => {
    const replaceStateSpy = vi.spyOn(window.history, 'replaceState');
    const onSaveAnswer = vi.fn(
      async (questionId: number, payload: { selectedOptionId?: number | null; isFlagged: boolean }): Promise<SaveAnswerResultDto> => ({
        questionId,
        selectedOptionId: payload.selectedOptionId ?? null,
        isFlagged: payload.isFlagged,
        remainingSeconds: 1790,
        isAutoSubmitted: false,
      }),
    );

    const take = makeTake();
    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/placement/take/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={onSaveAnswer}
        onSaveDraft={vi.fn(async () => ({ savedAt: new Date().toISOString(), wordCount: 4 }))}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const progressBefore = screen.getByRole('progressbar');
    expect(screen.getByText('Which city hosted the symposium?')).toBeInTheDocument();

    fireEvent.click(screen.getByText('London'));

    await waitFor(() => {
      expect(onSaveAnswer).toHaveBeenCalledWith(
        101,
        expect.objectContaining({ selectedOptionId: 1001, isFlagged: false }),
      );
    });

    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    await waitFor(() => {
      expect(screen.getByText('Summarize the chart in at least 50 words.')).toBeInTheDocument();
    });

    const progressAfter = screen.getByRole('progressbar');
    expect(progressAfter).toBe(progressBefore);
    expect(replaceStateSpy).toHaveBeenCalledWith(null, '', '/placement/take/77?q=2');
  });

  it('SIT-01: reconciles locked ?q= tampering back to active unlocked question via replaceState', async () => {
    const replaceStateSpy = vi.spyOn(window.history, 'replaceState');
    const take = makeTake();

    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        initialRequestedOrder={3}
        basePath="/placement/take/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onSubmit={vi.fn()}
      />,
    );

    expect(screen.getByText('Which city hosted the symposium?')).toBeInTheDocument();
    expect(replaceStateSpy).toHaveBeenCalledWith(null, '', '/placement/take/77?q=1');
  });

  it('updates live Writing word count and sends debounced draft save', async () => {
    vi.useFakeTimers();
    const take = makeTake();
    take.currentQuestion = take.questions[1];
    take.navigator[0].isCurrent = false;
    take.navigator[1].isCurrent = true;
    const onSaveDraft = vi.fn(async () => ({ savedAt: new Date().toISOString(), wordCount: 4 }));

    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/placement/take/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onSaveDraft={onSaveDraft}
        onSubmit={vi.fn()}
      />,
    );

    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: 'The chart illustrates growth' } });

    expect(screen.getByText(/4 words/i)).toBeInTheDocument();

    await act(async () => {
      vi.advanceTimersByTime(1300);
    });

    expect(onSaveDraft).toHaveBeenCalledWith(102, 'The chart illustrates growth');
  });

  it('PLC-17: queues failed save offline, updates placard away from Saved, and flushes on online event', async () => {
    const take = makeTake();
    const onSaveAnswer = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce({
        questionId: 101,
        selectedOptionId: 1002,
        isFlagged: false,
        remainingSeconds: 1790,
        isAutoSubmitted: false,
      });

    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/placement/take/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={onSaveAnswer}
        onSubmit={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByText('Paris'));

    await waitFor(() => {
      expect(screen.getByText(/Offline · 1 saved on this device/i)).toBeInTheDocument();
    });
    expect(readOfflineQueue('placement', 77)).toHaveLength(1);

    await act(async () => {
      window.dispatchEvent(new Event('online'));
    });

    await waitFor(() => {
      expect(readOfflineQueue('placement', 77)).toHaveLength(0);
      expect(screen.getByText('Saved')).toBeInTheDocument();
    });
  });

  it('PLC-18: displays Submit Modal with per-section answered/total and unanswered counts before submitting', async () => {
    const take = makeTake();
    take.navigator[0].isAnswered = true;
    take.answeredCount = 1;
    const onSubmit = vi.fn(async () => {});

    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/placement/take/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /review & submit/i }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Reading Comprehension/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/1 \/ 1 answered/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/0 \/ 2 answered/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/^2 unanswered$/i)).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('button', { name: /submit answers now/i }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith(false);
    });
  });
});
