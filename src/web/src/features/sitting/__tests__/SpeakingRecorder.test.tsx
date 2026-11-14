import './test-setup';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { SpeakingResponsePane } from '../SpeakingResponsePane';
import { ExamSittingShell } from '../ExamSittingShell';
import type { AttemptQuestionDto, PlacementTakeViewDto } from '@/lib/api';

class MockMediaRecorder {
  static lastInstance: MockMediaRecorder | null = null;
  state: 'inactive' | 'recording' | 'paused' = 'inactive';
  mimeType = 'audio/webm';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: ((e: Event) => void) | null = null;

  constructor(_stream: MediaStream) {
    MockMediaRecorder.lastInstance = this;
  }

  start() {
    this.state = 'recording';
  }

  stop() {
    if (this.state === 'inactive') return;
    this.state = 'inactive';
    const blob = new Blob(['audio-bytes'], { type: 'audio/webm' });
    this.ondataavailable?.({ data: blob });
    this.onstop?.();
  }
}

function makeSpeakingQuestion(overrides: Partial<AttemptQuestionDto> = {}): AttemptQuestionDto {
  return {
    questionId: 201,
    displayOrder: 1,
    totalQuestions: 2,
    sectionId: 4,
    sectionCode: 'SPK-1',
    sectionName: 'Speaking Interview',
    skillCode: 'speaking',
    skillName: 'Speaking',
    modality: 'speaking',
    stem: 'Describe a memorable trip you took recently.',
    options: [],
    isFlagged: false,
    questionType: 'free_response',
    prepSeconds: 15,
    responseSeconds: 45,
    ...overrides,
  };
}

describe('SpeakingResponsePane & ExamSittingShell Speaking Integration', () => {
  let trackStopSpy: ReturnType<typeof vi.fn>;
  let originalMediaRecorder: typeof window.MediaRecorder;
  let originalMediaDevices: MediaDevices | undefined;

  beforeEach(() => {
    MockMediaRecorder.lastInstance = null;
    trackStopSpy = vi.fn();
    originalMediaRecorder = window.MediaRecorder;
    originalMediaDevices = navigator.mediaDevices;

    (window as unknown as { MediaRecorder: unknown }).MediaRecorder = MockMediaRecorder;
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: vi.fn(async () => ({
          getTracks: () => [{ stop: trackStopSpy, addEventListener: vi.fn(), removeEventListener: vi.fn() }],
        })),
      },
    });
  });

  afterEach(() => {
    (window as unknown as { MediaRecorder: unknown }).MediaRecorder = originalMediaRecorder;
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: originalMediaDevices,
    });
    vi.restoreAllMocks();
  });

  it('records, uploads spoken response, and keeps Next button working inside ExamSittingShell (never renders textarea)', async () => {
    const q1 = makeSpeakingQuestion({ displayOrder: 1 });
    const q2 = makeSpeakingQuestion({
      questionId: 202,
      displayOrder: 2,
      stem: 'How does travel shape perspective?',
    });
    const take: PlacementTakeViewDto = {
      placementId: 88,
      attemptId: 999,
      examId: 12,
      examName: 'Speaking Placement',
      certCode: 'IELTS',
      status: 'in_progress',
      startedAt: new Date().toISOString(),
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      remainingSeconds: 900,
      totalQuestions: 2,
      answeredCount: 0,
      flaggedCount: 0,
      currentQuestion: q1,
      questions: [q1, q2],
      navigator: [
        { questionId: 201, displayOrder: 1, sectionId: 4, sectionCode: 'SPK-1', modality: 'speaking', isAnswered: false, isFlagged: false, isCurrent: true },
        { questionId: 202, displayOrder: 2, sectionId: 4, sectionCode: 'SPK-1', modality: 'speaking', isAnswered: false, isFlagged: false, isCurrent: false },
      ],
      isAutoSubmitted: false,
    };

    const onUploadAudio = vi.fn(async () => ({
      audioPath: '/speaking/attempts/999/201.webm',
      durationMs: 2500,
      savedAt: new Date().toISOString(),
    }));

    render(
      <ExamSittingShell
        sittingKind="placement"
        sittingId={88}
        attemptId={999}
        examName={take.examName}
        certCode={take.certCode}
        initialTake={take}
        basePath="/placement/take/88"
        onFetchQuestion={vi.fn(async () => take)}
        onSaveAnswer={vi.fn()}
        onUploadAudio={onUploadAudio}
        onSubmit={vi.fn()}
      />,
    );

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /start recording/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /stop recording/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /stop recording/i }));

    await waitFor(() => {
      expect(onUploadAudio).toHaveBeenCalledWith(
        201,
        expect.any(Blob),
        expect.stringMatching(/speaking-201/),
        expect.any(Number),
      );
      expect(screen.getByRole('button', { name: /record again/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    await waitFor(() => {
      expect(screen.getByText('How does travel shape perspective?')).toBeInTheDocument();
    });
  });

  it('PLC-15: resets cleanly to Record again and reports interruption when tab hides during recording', async () => {
    const onRecordInterruption = vi.fn(async () => ({
      interruptions: 1,
      outcome: 'retry',
      replacementQuestionId: null,
    }));
    const onNextQuestion = vi.fn();

    render(
      <SpeakingResponsePane
        question={makeSpeakingQuestion({ prepSeconds: 0 })}
        attemptId={999}
        onAudioSaved={vi.fn()}
        onNextQuestion={onNextQuestion}
        onUploadAudio={vi.fn()}
        onRecordInterruption={onRecordInterruption}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /start recording/i }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /stop recording/i })).toBeInTheDocument();
    });

    await act(async () => {
      Object.defineProperty(document, 'visibilityState', {
        configurable: true,
        value: 'hidden',
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });

    await waitFor(() => {
      expect(onRecordInterruption).toHaveBeenCalledWith(
        expect.objectContaining({
          questionId: 201,
          sessionToken: expect.any(String),
          capturedMs: expect.any(Number),
        }),
      );
      expect(screen.getByRole('button', { name: /record again/i })).toBeInTheDocument();
      expect(screen.getByText(/recording was interrupted/i)).toBeInTheDocument();
    });

    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      value: 'visible',
    });
  });

  it('surfaces non-blocking error Alert when microphone access is denied', async () => {
    (navigator.mediaDevices.getUserMedia as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      new DOMException('Permission denied', 'NotAllowedError'),
    );

    render(
      <SpeakingResponsePane
        question={makeSpeakingQuestion({ prepSeconds: 0 })}
        attemptId={999}
        onAudioSaved={vi.fn()}
        onNextQuestion={vi.fn()}
        onUploadAudio={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /start recording/i }));

    await waitFor(() => {
      expect(screen.getByText(/microphone access/i)).toBeInTheDocument();
    });
  });
});
