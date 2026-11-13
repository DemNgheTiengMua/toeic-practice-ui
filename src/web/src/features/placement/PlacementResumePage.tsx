import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Alert, AlertDialog, Button, Card, Chip, ProgressBar, Spinner } from '@heroui/react';
import { AlertTriangle, ArrowRight, CheckCircle2, Clock, Trash2 } from 'lucide-react';
import { api, type PlacementResumeViewDto } from '@/lib/api';

export function PlacementResumePage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const placementId = Number(attemptId);
  const navigate = useNavigate();

  const [resume, setResume] = useState<PlacementResumeViewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isAbandonOpen, setIsAbandonOpen] = useState(false);
  const [abandoning, setAbandoning] = useState(false);

  useEffect(() => {
    if (!Number.isFinite(placementId) || placementId <= 0) {
      setError('Invalid placement identifier.');
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    api.placement
      .resumeInfo(placementId)
      .then((data) => {
        if (!cancelled) {
          setResume(data);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load sitting details.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [placementId]);

  const handleConfirmAbandon = async () => {
    setAbandoning(true);
    try {
      await api.placement.abandon(placementId);
      setIsAbandonOpen(false);
      await navigate({ to: '/placement' });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to abandon sitting.');
      setAbandoning(false);
    }
  };

  if (loading) {
    return (
      <div className="mx-auto max-w-3xl p-6 flex items-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading saved sitting progress...</span>
      </div>
    );
  }

  if (error && !resume) {
    return (
      <div className="mx-auto max-w-3xl p-6 space-y-4">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Sitting unavailable</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/placement" className="button button--sm button--outline">
          Return to placement catalog
        </Link>
      </div>
    );
  }

  if (!resume) return null;

  const progressPct =
    resume.totalQuestions > 0
      ? Math.round((resume.syncedAnswerCount / resume.totalQuestions) * 100)
      : 0;

  return (
    <div className="mx-auto max-w-3xl p-4 md:p-6 space-y-6">
      <header className="space-y-1.5 border-b border-border pb-4">
        <div className="flex items-center gap-2">
          <Chip size="sm" variant="soft" color="accent" className="font-display">
            {resume.certCode}
          </Chip>
          <span className="text-xs font-mono-tabular text-muted">
            Stage {resume.activeStage ?? 1} of {resume.totalStages}
          </span>
        </div>
        <h1 className="font-display text-2xl font-semibold text-foreground">{resume.examName}</h1>
        <p className="text-sm text-muted">
          Your responses are already saved. Resume right where you paused with no extra charge, or
          abandon this sitting cleanly.
        </p>
      </header>

      {error && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      <Card>
        <Card.Header className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <Card.Title className="font-display text-base">Saved progress summary</Card.Title>
            <Card.Description>
              {resume.activeSkillName
                ? `Active section: ${resume.activeSkillName}`
                : 'Ready to resume'}
            </Card.Description>
          </div>
          <div className="text-right font-mono-tabular">
            <span className="text-lg font-semibold text-foreground">
              {resume.syncedAnswerCount} / {resume.totalQuestions}
            </span>
            <p className="text-xs text-muted">synced responses</p>
          </div>
        </Card.Header>

        <Card.Content className="space-y-4">
          <ProgressBar
            aria-label="Placement sitting progress"
            value={progressPct}
            minValue={0}
            maxValue={100}
            size="sm"
            color="accent"
          >
            <ProgressBar.Track>
              <ProgressBar.Fill />
            </ProgressBar.Track>
          </ProgressBar>

          <div className="divide-y divide-separator rounded-lg border border-border bg-surface-secondary/40">
            {resume.skills.map((skill) => (
              <div
                key={skill.skillId}
                className="flex items-center justify-between gap-3 px-4 py-3 text-sm"
              >
                <div className="flex items-center gap-2.5">
                  {skill.isDone ? (
                    <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
                  ) : (
                    <Clock
                      className={`h-4 w-4 ${skill.isActive ? 'text-accent' : 'text-muted'}`}
                      aria-hidden="true"
                    />
                  )}
                  <span className="font-medium text-foreground">{skill.name}</span>
                  {skill.isActive && (
                    <Chip size="sm" variant="soft" color="accent">
                      Active
                    </Chip>
                  )}
                  {skill.isDone && (
                    <Chip size="sm" variant="soft" color="success">
                      Completed
                    </Chip>
                  )}
                </div>

                <span className="font-mono-tabular text-xs text-muted">
                  {skill.answeredCount} / {skill.totalQuestions} answered
                </span>
              </div>
            ))}
          </div>
        </Card.Content>

        <Card.Footer className="flex flex-wrap items-center justify-between gap-3 pt-4 border-t border-border">
          <Button
            variant="danger-soft"
            size="md"
            onPress={() => setIsAbandonOpen(true)}
          >
            <Trash2 className="h-4 w-4 mr-1.5" aria-hidden="true" />
            Abandon placement
          </Button>

          <Link
            to="/placement/take/$attemptId"
            params={{ attemptId: String(resume.placementId) }}
            className="button button--md button--primary"
          >
            Resume placement
            <ArrowRight className="h-4 w-4 ml-1.5" aria-hidden="true" />
          </Link>
        </Card.Footer>
      </Card>

      {isAbandonOpen && (
        <AlertDialog.Backdrop isOpen={isAbandonOpen} onOpenChange={setIsAbandonOpen}>
          <AlertDialog.Container>
            <AlertDialog.Dialog>
              <AlertDialog.Header>
                <AlertDialog.Icon status="danger">
                  <AlertTriangle className="h-5 w-5" aria-hidden="true" />
                </AlertDialog.Icon>
                <AlertDialog.Heading>Abandon in-progress placement?</AlertDialog.Heading>
              </AlertDialog.Header>
              <AlertDialog.Body>
                <p className="text-sm text-muted">
                  Abandoning discards this sitting without producing scores or a learning path
                  update.
                </p>
              </AlertDialog.Body>
              <AlertDialog.Footer>
                <Button variant="outline" onPress={() => setIsAbandonOpen(false)}>
                  Keep sitting
                </Button>
                <Button
                  variant="danger"
                  isPending={abandoning}
                  onPress={() => void handleConfirmAbandon()}
                >
                  Confirm abandon
                </Button>
              </AlertDialog.Footer>
            </AlertDialog.Dialog>
          </AlertDialog.Container>
        </AlertDialog.Backdrop>
      )}
    </div>
  );
}
