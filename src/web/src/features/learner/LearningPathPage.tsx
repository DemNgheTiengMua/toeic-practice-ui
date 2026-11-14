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
  Award,
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
  Trophy,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  LearningPathSummaryDto,
  PathStepDto,
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
  const [isGenerateConfirmOpen, setIsGenerateConfirmOpen] = useState(false);

  // 1. Learning path summary
  const { data: summary, isLoading, error, refetch } = useQuery<LearningPathSummaryDto>({
    queryKey: ['learner', 'learning-path'],
    queryFn: () => api.learningPath.get(),
  });

  // 2. Path AI Insight
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
              <div className="flex items-center justify-center gap-2 mb-2">
                <Chip color="accent" variant="soft" size="sm">
                  <Chip.Label className="font-mono">5 credits</Chip.Label>
                </Chip>
                <Chip color="success" variant="soft" size="sm">
                  <Chip.Label>4-week sequential plan</Chip.Label>
                </Chip>
              </div>
              <h2 className="text-xl font-bold text-foreground m-0">No learning path yet</h2>
              <p className="text-sm text-muted max-w-md mt-2 mb-0 leading-relaxed">
                Take a diagnostic placement test first, or generate your 4-week sequential learning path for{' '}
                <strong>5 credits</strong> (includes 0-credit AI grading for Writing &amp; Speaking steps inside the path).
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
              {!isGenerateConfirmOpen ? (
                <Button
                  variant="secondary"
                  isDisabled={generateMutation.isPending}
                  onPress={() => setIsGenerateConfirmOpen(true)}
                  className="gap-2"
                >
                  <Sparkles className="w-4 h-4 text-warning" />
                  <span>Generate 4-week path (5 credits)</span>
                </Button>
              ) : (
                <div className="flex flex-wrap items-center gap-2 rounded-lg border border-accent/40 bg-accent/5 px-3 py-2">
                  <span className="text-xs font-medium text-foreground">Spend 5 credits to generate?</span>
                  <Button
                    size="sm"
                    variant="primary"
                    isDisabled={generateMutation.isPending}
                    onPress={() => generateMutation.mutate()}
                  >
                    {generateMutation.isPending ? <Spinner size="sm" /> : 'Confirm (5 credits)'}
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    isDisabled={generateMutation.isPending}
                    onPress={() => setIsGenerateConfirmOpen(false)}
                  >
                    Cancel
                  </Button>
                </div>
              )}
            </div>
          </Card.Content>
        </Card>
      </div>
    );
  }

  const progressPercent = summary.totalSteps > 0
    ? Math.round((summary.completedSteps / summary.totalSteps) * 100)
    : 0;

  const isSprintCompleted = Boolean(
    summary.isSprintCompleted ||
    summary.status === 'completed' ||
    (summary.totalSteps > 0 && summary.completedSteps === summary.totalSteps)
  );

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

      {/* Milestone Celebration Card */}
      {isSprintCompleted && (
        <Card className="border-2 border-primary/40 bg-gradient-to-r from-primary/10 via-success/10 to-accent/10 shadow-md">
          <Card.Content className="p-6 space-y-5">
            {/* Header Banner */}
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
              <div className="flex items-start sm:items-center gap-3">
                <div className="p-3 rounded-full bg-success/20 text-success border border-success/30 shrink-0">
                  <Trophy className="w-8 h-8" />
                </div>
                <div>
                  <div className="flex items-center gap-2 mb-1">
                    <Chip color="success" variant="soft" size="sm">
                      <Chip.Label className="font-semibold">Milestone Completed</Chip.Label>
                    </Chip>
                    <Chip color="accent" variant="soft" size="sm">
                      <Chip.Label className="font-mono">Target: {summary.toBandCode}</Chip.Label>
                    </Chip>
                  </div>
                  <h2 className="text-2xl font-bold tracking-tight text-foreground m-0">
                    Sprint Milestone Achieved!
                  </h2>
                  <p className="text-sm text-muted mt-1 mb-0">
                    Congratulations! You have completed all curriculum steps for{' '}
                    <strong>{summary.toBandName || summary.toBandCode}</strong>.
                  </p>
                </div>
              </div>
            </div>

            {/* Metrics Row */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-1">
              <div className="p-3.5 rounded-lg bg-surface/80 border border-border flex items-center gap-3">
                <CheckCircle2 className="w-5 h-5 text-success shrink-0" />
                <div>
                  <span className="text-xs text-muted block">Curriculum Covered</span>
                  <span className="font-bold text-sm text-foreground">
                    {summary.completedSteps}/{summary.totalSteps} steps completed
                  </span>
                </div>
              </div>

              <div className="p-3.5 rounded-lg bg-surface/80 border border-border flex items-center gap-3">
                <Sparkles className="w-5 h-5 text-accent shrink-0" />
                <div>
                  <span className="text-xs text-muted block">Milestone Band</span>
                  <span className="font-bold text-sm text-foreground">
                    {summary.toBandName || summary.toBandCode}
                  </span>
                </div>
              </div>

              <div className="p-3.5 rounded-lg bg-surface/80 border border-border flex items-center gap-3">
                <Award className="w-5 h-5 text-primary shrink-0" />
                <div>
                  <span className="text-xs text-muted block">Next Step</span>
                  <span className="font-bold text-sm text-foreground">
                    Verify Level via Exit Mock Exam
                  </span>
                </div>
              </div>
            </div>

            {/* CTAs */}
            <div className="flex flex-wrap items-center gap-3 pt-2 border-t border-border/60">
              <Button
                variant="primary"
                onPress={() => navigate({ to: '/exams' as any })}
                onClick={() => navigate({ to: '/exams' as any })}
                className="gap-2 font-semibold"
              >
                <Award className="w-4 h-4" />
                <span>Take Exit Mock Exam</span>
              </Button>

              {summary.canStartNextSprint && (
                !isGenerateConfirmOpen ? (
                  <Button
                    variant="secondary"
                    isDisabled={generateMutation.isPending}
                    onPress={() => setIsGenerateConfirmOpen(true)}
                    onClick={() => setIsGenerateConfirmOpen(true)}
                    className="gap-2 border-accent/40 text-accent font-semibold"
                  >
                    <Sparkles className="w-4 h-4 text-warning" />
                    <span>Unlock Next Sprint ({summary.nextMilestoneBandName || summary.nextMilestoneBandCode || 'Next Level'})</span>
                  </Button>
                ) : (
                  <div className="flex flex-wrap items-center gap-2 rounded-lg border border-accent/40 bg-accent/5 px-3 py-2">
                    <span className="text-xs font-medium text-foreground">Spend 5 credits to start next sprint?</span>
                    <Button
                      size="sm"
                      variant="primary"
                      isDisabled={generateMutation.isPending}
                      onPress={() => generateMutation.mutate()}
                      onClick={() => generateMutation.mutate()}
                    >
                      {generateMutation.isPending ? <Spinner size="sm" /> : 'Confirm (5 credits)'}
                    </Button>
                    <Link
                      to="/checkout"
                      search={{ credits: 5, returnUrl: '/learning-path' } as any}
                      className="text-xs text-accent hover:underline px-1"
                    >
                      Need credits?
                    </Link>
                    <Button
                      size="sm"
                      variant="ghost"
                      isDisabled={generateMutation.isPending}
                      onPress={() => setIsGenerateConfirmOpen(false)}
                      onClick={() => setIsGenerateConfirmOpen(false)}
                    >
                      Cancel
                    </Button>
                  </div>
                )
              )}
            </div>
          </Card.Content>
        </Card>
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

      {/* Path AI Insight */}

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
                          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                            <span>{formatStepType(step.stepType)}</span>
                            <span>·</span>
                            <span>{step.sizingTier}</span>
                            {step.estimatedMinutes && (
                              <>
                                <span>·</span>
                                <span>~{step.estimatedMinutes}m</span>
                              </>
                            )}
                            {(step.isZeroCreditAiGrading ||
                              step.modality === 'writing' ||
                              step.modality === 'speaking') && (
                              <Chip size="sm" color="success" variant="soft">
                                <Chip.Label>0-credit AI grading</Chip.Label>
                              </Chip>
                            )}
                          </div>
                          {(step.guideText || (step.rubricCriteria && step.rubricCriteria.length > 0)) && (
                            <div className="mt-2 rounded-md border border-border bg-surface-secondary/40 p-2.5 text-xs space-y-1">
                              {step.guideText && (
                                <p className="text-foreground m-0">
                                  <strong>Guide:</strong> {step.guideText}
                                </p>
                              )}
                              {step.rubricCriteria && step.rubricCriteria.length > 0 && (
                                <div className="text-muted">
                                  <strong className="text-foreground">Rubric:</strong>{' '}
                                  {step.rubricCriteria.join(' · ')}
                                </div>
                              )}
                            </div>
                          )}
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
