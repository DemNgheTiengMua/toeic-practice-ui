import React, { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  ListBox,
  Select,
  Spinner,
} from '@heroui/react';
import type { Key } from '@heroui/react';
import {
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  BookOpen,
  Calendar,
  CheckCircle2,
  Clock,
  ExternalLink,
  Flame,
  Mail,
  RotateCw,
  Sparkles,
  Target,
  Trophy,
} from 'lucide-react';
import { api, ApiClientError, LearnerDashboardDto } from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatFeasibilityStatus } from './learnerHelpers';

export function DashboardPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user, learnerContext, switchCertificate } = useAuth();

  const [activationFeedback, setActivationFeedback] = useState<string | null>(null);

  const { data: dashboard, isLoading, error, refetch } = useQuery<LearnerDashboardDto>({
    queryKey: ['learner', 'dashboard', learnerContext?.active?.code],
    queryFn: () => api.learner.dashboard(),
  });

  const resendEmailMutation = useMutation({
    mutationFn: () => api.auth.resendActivation(),
    onSuccess: () => {
      setActivationFeedback(
        `A new activation link has been sent to ${user?.email ?? 'your email'}. It expires in 24 hours.`,
      );
    },
    onError: () => {
      setActivationFeedback('Could not send the activation email. Please try again in a few minutes.');
    },
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[400px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading your dashboard…</p>
      </div>
    );
  }

  if (error || !dashboard) {
    return (
      <div className="max-w-4xl mx-auto py-8 space-y-4">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load learner dashboard</Alert.Title>
            <Alert.Description>
              {error instanceof Error ? error.message : 'Please check your connection and try again.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <Button variant="ghost" size="sm" onPress={() => refetch()} className="gap-1.5">
          <RotateCw className="w-3.5 h-3.5" />
          Retry
        </Button>
      </div>
    );
  }

  const { certificate, nextAction, goal, skills, unfinishedAttempt, unfinishedPractice } = dashboard;
  const isUnplaced = goal.hasNoMeasurement;

  // Determine countdown to exam date
  let isCountdown = false;
  let examDateDisplay = 'No exam date set';
  if (goal.targetDate) {
    examDateDisplay = goal.targetDate;
    const targetTime = new Date(goal.targetDate).getTime();
    const nowTime = Date.now();
    const diffDays = Math.ceil((targetTime - nowTime) / (1000 * 60 * 60 * 24));
    if (diffDays >= 0 && diffDays <= 14) {
      isCountdown = true;
      examDateDisplay = `${goal.targetDate} (${diffDays}d remaining)`;
    }
  }

  // Next action severity styling
  const severityBadgeClass =
    nextAction.severity === 'danger'
      ? 'border-danger/40 bg-danger/5'
      : nextAction.severity === 'warning'
      ? 'border-warning/40 bg-warning/5'
      : nextAction.severity === 'success'
      ? 'border-success/40 bg-success/5'
      : 'border-accent/40 bg-accent/5';

  return (
    <div className="max-w-5xl mx-auto py-6 space-y-6">
      {/* Email Verification Reminder if needed */}
      {user && !user.emailVerified && (
        <Alert status="warning" className="border-warning/40">
          <Alert.Indicator>
            <Mail className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 w-full">
            <div>
              <Alert.Title>Verify your email address</Alert.Title>
              <Alert.Description>
                Check your inbox for a verification link to ensure account recovery and notifications.
              </Alert.Description>
            </div>
            <Button
              size="sm"
              variant="secondary"
              isDisabled={resendEmailMutation.isPending}
              onPress={() => resendEmailMutation.mutate()}
              className="shrink-0"
            >
              {resendEmailMutation.isPending ? 'Sending…' : 'Resend email'}
            </Button>
          </Alert.Content>
        </Alert>
      )}

      {activationFeedback && (
        <Alert status={activationFeedback.includes('Could not') ? 'danger' : 'success'}>
          <Alert.Indicator>
            {activationFeedback.includes('Could not') ? (
              <AlertCircle className="w-4 h-4" />
            ) : (
              <CheckCircle2 className="w-4 h-4" />
            )}
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Description>{activationFeedback}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Unfinished Activity Banner */}
      {unfinishedAttempt && (
        <Alert status="accent" className="border-accent/30 bg-accent/5">
          <Alert.Indicator>
            <Clock className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 w-full">
            <div>
              <Alert.Title>Unfinished exam attempt in progress</Alert.Title>
              <Alert.Description>
                You have an in-progress sitting for <strong>{unfinishedAttempt.examTitle}</strong>.
              </Alert.Description>
            </div>
            <Link
              to={`/exams/take/${unfinishedAttempt.attemptId}`}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-accent text-white font-medium text-xs no-underline hover:opacity-90"
            >
              Resume sitting <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </Alert.Content>
        </Alert>
      )}

      {/* BLOCK 1: Hero Next Action */}
      <section
        className={`rounded-xl border p-6 transition-all ${severityBadgeClass}`}
        aria-labelledby="dashboard-hero-title"
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs uppercase font-bold text-accent tracking-wider">
              {nextAction.eyebrow}
            </span>
            <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface border border-border text-foreground font-semibold">
              {certificate.code}
            </span>
            {nextAction.metaLabel && (
              <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface border border-border text-muted">
                {nextAction.metaLabel}
              </span>
            )}
          </div>

          <div>
            <h1 id="dashboard-hero-title" className="text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
              {nextAction.title}
            </h1>
            <p className="text-muted text-sm sm:text-base mt-2 mb-0 max-w-2xl leading-relaxed">
              {nextAction.description}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3 pt-1">
            <a
              href={nextAction.actionUrl}
              className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-white font-semibold text-sm shadow hover:bg-accent/90 transition-colors no-underline"
            >
              <span>{nextAction.actionLabel}</span>
              <ArrowRight className="w-4 h-4" />
            </a>
          </div>

          {/* Week's execution pacing: rendered ONLY for placed learners */}
          {!isUnplaced && (
            <p className="text-xs font-mono text-muted m-0 pt-2 border-t border-border/50">
              This week: {goal.weekCompletedMinutes}m done of {goal.weekPlannedMinutes}m planned (
              {goal.completedPathSteps} of {goal.totalPathSteps} path steps completed)
            </p>
          )}
        </div>
      </section>

      {/* When unplaced: collapse to ONE block + #placement-notice sentence */}
      {isUnplaced ? (
        <p className="text-sm text-muted bg-surface-secondary p-4 rounded-lg border border-border" id="placement-notice">
          Placement is a free sitting that measures your level in every skill {certificate.code} examines. Until
          you take it there is no band to show, so this page shows none rather than empty figures.
        </p>
      ) : (
        <>
          {/* BLOCK 2: Goal Standing (4 tabular cells) */}
          <section className="space-y-3" aria-labelledby="dashboard-goal-title">
            <Card>
              <Card.Header className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 border-b border-border pb-3">
                <div className="flex items-center gap-2">
                  <Target className="w-4 h-4 text-accent" />
                  <h2 id="dashboard-goal-title" className="text-lg font-bold text-foreground m-0">
                    Goal Standing · {certificate.code}
                  </h2>
                </div>
                <div className="flex items-center gap-2">
                  {goal.overallLevelCode && (
                    <span className="font-mono text-xs px-2.5 py-0.5 rounded bg-surface-secondary text-foreground font-semibold border border-border">
                      OVERALL {goal.overallLevelCode}
                      {goal.limitingSkillName && ` · ${goal.limitingSkillName}`}
                    </span>
                  )}
                  <Link
                    to="/onboarding"
                    className="text-xs font-semibold text-accent hover:underline inline-flex items-center gap-1"
                  >
                    Adjust goal <ArrowRight className="w-3 h-3" />
                  </Link>
                </div>
              </Card.Header>

              <Card.Content className="p-0">
                <table className="w-full text-sm divide-y divide-border">
                  <tbody className="divide-y divide-border">
                    <tr className="hover:bg-surface-secondary/40">
                      <th scope="row" className="px-4 py-3 text-left font-medium text-muted w-1/3">
                        Level against target
                      </th>
                      <td className="px-4 py-3 font-mono font-semibold text-foreground">
                        {goal.currentBandCode ?? 'None'} → {goal.targetBandCode ?? 'None'}
                        {goal.targetBandName && ` (${goal.targetBandName})`}
                      </td>
                    </tr>
                    <tr className="hover:bg-surface-secondary/40">
                      <th scope="row" className="px-4 py-3 text-left font-medium text-muted">
                        Remaining guided hours
                      </th>
                      <td className="px-4 py-3 font-mono font-semibold text-foreground">
                        {goal.requiredTotalHours != null ? `${goal.requiredTotalHours}h remaining` : 'Cannot judge'}
                      </td>
                    </tr>
                    <tr className="hover:bg-surface-secondary/40">
                      <th scope="row" className="px-4 py-3 text-left font-medium text-muted">
                        Pacing
                      </th>
                      <td className="px-4 py-3 font-mono font-semibold text-foreground">
                        {goal.weeklyStudyHours != null ? `${goal.weeklyStudyHours}h/week planned` : 'No weekly pace set'} ·{' '}
                        {goal.weekCompletedMinutes}m / {goal.weekPlannedMinutes}m this week
                      </td>
                    </tr>
                    <tr className="hover:bg-surface-secondary/40">
                      <th scope="row" className="px-4 py-3 text-left font-medium text-muted">
                        Verdict
                      </th>
                      <td className="px-4 py-3 flex items-center gap-2.5 flex-wrap">
                        <span
                          id="exam-date"
                          className={`font-mono text-xs px-2 py-0.5 rounded ${
                            isCountdown
                              ? 'bg-warning/20 text-warning font-bold border border-warning/40'
                              : 'text-muted'
                          }`}
                        >
                          {examDateDisplay}
                        </span>
                        <span
                          id="feasibility-verdict"
                          className={`font-mono text-xs px-2.5 py-0.5 rounded font-bold ${
                            goal.status === 'feasible'
                              ? 'bg-success text-white'
                              : goal.status === 'stretch'
                              ? 'bg-warning text-foreground'
                              : goal.status === 'unrealistic'
                              ? 'bg-danger text-white'
                              : 'bg-surface-secondary text-muted'
                          }`}
                        >
                          {formatFeasibilityStatus(goal.status)}
                        </span>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </Card.Content>
            </Card>
          </section>

          {/* BLOCK 3: Per-Skill Standing */}
          <section className="space-y-3" aria-labelledby="dashboard-skills-title">
            <Card>
              <Card.Header className="flex items-center justify-between border-b border-border pb-3">
                <div className="flex items-center gap-2">
                  <BookOpen className="w-4 h-4 text-accent" />
                  <h2 id="dashboard-skills-title" className="text-lg font-bold text-foreground m-0">
                    Per-Skill Standing · {certificate.code}
                  </h2>
                </div>
                <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface-secondary text-muted">
                  {skills.length} {skills.length === 1 ? 'SKILL' : 'SKILLS'}
                </span>
              </Card.Header>

              <Card.Content className="p-4">
                {skills.length === 0 ? (
                  <p className="text-sm text-muted m-0">
                    {certificate.name} has no skills configured yet.
                  </p>
                ) : (
                  <div
                    className="grid gap-4"
                    style={{
                      gridTemplateColumns: `repeat(${Math.min(skills.length, 4)}, minmax(0, 1fr))`,
                    }}
                    id="skill-standing"
                  >
                    {skills.map((skill) => {
                      const isMeasured = skill.levelCode != null;
                      return (
                        <div
                          key={skill.skillId}
                          data-skill-id={skill.skillId}
                          className="p-4 rounded-lg border border-border bg-surface flex flex-col justify-between space-y-3"
                        >
                          <div>
                            <div className="flex items-center justify-between">
                              <span className="font-mono text-xs font-bold text-muted uppercase">
                                {skill.code}
                              </span>
                              {skill.isStale && (
                                <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-warning text-foreground font-semibold">
                                  Needs re-measuring
                                </span>
                              )}
                            </div>
                            <div className="text-sm font-semibold text-foreground mt-0.5">{skill.name}</div>
                          </div>

                          {isMeasured ? (
                            <div className="space-y-1">
                              <div className="font-mono text-xl font-bold text-foreground">
                                {skill.levelCode}
                              </div>
                              {skill.positionInBand != null && (
                                <div className="font-mono text-xs text-muted">
                                  Position: {skill.positionInBand}%
                                </div>
                              )}
                              {skill.latestScaledScore != null && (
                                <div className="font-mono text-xs text-muted">
                                  Score: {skill.latestScaledScore}
                                  {skill.maxScaledScore && ` / ${skill.maxScaledScore}`}
                                </div>
                              )}
                            </div>
                          ) : (
                            <div className="space-y-2">
                              <span className="text-xs text-warning font-semibold block">Not measured yet</span>
                              <Link
                                to="/placement"
                                className="inline-block text-xs font-semibold text-accent hover:underline"
                              >
                                Measure {skill.name} →
                              </Link>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}
              </Card.Content>
            </Card>
          </section>
        </>
      )}
    </div>
  );
}
