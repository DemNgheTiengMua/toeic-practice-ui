import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  AuthUserDto,
  LearnerContextDto,
  LearnerDashboardDto,
  TargetBandViewDto,
  LearningPathSummaryDto,
  CalibrationViewDto,
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
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  describe('Dashboard (/dashboard)', () => {
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

    it('shows email verification warning with working resend email when unverified', async () => {
      const unverifiedUser: AuthUserDto = {
        ...DEMO_USER,
        emailVerified: false,
      };

      let resendCalled = false;
      installFetchMock((url, init) => {
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
        if (url === '/api/auth/resend-activation' && init?.method === 'POST') {
          resendCalled = true;
          return jsonResponse({ sent: true });
        }
        return jsonResponse({}, 404);
      });

      renderRoute('/dashboard');

      const resendButton = await screen.findByRole('button', { name: /resend email/i });
      expect(resendButton).toBeInTheDocument();

      fireEvent.click(resendButton);

      await waitFor(() => {
        expect(resendCalled).toBe(true);
        expect(screen.getByText(/a new activation link has been sent to learner@demo\.local/i)).toBeInTheDocument();
      });
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

      // Select target band B2
      const b2Button = screen.getByRole('button', { name: /b2/i });
      fireEvent.click(b2Button);

      // Submit form
      const submitButton = screen.getByRole('button', { name: /save goal & continue/i });
      fireEvent.click(submitButton);

      await waitFor(() => {
        expect(submittedGoal).not.toBeNull();
        expect(submittedGoal.targetBandCode).toBe('B2');
        expect(submittedGoal.currentBandCode).toBe('B1');
      });
    });
  });

  describe('Learning Path (/learning-path)', () => {
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

      const generateBtn = screen.getByRole('button', { name: /generate path/i });
      fireEvent.click(generateBtn);

      expect(
        await screen.findByText(/requires 80 hours but only 20 hours remain/i),
      ).toBeInTheDocument();
    });

    it('renders active learning path with weeks, steps, calibration proposal, and insight panel', async () => {
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

      const calibrationView: CalibrationViewDto = {
        placedBandCode: 'B2',
        placedBandName: 'Vantage',
        validAnswerCount: 20,
        requiredValidAnswerCount: 15,
        excludedRushedCount: 2,
        observedAccuracyPct: 40.0,
        reason: 'diverged',
        proposal: {
          proposedBandCode: 'B1',
          proposedBandName: 'Independent',
          observedAccuracyPct: 40.0,
          validAnswerCount: 20,
          requiredAccuracyPct: 70,
          evidence: 'Your recent practice accuracy (40.0%) is below B2 expectations.',
        },
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
      let calibrationAdjustChosen: boolean | null = null;

      installFetchMock((url, init) => {
        if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
        if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
        if (url === '/api/learner/learning-path') return jsonResponse(activeSummary);
        if (url === '/api/learner/placement/calibration') return jsonResponse(calibrationView);
        if (url === '/api/learner/insights/path') return jsonResponse(pathInsight);
        if (url === '/api/learner/placement/calibration/decision' && init?.method === 'POST') {
          const body = JSON.parse(String(init.body));
          calibrationAdjustChosen = body.adjust;
          return jsonResponse({
            decision: body.adjust ? 'adjusted' : 'kept',
            placedBandCode: 'B2',
            proposedBandCode: 'B1',
            pathAdjusted: body.adjust,
          });
        }
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

      // Calibration divergence proposal check
      expect(await screen.findByText(/your practice does not match your placement band/i)).toBeInTheDocument();
      expect(await screen.findByText(/2 answers under 3 seconds were left out/i)).toBeInTheDocument();

      const lowerBandBtn = screen.getByRole('button', { name: /lower my starting band to b1/i });
      fireEvent.click(lowerBandBtn);

      await waitFor(() => {
        expect(calibrationAdjustChosen).toBe(true);
      });

      // Path AI Insight verification
      expect(await screen.findByText(/you are progressing steadily through week 1 connectors/i)).toBeInTheDocument();
      expect(screen.getByText('Priority')).toBeInTheDocument();
      expect(screen.getByText('Focus on contrastive transitions in short dialogs.')).toBeInTheDocument();

      // Start available step
      const startBtn = screen.getByRole('button', { name: /^start$/i });
      fireEvent.click(startBtn);

      await waitFor(() => {
        expect(startCalledWith).toBe(502);
      });
    });
  });

  describe('Micro-Lesson Reader (/lessons/$lessonId)', () => {
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
        certificate: { code: 'DEMO', name: 'DEMO' },
        hasData: false,
        gradedExamCount: 0,
        completedPracticeCount: 0,
        totalAnswered: 0,
        totalCorrect: 0,
        overallAccuracyPercentage: 0,
        sections: [],
        frequentlyMissedTopics: [],
        recommendations: [],
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

    it('renders sections, weakest topic floor note, and allows resolving recommendations', async () => {
      const weaknessWithData: WeaknessAnalysisDto = {
        certificate: { code: 'DEMO', name: 'DEMO' },
        hasData: true,
        gradedExamCount: 1,
        completedPracticeCount: 3,
        totalAnswered: 50,
        totalCorrect: 35,
        overallAccuracyPercentage: 70,
        sections: [
          {
            sectionId: 1,
            sectionCode: 'U1',
            sectionName: 'Short dialogs',
            skillCode: 'CMP',
            skillName: 'Comprehension',
            answeredCount: 30,
            correctCount: 18,
            accuracyPercentage: 60,
          },
        ],
        frequentlyMissedTopics: [
          {
            categoryId: 10,
            topicCode: 'L-DET',
            topicName: 'Key details',
            skillCode: 'CMP',
            skillName: 'Comprehension',
            answeredCount: 15,
            correctCount: 8,
            accuracyPercentage: 53,
            levelCode: 'B1',
            meetsFloor: true,
            isWeakest: true,
          },
        ],
        recommendations: [
          {
            recId: 77,
            text: 'Review time markers in short conversations.',
            severity: 'major',
            targetBandCode: 'B2',
            skillName: 'Comprehension',
            createdAt: '2026-11-13T00:00:00Z',
            isResolved: false,
          },
        ],
        topics: [
          {
            categoryId: 10,
            topicCode: 'L-DET',
            topicName: 'Key details',
            skillCode: 'CMP',
            skillName: 'Comprehension',
            answeredCount: 15,
            correctCount: 8,
            accuracyPercentage: 53,
            levelCode: 'B1',
            meetsFloor: true,
            isWeakest: true,
          },
        ],
        weakestTopic: null, // Sub-floor -> shows floor note
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

      // Floor notice verification (#243)
      expect(screen.getByText(/no topic holds enough answers to be called a weakness yet/i)).toBeInTheDocument();

      // Section accuracy
      expect(screen.getByText('U1 — Short dialogs')).toBeInTheDocument();
      expect(screen.getByText(/60% · 18\/30 answered/i)).toBeInTheDocument();

      // Frequently missed topics
      expect(screen.getAllByText('Key details')[0]).toBeInTheDocument();

      // Resolve recommendation
      expect(screen.getByText('Review time markers in short conversations.')).toBeInTheDocument();
      const resolveBtn = screen.getByRole('button', { name: /mark resolved/i });
      fireEvent.click(resolveBtn);

      await waitFor(() => {
        expect(resolvedRecId).toBe(77);
      });
    });
  });
});
