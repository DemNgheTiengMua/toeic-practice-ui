import React, { useEffect, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { AlertCircle, ArrowRight, Brain, CheckCircle2, RotateCw } from 'lucide-react';
import type { AiInsightDto } from '../../lib/api';
import { formatAiInsightTrend, formatRecommendationSeverity } from './learnerHelpers';
import { SafeMarkdown } from './SafeMarkdown';

interface AiInsightPanelProps {
  insight?: AiInsightDto | null;
  scope: 'dashboard' | 'path' | 'practice' | 'attempt';
  onRetry?: () => void;
  isLoading?: boolean;
  overallAccuracyPercent?: number | null;
}

export function AiInsightPanel({
  insight,
  scope,
  onRetry,
  isLoading = false,
  overallAccuracyPercent,
}: AiInsightPanelProps) {
  const [elapsed, setElapsed] = useState(0);

  const status = isLoading ? 'pending' : (insight?.status ?? 'done');
  const isPending = status === 'pending';
  const isFailed = status === 'failed';
  const isDone = status === 'done';

  useEffect(() => {
    let timer: any;
    if (isPending) {
      const start = Date.now();
      timer = setInterval(() => {
        setElapsed(Math.floor((Date.now() - start) / 100) / 10);
      }, 100);
    } else {
      setElapsed(0);
    }
    return () => clearInterval(timer);
  }, [isPending]);

  const trendInfo = insight?.trend ? formatAiInsightTrend(insight.trend) : null;

  return (
    <Card id="ai-insight-panel" className="border-border">
      <Card.Header className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 border-b border-border pb-3">
        <div className="flex items-center gap-2.5">
          <div className="p-1.5 rounded-md bg-accent/10 text-accent">
            <Brain className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs uppercase font-bold text-accent tracking-wider">
                {scope === 'dashboard' ? 'AI Study Mentor' : 'AI Insight'}
              </span>
              <strong className="text-foreground text-sm">
                {scope === 'dashboard'
                  ? 'Personal Diagnostic Guidance'
                  : 'AI Diagnostic Insights & Recommendations'}
              </strong>
            </div>
            {scope === 'dashboard' && (
              <p className="text-xs text-muted m-0 mt-0.5">
                Continuous diagnostic review &amp; targeted coaching — you are never studying alone.
              </p>
            )}
          </div>
        </div>

        <div className="flex items-center gap-2" id="ai-insight-header-badges">
          {scope === 'dashboard' && overallAccuracyPercent != null && (
            <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface-secondary text-foreground font-semibold">
              ACCURACY: {overallAccuracyPercent}%
            </span>
          )}
          {isDone && trendInfo && (
            <Chip
              id="ai-insight-trend-badge"
              size="sm"
              variant="soft"
              color={trendInfo.chipColor}
            >
              {trendInfo.label}
            </Chip>
          )}
        </div>
      </Card.Header>

      <Card.Content className="pt-4 space-y-4" id="ai-insight-body">
        {isPending && (
          <div id="ai-insight-loading" className="py-6 flex flex-col items-center text-center space-y-3">
            <div className="flex items-center justify-between w-full font-mono text-xs text-muted">
              <span className="font-bold text-warning uppercase">AI MENTOR // ANALYZING PROGRESS…</span>
              <span id="ai-insight-elapsed" aria-live="polite">
                ELAPSED: {elapsed.toFixed(1)}s
              </span>
            </div>
            <Spinner size="md" className="my-2" />
            <h3 className="text-base font-semibold text-foreground m-0">Analyzing your diagnostic progress…</h3>
            <p className="text-xs text-muted max-w-md m-0">
              Reviewing your latest question logs, accuracy trends, and target band alignment so you have clear next
              steps.
            </p>
          </div>
        )}

        {isFailed && (
          <div id="ai-insight-failed" className="py-4 space-y-3">
            <Alert status="danger">
              <Alert.Indicator>
                <AlertCircle className="w-4 h-4" />
              </Alert.Indicator>
              <Alert.Content>
                <Alert.Title>AI Insight Generation Did Not Complete</Alert.Title>
                <Alert.Description id="ai-insight-failure-reason">
                  {insight?.failureReason ?? 'Could not generate AI diagnostic insight at this time.'}
                </Alert.Description>
              </Alert.Content>
            </Alert>
            {onRetry && (
              <Button
                id="ai-insight-retry-btn"
                variant="primary"
                size="sm"
                onPress={onRetry}
                className="gap-1.5"
              >
                <RotateCw className="w-3.5 h-3.5" />
                Retry Analysis
              </Button>
            )}
          </div>
        )}

        {isDone && (
          <div id="ai-insight-content" className="space-y-4">
            {insight?.narrative || insight?.formattedNarrative ? (
              <div className="ai-insight-narrative text-foreground text-sm">
                <SafeMarkdown content={insight.formattedNarrative || insight.narrative || ''} />
              </div>
            ) : (
              <div className="ai-mentor-welcome space-y-2 p-3 rounded-md bg-surface-secondary">
                <h4 className="text-sm font-semibold text-foreground m-0">Ready to guide your preparation</h4>
                <p className="text-xs text-muted m-0">
                  I am your dedicated AI study mentor. As you complete practice questions and mock exams, I will
                  analyze your answer patterns, spot question traps, and give you step-by-step guidance to achieve your
                  target band.
                </p>
              </div>
            )}

            {((insight?.strengths && insight.strengths.length > 0) ||
              (insight?.weaknesses && insight.weaknesses.length > 0)) && (
              <div className="grid gap-3 sm:grid-cols-2 pt-2">
                {insight?.strengths && insight.strengths.length > 0 && (
                  <div className="rounded-lg border border-border bg-surface-secondary p-3 space-y-1.5">
                    <div className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-success">
                      <CheckCircle2 className="size-3.5" aria-hidden="true" />
                      Strengths
                    </div>
                    <ul className="space-y-1 pl-4 text-xs text-foreground list-disc m-0">
                      {insight.strengths.map((item, idx) => (
                        <li key={idx}>{item}</li>
                      ))}
                    </ul>
                  </div>
                )}
                {insight?.weaknesses && insight.weaknesses.length > 0 && (
                  <div className="rounded-lg border border-border bg-surface-secondary p-3 space-y-1.5">
                    <div className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-warning">
                      <AlertCircle className="size-3.5" aria-hidden="true" />
                      Weaknesses
                    </div>
                    <ul className="space-y-1 pl-4 text-xs text-foreground list-disc m-0">
                      {insight.weaknesses.map((item, idx) => (
                        <li key={idx}>{item}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            )}

            {insight?.recommendations && insight.recommendations.length > 0 && (
              <div className="pt-3 border-t border-border space-y-2">
                <div className="text-xs uppercase text-muted font-bold tracking-wider">
                  Action Directives from Your AI Mentor:
                </div>
                <div className="space-y-2">
                  {insight.recommendations.map((rec) => {
                    const sev = formatRecommendationSeverity(rec.severity);
                    const targetRoute = rec.severity?.toLowerCase() === 'major' || rec.severity?.toLowerCase() === 'high'
                      ? '/learning-path'
                      : '/practice';
                    const targetLabel = targetRoute === '/learning-path' ? 'Open learning path' : 'Practice now';
                    return (
                      <div
                        key={rec.recId}
                        className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 p-2.5 rounded border border-border bg-surface-secondary text-sm"
                      >
                        <div className="flex items-start sm:items-center gap-2">
                          <Chip size="sm" variant="soft" color={sev.chipColor}>
                            {sev.label}
                          </Chip>
                          <span className="text-foreground">{rec.text}</span>
                        </div>
                        <div className="flex items-center gap-2 self-start sm:self-auto">
                          {rec.targetBandCode && (
                            <Chip size="sm" variant="soft" color="default">
                              Band {rec.targetBandCode}
                            </Chip>
                          )}
                          <Link
                            to={targetRoute}
                            className="inline-flex items-center gap-1 text-xs font-medium text-accent hover:underline whitespace-nowrap"
                          >
                            {targetLabel}
                            <ArrowRight className="size-3" aria-hidden="true" />
                          </Link>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            <div className="text-[11px] text-muted font-mono pt-1">
              Model: {insight?.model ?? 'fake-ai-insight-v1'}
            </div>
          </div>
        )}
      </Card.Content>
    </Card>
  );
}
