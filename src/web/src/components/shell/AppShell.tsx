import React, { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from '@tanstack/react-router';
import { Button, Chip, ListBox, Select } from '@heroui/react';
import type { Key } from '@heroui/react';
import {
  Award,
  BookOpen,
  Compass,
  CreditCard,
  FileCheck2,
  GraduationCap,
  Layers,
  LayoutDashboard,
  LifeBuoy,
  LogOut,
  Mic,
  Moon,
  PenTool,
  Settings,
  ShieldAlert,
  Sparkles,
  Sun,
  TrendingUp,
  UserCheck,
  Users,
  Wallet,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  canManageBilling,
  canManageCatalog,
  canManageUsers,
  canTriageSupport,
  canViewDashboard,
} from '../../lib/security';

interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
}

const LEARNER_NAV: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', icon: <LayoutDashboard className="w-4 h-4" /> },
  { to: '/placement', label: 'Placement', icon: <Compass className="w-4 h-4" /> },
  { to: '/learning-path', label: 'Learning path', icon: <GraduationCap className="w-4 h-4" /> },
  { to: '/practice', label: 'Practice session', icon: <BookOpen className="w-4 h-4" /> },
  { to: '/speaking', label: 'Speaking practice', icon: <Mic className="w-4 h-4" /> },
  { to: '/writing', label: 'Writing practice', icon: <PenTool className="w-4 h-4" /> },
  { to: '/exams', label: 'Mock exams', icon: <FileCheck2 className="w-4 h-4" /> },
  { to: '/weakness', label: 'Weakness analysis', icon: <TrendingUp className="w-4 h-4" /> },
  { to: '/mentors', label: 'Mentors', icon: <UserCheck className="w-4 h-4" /> },
  { to: '/wallet', label: 'Wallet & billing', icon: <Wallet className="w-4 h-4" /> },
  { to: '/support', label: 'Support', icon: <LifeBuoy className="w-4 h-4" /> },
];

