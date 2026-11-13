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
import { DashboardPage } from './features/learner/DashboardPage';
import { OnboardingPage } from './features/learner/OnboardingPage';
import { LearningPathPage } from './features/learner/LearningPathPage';
import { LearningPathStepPage } from './features/learner/LearningPathStepPage';
import { LessonReaderPage } from './features/learner/LessonReaderPage';
import { WeaknessAnalysisPage } from './features/learner/WeaknessAnalysisPage';
import { WalletPage } from './features/wallet/WalletPage';
import { CheckoutPage } from './features/wallet/CheckoutPage';
import { OrderStatusPage } from './features/wallet/OrderStatusPage';
import { MentorsCatalogPage } from './features/mentors/MentorsCatalogPage';
import { MentorDetailPage } from './features/mentors/MentorDetailPage';
import { MentorBookingsPage } from './features/mentors/MentorBookingsPage';
import { MentorBookingDetailPage } from './features/mentors/MentorBookingDetailPage';
import { InstructorSessionsPage } from './features/instructor/InstructorSessionsPage';
import { ProfilePage } from './features/profile/ProfilePage';
import { SupportPage } from './features/support/SupportPage';
import { MockExamCatalogPage } from './features/exams/MockExamCatalogPage';
import { MockExamConfirmPage } from './features/exams/MockExamConfirmPage';
import { MockExamTakePage } from './features/exams/MockExamTakePage';
import { MockExamResultPage } from './features/exams/MockExamResultPage';
import { MockExamReviewPage } from './features/exams/MockExamReviewPage';
import { MockExamHistoryPage } from './features/exams/MockExamHistoryPage';

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
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams',
    component: MockExamCatalogPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams/$examId',
    component: MockExamConfirmPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams/take/$attemptId',
    validateSearch: (search: Record<string, unknown>) => ({
      q: typeof search.q === 'number' || typeof search.q === 'string' ? Number(search.q) : undefined,
    }),
    component: MockExamTakePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams/result/$attemptId',
    component: MockExamResultPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams/review/$attemptId',
    component: MockExamReviewPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/exams/history',
    component: MockExamHistoryPage,
  }),
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
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/dashboard',
    component: DashboardPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/onboarding',
    component: OnboardingPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/placement/target-band',
    component: OnboardingPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/learning-path',
    component: LearningPathPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/learning-path/steps/$stepId',
    component: LearningPathStepPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/lessons/$lessonId',
    component: LessonReaderPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/weakness',
    component: WeaknessAnalysisPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/weakness-analysis',
    component: WeaknessAnalysisPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/wallet',
    component: WalletPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/checkout',
    component: CheckoutPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/orders/$orderCode',
    component: OrderStatusPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/payment/result',
    component: OrderStatusPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/mentors',
    component: MentorsCatalogPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/mentors/$mentorId',
    component: MentorDetailPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/mentors/bookings',
    component: MentorBookingsPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/mentors/bookings/$bookingId',
    component: MentorBookingDetailPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/profile',
    component: ProfilePage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/support',
    component: SupportPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/support/report',
    component: SupportPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/support/history',
    component: SupportPage,
  }),
  createRoute({
    getParentRoute: () => rootRoute,
    path: '/instructor/sessions',
    component: InstructorSessionsPage,
  }),
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

