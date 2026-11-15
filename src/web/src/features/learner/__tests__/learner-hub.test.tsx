import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { Toast, toast } from '@heroui/react';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  AuthUserDto,
  LearnerContextDto,
  LearnerDashboardDto,
  TargetBandViewDto,
  LearningPathSummaryDto,
  LessonDetailDto,
  LessonProgressDto,
  MicroCheckResultDto,
  WeaknessAnalysisDto,
  AiInsightDto,
} from '../../../lib/api';

type FetchMock = ReturnType<typeof vi.fn>;

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
): FetchMock {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
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
        <Toast.Provider />
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { ...view, router, history };
}

const DEMO_USER: AuthUserDto = {
  userId: 42,
  email: 'learner@demo.local',
  fullName: 'Demo Learner',
  role: 'Learner',
  emailVerified: true,
};

const DEMO_CONTEXT: LearnerContextDto = {
  active: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
  certificates: [
    { code: 'DEMO', name: 'DEMO - Custom Assessment' },
    { code: 'ALT', name: 'ALT - Alternate Test' },
  ],
  creditBalance: 150,
  examinedSkills: ['listening', 'reading'],
  examDate: '2026-12-01',
};

describe('Learner Hub (#300)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as any;
    if (!Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [];
    }
  });

  afterEach(() => {
    toast.clear();
    cleanup();
    vi.unstubAllGlobals();
  });

  describe('Dashboard (/dashboard)', () => {
    it('renders skeleton loading placeholder instead of centered spinner while loading', async () => {
      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/dashboard') return new Promise(() => {});
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      await waitFor(() => {
        expect(screen.getByLabelText(/loading dashboard/i)).toBeInTheDocument();
      });
      expect(screen.queryByText(/loading your dashboard…/i)).not.toBeInTheDocument();
    });

    it('renders 3 blocks for a placed learner (hero, 4-cell goal table, per-skill table)', async () => {
      const placedDashboard: LearnerDashboardDto = {
        certificate: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        nextAction: {
          eyebrow: 'NEXT UP',
          title: 'Review Short Dialogs',
          description: 'Practice 10 questions on short conversational dialogues.',
          actionLabel: 'Resume practice',
          actionUrl: '/practice',
          severity: 'accent',
          cascadeStep: 'step_1_active_path',
          metaLabel: 'Step 3 of 12',
        },
        goal: {
          currentBandCode: 'B1',
          currentBandName: 'Independent',
          currentScore: 280,
          targetBandCode: 'B2',
          targetBandName: 'Vantage',
          targetMinScore: 350,
          targetDate: '2026-12-15',
          weeklyStudyHours: 5,
          status: 'feasible',
          completedPathSteps: 4,
          totalPathSteps: 12,
          currentWeekNumber: 2,
          weekPlannedMinutes: 120,
          weekCompletedMinutes: 60,
          scoreDisplayFactor: 1,
          requiredTotalHours: 40,
          overallLevelCode: 'B1',
          limitingSkillCode: 'CMP',
          limitingSkillName: 'Comprehension',
          hasNoMeasurement: false,
        },
        skills: [
          {
            skillId: 1,
            code: 'CMP',
            name: 'Comprehension',
            modality: 'listening',
            latestScaledScore: 140,
            maxScaledScore: 250,
            levelCode: 'B1',
            positionInBand: 60,
            weakestTopics: [],
            scoreDisplayFactor: 1,
            measuredAt: '2026-10-01T10:00:00Z',
            isStale: false,
          },
          {
            skillId: 2,
            code: 'EXP',
            name: 'Expression',
            modality: 'writing',
            latestScaledScore: 140,
            maxScaledScore: 250,
            levelCode: 'B1',
            positionInBand: 50,
            weakestTopics: [],
            scoreDisplayFactor: 1,
            measuredAt: '2026-10-01T10:00:00Z',
            isStale: false,
          },
        ],
        attemptCount: 2,
        practiceCount: 5,
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/dashboard') return jsonResponse(placedDashboard);
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      // Block 1: Next Action Hero
      expect(await screen.findByRole('heading', { name: /review short dialogs/i })).toBeInTheDocument();
      expect(screen.getByText(/practice 10 questions on short conversational dialogues/i)).toBeInTheDocument();
      expect(screen.getByText(/this week: 60m done of 120m planned/i)).toBeInTheDocument();

      // Block 2: Goal Standing 4 cells
      expect(screen.getByRole('heading', { name: /goal standing · demo/i })).toBeInTheDocument();
      expect(screen.getByText('Level against target')).toBeInTheDocument();
      expect(screen.getByText(/b1 → b2/i)).toBeInTheDocument();
      expect(screen.getByText('Remaining guided hours')).toBeInTheDocument();
      expect(screen.getByText('40h remaining')).toBeInTheDocument();
      expect(screen.getByText('Verdict')).toBeInTheDocument();
      expect(screen.getByText('Reachable at this pace')).toBeInTheDocument();

      // Block 3: Per-Skill Standing
      expect(screen.getByRole('heading', { name: /per-skill standing · demo/i })).toBeInTheDocument();
      expect(screen.getByText('Comprehension')).toBeInTheDocument();
      expect(screen.getByText('Expression')).toBeInTheDocument();

      // Placement notice should NOT be rendered
      expect(screen.queryByText(/placement is a free sitting/i)).not.toBeInTheDocument();
    });

    it('collapses to 1 block with placement notice for unplaced learners without ghost figures', async () => {
      const unplacedDashboard: LearnerDashboardDto = {
        certificate: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        nextAction: {
          eyebrow: 'GET STARTED',
          title: 'Take your diagnostic placement test',
          description: 'A 25-minute assessment to measure your baseline CEFR levels.',
          actionLabel: 'Start placement',
          actionUrl: '/placement',
          severity: 'accent',
          cascadeStep: 'step_0_unplaced',
        },
        goal: {
          completedPathSteps: 0,
          totalPathSteps: 0,
          weekPlannedMinutes: 0,
          weekCompletedMinutes: 0,
          scoreDisplayFactor: 1,
          hasNoMeasurement: true, // UNPLACED
        },
        skills: [],
        attemptCount: 0,
        practiceCount: 0,
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/dashboard') return jsonResponse(unplacedDashboard);
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      expect(
        await screen.findByRole('heading', { name: /take your diagnostic placement test/i }),
      ).toBeInTheDocument();

      // Exactly the placement notice sentence
      const notice = screen.getByText(/placement is a free sitting that measures your level in every skill demo examines/i);
      expect(notice).toBeInTheDocument();
      expect(notice).toHaveAttribute('id', 'placement-notice');

      // NO Goal Standing table or Per-Skill Standing blocks
      expect(screen.queryByRole('heading', { name: /goal standing/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: /per-skill standing/i })).not.toBeInTheDocument();
      expect(screen.queryByText(/this week:/i)).not.toBeInTheDocument();
    });

    it('never renders a verification banner on the dashboard, because an unverified account cannot sign in', async () => {
      // ADR-0018: this state is unreachable — the gate refuses the session. The reminder banner and
      // its resend button described the old model and were deleted with it.
      const unverifiedUser: AuthUserDto = {
        ...DEMO_USER,
        emailVerified: false,
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(unverifiedUser);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/dashboard') {
          return jsonResponse({
            certificate: { code: 'DEMO', name: 'DEMO' },
            nextAction: {
              eyebrow: 'NEXT',
              title: 'Continue',
              description: 'Next step',
              actionLabel: 'Go',
              actionUrl: '/practice',
              severity: 'accent',
              cascadeStep: 'step_1',
            },
            goal: { completedPathSteps: 0, totalPathSteps: 0, weekPlannedMinutes: 0, weekCompletedMinutes: 0, scoreDisplayFactor: 1, hasNoMeasurement: true },
            skills: [],
            attemptCount: 0,
            practiceCount: 0,
          });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      expect(await screen.findByText('Continue')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /resend email/i })).not.toBeInTheDocument();
      expect(screen.queryByText(/verify your email address/i)).not.toBeInTheDocument();
    });

    it('renders upcoming mentor session reminder banner with Join Meet link and booking detail link', async () => {
      const dashboardWithBookings: LearnerDashboardDto = {
        certificate: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        nextAction: {
          eyebrow: 'NEXT UP',
          title: 'Review Short Dialogs',
          description: 'Practice 10 questions on short conversational dialogues.',
          actionLabel: 'Resume practice',
          actionUrl: '/practice',
          severity: 'accent',
          cascadeStep: 'step_1_active_path',
        },
        goal: {
          completedPathSteps: 2,
          totalPathSteps: 10,
          weekPlannedMinutes: 120,
          weekCompletedMinutes: 60,
          scoreDisplayFactor: 1,
          hasNoMeasurement: true,
        },
        skills: [],
        attemptCount: 0,
        practiceCount: 0,
        upcomingBookings: [
          {
            bookingId: 777,
            mentorId: 1,
            mentorName: 'Dr. Trần Minh Khoa',
            certCode: 'IELTS',
            skillName: 'Speaking',
            startAt: '2026-11-22T12:00:00Z',
            endAt: '2026-11-22T13:00:00Z',
            meetLink: 'https://meet.google.com/khoa-speaking-room',
            focusArea: 'Part 2 cue card fluency & coherence',
          },
        ],
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/dashboard') return jsonResponse(dashboardWithBookings);
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      expect(await screen.findByText(/upcoming mentor session/i)).toBeInTheDocument();
      expect(screen.getByText('Dr. Trần Minh Khoa')).toBeInTheDocument();
      expect(screen.getByText(/IELTS · Speaking/i)).toBeInTheDocument();
      expect(screen.getByText(/Part 2 cue card fluency & coherence/i)).toBeInTheDocument();

      const joinMeetLink = screen.getByRole('link', { name: /join meet/i }) as HTMLAnchorElement;
      expect(joinMeetLink).toBeInTheDocument();
      expect(joinMeetLink.href).toBe('https://meet.google.com/khoa-speaking-room');
      expect(joinMeetLink.target).toBe('_blank');

      const detailLink = screen.getByRole('link', { name: /booking details/i }) as HTMLAnchorElement;
      expect(detailLink.getAttribute('href')).toBe('/mentors/bookings/777');
    });
  });

  describe('Onboarding & Goal Setup (/onboarding)', () => {
    it('allows selecting target band and submitting study goal with feasibility feedback', async () => {
      const targetBandView: TargetBandViewDto = {
        activeCert: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        currentBandCode: 'B1',
        currentTotalScore: 280,
        currentBandName: 'Independent',
        targetBandCode: null,
        cefrBands: [
          { code: 'B1', name: 'Independent', minTotal: 200, maxTotal: 299, isReached: true, isCurrent: true },
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, isReached: false, isCurrent: false },
        ],
        availableBands: [
          { code: 'B1', name: 'Independent', minTotal: 200, maxTotal: 299, description: 'Intermediate proficiency', isCurrent: true, isTarget: false },
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, description: 'Upper intermediate proficiency', isCurrent: false, isTarget: false },
        ],
        goal: null,
      };

      let submittedGoal: any = null;
      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/target-band') return jsonResponse(targetBandView);
        if (url === '/api/learner/goal' && init?.method === 'POST') {
          submittedGoal = JSON.parse(String(init.body));
          return jsonResponse({
            userId: 42,
            certId: 1,
            targetBandCode: 'B2',
            setAt: '2026-11-13T00:00:00Z',
            goal: {
              targetBandCode: 'B2',
              targetDate: '2026-12-31',
              weeklyStudyHours: 6,
              requiredHours: 40,
              availableHours: 42,
              status: 'feasible',
            },
          });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/onboarding');

      expect(await screen.findByRole('heading', { name: /set your study goal · demo/i })).toBeInTheDocument();
      expect(screen.getByText('Band B1 (Independent)')).toBeInTheDocument();

      // Select target band B2 via the single Select control (no duplicate band buttons)
      expect(screen.queryByRole('button', { name: /b2 — vantage/i })).not.toBeInTheDocument();
      const hiddenSelect = document.querySelector('[data-testid="hidden-select-container"] select') as HTMLSelectElement;
      expect(hiddenSelect).not.toBeNull();
      fireEvent.change(hiddenSelect, { target: { value: 'B2' } });

      // Submit form
      const submitButton = screen.getByRole('button', { name: /save goal & continue/i });
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(submittedGoal).not.toBeNull();
        expect(submittedGoal.targetBandCode).toBe('B2');
        expect(submittedGoal.currentBandCode).toBe('B1');
        expect(submittedGoal.weeklyStudyHours).toBe(7);
        const toastEl = screen.getByText('Study goal saved! Redirecting to your dashboard...');
        expect(toastEl.closest('[data-slot="toast-region"]')).toBeInTheDocument();
      });
    });

    it('advances to the learning-path step after saving a goal in OOBE without throwing', async () => {
      const targetBandView: TargetBandViewDto = {
        activeCert: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        currentBandCode: 'B1',
        currentTotalScore: 280,
        currentBandName: 'Independent',
        targetBandCode: 'B2',
        cefrBands: [
          { code: 'B1', name: 'Independent', minTotal: 200, maxTotal: 299, isReached: true, isCurrent: true },
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, isReached: false, isCurrent: false },
        ],
        availableBands: [
          { code: 'B1', name: 'Independent', minTotal: 200, maxTotal: 299, description: 'Intermediate proficiency', isCurrent: true, isTarget: false },
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, description: 'Upper intermediate proficiency', isCurrent: false, isTarget: false },
        ],
        goal: null,
      };

      let hasTargetGoal = false;
      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/onboarding-state') {
          return jsonResponse({
            hasCompletedPlacement: true,
            hasTargetGoal,
            hasActiveLearningPath: false,
            latestPlacementId: 10,
            activePlacementId: null,
          });
        }
        if (url === '/api/learner/placement/result/10') {
          return jsonResponse({
            placementId: 10,
            scope: 'listening_and_reading',
            isGraded: true,
            overallPct: 72,
            listeningPct: 75,
            readingPct: 69,
            certificatePreviews: [],
          });
        }
        if (url === '/api/learner/target-band') return jsonResponse(targetBandView);
        if (url === '/api/learner/goal' && init?.method === 'POST') {
          hasTargetGoal = true;
          return jsonResponse({
            userId: 42,
            certId: 1,
            targetBandCode: 'B2',
            setAt: '2026-11-15T00:00:00Z',
            goal: {
              targetBandCode: 'B2',
              targetDate: '2026-12-31',
              weeklyStudyHours: 7,
              requiredHours: 40,
              availableHours: 49,
              status: 'feasible',
            },
          });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/onboarding');

      // Step 2 -> Continue to goal setup (Step 3)
      const continueToGoalBtn = await screen.findByRole('button', { name: /continue to goal setup/i });
      fireEvent.click(continueToGoalBtn);

      expect(await screen.findByRole('heading', { name: /set your study goal · demo/i })).toBeInTheDocument();

      const submitButton = screen.getByRole('button', { name: /save goal & continue/i });
      fireEvent.click(submitButton);

      expect(await screen.findByRole('heading', { name: /generate your personalized learning path/i })).toBeInTheDocument();
    });

    it('refuses weekly hours above 25 and past exam dates at the field without calling the API', async () => {
      const targetBandView: TargetBandViewDto = {
        activeCert: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        currentBandCode: 'B1',
        currentTotalScore: 280,
        currentBandName: 'Independent',
        targetBandCode: 'B2',
        cefrBands: [],
        availableBands: [
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, description: 'Upper intermediate', isCurrent: false, isTarget: true },
        ],
        goal: null,
      };

      let goalPostCount = 0;
      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/target-band') return jsonResponse(targetBandView);
        if (url === '/api/learner/goal' && init?.method === 'POST') {
          goalPostCount += 1;
          return jsonResponse({}, 400);
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/onboarding');

      expect(await screen.findByRole('heading', { name: /set your study goal · demo/i })).toBeInTheDocument();

      const hoursInput = screen.getByLabelText(/weekly study hours/i) as HTMLInputElement;
      const dateInput = screen.getByLabelText(/target exam date/i) as HTMLInputElement;
      const form = hoursInput.closest('form');
      expect(form).toHaveAttribute('novalidate');
      expect(hoursInput).toHaveAttribute('min', '1');
      expect(hoursInput).toHaveAttribute('max', '25');

      // Enter pace above ceiling (26) and past date
      fireEvent.change(hoursInput, { target: { value: '26' } });
      fireEvent.change(dateInput, { target: { value: '2020-01-01' } });
      expect(hoursInput.value).toBe('26');
      expect(dateInput.value).toBe('2020-01-01');
      fireEvent.submit(form!);

      await waitFor(() => {
        expect(form!.textContent).toMatch(/between 1 and 25/i);
        expect(form!.textContent).toMatch(/today or in the future/i);
      });
      expect(goalPostCount).toBe(0);
    });

    it('renders no sustainable pace with earliest feasible date when suggested pace exceeds 25h/week', async () => {
      const targetBandView: TargetBandViewDto = {
        activeCert: { code: 'DEMO', name: 'DEMO - Custom Assessment' },
        currentBandCode: 'B1',
        currentTotalScore: 280,
        currentBandName: 'Independent',
        targetBandCode: 'B2',
        cefrBands: [],
        availableBands: [
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, description: 'Upper intermediate', isCurrent: false, isTarget: true },
        ],
        goal: {
          targetBandCode: 'B2',
          targetDate: '2026-11-22',
          weeklyStudyHours: 7,
          requiredHours: 60,
          availableHours: 7,
          status: 'unrealistic',
          suggestedWeeklyHours: 60,
          earliestFeasibleDate: '2027-01-17',
        },
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/target-band') return jsonResponse(targetBandView);
        return jsonResponse({}, 404);
      });

      renderRoute('/onboarding');

      expect(await screen.findByText(/no sustainable pace/i)).toBeInTheDocument();
      expect(screen.getByText('2027-01-17')).toBeInTheDocument();
      expect(screen.queryByText('60h/week')).not.toBeInTheDocument();
    });
  });

  describe('Learning Path (/learning-path)', () => {
    it('renders skeleton loading placeholder instead of centered spinner while loading', async () => {
      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return new Promise(() => {});
        return jsonResponse({}, 404);
      });

      renderRoute('/learning-path');

      await waitFor(() => {
        expect(screen.getByLabelText(/loading learning path/i)).toBeInTheDocument();
      });
      expect(screen.queryByText(/loading your learning path…/i)).not.toBeInTheDocument();
    });

    it('shows transient toast when generating learning path succeeds', async () => {
      const emptySummary: LearningPathSummaryDto = {
        hasActivePath: false,
        activeCert: { code: 'DEMO', name: 'DEMO' },
        totalSteps: 0,
        completedSteps: 0,
        totalEstimatedMinutes: 0,
        remainingMinutes: 0,
        cefrBands: [],
        openSteps: [],
      };

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return jsonResponse(emptySummary);
        if (url === '/api/learner/learning-path/generate' && init?.method === 'POST') {
          return jsonResponse({ pathId: 1 });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/learning-path');

      expect(await screen.findByRole('heading', { name: /no learning path yet/i })).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: /generate 4-week path \(5 credits\)/i }));
      fireEvent.click(await screen.findByRole('button', { name: /confirm \(5 credits\)/i }));

      await waitFor(() => {
        const toastEl = screen.getByText('Learning path generated successfully.');
        expect(toastEl.closest('[data-slot="toast-region"]')).toBeInTheDocument();
      });
    });

    it('renders empty state and allows generating path, surfacing 422 errors if unrealistic', async () => {
      const emptySummary: LearningPathSummaryDto = {
        hasActivePath: false,
        activeCert: { code: 'DEMO', name: 'DEMO' },
        totalSteps: 0,
        completedSteps: 0,
        totalEstimatedMinutes: 0,
        remainingMinutes: 0,
        cefrBands: [],
        openSteps: [],
      };

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return jsonResponse(emptySummary);
        if (url === '/api/learner/learning-path/generate' && init?.method === 'POST') {
          return jsonResponse(
            {
              title: 'Goal does not fit the time available',
              detail: 'Requires 80 hours but only 20 hours remain before your exam date.',
            },
            422,
          );
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/learning-path');

      expect(await screen.findByRole('heading', { name: /no learning path yet/i })).toBeInTheDocument();

      const generateBtn = screen.getByRole('button', { name: /generate 4-week path \(5 credits\)/i });
      fireEvent.click(generateBtn);

      const confirmBtn = await screen.findByRole('button', { name: /confirm \(5 credits\)/i });
      fireEvent.click(confirmBtn);

      expect(
        await screen.findByText(/requires 80 hours but only 20 hours remain/i),
      ).toBeInTheDocument();
    });

    it('renders active learning path with weeks, steps, and insight panel', async () => {
      const activeSummary: LearningPathSummaryDto = {
        hasActivePath: true,
        activeCert: { code: 'DEMO', name: 'DEMO' },
        learnerPathId: 101,
        fromBandCode: 'B1',
        toBandCode: 'B2',
        fromBandName: 'Independent',
        toBandName: 'Vantage',
        currentBandCode: 'B1',
        targetBandCode: 'B2',
        status: 'active',
        totalSteps: 8,
        completedSteps: 2,
        totalEstimatedMinutes: 120,
        remainingMinutes: 80,
        cefrBands: [
          { code: 'B1', name: 'Independent', minTotal: 200, maxTotal: 299, isReached: true, isCurrent: true },
          { code: 'B2', name: 'Vantage', minTotal: 300, maxTotal: 399, isReached: false, isCurrent: false, isTarget: true },
        ],
        openSteps: [],
        skillLevels: { Comprehension: 'B1', Expression: 'B1' },
        weeks: [
          {
            weekNumber: 1,
            plannedSteps: 4,
            completedSteps: 2,
            plannedMinutes: 60,
            completedMinutes: 30,
            isOnTrack: true,
            targetAccuracyPct: 75,
            actualAccuracyPct: 80.0,
            steps: [
              {
                stepId: 501,
                weekNumber: 1,
                weekSequence: 1,
                stepType: 'lesson',
                route: 'lesson_reader',
                title: 'Dialog Connectors',
                status: 'done',
                skillCode: 'CMP',
                skillName: 'Comprehension',
                modality: 'listening',
                topicCode: 'L-CONN',
                topicName: 'Connectors',
                targetCefr: 'B1',
                sizingTier: 'developing',
                canStart: false,
                targetAccuracyPct: 75,
              },
              {
                stepId: 502,
                weekNumber: 1,
                weekSequence: 2,
                stepType: 'practice',
                route: 'item_selector',
                title: 'Connectors Practice Drill',
                status: 'available',
                skillCode: 'CMP',
                skillName: 'Comprehension',
                modality: 'listening',
                topicCode: 'L-CONN',
                topicName: 'Connectors',
                targetCefr: 'B1',
                sizingTier: 'developing',
                canStart: true,
                targetAccuracyPct: 75,
              },
            ],
          },
        ],
      };

      const pathInsight: AiInsightDto = {
        insightId: 901,
        userId: 42,
        certId: 1,
        scope: 'path',
        status: 'done',
        narrative: 'You are progressing steadily through Week 1 connectors.',
        trend: 'improving',
        sourceFingerprint: 'hash',
        createdAt: '2026-11-13T00:00:00Z',
        recommendations: [
          {
            recId: 11,
            text: 'Focus on contrastive transitions in short dialogs.',
            severity: 'major',
            isResolved: false,
            createdAt: '2026-11-13T00:00:00Z',
          },
        ],
      };

      let startCalledWith: number | null = null;

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return jsonResponse(activeSummary);
        if (url === '/api/learner/insights/path') return jsonResponse(pathInsight);
        if (url === '/api/learner/learning-path/steps/502/start' && init?.method === 'POST') {
          startCalledWith = 502;
          return jsonResponse({
            stepId: 502,
            stepType: 'practice',
            route: 'item_selector',
            redirectUrl: '/practice',
          });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/learning-path');

      expect(await screen.findByRole('heading', { name: /b1 → b2 learning path/i })).toBeInTheDocument();
      expect(screen.getByText('Comprehension:')).toBeInTheDocument();
      expect(screen.getByText('Dialog Connectors')).toBeInTheDocument();
      expect(screen.getByText('Done')).toBeInTheDocument();

      // Path AI Insight verification
      expect(await screen.findByText(/you are progressing steadily through week 1 connectors/i)).toBeInTheDocument();
      expect(screen.getByText('High')).toBeInTheDocument();
      expect(screen.getByText('Focus on contrastive transitions in short dialogs.')).toBeInTheDocument();

      // Start available step
      const startBtn = screen.getByRole('button', { name: /^start$/i });
      fireEvent.click(startBtn);

      await waitFor(() => {
        expect(startCalledWith).toBe(502);
      });
    });

    it('renders sprint milestone celebration card with exit mock exam and next sprint CTAs when sprint is completed', async () => {
      const completedSummary: LearningPathSummaryDto = {
        hasActivePath: true,
        activeCert: { code: 'DEMO', name: 'DEMO' },
        learnerPathId: 101,
        fromBandCode: 'A1',
        toBandCode: 'A2',
        fromBandName: 'Elementary',
        toBandName: 'Pre-Intermediate',
        currentBandCode: 'A1',
        targetBandCode: 'B2',
        status: 'completed',
        totalSteps: 20,
        completedSteps: 20,
        totalEstimatedMinutes: 300,
        remainingMinutes: 0,
        isSprintCompleted: true,
        nextMilestoneBandCode: 'B1',
        nextMilestoneBandName: 'Intermediate',
        canStartNextSprint: true,
        cefrBands: [
          { code: 'A1', name: 'Elementary', minTotal: 100, maxTotal: 199, isReached: true, isCurrent: true },
          { code: 'A2', name: 'Pre-Intermediate', minTotal: 200, maxTotal: 299, isReached: true, isCurrent: false, isTarget: false },
          { code: 'B1', name: 'Intermediate', minTotal: 300, maxTotal: 399, isReached: false, isCurrent: false, isTarget: false },
          { code: 'B2', name: 'Upper Intermediate', minTotal: 400, maxTotal: 499, isReached: false, isCurrent: false, isTarget: true },
        ],
        openSteps: [],
        weeks: [],
      };

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return jsonResponse(completedSummary);
        if (url === '/api/learner/insights/path') return jsonResponse({}, 404);
        return jsonResponse({}, 404);
      });

      renderRoute('/learning-path');

      expect(await screen.findByRole('heading', { name: /sprint milestone achieved!/i })).toBeInTheDocument();
      expect(screen.getByText(/milestone completed/i)).toBeInTheDocument();
      expect(screen.getByText(/20\/20 steps completed/i)).toBeInTheDocument();

      // Primary CTA: Take Exit Mock Exam
      const exitMockBtn = screen.getByRole('button', { name: /take exit mock exam/i });
      expect(exitMockBtn).toBeInTheDocument();

      // Next sprint CTA
      const nextSprintBtn = screen.getByRole('button', { name: /unlock next sprint \(intermediate\)/i });
      expect(nextSprintBtn).toBeInTheDocument();

      // Click to open confirmation
      fireEvent.click(nextSprintBtn);
      expect(await screen.findByText(/spend 5 credits to start next sprint\?/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /confirm \(5 credits\)/i })).toBeInTheDocument();
    });
  });

  describe('Micro-Lesson Reader (/lessons/$lessonId)', () => {
    it('renders skeleton loading placeholder instead of centered spinner while loading', async () => {
      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url.startsWith('/api/learner/lessons/')) return new Promise(() => {});
        return jsonResponse({}, 404);
      });

      renderRoute('/lessons/42');

      await waitFor(() => {
        expect(screen.getByLabelText(/loading lesson/i)).toBeInTheDocument();
      });
      expect(screen.queryByText(/loading micro-lesson…/i)).not.toBeInTheDocument();
    });

    it('renders deck cards, tracks card view on Next, and grades micro-check via server POST', async () => {
      const lessonDetail: LessonDetailDto = {
        lessonId: 701,
        title: 'Contrastive Connectors',
        skillId: 1,
        topicId: 12,
        topicCode: 'L-CONN',
        topicName: 'Connectors',
        cefrLevel: 'B1',
        estimatedMinutes: 8,
        module: 'primary',
        hasRemedialModule: true,
        cards: [
          {
            cardId: 1,
            cardType: 'rule',
            title: 'Core Rule: Although vs However',
            contentMarkdown: 'Use **although** for subordinate clauses, and **however** for adverbial transitions.',
            displayOrder: 1,
            module: 'primary',
          },
          {
            cardId: 2,
            cardType: 'micro_check',
            title: 'Quick Check',
            contentMarkdown: 'Choose the connector that best completes the sentence.',
            questionPrompt: '____ the rain was heavy, we arrived on time.',
            optionA: 'Although',
            optionB: 'However',
            optionC: 'Despite of',
            optionD: 'Because',
            displayOrder: 2,
            module: 'primary',
          },
        ],
      };

      const lessonProgress: LessonProgressDto = {
        cardsCompleted: 0,
        totalCards: 2,
        isCompleted: false,
        resumeCardIndex: 1,
        module: 'primary',
      };

      let recordedCardView: number | null = null;
      let microCheckPayload: any = null;

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url.startsWith('/api/learner/lessons/701/cards/2/view') && init?.method === 'POST') {
          recordedCardView = 2;
          return new Response(null, { status: 204 });
        }
        if (url.startsWith('/api/learner/lessons/701/cards/2/micro-check') && init?.method === 'POST') {
          microCheckPayload = JSON.parse(String(init.body));
          return jsonResponse({
            isCorrect: true,
            correctOption: 'A',
            explanation: 'Although is a subordinating conjunction followed by a full clause.',
          });
        }
        if (url.startsWith('/api/learner/lessons/701/progress')) return jsonResponse(lessonProgress);
        if (url.startsWith('/api/learner/lessons/701')) return jsonResponse(lessonDetail);
        return jsonResponse({}, 404);
      });

      renderRoute('/lessons/701');

      // Card 1: Rule
      expect(await screen.findByRole('heading', { name: /core rule: although vs however/i })).toBeInTheDocument();
      expect(screen.getByText('although')).toBeInTheDocument();

      // Click Next card
      const nextBtn = screen.getByRole('button', { name: /next card/i });
      fireEvent.click(nextBtn);

      await waitFor(() => {
        expect(recordedCardView).toBe(2);
      });

      // Card 2: Micro-check
      expect(screen.getByText('Quick Check')).toBeInTheDocument();
      expect(screen.getByText(/the rain was heavy, we arrived on time/i)).toBeInTheDocument();

      // Select Option A
      const optionA = screen.getByDisplayValue('A');
      fireEvent.click(optionA);

      // Check answer
      const checkBtn = screen.getByRole('button', { name: /check answer/i });
      fireEvent.click(checkBtn);

      await waitFor(() => {
        expect(microCheckPayload).toEqual({ selectedOption: 'A' });
      });

      // Revealed explanation
      expect(screen.getByText('Correct!')).toBeInTheDocument();
      expect(screen.getByText(/subordinating conjunction followed by a full clause/i)).toBeInTheDocument();

      // Finish button visible
      expect(screen.getByRole('link', { name: /finish lesson & return to path/i })).toBeInTheDocument();
    });
  });

  describe('Weakness Analysis (/weakness)', () => {
    it('renders empty state when learner has no data', async () => {
      const emptyWeakness: WeaknessAnalysisDto = {
        certificate: { code: 'DEMO', name: 'DEMO', sectionNoun: 'Section' },
        hasData: false,
        gradedExamCount: 0,
        completedPracticeCount: 0,
        totalAnswered: 0,
        totalCorrect: 0,
        overallAccuracyPercentage: 0,
        sections: [],
        frequentlyMissedTopics: [],
        recommendations: [],
        priorityPracticeSection: null,
        topics: [],
        weakestTopic: null,
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/weakness-analysis') return jsonResponse(emptyWeakness);
        if (url.startsWith('/api/learner/insights/dashboard')) return jsonResponse(null, 404);
        return jsonResponse({}, 404);
      });

      renderRoute('/weakness');

      expect(await screen.findByRole('heading', { name: /not enough data to analyse/i })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /practice a section/i })).toBeInTheDocument();
    });

    it('renders sections, suppresses weakest badge below observation floor and on unattempted sections, and allows resolving recommendations', async () => {
      const subFloorTopic = {
        topicId: 11,
        topicCode: 'R-INF',
        topicName: 'Inference',
        modality: 'reading' as const,
        levelCode: 'B2',
        answered: 4,
        correct: 1,
        accuracyPercent: 25,
        isWeakest: false,
        observationFloor: 12,
        lessonId: null,
      };
      const rankedTopic = {
        topicId: 10,
        topicCode: 'L-DET',
        topicName: 'Key details',
        modality: 'listening' as const,
        levelCode: 'B1',
        answered: 15,
        correct: 8,
        accuracyPercent: 53,
        isWeakest: true,
        observationFloor: 12,
        lessonId: null,
      };

      const weaknessWithData: WeaknessAnalysisDto = {
        certificate: { code: 'DEMO', name: 'DEMO', sectionNoun: 'Section' },
        hasData: true,
        gradedExamCount: 1,
        completedPracticeCount: 3,
        totalAnswered: 44,
        totalCorrect: 25,
        overallAccuracyPercentage: 57,
        sections: [
          {
            sectionId: 1,
            code: 'U1',
            name: 'Short dialogs',
            answered: 30,
            correct: 18,
            accuracyPercentage: 60,
            isWeakest: true,
            isStrongest: false,
          },
          {
            sectionId: 2,
            code: 'U2',
            name: 'Long talks',
            answered: 14,
            correct: 12,
            accuracyPercentage: 86,
            isWeakest: false,
            isStrongest: true,
          },
          {
            sectionId: 3,
            code: 'U3',
            name: 'Reading passages',
            answered: 0,
            correct: 0,
            accuracyPercentage: 0,
            isWeakest: false,
            isStrongest: false,
          },
        ],
        frequentlyMissedTopics: [subFloorTopic, rankedTopic],
        recommendations: [
          {
            recId: 77,
            text: 'Review time markers in short conversations.',
            severity: 'major',
            targetBandCode: 'B2',
            skillName: 'Listening',
            createdAt: '2026-11-13T00:00:00Z',
            isResolved: false,
          },
        ],
        priorityPracticeSection: {
          sectionId: 1,
          code: 'U1',
          name: 'Short dialogs',
          answered: 30,
          correct: 18,
          accuracyPercentage: 60,
          isWeakest: true,
          isStrongest: false,
        },
        topics: [rankedTopic, subFloorTopic],
        weakestTopic: rankedTopic,
      };

      let resolvedRecId: number | null = null;

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/weakness-analysis') return jsonResponse(weaknessWithData);
        if (url.startsWith('/api/learner/insights/dashboard')) return jsonResponse(null, 404);
        if (url === '/api/learner/recommendations/77/resolve' && init?.method === 'POST') {
          resolvedRecId = 77;
          return jsonResponse({ recId: 77, isResolved: true });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/weakness');

      expect(await screen.findByRole('heading', { name: /weakness analysis · demo/i })).toBeInTheDocument();

      // Weakest topic summary renders real percentage and answer count
      expect(screen.getAllByText(/53%/).length).toBeGreaterThanOrEqual(1);
      expect(screen.getAllByText(/8\/15 answered/i).length).toBeGreaterThanOrEqual(1);

      // Topic table rows render skill, percentage, answer count, and evidence note below floor
      const rankedRow = screen.getAllByText('Key details').find((el) => el.closest('tr'))!.closest('tr')!;
      expect(rankedRow).toHaveTextContent('Listening');
      expect(rankedRow).toHaveTextContent('53%');
      expect(rankedRow).toHaveTextContent('8/15 answered');
      expect(rankedRow).toHaveTextContent('Weakest');

      const subFloorRow = screen.getAllByText('Inference').find((el) => el.closest('tr'))!.closest('tr')!;
      expect(subFloorRow).toHaveTextContent('Reading');
      expect(subFloorRow).toHaveTextContent('25%');
      expect(subFloorRow).toHaveTextContent('1/4 answered');
      expect(subFloorRow).toHaveTextContent(/not enough evidence \(12 needed\)/i);
      expect(subFloorRow).not.toHaveTextContent('Weakest');

      // Section accuracy: attempted sections show names and %, unattempted section never carries Weakest badge
      expect(screen.getByText('U1 — Short dialogs')).toBeInTheDocument();
      expect(screen.getByText(/60% · 18\/30 answered/i)).toBeInTheDocument();
      expect(screen.getByText('U2 — Long talks')).toBeInTheDocument();
      expect(screen.getByText(/86% · 12\/14 answered/i)).toBeInTheDocument();

      const unattemptedHeader = screen.getByText('U3 — Reading passages').closest('div')!;
      expect(unattemptedHeader).not.toHaveTextContent('Weakest');
      expect(screen.getByText('Not attempted yet')).toBeInTheDocument();

      // Frequently missed topics render name, level, percentage, and count
      expect(screen.getByText(/8\/15 correct/i)).toBeInTheDocument();
      expect(screen.getByText(/1\/4 correct/i)).toBeInTheDocument();

      // Priority practice section uses real code
      expect(screen.getByText(/Practice U1 now/i)).toBeInTheDocument();

      // Resolve recommendation
      expect(screen.getByText('Review time markers in short conversations.')).toBeInTheDocument();
      const resolveBtn = screen.getByRole('button', { name: /mark resolved/i });
      fireEvent.click(resolveBtn);

      await waitFor(() => {
        expect(resolvedRecId).toBe(77);
        const toastEl = screen.getByText('Recommendation marked as resolved.');
        expect(toastEl.closest('[data-slot="toast-region"]')).toBeInTheDocument();
      });
    });

    it('renders skeleton loading placeholder instead of centered spinner while loading', async () => {
      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/weakness-analysis') return new Promise(() => {});
        if (url.startsWith('/api/learner/insights/dashboard')) return new Promise(() => {});
        return jsonResponse({}, 404);
      });

      renderRoute('/weakness');

      await waitFor(() => {
        expect(screen.getByLabelText(/loading weakness analysis/i)).toBeInTheDocument();
      });
      expect(screen.queryByText(/loading weakness analysis…/i)).not.toBeInTheDocument();
    });

    it('shows floor notice and suppresses Weakest badge when all topics are below observation floor', async () => {
      const subFloorTopic = {
        topicId: 11,
        topicCode: 'R-INF',
        topicName: 'Inference',
        modality: 'reading' as const,
        levelCode: 'B2',
        answered: 4,
        correct: 1,
        accuracyPercent: 25,
        isWeakest: true, // even if mistakenly flagged, UI suppresses below floor
        observationFloor: 12,
        lessonId: null,
      };

      const subFloorOnly: WeaknessAnalysisDto = {
        certificate: { code: 'DEMO', name: 'DEMO', sectionNoun: 'Section' },
        hasData: true,
        gradedExamCount: 0,
        completedPracticeCount: 1,
        totalAnswered: 4,
        totalCorrect: 1,
        overallAccuracyPercentage: 25,
        sections: [],
        frequentlyMissedTopics: [subFloorTopic],
        recommendations: [],
        priorityPracticeSection: null,
        topics: [subFloorTopic],
        weakestTopic: null,
      };

      installFetchMock((url) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/weakness-analysis') return jsonResponse(subFloorOnly);
        if (url.startsWith('/api/learner/insights/dashboard')) return jsonResponse(null, 404);
        return jsonResponse({}, 404);
      });

      renderRoute('/weakness');

      expect(await screen.findByText(/no topic holds enough answers to be called a weakness yet/i)).toBeInTheDocument();
      const row = screen.getAllByText('Inference').find((el) => el.closest('tr'))!.closest('tr')!;
      expect(row).not.toHaveTextContent('Weakest');
      expect(row).toHaveTextContent(/not enough evidence \(12 needed\)/i);
    });
  });
});