function getInitialTheme(): 'light' | 'dark' {
  if (typeof window === 'undefined') return 'light';
  const stored = window.localStorage.getItem('ee_theme');
  if (stored === 'dark' || stored === 'light') return stored;
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

const OOBE_STUDY_ROUTES = new Set([
  '/dashboard',
  '/placement',
  '/learning-path',
  '/practice',
  '/speaking',
  '/writing',
  '/exams',
  '/weakness',
]);

export function AppShell({ children }: { children: React.ReactNode }) {
  const {
    user,
    learnerContext,
    onboardingState,
    isAuthenticated,
    isStaff,
    switchCertificate,
    signOut,
  } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [theme, setTheme] = useState<'light' | 'dark'>(getInitialTheme);
  const [skipRevision, setSkipRevision] = useState(0);

  useEffect(() => {
    const onSkipChanged = () => setSkipRevision((n) => n + 1);
    window.addEventListener('ee:oobe-skip-changed', onSkipChanged);
    return () => window.removeEventListener('ee:oobe-skip-changed', onSkipChanged);
  }, []);

  useEffect(() => {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(theme);
    root.setAttribute('data-theme', theme);
    window.localStorage.setItem('ee_theme', theme);
  }, [theme]);

  const isSittingFullscreen =
    (location.pathname.startsWith('/placement/take/') &&
      !location.pathname.endsWith('/resume')) ||
    location.pathname.startsWith('/exams/take/') ||
    location.pathname.startsWith('/speaking/practice/') ||
    location.pathname.startsWith('/writing/practice/');

  if (isSittingFullscreen) {
    return <div className="min-h-screen bg-background text-foreground">{children}</div>;
  }

  const staffNav: NavItem[] = [];
  if (canViewDashboard(user)) {
    staffNav.push({
      to: '/admin',
      label: 'Live overview',
      icon: <LayoutDashboard className="w-4 h-4" />,
    });
  }
  if (canManageUsers(user)) {
    staffNav.push({
      to: '/admin/users',
      label: 'People & audit',
      icon: <Users className="w-4 h-4" />,
    });
  }
  if (canManageCatalog(user)) {
    staffNav.push(
      {
        to: '/admin/certificates',
        label: 'Certificates',
        icon: <Award className="w-4 h-4" />,
      },
      {
        to: '/admin/exams',
        label: 'Exams studio',
        icon: <FileCheck2 className="w-4 h-4" />,
      },
      {
        to: '/admin/question-bank',
        label: 'Question bank',
        icon: <Layers className="w-4 h-4" />,
      },
    );
  }
  if (canManageBilling(user)) {
    staffNav.push({
      to: '/admin/finance',
      label: 'Finance & refunds',
      icon: <CreditCard className="w-4 h-4" />,
    });
  }
  if (canTriageSupport(user) || canManageUsers(user)) {
    staffNav.push({
      to: '/admin/operations',
      label: 'Operations desk',
      icon: <ShieldAlert className="w-4 h-4" />,
    });
  }

  const isOobeSkipped =
    skipRevision >= 0 &&
    typeof window !== 'undefined' &&
    window.sessionStorage.getItem('ee_oobe_skip_session') === '1';
  const isOobeLocked = Boolean(
    !isStaff && isAuthenticated && onboardingState && !onboardingState.isOobeComplete,
  );
  const filteredLearnerNav =
    isOobeLocked && !isOobeSkipped
      ? LEARNER_NAV.filter((item) => !OOBE_STUDY_ROUTES.has(item.to))
      : LEARNER_NAV;

  const navItems: NavItem[] = isStaff
    ? staffNav
    : isAuthenticated
      ? filteredLearnerNav
      : [];

  const staffRoleLabel = canManageUsers(user) ? 'Admin' : 'Staff';

  const handleSignOut = async () => {
    await signOut();
    navigate({ to: '/' });
  };

  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      <header className="bg-surface border-b border-border sticky top-0 z-30">
        <div className="w-full px-3 sm:px-6 min-h-14 py-2 flex flex-wrap sm:flex-nowrap items-center justify-between gap-x-2 gap-y-2 sm:gap-4">
          <div className="flex items-center gap-3 sm:gap-6 min-w-0">
            <Link
              to={isStaff ? '/admin' : isAuthenticated ? '/dashboard' : '/'}
              className="font-display font-bold tracking-tight text-base sm:text-lg text-foreground no-underline flex items-center gap-2 sm:gap-2.5 shrink-0 whitespace-nowrap"
            >
              <span className="bg-accent text-accent-foreground px-2 py-0.5 rounded-md font-mono-tabular text-xs font-semibold">
                EE
              </span>
              <span>Extra Efficiency</span>
            </Link>

            <nav aria-label="Primary" className="hidden md:flex items-center gap-4 text-sm font-medium">
              <Link
                to="/certificates"
                className="text-muted hover:text-foreground no-underline px-2 py-1 transition-colors"
              >
                Certificates
              </Link>
              <Link
                to="/pricing"
                className="text-muted hover:text-foreground no-underline px-2 py-1 transition-colors"
              >
                Pricing
              </Link>
              {isAuthenticated && !isStaff && (
                <Link
                  to="/saved-questions"
                  className="text-muted hover:text-foreground no-underline px-2 py-1 transition-colors"
                >
                  Saved questions
                </Link>
              )}
            </nav>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-1.5 sm:gap-2.5 max-w-full">
            {isAuthenticated && !isStaff && learnerContext && (
              <div className="flex items-center gap-1.5 sm:gap-2">
                <Select
                  aria-label="Active certificate"
                  className="w-[108px] sm:w-[190px]"
                  value={learnerContext.active.code as Key}
                  onChange={(value) => {
                    if (typeof value === 'string' && value !== learnerContext.active.code) {
                      void switchCertificate(value);
                    }
                  }}
                >
                  <Select.Trigger className="h-8 text-xs">
                    <Select.Value />
                    <Select.Indicator />
                  </Select.Trigger>
                  <Select.Popover>
                    <ListBox>
                      {(learnerContext.certificates ?? learnerContext.available ?? []).map((cert) => (
                        <ListBox.Item
                          key={cert.code}
                          id={cert.code}
                          textValue={`${cert.code} - ${cert.name}`}
                        >
                          {cert.code} — {cert.name}
                          <ListBox.ItemIndicator />
                        </ListBox.Item>
                      ))}
                    </ListBox>
                  </Select.Popover>
                </Select>

                <div className="inline-flex items-center rounded-md bg-surface-secondary border border-border text-foreground font-mono-tabular text-[11px] sm:text-xs font-semibold overflow-hidden whitespace-nowrap">
                  <Link
                    to="/wallet"
                    className="inline-flex items-center gap-1 sm:gap-1.5 px-2 sm:px-2.5 py-1 text-foreground no-underline hover:bg-surface-tertiary transition-colors whitespace-nowrap"
                  >
                    <Sparkles className="w-3.5 h-3.5 text-warning" />
                    <span>{learnerContext.creditBalance} credits</span>
                  </Link>
                  <Link
                    to="/checkout"
                    className="inline-flex items-center px-1.5 sm:px-2 py-1 text-accent font-sans hover:bg-surface-tertiary transition-colors border-l border-border no-underline whitespace-nowrap"
                  >
                    + Top up
                  </Link>
                </div>
              </div>
            )}

            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
              onPress={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
            >
              {theme === 'dark' ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
            </Button>

            {isAuthenticated && user ? (
              <div className="flex items-center gap-1.5 sm:gap-2">
                <Link
                  to="/profile"
                  className="text-xs text-muted hover:text-foreground no-underline font-medium flex items-center gap-1.5 px-1.5 sm:px-2 py-1"
                >
                  <Settings className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">{user.fullName || user.email}</span>
                </Link>
                {isStaff && (
                  <Chip size="sm" color="accent" variant="soft" className="hidden sm:inline-flex">
                    {staffRoleLabel}
                  </Chip>
                )}
                <Button size="sm" variant="secondary" className="px-2.5 sm:px-3" onPress={handleSignOut}>
                  <LogOut className="w-3.5 h-3.5 sm:mr-1" />
                  Sign out
                </Button>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <Link to="/sign-in" className="no-underline">
                  <Button size="sm" variant="secondary">
                    Sign in
                  </Button>
                </Link>
                <Link to="/register" className="no-underline">
                  <Button size="sm" variant="primary">
                    Register
                  </Button>
                </Link>
              </div>
            )}
          </div>
        </div>
      </header>

      {isOobeLocked && isOobeSkipped && (
        <div
          data-testid="oobe-return-banner"
          className="bg-accent/10 border-b border-accent/30 text-foreground px-4 sm:px-6 py-2 text-xs"
        >
          <div className="w-full flex items-center justify-between gap-4">
            <span className="font-medium">
              Bạn chưa hoàn tất thiết lập lộ trình học — Tiếp tục thiết lập (OOBE)
            </span>
            <Link to="/onboarding" className="font-semibold underline text-accent">
              Tiếp tục thiết lập (OOBE)
            </Link>
          </div>
        </div>
      )}

      {isAuthenticated && user && !user.emailVerified && !isStaff && (
        <div className="bg-warning/15 border-b border-warning/40 text-foreground px-4 sm:px-6 py-2 text-xs">
          <div className="w-full flex items-center justify-between gap-4">
            <span className="font-medium">
              Your email address ({user.email}) is not verified yet. Verify your email before purchasing credit packages.
            </span>
            <Link to="/profile" className="font-semibold underline text-accent">
              Verify or resend link
            </Link>
          </div>
        </div>
      )}

      <div className="flex-1 flex flex-col lg:flex-row w-full min-w-0">
        {navItems.length > 0 && (
          <aside className="w-full lg:w-56 xl:w-60 bg-surface border-b lg:border-b-0 lg:border-r border-border shrink-0 lg:sticky lg:top-14 lg:h-[calc(100vh-3.5rem)] lg:overflow-y-auto">
            <div className="px-4 py-3 border-b border-separator">
              <span className="text-xs font-medium text-muted">
                {isStaff ? 'Staff Studio' : 'Learner workspace'}
              </span>
            </div>
            <nav
              aria-label={isStaff ? 'Staff navigation' : 'Learner navigation'}
              className="p-2 flex lg:flex-col gap-1 overflow-x-auto"
            >
              {navItems.map((item) => {
                const isActive =
                  item.to === '/admin'
                    ? location.pathname === '/admin'
                    : location.pathname === item.to ||
                      location.pathname.startsWith(`${item.to}/`);
                return (
                  <Link
                    key={item.to}
                    to={item.to}
                    className={`flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm font-medium no-underline whitespace-nowrap transition-colors ${
                      isActive
                        ? 'bg-accent text-accent-foreground'
                        : 'text-foreground hover:bg-surface-secondary'
                    }`}
                  >
                    {item.icon}
                    <span>{item.label}</span>
                  </Link>
                );
              })}
            </nav>
          </aside>
        )}

        <main className="flex-1 p-4 sm:p-6 min-w-0">
          <div className="max-w-[1440px] mx-auto w-full min-w-0">{children}</div>
        </main>
      </div>
    </div>
  );
}
