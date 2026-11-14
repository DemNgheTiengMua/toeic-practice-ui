import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { SpeakingStudioPage } from '../SpeakingStudioPage';
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
    speaking: {
      catalog: vi.fn(),
      instructions: vi.fn(),
      start: vi.fn(),
    },
  },
}));

function makeCatalog(creditBalance = 5) {
  return {
    certificate: { code: 'IELTS', name: 'IELTS Academic', sectionNoun: 'Part' },
    creditBalance,
    exams: [
      {
        examId: 11,
        name: 'IELTS Speaking Set 1',
        description: 'Full 3-part interview simulation.',
        durationMinutes: 15,
        creditCost: 2,
        activeAttemptId: null,
        latestGradedAttemptId: 88,
        latestGradedAt: '2026-11-10T10:00:00Z',
      },
    ],
  };
}

function makeInstructions(creditBalance = 5, creditCost = 2) {
  return {
    examId: 11,
    certId: 1,
    certCode: 'IELTS',
    examName: 'IELTS Speaking Set 1',
    description: 'Full 3-part interview simulation.',
    durationMinutes: 15,
    questionCount: 3,
    creditCost,
    creditBalance,
    activeAttemptId: null,
    sections: [
      {
        sectionId: 101,
        code: 'P1',
        name: 'Part 1: Introduction',
        expectedQuestionCount: 1,
        prepSeconds: 5,
        responseSeconds: 45,
        displayOrder: 1,
      },
      {
        sectionId: 102,
        code: 'P2',
        name: 'Part 2: Cue card',
        expectedQuestionCount: 1,
        prepSeconds: 60,
        responseSeconds: 120,
        displayOrder: 2,
      },
    ],
    attempts: [
      {
        attemptId: 88,
        status: 'graded',
        startedAt: '2026-11-10T09:40:00Z',
        submittedAt: '2026-11-10T09:55:00Z',
        canReview: true,
        reopenPath: '/speaking/review/88',
      },
      {
        attemptId: 89,
        status: 'in_progress',
        startedAt: '2026-11-12T09:40:00Z',
        submittedAt: null,
        canReview: false,
        reopenPath: '/speaking/take/89',
      },
    ],
  };
}

function mockWorkingMic() {
  const stop = vi.fn();
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: {
      enumerateDevices: vi.fn().mockResolvedValue([
        { kind: 'audioinput', deviceId: 'mic-1', label: 'USB Studio Mic' },
        { kind: 'audioinput', deviceId: 'mic-2', label: 'Built-in Array' },
      ]),
      getUserMedia: vi.fn().mockResolvedValue({
        getTracks: () => [{ stop }],
      }),
    },
  });
}

describe('SpeakingStudioPage (/speaking)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockWorkingMic();
  });

  afterEach(() => {
    cleanup();
  });

  it('renders speaking catalog, instructions, and prior review link then starts when ready', async () => {
    vi.mocked(api.speaking.catalog).mockResolvedValue(makeCatalog(5));
    vi.mocked(api.speaking.instructions).mockResolvedValue(makeInstructions(5, 2));
    vi.mocked(api.speaking.start).mockResolvedValue({
      attemptId: 404,
    });

    render(<SpeakingStudioPage />);

    expect(await screen.findByText('IELTS Speaking Set 1')).toBeTruthy();
    expect(await screen.findByText(/Part 1: Introduction/i)).toBeTruthy();
    const resultLink = screen.getByRole('link', { name: /View result/i });
    expect(resultLink.getAttribute('href')).toBe('/speaking/result/88');
    expect(screen.getByText('Graded')).toBeTruthy();
    expect(screen.getByText('In progress')).toBeTruthy();
    expect(screen.queryByText('in_progress')).toBeNull();

    const startBtn = screen.getByRole('button', { name: /Start speaking session/i }) as HTMLButtonElement;
    await waitFor(() => expect(startBtn.disabled).toBe(false));

    fireEvent.click(startBtn);

    await waitFor(() => {
      expect(api.speaking.start).toHaveBeenCalledWith(11);
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/speaking/practice/$questionId',
        params: { questionId: '404' },
      });
    });
  });

  it('blocks start with checkout/pricing links when credits are insufficient', async () => {
    vi.mocked(api.speaking.catalog).mockResolvedValue(makeCatalog(1));
    vi.mocked(api.speaking.instructions).mockResolvedValue(makeInstructions(1, 2));

    render(<SpeakingStudioPage />);

    expect(await screen.findByText(/Not enough credits to start/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Top up wallet/i }).getAttribute('href')).toBe(
      '/checkout?credits=1&returnUrl=%2Fspeaking',
    );
    expect(screen.getByRole('link', { name: /View credit pricing/i }).getAttribute('href')).toBe('/pricing');
    const startBtn = screen.getByRole('button', { name: /Start speaking session/i }) as HTMLButtonElement;
    expect(startBtn.disabled).toBe(true);
    expect(api.speaking.start).not.toHaveBeenCalled();
  });

  it('shows graceful Alert, device Select, and Retry button when microphone permission is denied', async () => {
    vi.mocked(api.speaking.catalog).mockResolvedValue(makeCatalog(5));
    vi.mocked(api.speaking.instructions).mockResolvedValue(makeInstructions(5, 2));

    const getUserMedia = vi
      .fn()
      .mockRejectedValueOnce(Object.assign(new Error('Permission denied'), { name: 'NotAllowedError' }))
      .mockResolvedValueOnce({ getTracks: () => [{ stop: vi.fn() }] });

    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        enumerateDevices: vi.fn().mockResolvedValue([
          { kind: 'audioinput', deviceId: 'mic-1', label: 'USB Studio Mic' },
        ]),
        getUserMedia,
      },
    });

    render(<SpeakingStudioPage />);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/Microphone access was blocked/i);
    expect(screen.getByText(/Microphone device/i)).toBeTruthy();

    const retryBtn = screen.getByRole('button', { name: /Retry microphone check/i });
    fireEvent.click(retryBtn);

    await waitFor(() => {
      expect(screen.getByText(/Microphone ready/i)).toBeTruthy();
    });
  });
});
