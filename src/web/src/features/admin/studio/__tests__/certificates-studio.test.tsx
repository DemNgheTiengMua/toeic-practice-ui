import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../../context/AuthContext';
import { createAppRouter } from '../../../../router';
import { STAFF_PERMISSIONS } from '../../../../lib/security';

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

const mockCertificates = [
  {
    certId: 1,
    code: 'TOEIC',
    name: 'TOEIC Listening and Reading',
    description: 'Test of English for International Communication',
    scoreAggregationRule: 'SUM',
    presetCode: 'TOEIC_LR_DEFAULT',
    isActive: true,
    skillsCount: 2,
    sectionsCount: 7,
    examsCount: 4,
  },
  {
    certId: 2,
    code: 'IELTS',
    name: 'IELTS Academic',
    description: 'International English Language Testing System',
    scoreAggregationRule: 'AVERAGE',
    presetCode: 'IELTS_AC_DEFAULT',
    isActive: true,
    skillsCount: 4,
    sectionsCount: 10,
    examsCount: 2,
  },
];

const mockPresets = [
  {
    presetCode: 'TOEIC_LR_DEFAULT',
    name: 'TOEIC L&R Blueprint Preset',
    description: 'Standard 200-question listening and reading layout',
    scoreAggregationRule: 'SUM',
    skills: [
      {
        code: 'LISTENING',
        name: 'Listening',
        modality: 'listening',
        displayOrder: 1,
        durationMinutes: 45,
        sections: [
          {
            code: 'PART1',
            name: 'Photographs',
            displayOrder: 1,
            scoreWeight: 1,
            navigationMode: 'audio_driven',
            timerPolicyJson: null,
            layoutType: 'split_horizontal',
            optionCount: 4,
            clusterSize: 1,
            unitCount: 1,
            hideOptionText: true,
            requirePassage: false,
            requireAudio: true,
            requireImage: true,
            requiredKeywordCount: 0,
            expectedQuestionCount: 6,
            allowedComponentTypesJson: JSON.stringify(['mcq']),
            toolsConfigJson: null,
          },
        ],
      },
    ],
  },
];

const mockScoreConversion = {
  certId: 1,
  certCode: 'TOEIC',
  certName: 'TOEIC Listening and Reading',
  certificates: [{ certId: 1, code: 'TOEIC', name: 'TOEIC Listening and Reading' }],
  examId: null,
  examName: null,
  skillScales: [
    {
      skillId: 10,
      skillCode: 'LISTENING',
      skillName: 'Listening',
      maxQuestions: 100,
      scales: [
        { scaleId: 1, skillId: 10, skillCode: 'LISTENING', skillName: 'Listening', rawScore: 0, scaledScore: 5 },
        { scaleId: 2, skillId: 10, skillCode: 'LISTENING', skillName: 'Listening', rawScore: 100, scaledScore: 495 },
      ],
    },
  ],
  bands: [
    { bandId: 101, code: 'GOLD', name: 'Gold Proficiency', minTotal: 860, maxTotal: 990, displayOrder: 1 },
    { bandId: 102, code: 'BLUE', name: 'Blue Working', minTotal: 730, maxTotal: 855, displayOrder: 2 },
  ],
};

const mockRubricStudio = {
  certId: 1,
  certCode: 'TOEIC',
  skillId: 12,
  skillCode: 'SPEAKING',
  skillName: 'Speaking',
  modality: 'speaking',
  isLocked: false,
  lockedAt: null,
  criteria: [
    {
      criterionId: 201,
      code: 'PRON',
      name: 'Pronunciation and Intonation',
      maxScore: 3,
      weight: 1,
      displayOrder: 1,
      cells: [
        { band: 0, descriptor: 'Unintelligible pronunciation', binaryGates: 'No English words articulated' },
        { band: 1, descriptor: 'Frequent severe pronunciation errors', binaryGates: 'Single words identifiable' },
        { band: 2, descriptor: 'Frequent lapses in stress and intonation', binaryGates: 'Phonemes mostly clear in isolation' },
        { band: 3, descriptor: 'Intelligible though accent is evident', binaryGates: 'Sustained phrasing with minor errors' },
        { band: 4, descriptor: 'Generally clear with natural rhythm', binaryGates: 'Intonation supports meaning' },
        { band: 5, descriptor: 'Consistently clear and precise', binaryGates: 'Stress and rhythm consistently accurate' },
        { band: 6, descriptor: 'Near-native fluency and precision', binaryGates: 'Complete phonetic mastery' },
      ],
      anchors: [
        { band: 2, responseText: 'Sample A2 benchmark audio response transcript' },
        { band: 3, responseText: 'Sample B1 benchmark audio response transcript' },
        { band: 4, responseText: 'Sample B2 benchmark audio response transcript' },
      ],
    },
  ],
  ladder: null,
};

