import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../context/AuthContext';
import { createAppRouter } from '../../../router';
import type {
  AuthUserDto,
  LearnerContextDto,
  LearningPathSummaryDto,
} from '../../../lib/api';

function installFetchMock(
  handler: (url: string, init?: RequestInit) => Promise<Response> | Response,
) {
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

function renderLearningPathRoute() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const history = createMemoryHistory({ initialEntries: ['/learning-path'] });
  const router = createAppRouter(history);

  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  );
}

const DEMO_USER: AuthUserDto = {
  userId: 42,
  email: 'learner@demo.local',
  fullName: 'Demo Learner',
  role: 'Learner',
  emailVerified: true,
};

const DEMO_CONTEXT: LearnerContextDto = {
  active: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
  certificates: [{ code: 'TOEIC', name: 'TOEIC Listening & Reading' }],
  creditBalance: 150,
  examinedSkills: ['listening', 'reading'],
  examDate: '2026-12-01',
};

function makeWeek(weekNumber: number, overrides: Partial<NonNullable<LearningPathSummaryDto['weeks']>[number]> = {}) {
  return {
    weekNumber,
    plannedSteps: 2,
    completedSteps: 0,
    plannedMinutes: 30,
    completedMinutes: 0,
    isOnTrack: false,
    targetAccuracyPct: 75,
    actualAccuracyPct: null,
    steps: [
      {
        stepId: weekNumber * 100 + 1,
        weekNumber,
        weekSequence: 1,
        stepType: 'lesson',
        route: 'lesson_reader',
        title: `Week ${weekNumber} Lesson`,
        status: weekNumber === 1 ? 'available' : 'locked',
        skillCode: 'LIST',
        skillName: 'Listening',
        modality: 'listening',
        topicCode: 'L-MAIN',
        topicName: 'Main Idea',
        targetCefr: 'B1',
        sizingTier: 'developing',
        canStart: weekNumber === 1,
        targetAccuracyPct: 75,
      },
    ],
    ...overrides,
  };
}

describe('LearningPathPage track status (#343)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('suppresses "Off track" on all weeks of a brand-new unstarted learning path', async () => {
    const newPathSummary: LearningPathSummaryDto = {
      hasActivePath: true,
      activeCert: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
      learnerPathId: 201,
      fromBandCode: 'A2',
      toBandCode: 'B1',
      fromBandName: 'Pre-Intermediate',
      toBandName: 'Independent',
      currentBandCode: 'A2',
      targetBandCode: 'B1',
      status: 'active',
      totalSteps: 8,
      completedSteps: 0,
      totalEstimatedMinutes: 120,
      remainingMinutes: 120,
      cefrBands: [
        { code: 'A2', name: 'Pre-Intermediate', minTotal: 225, maxTotal: 549, isReached: true, isCurrent: true },
        { code: 'B1', name: 'Independent', minTotal: 550, maxTotal: 784, isReached: false, isCurrent: false, isTarget: true },
      ],
      openSteps: [],
      weeks: [makeWeek(1), makeWeek(2), makeWeek(3), makeWeek(4)],
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
      if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
      if (url === '/api/learner/learning-path') return jsonResponse(newPathSummary);
      if (url === '/api/learner/insights/path') return jsonResponse({}, 404);
      return jsonResponse({}, 404);
    });

    renderLearningPathRoute();

    expect(await screen.findByRole('heading', { name: /a2 → b1 learning path/i })).toBeInTheDocument();
    expect(screen.getByText('Week 1')).toBeInTheDocument();
    expect(screen.getByText('Week 4')).toBeInTheDocument();
    expect(screen.queryByText(/off track/i)).not.toBeInTheDocument();
  });

  it('shows "Off track" only on started weeks that are behind, not on unstarted future weeks', async () => {
    const partialPathSummary: LearningPathSummaryDto = {
      hasActivePath: true,
      activeCert: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
      learnerPathId: 202,
      fromBandCode: 'A2',
      toBandCode: 'B1',
      fromBandName: 'Pre-Intermediate',
      toBandName: 'Independent',
      currentBandCode: 'A2',
      targetBandCode: 'B1',
      status: 'active',
      totalSteps: 4,
      completedSteps: 1,
      totalEstimatedMinutes: 60,
      remainingMinutes: 45,
      cefrBands: [],
      openSteps: [],
      weeks: [
        makeWeek(1, {
          completedSteps: 1,
          completedMinutes: 15,
          isOnTrack: false,
        }),
        makeWeek(2, {
          completedSteps: 0,
          completedMinutes: 0,
          isOnTrack: false,
        }),
        makeWeek(3, {
          completedSteps: 2,
          completedMinutes: 30,
          isOnTrack: true,
        }),
      ],
    };

    installFetchMock((url) => {
      if (url === '/api/auth/me') return jsonResponse(DEMO_USER);
      if (url === '/api/learner/context') return jsonResponse(DEMO_CONTEXT);
      if (url === '/api/learner/learning-path') return jsonResponse(partialPathSummary);
      if (url === '/api/learner/insights/path') return jsonResponse({}, 404);
      return jsonResponse({}, 404);
    });

    renderLearningPathRoute();

    expect(await screen.findByText('Week 1')).toBeInTheDocument();
    expect(screen.getAllByText('Off track')).toHaveLength(1);
    expect(screen.getAllByText('On track')).toHaveLength(1);
  });
});
