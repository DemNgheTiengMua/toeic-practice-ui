import '@testing-library/jest-dom/vitest';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory } from '@tanstack/react-router';
import { AuthProvider } from '../../../../context/AuthContext';
import { createAppRouter } from '../../../../router';
import { STAFF_PERMISSIONS } from '../../../../lib/security';
import { SCORE_AGGREGATION_RULES, deriveDefaultBands } from '../CertificatesStudioPage';

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
    scoreAggregationRule: 'sum',
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
    scoreAggregationRule: 'avg_half_step',
    presetCode: 'IELTS_AC_DEFAULT',
    isActive: true,
    skillsCount: 4,
    sectionsCount: 10,
    examsCount: 2,
  },
];

const mockPresets = [
  {
    presetCode: 'two_skill',
    name: '2-Skill (Listening & Reading)',
    description: 'Listening and Reading template',
    scoreAggregationRule: 'sum',
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
            name: 'Listening Part 1',
            displayOrder: 1,
            scoreWeight: 1,
            navigationMode: 'audio_driven',
            timerPolicyJson: null,
            layoutType: 'split_horizontal',
            optionCount: 4,
            clusterSize: 1,
            unitCount: 1,
            hideOptionText: false,
            requirePassage: false,
            requireAudio: true,
            requireImage: false,
            requiredKeywordCount: 0,
            expectedQuestionCount: 25,
            allowedComponentTypesJson: JSON.stringify(['mcq']),
            toolsConfigJson: null,
          },
        ],
      },
      {
        code: 'READING',
        name: 'Reading',
        modality: 'reading',
        displayOrder: 2,
        durationMinutes: 60,
        sections: [
          {
            code: 'PART5',
            name: 'Reading Part 1',
            displayOrder: 1,
            scoreWeight: 1,
            navigationMode: 'full_sandbox',
            timerPolicyJson: null,
            layoutType: 'split_horizontal',
            optionCount: 4,
            clusterSize: 1,
            unitCount: 1,
            hideOptionText: false,
            requirePassage: true,
            requireAudio: false,
            requireImage: false,
            requiredKeywordCount: 0,
            expectedQuestionCount: 25,
            allowedComponentTypesJson: JSON.stringify(['mcq']),
            toolsConfigJson: null,
          },
        ],
      },
    ],
  },
  {
    presetCode: 'four_skill',
    name: '4-Skill (Listening, Reading, Speaking, Writing)',
    description: 'Full 4-skill template',
    scoreAggregationRule: 'avg_half_step',
    skills: [
      {
        code: 'LISTENING',
        name: 'Listening',
        modality: 'listening',
        displayOrder: 1,
        durationMinutes: 30,
        sections: [
          {
            code: 'L1',
            name: 'Listening Section',
            displayOrder: 1,
            scoreWeight: 1,
            navigationMode: 'audio_driven',
            timerPolicyJson: null,
            layoutType: 'split_horizontal',
            optionCount: 4,
            clusterSize: 1,
            unitCount: 1,
            hideOptionText: false,
            requirePassage: false,
            requireAudio: true,
            requireImage: false,
            requiredKeywordCount: 0,
            expectedQuestionCount: 20,
            allowedComponentTypesJson: JSON.stringify(['mcq']),
            toolsConfigJson: null,
          },
        ],
      },
    ],
  },
];


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
      // #360: score-conversion and the rubric ladder have no client call and no screen, so mocking
      // their routes would only keep a retired surface looking alive.
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    expect(await screen.findByRole('heading', { name: /certificates studio/i })).toBeInTheDocument();
    expect(screen.getByText('TOEIC')).toBeInTheDocument();
    expect(screen.getByText('IELTS')).toBeInTheDocument();

    // Invariant check: Certificate Isolation notice must be visible
    expect(screen.getByText(/certificate isolation/i)).toBeInTheDocument();

    // #310: Score Conversion and Rubric Studio tabs are removed
    expect(screen.queryByRole('tab', { name: /score conversion/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /rubric studio/i })).not.toBeInTheDocument();
  });

  it('supports 3-step blueprint wizard creation with 2-skill/4-skill templates and 3-field sections', async () => {
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
          sectionsCount: 2,
        });
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    const createBtn = await screen.findByRole('button', { name: /new certificate/i });
    fireEvent.click(createBtn);

    // Modal opens at Step 1
    expect(await screen.findByRole('heading', { name: /certificate blueprint wizard/i })).toBeInTheDocument();

    // Step 1: Select 2-skill template using HeroUI Select and fill certificate code & name
    fireEvent.click(screen.getByRole('button', { name: /template/i }));
    fireEvent.click(await screen.findByRole('option', { name: /two_skill/i }));

    const codeInput = screen.getByLabelText(/certificate code/i);
    fireEvent.change(codeInput, { target: { value: 'TOEFL' } });
    const nameInput = screen.getByLabelText(/certificate name/i);
    fireEvent.change(nameInput, { target: { value: 'TOEFL iBT' } });

    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    // Step 2: 3-field sections (Code, Name, ExpectedQuestionCount)
    const countInputs = await screen.findAllByLabelText(/expected questions/i);
    expect(countInputs.length).toBeGreaterThanOrEqual(2);
    fireEvent.change(countInputs[0], { target: { value: '30' } });

    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    // Step 3: ScoreBands & flat GradingCriteria
    expect(await screen.findByRole('heading', { name: /score bands/i })).toBeInTheDocument();

    // Submit wizard
    const saveBtn = screen.getByRole('button', { name: /save blueprint/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(savedRequest).not.toBeNull();
      expect(savedRequest.presetCode).toBe('two_skill');
      expect(savedRequest.code).toBe('TOEFL');
      expect(savedRequest.name).toBe('TOEFL iBT');
      expect(savedRequest.skills[0].sections[0].expectedQuestionCount).toBe(30);
      // #356: the payload carries the three codes the API accepts and the scale it requires.
      expect(savedRequest.scoreAggregationRule).toBe('sum');
      expect(savedRequest.minScaledScore).toBe(0);
      expect(savedRequest.maxScaledScore).toBe(100);
      expect(savedRequest.scoreStep).toBe(1);
      // Two skills summed reach 200, so the posted bands must tile 0..200, not 0..100.
      const bands = savedRequest.bands as Array<{ minTotal: number; maxTotal: number }>;
      expect(bands[0].minTotal).toBe(0);
      expect(bands[bands.length - 1].maxTotal).toBe(200);
      for (let i = 1; i < bands.length; i++) {
        expect(bands[i].minTotal).toBe(bands[i - 1].maxTotal + 1);
      }
    });
  });

  it('offers exactly the three aggregation rules the API accepts (#356)', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.BlueprintDesign],
        });
      }
      if (url === '/api/admin/certificates') return jsonResponse(mockCertificates);
      if (url === '/api/admin/certificates/presets') return jsonResponse(mockPresets);
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');
    fireEvent.click(await screen.findByRole('button', { name: /new certificate/i }));
    await screen.findByRole('heading', { name: /certificate blueprint wizard/i });

    fireEvent.click(screen.getByRole('button', { name: /score aggregation rule/i }));
    const options = await screen.findAllByRole('option');
    const shown = options.map((o) => (o.textContent ?? '').trim());
    const expectedLabels = SCORE_AGGREGATION_RULES.map((r) => r.label);

    // CertificateService.Wizard.cs accepts these three codes and refuses anything else.
    expect(shown.sort()).toEqual([...expectedLabels].sort());
    expect(SCORE_AGGREGATION_RULES.map((r) => r.id).sort()).toEqual([
      'avg_half_step',
      'avg_whole',
      'sum',
    ]);
    expect(screen.queryByText(/AVERAGE/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BAND_CEFR/)).not.toBeInTheDocument();
  });

  it('selects and displays the template of a chosen preset (#356)', async () => {
    installFetchMock((url) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'author@demo.local',
          fullName: 'Content Author',
          role: 'staff',
          permissions: [STAFF_PERMISSIONS.BlueprintDesign],
        });
      }
      if (url === '/api/admin/certificates') return jsonResponse(mockCertificates);
      if (url === '/api/admin/certificates/presets') return jsonResponse(mockPresets);
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');
    fireEvent.click(await screen.findByRole('button', { name: /new certificate/i }));
    await screen.findByRole('heading', { name: /certificate blueprint wizard/i });

    fireEvent.click(screen.getByRole('button', { name: /template/i }));
    fireEvent.click(await screen.findByRole('option', { name: /four_skill/i }));

    // The preset's own rule is `avg_half_step`; the control must show it, not render empty.
    const ruleTrigger = screen.getByRole('button', { name: /score aggregation rule/i });
    expect(within(ruleTrigger).getByText(/average, rounded to nearest 0\.5/i)).toBeInTheDocument();
  });

  it('derives default bands that tile the total range for both rules and 2/4 skills (#356)', () => {
    // The validator checks exactly this: contiguous, no gap at either end, no overlap.
    const assertTiles = (rule: string, skillCount: number, min: number, max: number) => {
      const bands = deriveDefaultBands(rule, skillCount, min, max);
      const expectedMax = rule === 'sum' ? max * skillCount : max;
      const expectedMin = rule === 'sum' ? min * skillCount : min;

      expect(bands[0].minTotal).toBeLessThanOrEqual(expectedMin);
      expect(bands[bands.length - 1].maxTotal).toBeGreaterThanOrEqual(expectedMax);
      for (let i = 1; i < bands.length; i++) {
        expect(bands[i].minTotal).toBe(bands[i - 1].maxTotal + 1);
      }
      expect(bands.length).toBe(4);
    };

    assertTiles('sum', 2, 0, 100);
    assertTiles('sum', 4, 0, 100);
    assertTiles('avg_half_step', 2, 0, 100);
    assertTiles('avg_whole', 4, 0, 100);
    // A sum over two skills reaches 200, which the old hardcoded 0-100 bands never covered.
    expect(deriveDefaultBands('sum', 2, 0, 100).at(-1)!.maxTotal).toBe(200);

    // Pinned because CertificateWizardValidationTests.DerivedBands posts these same numbers
    // through the real validator; if this algorithm changes, that test's shape drifts too.
    expect(deriveDefaultBands('sum', 2, 0, 100).map((b) => [b.minTotal, b.maxTotal])).toEqual([
      [0, 50],
      [51, 101],
      [102, 152],
      [153, 200],
    ]);
    expect(deriveDefaultBands('avg_half_step', 4, 0, 100).map((b) => [b.minTotal, b.maxTotal])).toEqual([
      [0, 25],
      [26, 51],
      [52, 77],
      [78, 100],
    ]);
  });

  it('binds backend RFC 7807 field errors to HeroUI FieldError controls in the certificate wizard', async () => {
    installFetchMock((url, init) => {
      if (url === '/api/auth/me') {
        return jsonResponse({
          id: 1,
          email: 'catalog@demo.local',
          fullName: 'Catalog Manager',
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
        return jsonResponse(
          {
            title: 'Validation failed',
            status: 400,
            code: 'VALIDATION_ERROR',
            errors: {
              code: ['Certificate code is required.'],
              name: ['Certificate name is required.'],
            },
          },
          400,
        );
      }
      return jsonResponse({}, 404);
    });

    renderRoute('/admin/certificates');

    fireEvent.click(await screen.findByRole('button', { name: /new certificate/i }));
    expect(await screen.findByRole('heading', { name: /certificate blueprint wizard/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /template/i }));
    fireEvent.click(await screen.findByRole('option', { name: /two_skill/i }));
    fireEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findAllByLabelText(/expected questions/i);
    fireEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByRole('heading', { name: /score bands/i });

    fireEvent.click(screen.getByRole('button', { name: /save blueprint/i }));

    expect(await screen.findByText('Certificate code is required.')).toBeInTheDocument();
    expect(screen.getByText('Certificate name is required.')).toBeInTheDocument();
  });
});
