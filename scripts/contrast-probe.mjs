#!/usr/bin/env node
/**
 * Contrast & font-coverage probe for ExtraEfficiency React SPA (#334).
 *
 * Usage:
 *   node scripts/contrast-probe.mjs                 # builds/serves dist via vite preview with mocked /api/*
 *   node scripts/contrast-probe.mjs --url http://localhost:5000   # probes a running server
 *   node scripts/contrast-probe.mjs --no-mock       # disables /api/* mocking
 */

import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..');
const webRoot = path.join(repoRoot, 'src', 'web');

function loadPlaywright() {
  try {
    return require('playwright');
  } catch {
    const fallback = path.join(os.homedir(), 'node_modules', 'playwright');
    if (fs.existsSync(fallback)) {
      return require(fallback);
    }
    throw new Error('Playwright not found. Install playwright or ensure ~/node_modules/playwright exists.');
  }
}

function parseArgs(argv) {
  let url = null;
  let mockApi = true;
  for (let i = 2; i < argv.length; i++) {
    if (argv[i] === '--url' && argv[i + 1]) {
      url = argv[++i];
      mockApi = false;
    } else if (argv[i] === '--no-mock') {
      mockApi = false;
    } else if (argv[i] === '--mock') {
      mockApi = true;
    }
  }
  return { url, mockApi };
}

const ROUTES = [
  '/',
  '/pricing',
  '/certificates',
  '/sign-in',
  '/dashboard',
  '/practice',
  '/wallet',
  '/exams',
];