describe('Certificates Studio (/admin/certificates) (#303)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.scrollTo = vi.fn();
    if (typeof window.ResizeObserver === 'undefined') {
      window.ResizeObserver = class {
        observe() {}
        unobserve() {}
        disconnect() {}
      } as any;
    }
    if (!Element.prototype.getAnimations) {
      Element.prototype.getAnimations = () => [] as any;
    }
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('refuses access to unauthorized learners without catalog management permissions', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 42,
          email: 'learner@demo.local',
          fullName: 'Learner User',
          role: 'learner',
          permissions: [],
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    expect(await screen.findByText(/access denied/i)).toBeInTheDocument();
  });

  it('renders certificate catalog and isolation notice for authorized catalog managers', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.BlueprintDesign, STAFF_PERMISSIONS.ContentAuthor],
        });
      }
      if (url === '/api/admin/certificates') {
        return jsonResponse(mockCertificates);
      }
      if (url === '/api/admin/certificates/presets') {
        return jsonResponse(mockPresets);
      }
      if (url.startsWith('/api/admin/score-conversion')) {
        return jsonResponse(mockScoreConversion);
      }
      if (url.startsWith('/api/admin/studio/rubric')) {
        return jsonResponse(mockRubricStudio);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    expect(await screen.findByRole('heading', { name: /certificates studio/i })).toBeInTheDocument();
    expect(screen.getByText('TOEIC')).toBeInTheDocument();
    expect(screen.getByText('IELTS')).toBeInTheDocument();

    // Invariant check: Certificate Isolation notice must be visible
    expect(screen.getByText(/certificate isolation/i)).toBeInTheDocument();
  });

  it('supports blueprint wizard creation with presets and saves via /api/admin/certificates/wizard', async () => {
    let savedRequest: any = null;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.BlueprintDesign],
        });
      }
      if (url === '/api/admin/certificates') {
        return jsonResponse(mockCertificates);
      }
      if (url === '/api/admin/certificates/presets') {
        return jsonResponse(mockPresets);
      }
      if (url === '/api/admin/certificates/wizard' && init?.method === 'POST') {
        savedRequest = JSON.parse(init.body as string);
        return jsonResponse({
          certId: 3,
          code: savedRequest.code,
          name: savedRequest.name,
          skillsCount: savedRequest.skills.length,
          sectionsCount: 1,
        });
      }
      if (url.startsWith('/api/admin/score-conversion')) {
        return jsonResponse(mockScoreConversion);
      }
      if (url.startsWith('/api/admin/studio/rubric')) {
        return jsonResponse(mockRubricStudio);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    const createBtn = await screen.findByRole('button', { name: /new certificate/i });
    fireEvent.click(createBtn);

    // Modal opens
    expect(await screen.findByRole('heading', { name: /certificate blueprint wizard/i })).toBeInTheDocument();

    // Select preset
    const presetSelect = screen.getByLabelText(/preset/i);
    fireEvent.change(presetSelect, { target: { value: 'TOEIC_LR_DEFAULT' } });

    // Fill certificate code and name
    const codeInput = screen.getByLabelText(/certificate code/i);
    fireEvent.change(codeInput, { target: { value: 'TOEFL' } });
    const nameInput = screen.getByLabelText(/certificate name/i);
    fireEvent.change(nameInput, { target: { value: 'TOEFL iBT' } });

    // Submit wizard
    const saveBtn = screen.getByRole('button', { name: /save blueprint/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(savedRequest).not.toBeNull();
      expect(savedRequest.code).toBe('TOEFL');
      expect(savedRequest.name).toBe('TOEFL iBT');
    });
  });

  it('runs score preview simulation and rubric calibration ladder with authoritative outcome', async () => {
    let ladderRan = false;

    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.BlueprintDesign, STAFF_PERMISSIONS.ContentAuthor],
        });
      }
      if (url === '/api/admin/certificates') {
        return jsonResponse(mockCertificates);
      }
      if (url === '/api/admin/certificates/presets') {
        return jsonResponse(mockPresets);
      }
      if (url.startsWith('/api/admin/score-conversion/preview') && init?.method === 'POST') {
        return jsonResponse({
          scoreAggregationRule: 'SUM',
          skillScores: [
            { skillId: 10, skillCode: 'LISTENING', rawScore: 50, scaledScore: 250, sections: [] },
          ],
          totalScore: 250,
          bandCode: 'BLUE',
          bandName: 'Blue Working',
        });
      }
      if (url.startsWith('/api/admin/score-conversion')) {
        return jsonResponse(mockScoreConversion);
      }
      if (url === '/api/admin/studio/rubric/ladder' && init?.method === 'POST') {
        ladderRan = true;
        return jsonResponse({
          outcome: 'accepted',
          accepted: true,
          reason: 'Every adjacent band separated cleanly and the verbose trap stayed under ceiling.',
          responsibleCriteria: [],
          points: [
            { band: 0, composite: 0.2, isVerboseTrap: false },
            { band: 1, composite: 1.1, isVerboseTrap: false },
            { band: 2, composite: 2.3, isVerboseTrap: false },
            { band: 3, composite: 3.4, isVerboseTrap: false },
            { band: 4, composite: 4.5, isVerboseTrap: false },
            { band: 5, composite: 5.6, isVerboseTrap: false },
            { band: 6, composite: 6.0, isVerboseTrap: false },
            { band: 3, composite: 3.2, isVerboseTrap: true },
          ],
          ranAt: new Date().toISOString(),
        });
      }
      if (url.startsWith('/api/admin/studio/rubric')) {
        return jsonResponse(mockRubricStudio);
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    // Switch to Rubric Studio tab
    const rubricTab = await screen.findByRole('tab', { name: /rubric studio/i });
    fireEvent.click(rubricTab);

    // Verify 28-cell grid rendered with distinct Descriptor and Binary Gates columns
    expect(await screen.findByText('Pronunciation and Intonation')).toBeInTheDocument();
    expect(screen.getByText('Pre-A1')).toBeInTheDocument();
    expect(screen.getByText('Unintelligible pronunciation')).toBeInTheDocument();
    expect(screen.getByText('No English words articulated')).toBeInTheDocument();

    // Trigger ladder calibration
    const runLadderBtn = screen.getByRole('button', { name: /run calibration ladder/i });
    fireEvent.click(runLadderBtn);

    await waitFor(() => {
      expect(ladderRan).toBe(true);
      expect(screen.getByText(/calibrated: every adjacent band separated/i)).toBeInTheDocument();
    });
  });
});
