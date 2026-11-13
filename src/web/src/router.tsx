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
import { AdminOverviewPage } from './features/admin/AdminOverviewPage';
import { AdminUsersPage } from './features/admin/AdminUsersPage';
import { AdminAuditBatchPage } from './features/admin/AdminAuditBatchPage';

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
  stubRoute('/placement', 'Placement', '296'),
  stubRoute('/exams', 'Mock exams', '297'),
  stubRoute('/practice', 'Practice session', '298'),
  stubRoute('/saved-questions', 'Saved questions', '298'),
  stubRoute('/speaking', 'Speaking practice', '299'),
  stubRoute('/writing', 'Writing practice', '299'),
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
  stubRoute('/admin/certificates', 'Certificates studio', '303'),
  stubRoute('/admin/exams', 'Exams studio', '303'),
  stubRoute('/admin/question-bank', 'Question bank studio', '303'),
  stubRoute('/admin/finance', 'Finance & refunds', '304'),
  stubRoute('/admin/operations', 'Operations desk', '304'),
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
