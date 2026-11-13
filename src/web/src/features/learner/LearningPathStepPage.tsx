import React, { useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { AlertCircle, ArrowLeft, ArrowRight, BookOpen, Clock, Lock, Play, Target } from 'lucide-react';
import { api, ApiClientError, PathStepDto } from '../../lib/api';
import { formatPathStepStatus, formatStepType } from './learnerHelpers';

export function LearningPathStepPage() {
  const { stepId } = useParams({ strict: false }) as { stepId: string };
  const numericStepId = parseInt(stepId, 10);
  const navigate = useNavigate();

  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data: step, isLoading, error } = useQuery<PathStepDto>({
    queryKey: ['learner', 'learning-path', 'step', numericStepId],
    queryFn: () => api.learningPath.step(numericStepId),
    enabled: !isNaN(numericStepId),
  });

  const startStepMutation = useMutation({
    mutationFn: async () => {
      return await api.learningPath.startStep(numericStepId);
    },
    onSuccess: (res) => {
      if (res.redirectUrl) {
        const dest = res.redirectUrl.startsWith('/') ? res.redirectUrl : `/${res.redirectUrl}`;
        window.location.href = dest;
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

  if (isNaN(numericStepId)) {
    return (
      <div className="max-w-2xl mx-auto py-8">
        <Alert status="danger">
          <Alert.Title>Invalid step ID</Alert.Title>
        </Alert>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[350px] space-y-3">
        <Spinner size="lg" />
        <p className="text-sm text-muted font-mono">Loading step details…</p>
      </div>
    );
  }

  if (error || !step) {
    return (
      <div className="max-w-2xl mx-auto py-8 space-y-4">
        <Alert status="danger">
          <Alert.Indicator>
            <AlertCircle className="w-4 h-4" />
          </Alert.Indicator>
          <Alert.Content>
            <Alert.Title>Could not load step</Alert.Title>
            <Alert.Description>
              {error instanceof Error ? error.message : 'Please check your connection and try again.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/learning-path" className="inline-flex items-center gap-1.5 text-sm text-accent hover:underline">
          <ArrowLeft className="w-4 h-4" /> Back to learning path
        </Link>
      </div>
    );
  }

  const isDone = step.status === 'done';
  const isInProgress = step.status === 'in_progress';
  const isLocked = step.status === 'locked';

  return (
    <div className="max-w-3xl mx-auto py-6 space-y-6">
      <Link
        to="/learning-path"
        className="inline-flex items-center gap-1.5 text-xs text-muted hover:text-foreground no-underline font-mono"
      >
        <ArrowLeft className="w-3.5 h-3.5" /> Back to learning path
      </Link>

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

      <Card>
        <Card.Header className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 border-b border-border pb-4">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs uppercase font-bold text-accent">
                Week {step.weekNumber} · Step {step.weekSequence}
              </span>
              <span className="font-mono text-xs px-2 py-0.5 rounded bg-surface-secondary border border-border text-foreground font-semibold">
                {step.targetCefr}
              </span>
            </div>
            <h1 className="text-xl sm:text-2xl font-bold text-foreground m-0">{step.title}</h1>
          </div>
          <span
            className={`font-mono text-xs px-2.5 py-1 rounded font-semibold ${
              isDone
                ? 'bg-success/10 text-success border border-success/30'
                : isInProgress
                ? 'bg-warning/20 text-warning border border-warning/40'
                : isLocked
                ? 'bg-surface-secondary text-muted'
                : 'bg-accent/10 text-accent border border-accent/30'
            }`}
          >
            {formatPathStepStatus(step.status)}
          </span>
        </Card.Header>

        <Card.Content className="p-6 space-y-6">
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-xs font-mono">
            <div className="p-3 rounded bg-surface-secondary border border-border">
              <span className="text-muted block">Step type</span>
              <span className="font-bold text-foreground text-sm">{formatStepType(step.stepType)}</span>
            </div>
            <div className="p-3 rounded bg-surface-secondary border border-border">
              <span className="text-muted block">Skill</span>
              <span className="font-bold text-foreground text-sm">{step.skillName}</span>
            </div>
            <div className="p-3 rounded bg-surface-secondary border border-border">
              <span className="text-muted block">Topic code</span>
              <span className="font-bold text-foreground text-sm">{step.topicCode}</span>
            </div>
            <div className="p-3 rounded bg-surface-secondary border border-border">
              <span className="text-muted block">Est. duration</span>
              <span className="font-bold text-foreground text-sm">~{step.estimatedMinutes ?? 15}m</span>
            </div>
          </div>

          <div className="space-y-2">
            <h3 className="text-sm font-semibold text-foreground m-0">Topic Details</h3>
            <p className="text-sm text-muted m-0">
              {step.topicName} — Target level {step.targetCefr} ({step.sizingTier} tier).
            </p>
          </div>

          <div className="pt-4 border-t border-border flex items-center justify-between">
            <Link to="/learning-path" className="text-sm text-muted hover:text-foreground">
              Cancel
            </Link>

            {isLocked ? (
              <span className="text-xs text-muted font-mono flex items-center gap-1.5">
                <Lock className="w-3.5 h-3.5" /> Complete prior steps to unlock
              </span>
            ) : isDone ? (
              <Button
                variant="secondary"
                isDisabled={startStepMutation.isPending}
                onPress={() => startStepMutation.mutate()}
                className="gap-1.5"
              >
                <Play className="w-4 h-4" /> Review step
              </Button>
            ) : (
              <Button
                variant="primary"
                isDisabled={startStepMutation.isPending}
                onPress={() => startStepMutation.mutate()}
                className="gap-1.5"
              >
                {startStepMutation.isPending ? <Spinner size="sm" /> : <Play className="w-4 h-4" />}
                {isInProgress ? 'Resume step' : 'Start step'}
              </Button>
            )}
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
