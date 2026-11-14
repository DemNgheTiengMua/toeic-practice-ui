import '../../sitting/__tests__/test-setup';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlacementCatalogPage } from '../PlacementCatalogPage';
import { PlacementRetakePage } from '../PlacementRetakePage';
import { api, type PlacementCatalogViewDto, type PlacementPreflightViewDto } from '@/lib/api';

const mockNavigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  useParams: () => ({ certId: '1' }),
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
      placement: {
        ...actual.api.placement,
        catalog: vi.fn(),
        preflightGate: vi.fn(),
        start: vi.fn(),
      },
    },
  };
});

function makeCatalog(overrides: Partial<PlacementCatalogViewDto> = {}): PlacementCatalogViewDto {
  return {
    activeCert: {
      code: 'IELTS',
      name: 'IELTS Academic',
    },
    currentBandCode: null,
    currentBandName: null,
    currentTotalScore: null,
    exams: [
      {
        examId: 10,
        certId: 1,
        certCode: 'IELTS',
        name: 'IELTS Diagnostic Placement',
        description: 'Assesses listening, reading, writing, and speaking.',
        durationMinutes: 45,
        questionCount: 24,
        isRecommended: true,
        activePlacementId: null,
      },
    ],
    activePlacementId: null,
    activeAttemptId: null,
    isRetake: false,
    allSkillsRetakeCredits: 5,
    listeningAndReadingRetakeCredits: 3,
    creditBalance: 0,
    ...overrides,
  };
}

function makePreflight(
  overrides: Partial<PlacementPreflightViewDto> = {},
): PlacementPreflightViewDto {
  return {
    certId: 1,
    certCode: 'IELTS',
    examId: 10,
    offers: [
      {
        skillId: 1,
        skillCode: 'listening',
        skillName: 'Listening',
        modality: 'listening',
        requiresMicrophone: false,
        requiresAudio: true,
        levelCode: 'B1',
        positionInBand: 55,
        measuredAt: '2026-10-01T00:00:00Z',
        expiresAt: '2026-12-01T00:00:00Z',
        canReuse: true,
        isAged: false,
      },
      {
        skillId: 2,
        skillCode: 'reading',
        skillName: 'Reading',
        modality: 'reading',
        requiresMicrophone: false,
        requiresAudio: false,
        levelCode: 'B1',
        positionInBand: 60,
        measuredAt: '2026-10-01T00:00:00Z',
        expiresAt: '2026-12-01T00:00:00Z',
        canReuse: true,
        isAged: false,
      },
      {
        skillId: 3,
        skillCode: 'writing',
        skillName: 'Writing',
        modality: 'writing',
        requiresMicrophone: false,
        requiresAudio: false,
        levelCode: null,
        positionInBand: null,
        measuredAt: null,
        expiresAt: null,
        canReuse: false,
        isAged: false,
      },
      {
        skillId: 4,
        skillCode: 'speaking',
        skillName: 'Speaking',
        modality: 'speaking',
        requiresMicrophone: true,
        requiresAudio: false,
        levelCode: null,
        positionInBand: null,
        measuredAt: null,
        expiresAt: null,
        canReuse: false,
        isAged: false,
      },
    ],
    cooldownUntil: null,
    cooldownWaived: false,
    hasReusableSkills: true,
    isStartable: true,
    requiresMicrophone: true,
    requiresAudio: true,
    isRetake: true,
    allSkillsRetakeCredits: 5,
    listeningAndReadingRetakeCredits: 3,
    creditBalance: 10,
    ...overrides,
  };
}

