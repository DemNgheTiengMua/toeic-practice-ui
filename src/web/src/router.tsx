import React from 'react';
import {
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
  Link,
} from '@tanstack/react-router';
import { Button, Card } from '@heroui/react';
import { AppShell } from './components/shell/AppShell';
import { SpeakingStudioPage } from './features/productive/speaking/SpeakingStudioPage';
import { SpeakingPracticePage } from './features/productive/speaking/SpeakingPracticePage';
import { SpeakingResultPage } from './features/productive/speaking/SpeakingResultPage';
import { WritingStudioPage } from './features/productive/writing/WritingStudioPage';
import { WritingPracticePage } from './features/productive/writing/WritingPracticePage';
import { WritingResultPage } from './features/productive/writing/WritingResultPage';

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
  stubRoute('/', 'Extra Efficiency', '295'),
  stubRoute('/certificates', 'Certificates', '295'),
  stubRoute('/pricing', 'Credit Pricing', '295'),
  stubRoute('/sign-in', 'Sign in', '295'),
  stubRoute('/register', 'Register', '295'),
  stubRoute('/forgot-password', 'Forgot password', '295'),
  stubRoute('/reset-password', 'Reset password', '295'),
  stubRoute('/verify-email', 'Verify email', '295'),
  stubRoute('/placement', 'Placement', '296'),
  stubRoute('/exams', 'Mock exams', '297'),
  stubRoute('/practice', 'Practice session', '298'),
  stubRoute('/saved-questions', 'Saved questions', '298'),
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
  stubRoute('/admin', 'Admin overview', '302'),
  stubRoute('/admin/users', 'People & audit', '302'),
  stubRoute('/admin/certificates', 'Certificates studio', '303'),
  stubRoute('/admin/exams', 'Exams studio', '303'),
  stubRoute('/admin/question-bank', 'Question bank studio', '303'),
  stubRoute('/admin/finance', 'Finance & refunds', '304'),
  stubRoute('/admin/operations', 'Operations desk', '304'),
]);

export const router = createRouter({ routeTree });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
