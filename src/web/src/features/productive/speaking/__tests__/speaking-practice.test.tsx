import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent, act } from '@testing-library/react';
import { SpeakingPracticePage } from '../SpeakingPracticePage';
import { api } from '@/lib/api';

const mockNavigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  Link: ({ to, children, ...props }: { to: string; children: React.ReactNode; [k: string]: unknown }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));

vi.mock('@/lib/api', () => ({
  api: {
    speaking: {
      take: vi.fn(),
      uploadAudio: vi.fn(),
      submit: vi.fn(),
    },
  },
}));

function makeTakeData(audioPath: string | null = null) {
  return {
    attemptId: 404,
    examId: 11,
    examName: 'IELTS Speaking Set 1',
    certId: 1,
    certCode: 'IELTS',
    status: 'in_progress' as const,
    startedAt: '2026-11-13T10:00:00Z',
    expiresAt: '2026-11-13T10:15:00Z',
    remainingSeconds: 600,
    currentQuestionId: 901,
    currentQuestionIndex: 0,
    totalQuestions: 2,
    savedCount: audioPath ? 1 : 0,
    question: {
      questionId: 901,
      displayOrder: 1,
      stem: 'Describe a memorable trip you took recently.',
      prepSeconds: 5,
      responseSeconds: 45,
      hint: 'Cover where you went and why it stood out.',
      sectionCode: 'P2',
      sectionName: 'Part 2: Cue card',
      groupImagePath: '/media/speaking/cue.png',
      groupImageAlt: 'Travel illustration',
      groupAudioPath: null,
      audioPath,
      durationMs: audioPath ? 12000 : null,
      responseText: null,
      draftSavedAt: null,
      status: (audioPath ? 'pending_ai' : 'draft') as 'draft' | 'pending_ai',
      readAloudText: null,
    },
    questions: [
      { questionId: 901, displayOrder: 1, sectionName: 'Part 2', hasAudio: Boolean(audioPath), status: 'draft' as const },
      { questionId: 902, displayOrder: 2, sectionName: 'Part 3', hasAudio: false, status: 'draft' as const },
    ],
  };
}

class FakeMediaRecorder {
  static isTypeSupported = () => true;
  state: 'inactive' | 'recording' = 'inactive';
  mimeType = 'audio/webm';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: (() => void) | null = null;

  start() {
    this.state = 'recording';
  }

  stop() {
    this.state = 'inactive';
    const blob = new Blob(['fake-audio'], { type: 'audio/webm' });
    this.ondataavailable?.({ data: blob });
    this.onstop?.();
  }
}

describe('SpeakingPracticePage (/speaking/practice/$questionId)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    if (!globalThis.CSS || typeof globalThis.CSS.escape !== 'function') {
      (globalThis as unknown as { CSS: { escape: (s: string) => string } }).CSS = {
        escape: (s: string) => s,
      };
    }
    (globalThis as unknown as { MediaRecorder: typeof FakeMediaRecorder }).MediaRecorder = FakeMediaRecorder;
    Object.defineProperty(globalThis.URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:mock-audio'),
    });
    Object.defineProperty(globalThis.URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        enumerateDevices: vi.fn().mockResolvedValue([
          { kind: 'audioinput', deviceId: 'mic-1', label: 'Studio USB Mic' },
        ]),
        getUserMedia: vi.fn().mockResolvedValue({
          getTracks: () => [{ stop: vi.fn() }],
        }),
      },
    });
  });

  afterEach(() => {
    cleanup();
  });

  it('records audio, recovers from upload failure via Retry upload, and submits to result route', async () => {
    vi.mocked(api.speaking.take)
      .mockResolvedValueOnce(makeTakeData(null))
      .mockResolvedValueOnce(makeTakeData('audio/404/901.webm'));
    vi.mocked(api.speaking.uploadAudio)
      .mockRejectedValueOnce(new Error('Network timeout'))
      .mockResolvedValueOnce({
        freeResponseId: 55,
        questionId: 901,
        audioPath: 'audio/404/901.webm',
        durationMs: 1000,
        savedAt: '2026-11-13T10:02:00Z',
      });
    vi.mocked(api.speaking.submit).mockResolvedValue({
      attemptId: 404,
      status: 'submitted',
      submittedAt: '2026-11-13T10:03:00Z',
    });

    render(<SpeakingPracticePage attemptId={404} />);

    expect(await screen.findByText('Describe a memorable trip you took recently.')).toBeTruthy();

    const recordBtn = await screen.findByRole('button', { name: /Start recording/i });
    await act(async () => {
      fireEvent.click(recordBtn);
    });

    const stopBtn = await screen.findByRole('button', { name: /Stop & upload/i });
    await act(async () => {
      fireEvent.click(stopBtn);
    });

    expect(await screen.findByText(/Upload failed/i)).toBeTruthy();
    const retryUploadBtn = screen.getByRole('button', { name: /Retry upload/i });
    await act(async () => {
      fireEvent.click(retryUploadBtn);
    });

    await waitFor(() => {
      expect(api.speaking.uploadAudio).toHaveBeenCalledTimes(2);
    });

    const submitTrigger = screen.getByRole('button', { name: /Submit attempt/i });
    fireEvent.click(submitTrigger);

    const confirmBtn = await screen.findByRole('button', { name: /Confirm submit/i });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(api.speaking.submit).toHaveBeenCalledWith(404);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/speaking/result/$attemptId',
        params: { attemptId: '404' },
      });
    });
  });

  it('shows graceful Alert + device Select + Retry check when mic access fails mid-sitting', async () => {
    vi.mocked(api.speaking.take).mockResolvedValue(makeTakeData(null));
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        enumerateDevices: vi.fn().mockResolvedValue([
          { kind: 'audioinput', deviceId: 'mic-1', label: 'Headset Mic' },
        ]),
        getUserMedia: vi.fn().mockRejectedValue(Object.assign(new Error('No permission'), { name: 'NotAllowedError' })),
      },
    });

    render(<SpeakingPracticePage attemptId={404} />);

    expect(await screen.findByText('Describe a memorable trip you took recently.')).toBeTruthy();
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/Microphone access was blocked/i);
    expect(screen.getByText(/Microphone device/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /Retry microphone check/i })).toBeTruthy();
  });
});
