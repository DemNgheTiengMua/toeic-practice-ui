import React, { useEffect, useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Input, Label, Table, TextField } from '@heroui/react';
import {
  AlertTriangle,
  ArrowRight,
  ChevronLeft,
  Clock,
  CreditCard,
  FileCheck2,
  Layers,
  Radio,
  RefreshCw,
  ShieldCheck,
  TrendingDown,
  TrendingUp,
  Users,
  Wallet,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { api, type AdminDashboardDto, type DashboardRevision } from '../../lib/api';
import {
  canManageUsers,
  formatSignedVnd,
  getHeldCapabilities,
  STAFF_PERMISSIONS,
} from '../../lib/security';
import { areRevisionsEqual, STAFF_CAPABILITY_CATALOG } from './adminHelpers';
import { AdminAccessGuard } from './AdminAccessGuard';

/** The month names `ReportingPeriod.MonthLabel` renders, in the order its `MMMM` format produces them. */
const MONTH_NAMES = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

export function AdminOverviewPage() {
  const { user } = useAuth();
  const isSuperAdmin = canManageUsers(user);
  const heldCapabilities = getHeldCapabilities(user);

  const [dashboard, setDashboard] = useState<AdminDashboardDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [isLiveConnected, setIsLiveConnected] = useState(true);

  // #351: `YYYY-MM`, or '' for "whatever month the server's clock is in". Kept as the raw month
  // rather than a date so the client never re-derives a reporting boundary from its own calendar.
  const [selectedMonth, setSelectedMonth] = useState('');

  const revisionRef = useRef<DashboardRevision | null>(null);

  useEffect(() => {
    if (!isSuperAdmin) {
      setLoading(false);
      return;
    }

    let isMounted = true;
    const month = selectedMonth || undefined;

    async function fetchInitial() {
      try {
        setLoading(true);
        const [dashData, revData] = await Promise.all([
          api.admin.dashboard(month),
          api.admin.dashboardRevision().catch(() => null),
        ]);
        if (!isMounted) return;
        setDashboard(dashData);
        revisionRef.current = revData;
        setLastUpdated(new Date());
        setIsLiveConnected(true);
      } catch (err: any) {
        if (!isMounted) return;
        setError(err.detail || err.message || 'Failed to load dashboard metrics.');
        setIsLiveConnected(false);
      } finally {
        if (isMounted) setLoading(false);
      }
    }

    void fetchInitial();

    // 5-second polling interval for revision changes (ADR-0013). It refetches the same month the screen
    // is showing: a poll that quietly asked for the current month would snap a chosen month back.
    const intervalId = setInterval(async () => {
      try {
        const nextRev = await api.admin.dashboardRevision();
        setIsLiveConnected(true);
        if (!areRevisionsEqual(revisionRef.current, nextRev)) {
          revisionRef.current = nextRev;
          const updatedDash = await api.admin.dashboard(month);
          if (isMounted) {
            setDashboard(updatedDash);
            setLastUpdated(new Date());
          }
        }
      } catch {
        if (isMounted) {
          setIsLiveConnected(false);
        }
      }
    }, 5000);

    return () => {
      isMounted = false;
      clearInterval(intervalId);
    };
  }, [isSuperAdmin, selectedMonth]);

  /**
   * #351: steps one month back from the period on screen.
   * <para>
   * The month is read from the server's own label - which <c>ReportingPeriod.MonthLabel</c> renders in
   * invariant English - rather than from the browser's clock. A client that computed "current month"
   * itself would disagree with the server for the few hours around a month boundary whenever the
   * browser is not on UTC+7, and "last month" would then mean a different month than the figure it
   * sits under.
   * </para>
   */
  function goToPreviousMonth() {
    const [monthName, year] = (dashboard?.periodLabel ?? '').split(' ');
    const monthIndex = MONTH_NAMES.indexOf(monthName);
    if (monthIndex < 0 || !year) return;
    const previous = new Date(Number(year), monthIndex - 1, 1);
    setSelectedMonth(
      `${previous.getFullYear()}-${String(previous.getMonth() + 1).padStart(2, '0')}`,
    );
  }

  return (
    <AdminAccessGuard>
      <div className="space-y-6">
        {/* Page Header */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="font-display text-2xl font-bold tracking-tight text-foreground sm:text-3xl">
              Command center
            </h1>
            <p className="text-sm text-muted">
              Live operational metrics, revenue tracking (UTC+7), and system health.
            </p>
          </div>

          {isSuperAdmin && (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span className="flex items-center gap-1.5 text-xs text-muted">
                <Radio
                  className={`h-3 w-3 ${isLiveConnected ? 'text-success animate-pulse' : 'text-danger'}`}
                />
                <span>{isLiveConnected ? 'Live revision stream' : 'Connection lost'}</span>
              </span>
              {lastUpdated && (
                <span className="text-xs text-muted font-mono-tabular">
                  Updated {lastUpdated.toLocaleTimeString('en-US', { hour12: false })}
                </span>
              )}
            </div>
          )}
        </div>

        {/* #351: the period control. It sits above every card it moves, so a reader cannot mistake
            which figures follow it. Today's two live figures are marked "today" in their own titles
            and are deliberately not part of it. */}
        {isSuperAdmin && (
          <div className="flex flex-wrap items-end gap-3">
            <TextField className="w-full sm:w-56">
              <Label>Reporting month</Label>
              <Input
                type="month"
                aria-label="Reporting month"
                value={selectedMonth}
                onChange={(e) => setSelectedMonth(e.target.value)}
              />
            </TextField>

            <Button
              size="sm"
              variant="secondary"
              isDisabled={!dashboard?.periodLabel}
              onPress={goToPreviousMonth}
            >
              <ChevronLeft className="h-4 w-4 mr-1.5" />
              Last month
            </Button>

            {selectedMonth && (
              <Button size="sm" variant="ghost" onPress={() => setSelectedMonth('')}>
                This month
              </Button>
            )}

            <p className="text-xs text-muted pb-2">
              Showing{' '}
              <span className="font-medium text-foreground">{dashboard?.periodLabel ?? 'the current month'}</span>
              . Today's figures below stay on today.
            </p>
          </div>
        )}

        {/* Non-SystemAdmin Staff Capability Guide */}
        {!isSuperAdmin && (
          <div className="space-y-6">
            <Alert status="accent">
              <Alert.Title className="text-sm font-semibold">Staff workspace access</Alert.Title>
              <Alert.Description className="text-xs leading-relaxed">
                You are authenticated with staff credentials. Financial KPIs, user management, and manual credit grants require the <code className="font-mono-tabular text-foreground font-semibold">{STAFF_PERMISSIONS.SystemAdmin}</code> capability.
              </Alert.Description>
            </Alert>

            <Card className="border border-border">
              <Card.Header>
                <Card.Title className="font-display text-lg font-semibold">
                  Your held capabilities
                </Card.Title>
                <Card.Description className="text-xs text-muted">
                  The active permissions granted to your staff identity.
                </Card.Description>
              </Card.Header>
              <Card.Content className="space-y-4">
                <div className="flex flex-wrap gap-2">
                  {heldCapabilities.map((cap) => (
                    <Chip key={cap} color="accent" variant="soft" className="font-mono-tabular text-xs">
                      {cap}
                    </Chip>
                  ))}
                </div>

                <div className="grid gap-3 sm:grid-cols-2 pt-2">
                  {STAFF_CAPABILITY_CATALOG.filter((c) => heldCapabilities.includes(c.code)).map((meta) => (
                    <div
                      key={meta.code}
                      className="rounded-lg border border-border/60 bg-surface-secondary/40 p-3.5 space-y-1.5"
                    >
                      <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-foreground">{meta.name}</span>
                        <span className="text-[11px] text-muted uppercase tracking-wider">{meta.area}</span>
                      </div>
                      <p className="text-xs text-muted">{meta.description}</p>
                    </div>
                  ))}
                </div>
              </Card.Content>
            </Card>

            <Card className="border border-border">
              <Card.Header>
                <Card.Title className="font-display text-lg font-semibold">
                  Available workspaces
                </Card.Title>
                <Card.Description className="text-xs text-muted">
                  Sections of the administration portal accessible with your permissions.
                </Card.Description>
              </Card.Header>
              <Card.Content className="grid gap-3 sm:grid-cols-3">
                <Link
                  to="/admin/certificates"
                  className="group rounded-lg border border-border p-3.5 hover:border-accent hover:bg-surface-secondary/50 transition-colors no-underline block"
                >
                  <div className="flex items-center justify-between pb-1">
                    <span className="text-sm font-semibold text-foreground group-hover:text-accent">
                      Certificates studio
                    </span>
                    <ArrowRight className="h-4 w-4 text-muted group-hover:text-accent transition-transform group-hover:translate-x-0.5" />
                  </div>
                  <p className="text-xs text-muted">Manage blueprints, score conversions, and modules.</p>
                </Link>

                <Link
                  to="/admin/exams"
                  className="group rounded-lg border border-border p-3.5 hover:border-accent hover:bg-surface-secondary/50 transition-colors no-underline block"
                >
                  <div className="flex items-center justify-between pb-1">
                    <span className="text-sm font-semibold text-foreground group-hover:text-accent">
                      Exams studio
                    </span>
                    <ArrowRight className="h-4 w-4 text-muted group-hover:text-accent transition-transform group-hover:translate-x-0.5" />
                  </div>
                  <p className="text-xs text-muted">Draft, assemble, audit, and publish diagnostic exams.</p>
                </Link>

                <Link
                  to="/admin/question-bank"
                  className="group rounded-lg border border-border p-3.5 hover:border-accent hover:bg-surface-secondary/50 transition-colors no-underline block"
                >
                  <div className="flex items-center justify-between pb-1">
                    <span className="text-sm font-semibold text-foreground group-hover:text-accent">
                      Question bank
                    </span>
                    <ArrowRight className="h-4 w-4 text-muted group-hover:text-accent transition-transform group-hover:translate-x-0.5" />
                  </div>
                  <p className="text-xs text-muted">Author items, inspect coverage, and verify stimuli.</p>
                </Link>
              </Card.Content>
            </Card>
          </div>
        )}

        {/* SuperAdmin Dashboard View */}
        {isSuperAdmin && (
          <>
            {error && (
              <Alert status="danger">
                <Alert.Title className="text-sm font-semibold">Dashboard error</Alert.Title>
                <Alert.Description className="text-xs">{error}</Alert.Description>
              </Alert>
            )}

            {/* Top KPI Cards */}
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              {/* Active Learners */}
              <Card className="border border-border min-w-0">
                <Card.Header className="flex flex-row items-center justify-between pb-2">
                  <span className="text-xs font-medium text-muted">Active learners</span>
                  <Users className="h-4 w-4 text-muted" />
                </Card.Header>
                <Card.Content>
                  <div className="text-2xl font-bold font-mono-tabular text-foreground">
                    {loading ? '—' : dashboard?.activeLearnersCount.toLocaleString()}
                  </div>
                  <p className="text-xs text-muted pt-1">
                    +{loading ? '—' : dashboard?.newLearners7DaysCount} new in last 7 days
                  </p>
                </Card.Content>
              </Card>

              {/* Revenue Today (UTC+7) */}
              <Card className="border border-border">
                <Card.Header className="flex flex-row items-center justify-between pb-2">
                  <span className="text-xs font-medium text-muted">Revenue today (UTC+7)</span>
                  <Clock className="h-4 w-4 text-muted" />
                </Card.Header>
                <Card.Content>
                  <div
                    data-testid="revenue-today-value"
                    className={`text-2xl font-bold font-mono-tabular ${
                      (dashboard?.revenueTodayVnd ?? 0) < 0 ? 'text-danger' : 'text-foreground'
                    }`}
                  >
                    {loading ? '—' : formatSignedVnd(dashboard?.revenueTodayVnd ?? 0)}
                  </div>
                  <p className="text-xs text-muted pt-1">Net settled in current calendar day</p>
                </Card.Content>
              </Card>

              {/* Revenue in the chosen period (UTC+7) */}
              <Card className="border border-border">
                <Card.Header className="flex flex-row items-center justify-between pb-2">
                  <span className="text-xs font-medium text-muted">
                    Revenue in {dashboard?.periodLabel ?? 'period'}
                  </span>
                  <TrendingUp className="h-4 w-4 text-muted" />
                </Card.Header>
                <Card.Content>
                  <div
                    data-testid="revenue-period-value"
                    className={`text-2xl font-bold font-mono-tabular ${
                      (dashboard?.revenueInPeriodVnd ?? 0) < 0 ? 'text-danger' : 'text-foreground'
                    }`}
                  >
                    {loading ? '—' : formatSignedVnd(dashboard?.revenueInPeriodVnd ?? 0)}
                  </div>
                  <p className="text-xs text-muted pt-1">
                    {loading ? '—' : dashboard?.paidOrdersInPeriodCount} paid orders
                  </p>
                </Card.Content>
              </Card>

              {/* Credit Burn & Refunds */}
              <Card className="border border-border">
                <Card.Header className="flex flex-row items-center justify-between pb-2">
                  <span className="text-xs font-medium text-muted">Credit consumption</span>
                  <Wallet className="h-4 w-4 text-muted" />
                </Card.Header>
                <Card.Content>
                  <div className="text-2xl font-bold font-mono-tabular text-foreground">
                    {loading ? '—' : `${dashboard?.creditsUsedInPeriod.toLocaleString()} cr`}
                  </div>
                  <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-xs text-muted pt-1">
                    <span>Refunded: <span className="font-mono-tabular font-medium text-foreground">{loading ? '—' : dashboard?.creditsRefundedInPeriod.toLocaleString()}</span> cr</span>
                    <span>Avg: <span className="font-mono-tabular font-medium text-foreground">{loading ? '—' : dashboard?.creditsPerDayAverage.toFixed(1)}</span>/day</span>
                  </div>
                </Card.Content>
              </Card>
            </div>

            {/* Needs Attention & Recent Activity */}
            <div className="grid gap-6 xl:grid-cols-3">
              {/* Needs Attention */}
              <Card className="border border-border xl:col-span-1 min-w-0">
                <Card.Header>
                  <Card.Title className="font-display text-lg font-semibold flex items-center gap-2">
                    <AlertTriangle className="h-5 w-5 text-warning" />
                    Needs attention
                  </Card.Title>
                  <Card.Description className="text-xs text-muted">
                    Action items requiring staff intervention or review.
                  </Card.Description>
                </Card.Header>
                <Card.Content className="space-y-3">
                  <Link
                    to="/admin/exams"
                    className="flex items-center justify-between rounded-lg border border-border p-3 hover:bg-surface-secondary/50 transition-colors no-underline"
                  >
                    <div>
                      <div className="text-sm font-medium text-foreground">Draft exams awaiting review</div>
                      <div className="text-xs text-muted">Unpublished diagnostic papers</div>
                    </div>
                    <span className="font-mono-tabular font-bold text-sm text-foreground bg-surface-secondary px-2 py-0.5 rounded">
                      {dashboard?.needsAttention.draftExamsCount ?? 0}
                    </span>
                  </Link>

                  <Link
                    to="/admin/finance"
                    className="flex items-center justify-between rounded-lg border border-border p-3 hover:bg-surface-secondary/50 transition-colors no-underline"
                  >
                    <div>
                      <div className="text-sm font-medium text-foreground">Stale pending orders</div>
                      <div className="text-xs text-muted">Pending VietQR checkouts &gt; 15m</div>
                    </div>
                    <span className="font-mono-tabular font-bold text-sm text-foreground bg-surface-secondary px-2 py-0.5 rounded">
                      {dashboard?.needsAttention.stalePendingOrdersCount ?? 0}
                    </span>
                  </Link>

                  <Link
                    to="/admin/operations"
                    className="flex items-center justify-between rounded-lg border border-border p-3 hover:bg-surface-secondary/50 transition-colors no-underline"
                  >
                    <div>
                      <div className="text-sm font-medium text-foreground">Open learner complaints</div>
                      <div className="text-xs text-muted">Support tickets and grading appeals</div>
                    </div>
                    <span className="font-mono-tabular font-bold text-sm text-foreground bg-surface-secondary px-2 py-0.5 rounded">
                      {dashboard?.needsAttention.openComplaintsCount ?? 0}
                    </span>
                  </Link>
                </Card.Content>
              </Card>

              {/* Most Attempted Exams */}
              <Card className="border border-border xl:col-span-2 min-w-0">
                <Card.Header>
                  <Card.Title className="font-display text-lg font-semibold flex items-center gap-2">
                    <FileCheck2 className="h-5 w-5 text-accent" />
                    Most attempted exams
                  </Card.Title>
                  <Card.Description className="text-xs text-muted">
                    Popular exams across all active learners in {dashboard?.periodLabel ?? 'the chosen period'}.
                  </Card.Description>
                </Card.Header>
                <Card.Content>
                  {dashboard?.mostAttemptedExams && dashboard.mostAttemptedExams.length > 0 ? (
                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-sm">
                        <thead className="border-b border-border text-xs text-muted uppercase">
                          <tr>
                            <th className="py-2.5 font-medium">Exam title</th>
                            <th className="py-2.5 text-right font-medium">Attempts</th>
                            <th className="py-2.5 text-right font-medium">Avg score</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-border/60">
                          {dashboard.mostAttemptedExams.map((exam) => (
                            <tr key={exam.examId} className="hover:bg-surface-secondary/30">
                              <td className="py-2.5 font-medium text-foreground">{exam.examName}</td>
                              <td className="py-2.5 text-right font-mono-tabular text-muted">
                                {exam.attemptsCount.toLocaleString()}
                              </td>
                              <td className="py-2.5 text-right font-mono-tabular font-medium text-foreground">
                                {exam.averageScore != null ? Math.round(exam.averageScore) : '—'}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="py-8 text-center text-xs text-muted">
                      No exam attempts recorded in {dashboard?.periodLabel ?? 'the chosen period'}.
                    </div>
                  )}
                </Card.Content>
              </Card>
            </div>
          </>
        )}
      </div>
    </AdminAccessGuard>
  );
}