function installApiMocks(page) {
  const user = {
    userId: 101,
    email: 'learner@demo.local',
    fullName: 'Demo Learner',
    role: 'Learner',
    emailVerified: true,
    permissions: [],
  };

  const learnerContext = {
    active: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
    certificates: [
      { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
      { code: 'IELTS', name: 'IELTS Academic' },
    ],
    creditBalance: 12,
    examinedSkills: ['listening', 'reading'],
    examDate: '2026-12-31',
  };

  return page.route('**/api/**', async (route) => {
    const reqUrl = new URL(route.request().url());
    const pathname = reqUrl.pathname;
    const json = (body, status = 200) =>
      route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify(body),
      });

    if (pathname === '/api/auth/me') return json(user);
    if (pathname === '/api/learner/context') return json(learnerContext);
    if (pathname === '/api/learner/onboarding-state') {
      return json({
        hasCompletedPlacement: true,
        activePlacementAttemptId: null,
        activePlacementId: null,
        latestPlacementId: 1,
        hasTargetGoal: true,
        hasActiveLearningPath: true,
        isOobeComplete: true,
      });
    }
    if (pathname === '/api/certificates' || pathname === '/api/public/certificates') {
      return json([
        {
          certId: 1,
          code: 'TOEIC',
          name: 'TOEIC Listening & Reading',
          description: 'Business English Listening and Reading certification.',
          maxScore: 990,
          scoreStep: 5,
          isActive: true,
        },
      ]);
    }
    if (pathname === '/api/credits/packages' || pathname === '/api/public/pricing') {
      return json({
        vndPerCredit: 20000,
        minCredits: 1,
        maxCredits: 100,
        packages: [
          { packageId: 1, name: 'Starter Pack', credits: 5, priceVnd: 100000, isPopular: false },
          { packageId: 2, name: 'Standard Pack', credits: 20, priceVnd: 400000, isPopular: true },
        ],
      });
    }
    if (pathname === '/api/learner/dashboard') {
      return json({
        certificate: { code: 'TOEIC', name: 'TOEIC Listening & Reading' },
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
          currentScore: 550,
          targetBandCode: 'B2',
          targetBandName: 'Vantage',
          targetMinScore: 785,
          targetDate: '2026-12-31',
          weeklyStudyHours: 7,
          status: 'feasible',
          completedPathSteps: 4,
          totalPathSteps: 12,
          currentWeekNumber: 2,
          weekPlannedMinutes: 120,
          weekCompletedMinutes: 60,
          scoreDisplayFactor: 1,
          requiredTotalHours: 40,
          overallLevelCode: 'B1',
          limitingSkillCode: 'RDG',
          limitingSkillName: 'Reading',
          hasNoMeasurement: false,
        },
        skills: [
          {
            skillId: 1,
            code: 'LST',
            name: 'Listening',
            modality: 'listening',
            latestScaledScore: 300,
            maxScaledScore: 495,
            levelCode: 'B1',
            positionInBand: 60,
            weakestTopics: [],
            scoreDisplayFactor: 1,
            measuredAt: '2026-11-10T10:00:00Z',
            isStale: false,
          },
          {
            skillId: 2,
            code: 'RDG',
            name: 'Reading',
            modality: 'reading',
            latestScaledScore: null,
            maxScaledScore: 495,
            levelCode: null,
            positionInBand: null,
            weakestTopics: [],
            scoreDisplayFactor: 1,
            measuredAt: null,
            isStale: false,
          },
        ],
        attemptCount: 1,
        practiceCount: 2,
      });
    }
    if (pathname === '/api/learner/practice/catalog') {
      return json({
        certificateCode: 'TOEIC',
        sections: [
          {
            sectionId: 1,
            code: 'P1',
            name: 'Photographs',
            skillName: 'Listening',
            questionCount: 30,
            isUnlocked: true,
          },
          {
            sectionId: 2,
            code: 'P5',
            name: 'Incomplete Sentences',
            skillName: 'Reading',
            questionCount: 40,
            isUnlocked: false,
          },
        ],
        topics: [
          { topicId: 1, name: 'Office Conversations', sectionId: 1, questionCount: 15 },
        ],
        cefrLevels: ['A2', 'B1', 'B2'],
      });
    }
    if (pathname.startsWith('/api/learner/practice/free-drill')) {
      return json({
        totalEligible: 30,
        topics: [],
        sections: [],
      });
    }
    if (pathname === '/api/learner/wallet') {
      return json({
        creditBalance: 12,
        transactions: [
          {
            transactionId: 1,
            createdAt: '2026-11-14T10:00:00Z',
            transactionType: 'purchase',
            amount: 15,
            description: 'Purchased 15 credits',
          },
          {
            transactionId: 2,
            createdAt: '2026-11-15T09:00:00Z',
            transactionType: 'mock_exam',
            amount: -5,
            description: 'Mock exam sitting',
          },
        ],
        orders: [
          {
            orderId: 10,
            orderCode: 'ORD-1001',
            createdAt: '2026-11-14T10:00:00Z',
            credits: 15,
            amountVnd: 300000,
            status: 'paid',
          },
        ],
      });
    }
    if (pathname === '/api/learner/exams') {
      return json({
        certificateCode: 'TOEIC',
        creditBalance: 12,
        exams: [
          {
            examId: 1,
            title: 'TOEIC Practice Test 01',
            durationMinutes: 120,
            questionCount: 200,
            creditCost: 5,
            isCompleted: false,
          },
        ],
      });
    }
    return json({});
  });
}

async function launchBrowser(chromium) {
  for (const channel of ['chrome', 'msedge', undefined]) {
    try {
      return await chromium.launch(channel ? { channel } : {});
    } catch {
      // try next channel
    }
  }
  throw new Error('Could not launch Chromium, Chrome, or Edge via Playwright.');
}