describe('PlacementCatalogPage & PlacementRetakePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('catalog renders free start flow for first-time learner and starts placement', async () => {
    vi.mocked(api.placement.catalog).mockResolvedValueOnce(makeCatalog());
    vi.mocked(api.placement.start).mockResolvedValueOnce({
      placementId: 33,
      attemptId: 44,
      examId: 10,
      status: 'in_progress',
      startedAt: new Date().toISOString(),
      expiresAt: new Date().toISOString(),
      remainingSeconds: 2700,
      isAutoSubmitted: false,
      scope: 'all_skills',
      creditsCharged: 0,
    });

    render(<PlacementCatalogPage />);

    expect(await screen.findByText('IELTS Diagnostic Placement')).toBeInTheDocument();
    expect(screen.getByText(/Free first placement/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /start free placement/i }));

    await waitFor(() => {
      expect(api.placement.start).toHaveBeenCalledWith(
        expect.objectContaining({
          examId: 10,
          scope: 'all_skills',
        }),
      );
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/placement/take/$attemptId',
        params: { attemptId: '33' },
      });
    });
  });

  it('catalog surfaces resume link when activePlacementId exists and retake link when placed', async () => {
    vi.mocked(api.placement.catalog).mockResolvedValueOnce(
      makeCatalog({
        isRetake: true,
        currentBandCode: 'B1',
        currentBandName: 'Intermediate',
        currentTotalScore: 55,
        activePlacementId: 91,
        creditBalance: 6,
      }),
    );

    render(<PlacementCatalogPage />);

    expect(await screen.findByText(/B1/)).toBeInTheDocument();
    const resumeLink = screen.getByRole('link', { name: /resume sitting/i });
    expect(resumeLink).toHaveAttribute('href', '/placement/resume/91');

    const retakeLink = screen.getByRole('link', { name: /retake options/i });
    expect(retakeLink).toHaveAttribute('href', '/placement/retake/1');
  });

  it('retake preflight blocks insufficient credits and links to /wallet without calling start', async () => {
    vi.mocked(api.placement.preflightGate).mockResolvedValueOnce(
      makePreflight({
        creditBalance: 1,
        allSkillsRetakeCredits: 5,
        listeningAndReadingRetakeCredits: 3,
      }),
    );

    render(<PlacementRetakePage />);

    expect((await screen.findAllByText(/5 credits/i)).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/3 credits/i)).toBeInTheDocument();
    expect(screen.getByText(/Left unmeasured: Writing, Speaking/i)).toBeInTheDocument();

    const startBtn = screen.getByRole('button', { name: /start placement retake/i });
    expect(startBtn).toBeDisabled();
    const walletLink = screen.getByRole('link', { name: /top up wallet/i });
    expect(walletLink).toHaveAttribute('href', '/wallet');
    expect(api.placement.start).not.toHaveBeenCalled();
  });

  it('retake preflight starts listening_and_reading retake cleanly when learner picks 3-credit scope', async () => {
    vi.mocked(api.placement.preflightGate).mockResolvedValueOnce(
      makePreflight({
        creditBalance: 4,
      }),
    );
    vi.mocked(api.placement.start).mockResolvedValueOnce({
      placementId: 55,
      attemptId: 66,
      examId: 10,
      status: 'in_progress',
      startedAt: new Date().toISOString(),
      expiresAt: new Date().toISOString(),
      remainingSeconds: 1800,
      isAutoSubmitted: false,
      scope: 'listening_and_reading',
      creditsCharged: 3,
    });

    render(<PlacementRetakePage />);

    expect(await screen.findByText(/Listening \+ Reading only/i)).toBeInTheDocument();
    fireEvent.click(screen.getByText(/Listening \+ Reading only/i));

    const startBtn = screen.getByRole('button', { name: /start placement retake/i });
    expect(startBtn).not.toBeDisabled();
    fireEvent.click(startBtn);

    await waitFor(() => {
      expect(api.placement.start).toHaveBeenCalledWith(
        expect.objectContaining({
          examId: 10,
          scope: 'listening_and_reading',
          reuseValidSkills: true,
        }),
      );
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/placement/take/$attemptId',
        params: { attemptId: '55' },
      });
    });
  });
});
