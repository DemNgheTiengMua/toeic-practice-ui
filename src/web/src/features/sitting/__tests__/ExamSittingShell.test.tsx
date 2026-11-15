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

  it('renders placement deadline as a quiet session expiry when >= 1 hour remains', () => {
    const take = makeTake();
    take.remainingSeconds = 48 * 3600; // 48 hours

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
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const expiryEl = screen.getByLabelText('Session expiry');
    expect(expiryEl).toBeInTheDocument();
    expect(expiryEl).toHaveTextContent(/Session expires in 48h/i);
    expect(expiryEl.className).not.toContain('text-danger');
    expect(screen.queryByLabelText('Time remaining')).not.toBeInTheDocument();
    expect(screen.queryByText('48:00:00')).not.toBeInTheDocument();
  });

  it('escalates placement session expiry only when under an hour', () => {
    const take = makeTake();
    take.remainingSeconds = 1800; // 30 minutes

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
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const expiryEl = screen.getByLabelText('Session expiry');
    expect(expiryEl).toHaveTextContent(/Session expires in 30:00/i);
    expect(expiryEl.className).toContain('text-danger');
  });

  it('auto-submits a placement sitting when expiry reaches zero', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const take = makeTake();
    take.remainingSeconds = 2;
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

    await vi.advanceTimersByTimeAsync(2500);
    expect(onSubmit).toHaveBeenCalledWith(true);
    vi.useRealTimers();
  });

  it('keeps mock exam countdown unchanged and escalates only at <= 60s', () => {
    const take = makeTake();
    take.remainingSeconds = 1800; // 30 minutes

    const { unmount } = render(
      <ExamSittingShell
        sittingKind="mock"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/exams/mock/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const timerEl = screen.getByLabelText('Time remaining');
    expect(timerEl).toHaveTextContent('30:00');
    expect(timerEl.className).not.toContain('text-danger');
    expect(screen.queryByLabelText('Session expiry')).not.toBeInTheDocument();

    unmount();

    const urgentTake = makeTake();
    urgentTake.remainingSeconds = 45;
    render(
      <ExamSittingShell
        sittingKind="mock"
        sittingId={78}
        attemptId={501}
        examName={urgentTake.examName}
        certCode={urgentTake.certCode}
        initialTake={urgentTake}
        basePath="/exams/mock/78"
        onFetchQuestion={vi.fn(async () => urgentTake)}
        onSaveAnswer={vi.fn()}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const urgentTimerEl = screen.getByLabelText('Time remaining');
    expect(urgentTimerEl).toHaveTextContent('00:45');
    expect(urgentTimerEl.className).toContain('text-danger');
  });

  it('supports keyboard shortcuts (1-4 option, N next, P previous, F flag) outside inputs and renders a Kbd legend', async () => {
    const take = makeTake();
    const onSaveAnswer = vi.fn(async (): Promise<SaveAnswerResultDto> => ({
      questionId: 101,
      selectedOptionId: 1002,
      isFlagged: false,
      remainingSeconds: 1800,
      isAutoSubmitted: false,
    }));

    render(
      <ExamSittingShell
        sittingKind="mock"
        sittingId={77}
        attemptId={500}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/exams/mock/77"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={onSaveAnswer}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    // Kbd legend is visible in the sitting
    const shortcutsRegion = screen.getByLabelText('Keyboard shortcuts');
    expect(shortcutsRegion).toBeInTheDocument();
    expect(shortcutsRegion.querySelectorAll('kbd').length).toBeGreaterThanOrEqual(3);

    // Press '2' to select option B (1002)
    fireEvent.keyDown(window, { key: '2' });
    await waitFor(() => {
      expect(onSaveAnswer).toHaveBeenCalledWith(
        101,
        expect.objectContaining({ selectedOptionId: 1002 }),
      );
    });

    // Press 'f' to flag question 1
    fireEvent.keyDown(window, { key: 'f' });
    await waitFor(() => {
      expect(onSaveAnswer).toHaveBeenCalledWith(
        101,
        expect.objectContaining({ isFlagged: true }),
      );
    });

    // Press 'n' to advance to question 2 (writing)
    fireEvent.keyDown(window, { key: 'n' });
    await waitFor(() => {
      expect(screen.getByText(/Summarize the chart in at least 50 words/i)).toBeInTheDocument();
    });

    // Typing 'p' inside the writing textarea must NOT navigate away
    const textarea = screen.getByPlaceholderText(/Write your response here/i);
    fireEvent.keyDown(textarea, { key: 'p' });
    expect(screen.getByText(/Summarize the chart in at least 50 words/i)).toBeInTheDocument();

    // Press 'p' outside inputs to return to question 1
    fireEvent.keyDown(window, { key: 'p' });
    await waitFor(() => {
      expect(screen.getByText(/Which city hosted the symposium/i)).toBeInTheDocument();
    });
  });

  it('uses roving tabindex, arrow-key focus movement, aria-current, and focus-ring on the question navigator', async () => {
    const questions: AttemptQuestionDto[] = Array.from({ length: 12 }, (_, idx) => {
      const order = idx + 1;
      return {
        questionId: 200 + order,
        displayOrder: order,
        totalQuestions: 12,
        sectionId: 1,
        sectionCode: 'REA-1',
        sectionName: 'Reading Comprehension',
        skillCode: 'reading',
        skillName: 'Reading',
        modality: 'reading',
        stem: `Question ${order} stem`,
        options: [
          { optionId: order * 10 + 1, label: 'A', text: 'Opt A' },
          { optionId: order * 10 + 2, label: 'B', text: 'Opt B' },
        ],
        selectedOptionId: null,
        isFlagged: false,
        questionType: 'mcq',
      };
    });

    const take: PlacementTakeViewDto = {
      ...makeTake(),
      totalQuestions: 12,
      currentQuestion: questions[0],
      questions,
      navigator: questions.map((q, idx) => ({
        questionId: q.questionId,
        displayOrder: q.displayOrder,
        sectionId: 1,
        sectionCode: 'REA-1',
        modality: 'reading',
        isAnswered: false,
        isFlagged: false,
        isCurrent: idx === 0,
        isLocked: idx === 11, // last question locked to verify skipping
      })),
    };

    render(
      <ExamSittingShell
        sittingKind="mock"
        sittingId={79}
        attemptId={502}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/exams/mock/79"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const nav = screen.getByRole('group', { name: /question navigator/i });
    const cells = within(nav).getAllByRole('button');
    expect(cells).toHaveLength(12);

    // Bounded tab stops: exactly one cell has tabIndex=0, all others -1
    const tabbableCells = cells.filter((c) => c.getAttribute('tabindex') === '0');
    expect(tabbableCells).toHaveLength(1);
    expect(tabbableCells[0]).toBe(cells[0]);
    for (let i = 1; i < cells.length; i++) {
      expect(cells[i]).toHaveAttribute('tabindex', '-1');
    }

    // Current cell has aria-current="step"; others do not
    expect(cells[0]).toHaveAttribute('aria-current', 'step');
    expect(cells[1]).not.toHaveAttribute('aria-current');

    // Uses product focus-ring rather than browser default outline
    expect(cells[0].className).toContain('focus-ring');
    expect(cells[0].className).toContain('outline-none');

    // ArrowRight moves focus to Q2, ArrowDown moves focus +5 to Q7, End skips locked Q12 to Q11
    cells[0].focus();
    expect(document.activeElement).toBe(cells[0]);

    fireEvent.keyDown(cells[0], { key: 'ArrowRight' });
    expect(document.activeElement).toBe(cells[1]);
    expect(cells[1]).toHaveAttribute('tabindex', '0');
    expect(cells[0]).toHaveAttribute('tabindex', '-1');

    fireEvent.keyDown(cells[1], { key: 'ArrowDown' });
    expect(document.activeElement).toBe(cells[6]);

    fireEvent.keyDown(cells[6], { key: 'End' });
    expect(document.activeElement).toBe(cells[10]);

    fireEvent.keyDown(cells[10], { key: 'Home' });
    expect(document.activeElement).toBe(cells[0]);
  });

  it('announces the save-state placard in an aria-live="polite" region', async () => {
    const take = makeTake();
    let resolveSave!: (val: SaveAnswerResultDto) => void;
    const onSaveAnswer = vi.fn(
      () =>
        new Promise<SaveAnswerResultDto>((resolve) => {
          resolveSave = resolve;
        }),
    );

    render(
      <ExamSittingShell
        sittingKind="mock"
        sittingId={80}
        attemptId={503}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/exams/mock/80"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={onSaveAnswer}
        onSubmit={vi.fn(async () => {})}
      />,
    );

    const statusRegion = screen.getByRole('status');
    expect(statusRegion).toHaveAttribute('aria-live', 'polite');
    expect(statusRegion).toHaveTextContent(/saved/i);

    fireEvent.keyDown(window, { key: '1' });
    await waitFor(() => {
      expect(statusRegion).toHaveTextContent(/saving/i);
    });

    await act(async () => {
      resolveSave({
        questionId: 101,
        selectedOptionId: 1001,
        isFlagged: false,
        remainingSeconds: 1800,
        isAutoSubmitted: false,
      });
    });

    await waitFor(() => {
      expect(statusRegion).toHaveTextContent(/saved/i);
    });
  });
});