async function measurePage(page, route, theme) {
  await page.evaluate((t) => {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(t);
    root.setAttribute('data-theme', t);
    window.localStorage.setItem('ee_theme', t);
  }, theme);
  await page.waitForTimeout(150);

  return page.evaluate(
    ({ routeName, themeName }) => {
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      const ctx = canvas.getContext('2d', { willReadFrequently: true });

      function parseCssColor(cssColor) {
        if (!cssColor || cssColor === 'transparent' || cssColor === 'rgba(0, 0, 0, 0)') {
          return { r: 0, g: 0, b: 0, a: 0 };
        }
        const m = cssColor.match(/^rgba?\(([^)]+)\)$/);
        if (m) {
          const parts = m[1].split(/[\s,/]+/).filter(Boolean).map(Number);
          if (parts.length >= 3) {
            return {
              r: parts[0],
              g: parts[1],
              b: parts[2],
              a: parts.length >= 4 && !Number.isNaN(parts[3]) ? parts[3] : 1,
            };
          }
        }
        ctx.clearRect(0, 0, 1, 1);
        ctx.fillStyle = '#000000';
        ctx.fillStyle = cssColor;
        ctx.fillRect(0, 0, 1, 1);
        const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
        return { r, g, b, a: a / 255 };
      }

      function compositeOver(fg, bg) {
        const a = fg.a + bg.a * (1 - fg.a);
        if (a === 0) return { r: 0, g: 0, b: 0, a: 0 };
        return {
          r: (fg.r * fg.a + bg.r * bg.a * (1 - fg.a)) / a,
          g: (fg.g * fg.a + bg.g * bg.a * (1 - fg.a)) / a,
          b: (fg.b * fg.a + bg.b * bg.a * (1 - fg.a)) / a,
          a,
        };
      }

      function luminance({ r, g, b }) {
        const toLin = (c) => {
          const s = c / 255;
          return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
        };
        return 0.2126 * toLin(r) + 0.7152 * toLin(g) + 0.0722 * toLin(b);
      }

      function contrastRatio(fg, bg) {
        const l1 = luminance(fg);
        const l2 = luminance(bg);
        const lighter = Math.max(l1, l2);
        const darker = Math.min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
      }

      const defaultBg =
        themeName === 'dark' ? { r: 9, g: 9, b: 11, a: 1 } : { r: 255, g: 255, b: 255, a: 1 };

      function getEffectiveBg(el) {
        const layers = [];
        let cur = el;
        while (cur && cur.nodeType === Node.ELEMENT_NODE) {
          const style = getComputedStyle(cur);
          const bg = parseCssColor(style.backgroundColor);
          if (bg.a > 0) {
            layers.push(bg);
          }
          cur = cur.parentElement;
        }
        let result = defaultBg;
        for (let i = layers.length - 1; i >= 0; i--) {
          result = compositeOver(layers[i], result);
        }
        return result;
      }

      function isVisible(el) {
        if (!el) return false;
        const style = getComputedStyle(el);
        if (style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity) === 0) {
          return false;
        }
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 || rect.height === 0) return false;
        return true;
      }

      const failures = [];
      const seenElements = new Set();
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      let node;
      while ((node = walker.nextNode())) {
        const text = node.textContent?.trim();
        if (!text) continue;
        const el = node.parentElement;
        if (!el || seenElements.has(el) || !isVisible(el)) continue;
        seenElements.add(el);

        const style = getComputedStyle(el);
        const fgRaw = parseCssColor(style.color);
        const bg = getEffectiveBg(el);
        const fg = fgRaw.a < 1 ? compositeOver(fgRaw, bg) : fgRaw;
        const ratio = contrastRatio(fg, bg);

        const fontSize = parseFloat(style.fontSize) || 16;
        const fontWeight = parseInt(style.fontWeight, 10) || 400;
        const isLarge = fontSize >= 24 || (fontSize >= 18.66 && fontWeight >= 700);
        const required = isLarge ? 3.0 : 4.5;

        if (ratio < required) {
          failures.push({
            route: routeName,
            theme: themeName,
            text: text.slice(0, 60),
            tag: el.tagName.toLowerCase(),
            classes: typeof el.className === 'string' ? el.className.slice(0, 120) : '',
            fg: `rgb(${Math.round(fg.r)},${Math.round(fg.g)},${Math.round(fg.b)})`,
            bg: `rgb(${Math.round(bg.r)},${Math.round(bg.g)},${Math.round(bg.b)})`,
            ratio: Number(ratio.toFixed(2)),
            required,
          });
        }
      }
      return failures;
    },
    { routeName: route, themeName: theme },
  );
}

