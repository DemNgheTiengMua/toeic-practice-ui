import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  AuthUserDto,
  LearnerContextDto,
  OnboardingStateDto,
  PlacementCatalogViewDto,
  PlacementResultViewDto,
  TargetBandViewDto,
} from '../../../lib/api';

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url =
      typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    return handler(url, init);
  });
  vi.stubGlobal('fetch', mock);
  return mock;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderRoute(initialUrl: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const history = createMemoryHistory({ initialEntries: [initialUrl] });
  const router = createAppRouter(history);

  const view = render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { ...view, router, history };
}

const LEARNER_USER: AuthUserDto = {
  userId: 101,
  email: 'new-learner@demo.local',
  fullName: 'New Learner',
  role: 'Learner',
  emailVerified: true,
};

const LEARNER_CONTEXT: LearnerContextDto = {
  active: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  certificates: [
    { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
    { code: 'IELTS', name: 'IELTS Academic' },
  ],
  creditBalance: 20,
  examinedSkills: ['listening', 'reading'],
  examDate: null,
};

const CATALOG: PlacementCatalogViewDto = {
  activeCert: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  currentBandCode: null,
  currentBandName: null,
  currentTotalScore: null,
  exams: [
    {
      examId: 6,
      certId: 2,
      certCode: 'IELTS',
      name: 'Universal Placement Exam',
      description: 'Single universal 4-skill placement exam',
      durationMinutes: 60,
      questionCount: 12,
      isRecommended: true,
      activePlacementId: null,
    },
  ],
  activePlacementId: null,
  activeAttemptId: null,
  isRetake: false,
  allSkillsRetakeCredits: 5,
  listeningAndReadingRetakeCredits: 3,
  creditBalance: 20,
};

describe('OOBE 4-Step Wizard & Soft-Lock (#311)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.sessionStorage.clear();
    window.scrollTo = vi.fn();
  });

  afterEach(() => {
    cleanup();
  });

  it('hides study nav and renders OobeSoftLockEmptyState on gated routes when OOBE is incomplete', async () => {
    const incompleteState: OnboardingStateDto = {
      hasCompletedPlacement: false,
      activePlacementAttemptId: null,
      activePlacementId: null,
      latestPlacementId: null,
      hasTargetGoal: false,
      hasActiveLearningPath: false,
      isOobeComplete: false,
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(LEARNER_USER);
      if (url === '/api/learner/context') return jsonResponse(LEARNER_CONTEXT);
      if (url === '/api/learner/onboarding-state') return jsonResponse(incompleteState);
      return jsonResponse({}, 404);
    });

    renderRoute('/practice');

    expect(await screen.findByTestId('oobe-softlock-empty-state')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /continue oobe|tiếp tục thiết lập/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /learning path/i })).not.toBeInTheDocument();
  });

  it('supports temporary session skip ("Explore for now") and renders persistent return banner', async () => {
    const incompleteState: OnboardingStateDto = {
      hasCompletedPlacement: false,
      activePlacementAttemptId: null,
      activePlacementId: null,
      latestPlacementId: null,
      hasTargetGoal: false,
      hasActiveLearningPath: false,
      isOobeComplete: false,
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(LEARNER_USER);
      if (url === '/api/learner/context') return jsonResponse(LEARNER_CONTEXT);
      if (url === '/api/learner/onboarding-state') return jsonResponse(incompleteState);
      if (url === '/api/learner/placement') return jsonResponse(CATALOG);
      if (url === '/api/learner/target-band') {
        return jsonResponse({
          activeCert: LEARNER_CONTEXT.active,
          currentBandCode: null,
          currentTotalScore: null,
          currentBandName: null,
          targetBandCode: null,
          cefrBands: [],
          availableBands: [],
          goal: null,
        } satisfies TargetBandViewDto);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/onboarding');

    const exploreBtn = await screen.findByRole('button', { name: /explore for now/i });
    expect(screen.getByText(/2 skills/i)).toBeInTheDocument();
    expect(screen.getByText(/4 skills/i)).toBeInTheDocument();

    fireEvent.click(exploreBtn);

    await waitFor(() => {
      expect(window.sessionStorage.getItem('ee_oobe_skip_session')).toBe('1');
    });
    expect(await screen.findByTestId('oobe-return-banner')).toBeInTheDocument();
  });

  it('displays cross-certificate band previews in Step 2 and generates 5-credit path in Step 4', async () => {
    const placedState: OnboardingStateDto = {
      hasCompletedPlacement: true,
      activePlacementAttemptId: null,
      activePlacementId: null,
      latestPlacementId: 77,
      hasTargetGoal: false,
      hasActiveLearningPath: false,
      isOobeComplete: false,
    };

    const resultDto: PlacementResultViewDto = {
      placementId: 77,
      attemptId: 88,
      certId: 2,
      certCode: 'IELTS',
      examName: 'Universal Placement Exam',
      takenAt: '2026-11-14T00:00:00Z',
      durationMinutes: 15,
      totalQuestions: 4,
      totalScore: 75,
      resultBandCode: '6.5',
      resultBandName: 'Band 6.5',
      cefrBands: [],
      skillScores: [
        { skillId: 1, skillCode: 'LIST', skillName: 'Listening', rawScore: 2, totalQuestions: 2, scaledScore: 100 },
        { skillId: 2, skillCode: 'READ', skillName: 'Reading', rawScore: 1, totalQuestions: 2, scaledScore: 50 },
      ],
      isGraded: true,
      scope: 'listening_and_reading',
      listeningPct: 100,
      readingPct: 50,
      overallPct: 75,
      certificatePreviews: [
        {
          certId: 1,
          certCode: 'TOEIC',
          certName: 'TOEIC Listening & Reading',
          skillCount: 2,
          mappedScaledScore: 745,
          mappedBandCode: 'B2',
          mappedBandName: 'Upper-Intermediate',
          scoreDisplayFactor: 1,
        },
        {
          certId: 2,
          certCode: 'IELTS',
          certName: 'IELTS Academic',
          skillCount: 4,
          mappedScaledScore: 65,
          mappedBandCode: '6.5',
          mappedBandName: 'Band 6.5',
          scoreDisplayFactor: 10,
        },
      ],
    };

    let generatedPath = false;
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') return jsonResponse(LEARNER_USER);
      if (url === '/api/learner/context') return jsonResponse(LEARNER_CONTEXT);
      if (url === '/api/learner/onboarding-state') return jsonResponse(placedState);
      if (url === '/api/learner/placement/result/77') return jsonResponse(resultDto);
      if (url === '/api/learner/target-band') {
        return jsonResponse({
          activeCert: LEARNER_CONTEXT.active,
          currentBandCode: 'B2',
          currentTotalScore: 745,
          currentBandName: 'Upper-Intermediate',
          targetBandCode: 'C1',
          cefrBands: [],
          availableBands: [
            { code: 'B2', name: 'Upper-Intermediate', minTotal: 605, maxTotal: 780, isCurrent: true, isTarget: false },
            { code: 'C1', name: 'Advanced', minTotal: 785, maxTotal: 990, isCurrent: false, isTarget: true },
          ],
          goal: {
            currentBandCode: 'B2',
            targetBandCode: 'C1',
            targetDate: '2026-12-31',
            weeklyStudyHours: 8,
            requiredHours: 80,
            availableHours: 96,
            status: 'feasible',
          },
        } satisfies TargetBandViewDto);
      }
      if (url === '/api/learner/learning-path/generate' && init?.method === 'POST') {
        generatedPath = true;
        return jsonResponse({ hasActivePath: true });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/onboarding');

    expect(await screen.findByText(/75%/i)).toBeInTheDocument();
    expect(screen.getAllByText(/TOEIC/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/B2/).length).toBeGreaterThan(0);

    // Advance to Step 3 -> Step 4
    fireEvent.click(screen.getByRole('button', { name: /continue to goal/i }));
    expect(await screen.findByRole('button', { name: /continue to learning path/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /continue to learning path/i }));

    const generateBtn = await screen.findByRole('button', { name: /generate.*5 credits/i });
    fireEvent.click(generateBtn);

    await waitFor(() => {
      expect(generatedPath).toBe(true);
    });
  });

  it('redirects /onboarding to /dashboard when isOobeComplete is true', async () => {
    const completeState: OnboardingStateDto = {
      hasCompletedPlacement: true,
      activePlacementAttemptId: null,
      activePlacementId: null,
      latestPlacementId: 77,
      hasTargetGoal: true,
      hasActiveLearningPath: true,
      isOobeComplete: true,
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(LEARNER_USER);
      if (url === '/api/learner/context') return jsonResponse(LEARNER_CONTEXT);
      if (url === '/api/learner/onboarding-state') return jsonResponse(completeState);
      if (url === '/api/learner/dashboard') {
        return jsonResponse({
          activeCert: LEARNER_CONTEXT.active,
          skills: [],
          goal: {
            completedPathSteps: 0,
            totalPathSteps: 4,
            weekPlannedMinutes: 60,
            weekCompletedMinutes: 0,
            scoreDisplayFactor: 1,
          },
          nextAction: { kind: 'start_path', label: 'Start', ctaLabel: 'Go', route: '/learning-path' },
          recentAttempts: [],
          recentPractice: [],
          upcomingBookings: [],
        });
      }
      return jsonResponse({}, 404);
    });

    const { history } = renderRoute('/onboarding');

    await waitFor(() => {
      expect(history.location.pathname).toBe('/dashboard');
    });
  });
});
