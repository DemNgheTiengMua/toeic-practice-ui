import React from 'react';
import {
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  type RouterHistory,
} from '@tanstack/react-router';
import { Button, Card } from '@heroui/react';
import { AppShell } from './components/shell/AppShell';
import { SignInPage } from './features/auth/SignInPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage';
import { ResetPasswordPage } from './features/auth/ResetPasswordPage';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage';
import { LandingPage } from './features/public/LandingPage';
import { CertificatesCatalogPage } from './features/public/CertificatesCatalogPage';
import { CertificateDetailPage } from './features/public/CertificateDetailPage';
import { PricingPage } from './features/public/PricingPage';
import { TermsPage } from './features/public/TermsPage';
import { PrivacyPage } from './features/public/PrivacyPage';
import { AccessDeniedPage } from './features/public/AccessDeniedPage';
import {
  PracticeBuilderPage,
  PracticeResultPage,
  PracticeSessionPage,
  SavedQuestionDetailPage,
  SavedQuestionsPage,
} from './features/practice';
import { SpeakingStudioPage } from './features/productive/speaking/SpeakingStudioPage';
import { SpeakingPracticePage } from './features/productive/speaking/SpeakingPracticePage';
import { SpeakingResultPage } from './features/productive/speaking/SpeakingResultPage';
import { WritingStudioPage } from './features/productive/writing/WritingStudioPage';
import { WritingPracticePage } from './features/productive/writing/WritingPracticePage';
import { WritingResultPage } from './features/productive/writing/WritingResultPage';
import { PlacementCatalogPage } from './features/placement/PlacementCatalogPage';
import { PlacementRetakePage } from './features/placement/PlacementRetakePage';
import { PlacementResumePage } from './features/placement/PlacementResumePage';
import { PlacementTakePage } from './features/placement/PlacementTakePage';
import { PlacementResultPage } from './features/placement/PlacementResultPage';
import { AdminFinancePage } from './features/admin/ops/AdminFinancePage';
import { AdminOperationsPage } from './features/admin/ops/AdminOperationsPage';
import { AdminOverviewPage } from './features/admin/AdminOverviewPage';
import { AdminUsersPage } from './features/admin/AdminUsersPage';
import { AdminAuditBatchPage } from './features/admin/AdminAuditBatchPage';
import { CertificatesStudioPage } from './features/admin/studio/CertificatesStudioPage';
import { ExamsStudioPage } from './features/admin/studio/ExamsStudioPage';
import { QuestionBankStudioPage } from './features/admin/studio/QuestionBankStudioPage';

function PlaceholderView({ title, ticket }: { title: string; ticket: string }) {
  return (
    <Card className="max-w-2xl">
      <Card.Header>
        <Card.Title>{title}</Card.Title>
        <Card.Description>
          Scheduled for implementation in GitHub ticket #{ticket}.
        </Card.Description>
      </Card.Header>
      <Card.Content className="flex gap-2">
        <Link to="/" className="no-underline">
          <Button size="sm" variant="secondary">
            Home
          </Button>
        </Link>
      </Card.Content>
    </Card>
  );
}

const rootRoute = createRootRoute({
  component: () => (
    <AppShell>
      <Outlet />
    </AppShell>
  ),
  notFoundComponent: () => (
    <Card className="max-w-lg mx-auto mt-8">
      <Card.Header>
        <Card.Title>Page not found</Card.Title>
        <Card.Description>The requested route does not exist.</Card.Description>
      </Card.Header>
      <Card.Content>
        <Link to="/" className="no-underline">
          <Button size="sm" variant="primary">
            Return home
          </Button>
        </Link>
      </Card.Content>
    </Card>
  ),
});

function stubRoute<TPath extends string>(path: TPath, title: string, ticket: string) {
  return createRoute({
    getParentRoute: () => rootRoute,
    path,
    component: () => <PlaceholderView title={title} ticket={ticket} />,
  });
}

const speakingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/speaking',
  component: SpeakingStudioPage,
});

const speakingPracticeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/speaking/practice/$questionId',
  component: function SpeakingPracticeRouteView() {
    const { questionId } = speakingPracticeRoute.useParams();
    return <SpeakingPracticePage attemptId={Number(questionId)} />;
  },
});

const speakingResultRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/speaking/result/$attemptId',
  component: function SpeakingResultRouteView() {
    const { attemptId } = speakingResultRoute.useParams();
    return <SpeakingResultPage attemptId={Number(attemptId)} />;
  },
});

const writingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/writing',
  component: WritingStudioPage,
});

const writingPracticeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/writing/practice/$questionId',
  component: function WritingPracticeRouteView() {
    const { questionId } = writingPracticeRoute.useParams();
    return <WritingPracticePage attemptId={Number(questionId)} />;
  },
});

const writingResultRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/writing/result/$attemptId',
  component: function WritingResultRouteView() {
    const { attemptId } = writingResultRoute.useParams();
    return <WritingResultPage attemptId={Number(attemptId)} />;
  },
});

const routeTree = rootRoute.addChildren([
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/',
    component: LandingPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/certificates',
    component: CertificatesCatalogPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/certificates/$slug',
    component: CertificateDetailPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/pricing',
    component: PricingPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/terms',
    component: TermsPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/privacy',
    component: PrivacyPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/access-denied',
    component: AccessDeniedPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/denied',
    component: AccessDeniedPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/sign-in',
    component: SignInPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/sign-in',
    component: SignInPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/register',
    component: RegisterPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/register',
    component: RegisterPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/forgot-password',
    component: ForgotPasswordPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/forgot-password',
    component: ForgotPasswordPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/reset-password',
    component: ResetPasswordPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/reset-password',
    component: ResetPasswordPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/verify-email',
    component: VerifyEmailPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/auth/verify-email',
    component: VerifyEmailPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement',
    component: PlacementCatalogPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/retake/$certId',
    component: PlacementRetakePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/preflight',
    component: PlacementRetakePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/resume/$attemptId',
    component: PlacementResumePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/take/$attemptId',
    validateSearch: (search: Record<string, unknown>) => ({
      q: typeof search.q === 'number' || typeof search.q === 'string' ? Number(search.q) : undefined,
    }),
    component: PlacementTakePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/result/$attemptId',
    component: PlacementResultPage,
  }),
  stubRoute('/exams', 'Mock exams', '297'),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice',
    component: PracticeBuilderPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/session/$sessionId',
    component: PracticeSessionPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/practice/result/$sessionId',
    component: PracticeResultPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/saved-questions',
    component: SavedQuestionsPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/saved-questions/$savedQuestionId',
    component: SavedQuestionDetailPage,
  }),
  speakingRoute,
  speakingPracticeRoute,
  speakingResultRoute,
  writingRoute,
  writingPracticeRoute,
  writingResultRoute,
  stubRoute('/dashboard', 'Dashboard', '300'),
  stubRoute('/learning-path', 'Learning path', '300'),
  stubRoute('/weakness', 'Weakness analysis', '300'),
  stubRoute('/wallet', 'Wallet & billing', '301'),
  stubRoute('/mentors', 'Mentors', '301'),
  stubRoute('/profile', 'Profile', '301'),
  stubRoute('/support', 'Support', '301'),
  stubRoute('/instructor/sessions', 'Instructor sessions', '301'),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin',
    component: AdminOverviewPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/users',
    component: AdminUsersPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/audit/$batchId',
    component: AdminAuditBatchPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/certificates',
    component: CertificatesStudioPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/exams',
    component: ExamsStudioPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/question-bank',
    component: QuestionBankStudioPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/finance',
    component: AdminFinancePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/admin/operations',
    component: AdminOperationsPage,
  }),
]);

export function createAppRouter(history?: RouterHistory) {
  return createRouter({
    routeTree,
    ...(history ? { history } : {}),
  });
}

export const router = createAppRouter();

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

