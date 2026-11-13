import React, { useState } from 'react';
import { Link } from '@tanstack/react-router';
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
  ArrowRight,
  BarChart3,
  BookOpen,
  CheckCircle2,
  HelpCircle,
  RotateCw,
  Sparkles,
  Target,
  Trophy,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  WeaknessAnalysisDto,
  AiInsightDto,
} from '../../lib/api';
import { useAuth } from '../../context/AuthContext';
import { formatModality, formatRecommendationSeverity } from './learnerHelpers';
import { AiInsightPanel } from './AiInsightPanel';

export function WeaknessAnalysisPage() {
  const queryClient = useQueryClient();
  const { learnerContext } = useAuth();

  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // 1. Weakness data
  const {
    data: weaknessData,
    isLoading,
    error,
    refetch,
  } = useQuery<WeaknessAnalysisDto>({
    queryKey: ['learner', 'weakness-analysis', learnerContext?.active?.code],
    queryFn: () => api.learningPath.weaknessAnalysis(),
  });

  // 2. AI Insight (dashboard/weakness scope)
  const {
    data: insight,
    isLoading: isInsightLoading,
    refetch: refetchInsight,
  } = useQuery<AiInsightDto>({
    queryKey: ['learner', 'insights', 'dashboard', learnerContext?.active?.code],
    queryFn: () => api.insights.dashboard(),
  });

  // Resolve recommendation mutation
  const resolveRecMutation = useMutation({
    mutationFn: async (recId: number) => {
      return await api.learningPath.resolveRecommendation(recId);
    },
    onSuccess: () => {
      setSuccessMessage('Recommendation marked as resolved.');
      queryClient.invalidateQueries({
        queryKey: ['learner', 'weakness-analysis'],
      });
    },
    onError: () => {
      setErrorMessage('Could not resolve recommendation.');
    },
  });

  // Retry AI Insight mutation
  const retryInsightMutation = useMutation({
    mutationFn: async () => {
      await api.insights.retryDashboard();
    },
    onSuccess: () => {
      refetchInsight();
    },
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[400px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading weakness analysis…</p>
      </div>
    );
  }

  if (error || !weaknessData) {
    return (
      <div className="max-w-4xl mx-auto py-8 space-y-4">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load weakness analysis</Alert.Title>
            <Alert.Description>
              {error instanceof Error ? error.message : 'Please check your connection.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <Button variant="ghost" size="sm" onPress={() => refetch()} className="gap-1.5">
          <RotateCw className="w-3.5 h-3.5" /> Retry
        </Button>
      </div>
    );
  }

  const {
    certificate,
    hasData,
    gradedExamCount,
    completedPracticeCount,
    sections = [],
    topics = [],
    weakestTopic,
    frequentlyMissedTopics = [],
    recommendations = [],
    priorityPracticeSection,
  } = weaknessData;

  const openRecommendations = recommendations.filter((r) => !r.isResolved);

  return (
    <div className="max-w-5xl mx-auto py-6 space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="font-display text-2xl sm:text-3xl font-bold tracking-tight text-foreground m-0">
            Weakness analysis · {certificate.code}
          </h1>
          <p className="text-sm text-muted mt-1 mb-0">
            Combined from {gradedExamCount} {gradedExamCount === 1 ? 'graded exam' : 'graded exams'} and{' '}
            {completedPracticeCount} {completedPracticeCount === 1 ? 'practice session' : 'practice sessions'}.
          </p>
        </div>
        <span className="text-xs px-2.5 py-1 rounded bg-success/10 text-success border border-success/30 font-mono font-semibold self-start sm:self-auto">
          Free analysis
        </span>
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

      {/* AI Diagnostic Insight Panel */}
      {insight && (
        <AiInsightPanel
          scope="dashboard"
          insight={insight}
          isLoading={isInsightLoading}
          onRetry={() => retryInsightMutation.mutate()}
          overallAccuracyPercent={weaknessData.overallAccuracyPercentage}
        />
      )}

      {/* EMPTY STATE: Not enough data to analyse */}
      {!hasData ? (
        <Card className="border-border">
          <Card.Content className="py-12 px-6 flex flex-col items-center text-center space-y-4">
            <div className="p-3 rounded-full bg-accent/10 text-accent">
              <BarChart3 className="w-8 h-8" />
            </div>
            <div>
              <h2 className="text-xl font-bold text-foreground m-0">Not enough data to analyse</h2>
              <p className="text-sm text-muted max-w-md mt-2 mb-0 leading-relaxed">
                At least one completed practice session or one graded exam is required. Practicing any single section
                gives you results right away — <strong>free, no credit deducted</strong>.
              </p>
            </div>
            <div className="flex flex-wrap items-center justify-center gap-3 pt-2">
              <Link
                to="/practice"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-white font-semibold text-sm no-underline hover:bg-accent/90"
              >
                <span>Practice a section</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
              <Link
                to="/exams"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-surface-secondary border border-border text-foreground font-semibold text-sm no-underline hover:bg-surface-tertiary"
              >
                <span>Take a mock exam</span>
              </Link>
            </div>
          </Card.Content>
        </Card>
      ) : (
        <>
          <Alert status="accent" className="border-accent/30 bg-accent/5">
            <Alert.Indicator>
              <HelpCircle className="w-4 h-4 text-accent" />
            </Alert.Indicator>
            <Alert.Content>
              <Alert.Description className="text-xs">
                This is a study tool, not an official exam result. The figures come from every question you have answered,
                including diagnostic practice drilling.
              </Alert.Description>
            </Alert.Content>
          </Alert>

          {/* CARD 1: Weakest Topic at its Level */}
          <Card id="topic-weakness">
            <Card.Header className="border-b border-border pb-3">
              <strong className="text-sm font-bold text-foreground">Weakest topic at its level</strong>
            </Card.Header>
            <Card.Content className="p-4 sm:p-6 space-y-4">
              {weakestTopic ? (
                <div className="p-4 rounded-lg bg-surface-secondary border border-border space-y-2">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-mono text-sm font-bold text-foreground">
                      {weakestTopic.topicCode} — {weakestTopic.topicName}
                    </span>
                    {weakestTopic.levelCode && (
                      <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface border border-border text-foreground">
                        {weakestTopic.levelCode}
                      </span>
                    )}
                    <span className="text-xs px-2 py-0.5 rounded bg-danger text-white font-mono font-semibold">
                      Weakest
                    </span>
                  </div>
                  <p className="text-xs text-muted m-0">
                    <span className="font-mono font-semibold text-foreground">{weakestTopic.accuracyPercentage}%</span>{' '}
                    accuracy ({weakestTopic.correctCount}/{weakestTopic.answeredCount} answered)
                  </p>
                </div>
              ) : (
                <p className="text-xs text-muted m-0 bg-surface-secondary p-3.5 rounded border border-border" id="no-ranked-weakness">
                  No topic holds enough answers to be called a weakness yet. A cell is ranked at 12 answers in listening
                  and reading, or 3 tasks in speaking and writing, so one short session cannot name your weakest topic.
                </p>
              )}

              {/* Topics Table */}
              {topics.length > 0 && (
                <div className="overflow-x-auto pt-2">
                  <table className="w-full text-xs text-left">
                    <caption className="text-left text-xs text-muted pb-2">
                      Every topic at a level you have answered in. Ranked cells come first.
                    </caption>
                    <thead className="border-b border-border text-muted font-mono uppercase">
                      <tr>
                        <th className="py-2 pr-3">Topic</th>
                        <th className="py-2 px-3">Level</th>
                        <th className="py-2 px-3">Skill</th>
                        <th className="py-2 px-3">Accuracy</th>
                        <th className="py-2 px-3">Evidence</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border font-mono">
                      {topics.map((t) => (
                        <tr key={t.categoryId} className="hover:bg-surface-secondary/40">
                          <td className="py-2.5 pr-3 font-medium text-foreground">
                            <strong>{t.topicCode}</strong> {t.topicName}
                            {t.isWeakest && (
                              <span className="ml-1.5 px-1.5 py-0.5 rounded bg-danger text-white text-[10px]">
                                Weakest
                              </span>
                            )}
                          </td>
                          <td className="py-2.5 px-3">{t.levelCode ?? '--'}</td>
                          <td className="py-2.5 px-3 font-sans">{formatModality(t.skillCode)}</td>
                          <td className="py-2.5 px-3 font-bold">{t.accuracyPercentage}%</td>
                          <td className="py-2.5 px-3 text-muted">
                            {t.correctCount}/{t.answeredCount} answered
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card.Content>
          </Card>

          {/* CARD 2: Accuracy by Section */}
          <Card>
            <Card.Header className="border-b border-border pb-3">
              <strong className="text-sm font-bold text-foreground">Accuracy by section</strong>
            </Card.Header>
            <Card.Content className="p-4 sm:p-6 space-y-4">
              <p className="text-xs text-muted m-0">
                Sorted weakest first. The questions column includes both exams and practice drill attempts.
              </p>

              <div className="space-y-4 pt-1">
                {sections.map((sec) => {
                  const hasAnswered = sec.answeredCount > 0;
                  const isWeakest = sec.accuracyPercentage < 50;
                  const isStrongest = sec.accuracyPercentage >= 80;

                  return (
                    <div key={sec.sectionId} className="space-y-1.5">
                      <div className="flex items-center justify-between text-xs">
                        <div className="flex items-center gap-2">
                          <span className="font-semibold text-foreground">
                            {sec.sectionCode} — {sec.sectionName}
                          </span>
                          {isWeakest && (
                            <span className="px-1.5 py-0.5 rounded bg-danger text-white text-[10px] font-mono">
                              Weakest
                            </span>
                          )}
                          {isStrongest && (
                            <span className="px-1.5 py-0.5 rounded bg-success text-white text-[10px] font-mono">
                              Strongest
                            </span>
                          )}
                        </div>
                        <span className="font-mono text-muted">
                          {hasAnswered ? (
                            <span>
                              {sec.accuracyPercentage}% · {sec.correctCount}/{sec.answeredCount} answered
                            </span>
                          ) : (
                            'Not attempted yet'
                          )}
                        </span>
                      </div>

                      <div className="w-full bg-surface-secondary rounded-full h-2 overflow-hidden border border-border">
                        <div
                          className={`h-2 rounded-full transition-all duration-300 ${
                            sec.accuracyPercentage >= 75
                              ? 'bg-success'
                              : sec.accuracyPercentage >= 50
                              ? 'bg-warning'
                              : 'bg-danger'
                          }`}
                          style={{ width: `${hasAnswered ? sec.accuracyPercentage : 0}%` }}
                          role="progressbar"
                          aria-valuenow={sec.accuracyPercentage}
                          aria-valuemin={0}
                          aria-valuemax={100}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            </Card.Content>
          </Card>

          {/* CARD 3 & 4: What to Practice First & Frequently Missed Topics */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <Card>
              <Card.Header className="border-b border-border pb-3">
                <strong className="text-sm font-bold text-foreground">What to practice first</strong>
              </Card.Header>
              <Card.Content className="p-4 sm:p-6 space-y-3">
                {priorityPracticeSection ? (
                  <>
                    <p className="text-xs text-muted m-0 leading-relaxed">
                      {priorityPracticeSection.sectionCode} sits at your lowest accuracy level (
                      {priorityPracticeSection.accuracyPercentage}%). This is where you can improve your score
                      fastest.
                    </p>
                    <div className="pt-2">
                      <Link
                        to="/practice"
                        className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-accent text-white text-xs font-semibold no-underline"
                      >
                        Practice {priorityPracticeSection.sectionCode} now <ArrowRight className="w-3.5 h-3.5" />
                      </Link>
                    </div>
                  </>
                ) : (
                  <>
                    <p className="text-xs text-muted m-0">
                      Keep practicing across all sections to uncover your target areas for improvement.
                    </p>
                    <div className="pt-2">
                      <Link
                        to="/practice"
                        className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-accent text-white text-xs font-semibold no-underline"
                      >
                        Practice now <ArrowRight className="w-3.5 h-3.5" />
                      </Link>
                    </div>
                  </>
                )}
              </Card.Content>
            </Card>

            <Card>
              <Card.Header className="border-b border-border pb-3">
                <strong className="text-sm font-bold text-foreground">Frequently missed topics</strong>
              </Card.Header>
              <Card.Content className="p-4 sm:p-6">
                {frequentlyMissedTopics.length > 0 ? (
                  <ul className="space-y-2 m-0 pl-4 text-xs">
                    {frequentlyMissedTopics.map((cell) => (
                      <li key={cell.categoryId} className="text-foreground">
                        <strong>{cell.topicName}</strong>{' '}
                        <span className="font-mono text-[10px] px-1 rounded bg-surface-secondary border border-border">
                          {cell.levelCode}
                        </span>
                        : <span className="font-mono font-semibold">{cell.accuracyPercentage}%</span> ({cell.correctCount}/
                        {cell.answeredCount} correct)
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="text-xs text-muted m-0">
                    No topic holds a graded answer yet, so there is nothing to report here.
                  </p>
                )}
              </Card.Content>
            </Card>
          </div>

          {/* CARD 5: Recommendations */}
          <Card id="recommendations-card">
            <Card.Header className="border-b border-border pb-3">
              <strong className="text-sm font-bold text-foreground">Recommendations</strong>
            </Card.Header>
            <Card.Content className="p-4 sm:p-6 space-y-3" id="recommendations-list">
              <p className="text-xs text-muted m-0">
                Actionable recommendations from your study history. Open items remain until marked resolved.
              </p>

              {openRecommendations.length > 0 ? (
                <div className="space-y-3 pt-1">
                  {openRecommendations.map((reco) => {
                    const sev = formatRecommendationSeverity(reco.severity);
                    return (
                      <div
                        key={reco.recId}
                        id={`reco-${reco.recId}`}
                        className="p-3.5 rounded-lg border border-border bg-surface-secondary/40 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 reco-item"
                      >
                        <div className="space-y-1">
                          <div className="flex items-center gap-2">
                            <span className={`text-[11px] font-mono font-bold px-2 py-0.5 rounded ${sev.badgeClass}`}>
                              {sev.label}
                            </span>
                            {reco.targetBandCode && (
                              <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-surface border border-border text-muted">
                                Target {reco.targetBandCode}
                              </span>
                            )}
                            {reco.skillName && (
                              <span className="text-xs text-muted font-medium">({reco.skillName})</span>
                            )}
                          </div>
                          <div className="text-xs text-foreground leading-relaxed">{reco.text}</div>
                        </div>

                        <Button
                          size="sm"
                          variant="secondary"
                          isDisabled={resolveRecMutation.isPending}
                          onPress={() => resolveRecMutation.mutate(reco.recId)}
                          className="shrink-0 text-xs"
                        >
                          Mark resolved
                        </Button>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <div
                  id="no-recos-message"
                  className="p-6 text-center text-xs text-muted border border-border rounded-lg bg-surface-secondary/20"
                >
                  All recommendations resolved. Great work!
                </div>
              )}
            </Card.Content>
          </Card>
        </>
      )}
    </div>
  );
}
