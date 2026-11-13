import React, { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  Chip,
  ProgressBar,
  Spinner,
} from '@heroui/react';
import {
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  BookOpen,
  Calendar,
  CheckCircle2,
  Clock,
  Compass,
  FileCheck2,
  Lock,
  Play,
  RotateCw,
  Sparkles,
  TrendingUp,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  LearningPathSummaryDto,
  PathStepDto,
  CalibrationViewDto,
  AiInsightDto,
} from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import {
  formatPathStepStatus,
  formatStepType,
} from './learnerHelpers';
import { AiInsightPanel } from './AiInsightPanel';

export function LearningPathPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { learnerContext } = useAuth();

  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isResetConfirmOpen, setIsResetConfirmOpen] = useState(false);

  // 1. Learning path summary
  const { data: summary, isLoading, error, refetch } = useQuery<LearningPathSummaryDto>({
    queryKey: ['learner', 'learning-path'],
    queryFn: () => api.learningPath.get(),
  });

  // 2. Calibration status
  const { data: calibration, refetch: refetchCalibration } = useQuery<CalibrationViewDto>({
    queryKey: ['learner', 'calibration'],
    queryFn: () => api.placement.calibration(),
    enabled: Boolean(summary?.hasActivePath),
  });

  // 3. Path AI Insight
  const { data: insight, isLoading: isInsightLoading, refetch: refetchInsight } = useQuery<AiInsightDto>({
    queryKey: ['learner', 'insights', 'path'],
    queryFn: () => api.insights.path(),
    enabled: Boolean(summary?.hasActivePath),
  });

  // Generate path mutation
  const generateMutation = useMutation({
    mutationFn: async () => {
      return await api.learningPath.generate();
    },
    onSuccess: () => {
      setSuccessMessage('Learning path generated successfully.');
      queryClient.invalidateQueries({ queryKey: ['learner', 'learning-path'] });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setErrorMessage(err.detail || err.title || 'Could not generate learning path.');
      } else {
        setErrorMessage('An unexpected error occurred while generating your learning path.');
      }
    },
  });

  // Start step mutation
  const startStepMutation = useMutation({
    mutationFn: async (stepId: number) => {
      return await api.learningPath.startStep(stepId);
    },
    onSuccess: (res) => {
      if (res.redirectUrl) {
        const dest = res.redirectUrl.startsWith('/') ? res.redirectUrl : `/${res.redirectUrl}`;
        navigate({ to: dest as any });
      } else {
        queryClient.invalidateQueries({ queryKey: ['learner', 'learning-path'] });
      }
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setErrorMessage(err.detail || err.title || 'Could not start step.');
      } else {
        setErrorMessage('Failed to start step.');
      }
    },
  });

  // Reset path mutation
  const resetMutation = useMutation({
    mutationFn: async () => {
      return await api.learningPath.reset();
    },
    onSuccess: () => {
      setIsResetConfirmOpen(false);
      setSuccessMessage('Learning path progress has been reset. Placement result was kept.');
      queryClient.invalidateQueries({ queryKey: ['learner', 'learning-path'] });
    },
    onError: (err) => {
      if (err instanceof ApiClientError) {
        setErrorMessage(err.detail || err.title || 'Could not reset learning path.');
      } else {
        setErrorMessage('Failed to reset learning path.');
      }
    },
  });

  // Calibration decision mutation
  const calibrationMutation = useMutation({
    mutationFn: async (adjust: boolean) => {
      return await api.placement.decideCalibration(adjust);
    },
    onSuccess: (res) => {
      setSuccessMessage(
        res.pathAdjusted
          ? `Starting band adjusted to ${res.proposedBandCode}. Your path has been updated.`
          : 'Path kept as is for now.',
      );
      queryClient.invalidateQueries({ queryKey: ['learner', 'learning-path'] });
      queryClient.invalidateQueries({ queryKey: ['learner', 'calibration'] });
    },
    onError: () => {
      setErrorMessage('Could not record calibration decision.');
    },
  });

  // Retry AI Insight
  const retryInsightMutation = useMutation({
    mutationFn: async () => {
      await api.insights.retryPath();
    },
    onSuccess: () => {
      refetchInsight();
    },
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[400px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading your learning path…</p>
      </div>
    );
  }

  if (error || !summary) {
    return (
      <div className="max-w-4xl mx-auto py-8 space-y-4">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load learning path</Alert.Title>
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

  // EMPTY STATE: No active learning path
  if (!summary.hasActivePath) {
    return (
      <div className="max-w-3xl mx-auto py-8 space-y-6">
        <div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Guided Learning Path
          </h1>
          <p className="text-sm text-muted mt-1.5 mb-0">
            A 4-week structured curriculum tailored to your assessed skill levels.
          </p>
        </div>

        {errorMessage && (
          <Alert status="danger">
            <Alert.Indicator>
              <AlertCircle className="w-4 h-4" />
            </Alert.Indicator>
            <Alert.Content>
              <Alert.Title>Cannot generate path</Alert.Title>
              <Alert.Description>{errorMessage}</Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        <Card>
          <Card.Content className="py-12 px-6 flex flex-col items-center text-center space-y-4">
            <div className="p-3 rounded-full bg-accent/10 text-accent">
              <Compass className="w-8 h-8" />
            </div>
            <div>
              <h2 className="text-xl font-bold text-foreground m-0">No learning path yet</h2>
              <p className="text-sm text-muted max-w-md mt-2 mb-0 leading-relaxed">
                Take a diagnostic placement test to generate your personalized learning path, or generate one from your
                current assessed level.
              </p>
            </div>
            <div className="flex flex-wrap items-center justify-center gap-3 pt-2">
              <Link
                to="/placement"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-white font-semibold text-sm no-underline hover:bg-accent/90 transition-colors"
              >
                <span>Start placement test</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
              <Button
                variant="secondary"
                isDisabled={generateMutation.isPending}
                onPress={() => generateMutation.mutate()}
                className="gap-2"
              >
                {generateMutation.isPending ? <Spinner size="sm" /> : <Sparkles className="w-4 h-4 text-warning" />}
                <span>Generate path</span>
              </Button>
            </div>
          </Card.Content>
        </Card>
      </div>
    );
  }

  const progressPercent = summary.totalSteps > 0
    ? Math.round((summary.completedSteps / summary.totalSteps) * 100)
    : 0;

  return (
    <div className="max-w-4xl mx-auto py-6 space-y-6">
      {/* Title & Info */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            {summary.fromBandCode} → {summary.toBandCode} learning path
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            From <strong>{summary.fromBandName}</strong> to <strong>{summary.toBandName}</strong>. Steps unlock in order.
          </p>
        </div>
      </div>

      {successMessage && (
        <Alert status="success">
          <Alert.Indicator>
            <CheckCircle2 className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Description>{successMessage}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {errorMessage && (
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Description>{errorMessage}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Level by Skill Strip */}
      {summary.skillLevels && Object.keys(summary.skillLevels).length > 0 && (
        <Card>
          <Card.Content className="p-4 space-y-2">
            <span className="text-xs font-semibold text-muted uppercase tracking-wider block">Level by skill</span>
            <div className="flex flex-wrap items-center gap-4">
              {Object.entries(summary.skillLevels).map(([skill, level]) => (
                <div key={skill} className="font-mono text-sm flex items-center gap-1.5">
                  <span className="text-muted">{skill}:</span>
                  <span className="font-bold text-foreground">{level}</span>
                </div>
              ))}
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Path Progress & CEFR Scale */}
      <Card>
        <Card.Content className="p-4 sm:p-6 space-y-4">
          <div className="flex items-center justify-between text-sm">
            <span className="font-semibold text-foreground">Path progress</span>
            <span className="font-mono text-xs text-muted">
              {summary.completedSteps} of {summary.totalSteps} steps done · {summary.remainingMinutes}m remaining
            </span>
          </div>

          <div className="w-full bg-surface-secondary rounded-full h-2.5 overflow-hidden border border-border">
            <div
              className="bg-accent h-2.5 rounded-full transition-all duration-300"
              style={{ width: `${progressPercent}%` }}
              role="progressbar"
              aria-valuenow={summary.completedSteps}
              aria-valuemin={0}
              aria-valuemax={summary.totalSteps}
            />
          </div>

          {/* CEFR Bands Track */}
          {summary.cefrBands && summary.cefrBands.length > 0 && (
            <div className="pt-2 space-y-1">
              <div className="flex items-center justify-between text-xs font-mono text-muted">
                <span>{summary.fromBandCode} → {summary.toBandCode}</span>
                <span>
                  {summary.currentScore != null && `${summary.currentScore} at placement`}
                  {summary.currentScore != null && summary.targetScore != null && ' · '}
                  {summary.targetScore != null && `${summary.targetScore} target`}
                </span>
              </div>
              <div className="grid gap-1.5" style={{ gridTemplateColumns: `repeat(${summary.cefrBands.length}, 1fr)` }}>
                {summary.cefrBands.map((b) => {
                  const isCurrent = b.code === summary.currentBandCode;
                  const isReached = b.isReached;
                  return (
                    <div
                      key={b.code}
                      className={`text-center py-1 rounded font-mono text-xs border ${
                        isCurrent
                          ? 'border-accent bg-accent text-white font-bold'
                          : isReached
                          ? 'border-success/40 bg-success/10 text-success font-semibold'
                          : 'border-border bg-surface text-muted'
                      }`}
                    >
                      {b.code}
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </Card.Content>
      </Card>

      {/* Calibration Proposal Alert if diverged */}
      {calibration?.reason === 'diverged' && calibration.proposal && (
        <Card className="border-warning/60 bg-warning/5" id="calibration-card">
          <Card.Header className="border-b border-warning/20 pb-3">
            <div className="flex items-center gap-2">
              <AlertTriangle className="w-4 h-4 text-warning" />
              <strong className="text-foreground text-sm">
                Your practice does not match your placement band
              </strong>
            </div>
          </Card.Header>
          <Card.Content className="space-y-4 pt-3">
            <p className="text-sm text-foreground m-0 leading-relaxed">
              {calibration.proposal.evidence}
            </p>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3 rounded-lg bg-surface border border-border text-xs font-mono">
              <div>
                <span className="text-muted block">Valid answers</span>
                <span className="font-bold text-foreground">{calibration.proposal.validAnswerCount}</span>
              </div>
              <div>
                <span className="text-muted block">Accuracy</span>
                <span className="font-bold text-foreground">
                  {calibration.proposal.observedAccuracyPct.toFixed(1)}%
                </span>
              </div>
              <div>
                <span className="text-muted block">Placement band</span>
                <span className="font-bold text-foreground">{calibration.placedBandCode}</span>
              </div>
              <div>
                <span className="text-muted block">Proposed band</span>
                <span className="font-bold text-accent">{calibration.proposal.proposedBandCode}</span>
              </div>
            </div>

            {calibration.excludedRushedCount > 0 && (
              <p className="text-xs text-muted m-0">
                {`${calibration.excludedRushedCount} answer${calibration.excludedRushedCount === 1 ? '' : 's'} under 3 seconds ${calibration.excludedRushedCount === 1 ? 'was' : 'were'} left out: quick guesses are not counted as level evidence.`}
              </p>
            )}

            <p className="text-xs text-muted m-0">
              Nothing changes until you choose. "Keep my path" leaves everything untouched.
            </p>

            <div className="flex flex-wrap items-center gap-2.5 pt-1">
              <Button
                variant="primary"
                size="sm"
                isDisabled={calibrationMutation.isPending}
                onPress={() => calibrationMutation.mutate(true)}
                onClick={() => calibrationMutation.mutate(true)}
              >
                Lower my starting band to {calibration.proposal.proposedBandCode}
              </Button>
              <Button
                variant="secondary"
                size="sm"
                isDisabled={calibrationMutation.isPending}
                onPress={() => calibrationMutation.mutate(false)}
                onClick={() => calibrationMutation.mutate(false)}
              >
                Keep my path for now
              </Button>
            </div>
          </Card.Content>
        </Card>
      )}

      {/* AI Diagnostic Insight Panel for Path */}
      {insight && (
        <AiInsightPanel
          scope="path"
          insight={insight}
          isLoading={isInsightLoading}
          onRetry={() => retryInsightMutation.mutate()}
        />
      )}

      {/* 4-Week Plan */}
      {summary.weeks && summary.weeks.length > 0 && (
        <section className="space-y-4">
          <h2 className="text-xl font-bold tracking-tight text-foreground m-0">Four-week plan</h2>
          <div className="space-y-4">
            {summary.weeks.map((week) => (
              <Card key={week.weekNumber} className="border-border">
                <Card.Header className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 border-b border-border pb-3 bg-surface-secondary/40">
                  <div className="flex items-center gap-2.5">
                    <span
                      className={`text-xs px-2 py-0.5 rounded font-mono font-semibold ${
                        week.isOnTrack ? 'bg-success text-white' : 'bg-warning text-foreground'
                      }`}
                    >
                      {week.isOnTrack ? 'On track' : 'Off track'}
                    </span>
                    <strong className="text-foreground text-sm">Week {week.weekNumber}</strong>
                  </div>
                  <span className="font-mono text-xs text-muted">
                    {week.completedSteps}/{week.plannedSteps} steps done · {week.completedMinutes} of {week.plannedMinutes}m planned
                    {week.actualAccuracyPct != null && ` · accuracy ${week.actualAccuracyPct.toFixed(1)}% (target ${week.targetAccuracyPct}%)`}
                  </span>
                </Card.Header>

                <Card.Content className="p-0 divide-y divide-border">
                  {week.steps.map((step) => {
                    const isDone = step.status === 'done';
                    const isInProgress = step.status === 'in_progress';
                    const isLocked = step.status === 'locked';
                    const isAvailable = step.status === 'available';

                    return (
                      <div
                        key={step.stepId}
                        className="p-3.5 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 hover:bg-surface-secondary/20 transition-colors"
                      >
                        <div className="space-y-1">
                          <div className="flex items-center gap-2">
                            <span className="font-mono text-xs font-bold text-muted">
                              {step.weekSequence}. {step.targetCefr}
                            </span>
                            <span className="font-mono text-xs px-1.5 py-0.5 rounded bg-surface-secondary border border-border text-foreground">
                              {step.topicCode}
                            </span>
                            <span className="text-sm font-semibold text-foreground">{step.title}</span>
                          </div>
                          <div className="flex items-center gap-2 text-xs text-muted">
                            <span>{formatStepType(step.stepType)}</span>
                            <span>·</span>
                            <span>{step.sizingTier}</span>
                            {step.estimatedMinutes && (
                              <>
                                <span>·</span>
                                <span>~{step.estimatedMinutes}m</span>
                              </>
                            )}
                          </div>
                        </div>

                        <div className="flex items-center gap-2 self-end sm:self-auto">
                          {isDone && (
                            <span className="text-xs px-2.5 py-1 rounded bg-success/10 text-success border border-success/30 font-mono font-semibold">
                              Done
                            </span>
                          )}
                          {isInProgress && (
                            <div className="flex items-center gap-2">
                              <span className="text-xs px-2.5 py-1 rounded bg-warning/20 text-warning border border-warning/40 font-mono font-semibold">
                                In progress
                              </span>
                              <Button
                                size="sm"
                                variant="primary"
                                isDisabled={startStepMutation.isPending}
                                onPress={() => startStepMutation.mutate(step.stepId)}
                                className="gap-1"
                              >
                                <Play className="w-3 h-3" />
                                Resume
                              </Button>
                            </div>
                          )}
                          {isLocked && (
                            <span className="text-xs px-2.5 py-1 rounded bg-surface-secondary text-muted font-mono flex items-center gap-1">
                              <Lock className="w-3 h-3" /> Locked
                            </span>
                          )}
                          {isAvailable && (
                            <Button
                              size="sm"
                              variant="primary"
                              isDisabled={startStepMutation.isPending}
                              onPress={() => startStepMutation.mutate(step.stepId)}
                              onClick={() => startStepMutation.mutate(step.stepId)}
                              className="gap-1"
                            >
                              <Play className="w-3 h-3" />
                              Start
                            </Button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </Card.Content>
              </Card>
            ))}
          </div>
        </section>
      )}

      {/* Reset Path Section */}
      <Card className="border-border">
        <Card.Content className="p-4 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
          <div>
            <span className="text-sm font-semibold text-foreground block">Reset learning path</span>
            <span className="text-xs text-muted">
              Resetting clears your step progress. Your placement result and target level are kept.
            </span>
          </div>
          {isResetConfirmOpen ? (
            <div className="flex items-center gap-2 shrink-0">
              <Button size="sm" variant="ghost" onPress={() => setIsResetConfirmOpen(false)}>
                Cancel
              </Button>
              <Button
                size="sm"
                variant="danger"
                isDisabled={resetMutation.isPending}
                onPress={() => resetMutation.mutate()}
              >
                {resetMutation.isPending ? 'Resetting…' : 'Confirm reset'}
              </Button>
            </div>
          ) : (
            <Button
              size="sm"
              variant="danger"
              onPress={() => setIsResetConfirmOpen(true)}
              className="shrink-0"
            >
              Reset progress
            </Button>
          )}
        </Card.Content>
      </Card>
    </div>
  );
}