async function checkFonts(page) {
  return page.evaluate(async () => {
    const checks = [
      { font: '16px "IBM Plex Mono"', family: 'IBM Plex Mono', weight: '400' },
      { font: '600 16px "IBM Plex Mono"', family: 'IBM Plex Mono', weight: '600' },
      { font: '16px "IBM Plex Sans"', family: 'IBM Plex Sans', weight: '400' },
      { font: '600 16px "IBM Plex Sans Condensed"', family: 'IBM Plex Sans Condensed', weight: '600' },
      { font: '700 16px "IBM Plex Sans Condensed"', family: 'IBM Plex Sans Condensed', weight: '700' },
    ];
    const results = [];
    for (const c of checks) {
      await document.fonts.load(c.font, '\u20AB');
      results.push({
        ...c,
        hasDong: document.fonts.check(c.font, '\u20AB'),
      });
    }
    const ctx = document.createElement('canvas').getContext('2d');
    ctx.font = '16px "IBM Plex Mono", sans-serif';
    const monoWidth = ctx.measureText('\u20AB').width;
    ctx.font = '16px sans-serif';
    const sansWidth = ctx.measureText('\u20AB').width;
    return {
      checks: results,
      monoWidth,
      sansWidth,
      monoDistinctFromSans: Math.abs(monoWidth - sansWidth) > 0.01,
    };
  });
}

async function main() {
  const { url, mockApi } = parseArgs(process.argv);
  let server = null;
  const baseUrl = url || 'http://127.0.0.1:4173';

  if (!url) {
    server = spawn('npx', ['vite', 'preview', '--host', '127.0.0.1', '--port', '4173', '--strictPort'], {
      cwd: webRoot,
      shell: true,
      stdio: 'ignore',
    });
    for (let i = 0; i < 30; i++) {
      try {
        const res = await fetch(baseUrl);
        if (res.ok) break;
      } catch {
        // wait
      }
      await new Promise((r) => setTimeout(r, 300));
    }
  }

  const { chromium } = loadPlaywright();
  const browser = await launchBrowser(chromium);
  const allFailures = [];
  let fontReport = null;

  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const page = await context.newPage();
    if (mockApi) {
      await installApiMocks(page);
    }

    for (const route of ROUTES) {
      await page.goto(`${baseUrl}${route}`, { waitUntil: 'networkidle' });
      await page.evaluate(() => document.fonts.ready);
      if (!fontReport) {
        fontReport = await checkFonts(page);
      }
      for (const theme of ['light', 'dark']) {
        const failures = await measurePage(page, route, theme);
        allFailures.push(...failures);
      }
    }

    console.log('=== FONT COVERAGE (U+20AB) ===');
    console.log(JSON.stringify(fontReport, null, 2));
    console.log(`\n=== CONTRAST PROBE (${ROUTES.length} routes x 2 themes) ===`);
    console.log(`Total failing text nodes: ${allFailures.length}`);
    if (allFailures.length > 0) {
      for (const f of allFailures.slice(0, 40)) {
        console.log(
          `  [${f.theme}] ${f.route} | ${f.ratio}:1 < ${f.required}:1 | ${f.fg} on ${f.bg} | <${f.tag} class="${f.classes}"> "${f.text}"`,
        );
      }
      if (allFailures.length > 40) {
        console.log(`  ... and ${allFailures.length - 40} more`);
      }
    }

    const fontFailed =
      !fontReport ||
      fontReport.checks.some((c) => !c.hasDong) ||
      !fontReport.monoDistinctFromSans;

    process.exitCode = allFailures.length > 0 || fontFailed ? 1 : 0;
  } finally {
    await browser.close();
    if (server) server.kill();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
