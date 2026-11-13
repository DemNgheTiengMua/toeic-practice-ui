import { useEffect, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Radio, RadioGroup, Label, Spinner } from '@heroui/react';
import { Award, Clock, Play, RotateCcw, Sparkles, Wallet } from 'lucide-react';
import {
  api,
  ApiClientError,
  type PlacementCatalogViewDto,
  type PlacementScopeCode,
} from '@/lib/api';

function makeIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `plc-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export function PlacementCatalogPage() {
  const navigate = useNavigate();
  const [catalog, setCatalog] = useState<PlacementCatalogViewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedScope, setSelectedScope] = useState<PlacementScopeCode>('all_skills');
  const [startingExamId, setStartingExamId] = useState<number | null>(null);
  const [idempotencyKey] = useState<string>(() => makeIdempotencyKey());

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api.placement
      .catalog()
      .then((data) => {
        if (!cancelled) {
          setCatalog(data);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load placement catalog.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleStartFirstPlacement = async (examId: number) => {
    setStartingExamId(examId);
    setError(null);
    try {
      const started = await api.placement.start({
        examId,
        reuseValidSkills: false,
        scope: selectedScope,
        idempotencyKey,
      });
      await navigate({
        to: '/placement/take/$attemptId',
        params: { attemptId: String(started.placementId) },
      });
    } catch (err) {
      if (err instanceof ApiClientError && err.code === 'insufficient_credits') {
        setError('Insufficient credits for this placement scope. Top up your wallet to continue.');
      } else {
        setError(err instanceof Error ? err.message : 'Unable to start placement test.');
      }
      setStartingExamId(null);
    }
  };

  if (loading) {
    return (
      <div className="mx-auto max-w-5xl p-6 flex items-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading placement catalog...</span>
      </div>
    );
  }

  if (error && !catalog) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to load placement catalog</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  if (!catalog) return null;

  return (
    <div className="mx-auto max-w-5xl p-4 md:p-6 space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-5">
        <div className="space-y-1.5">
          <div className="flex items-center gap-2">
            <Chip size="sm" variant="soft" color="accent" className="font-display">
              {catalog.activeCert.code}
            </Chip>
            {!catalog.isRetake ? (
              <Chip size="sm" variant="soft" color="success">
                Free first placement
              </Chip>
            ) : (
              <Chip size="sm" variant="soft" color="default">
                Retake ready
              </Chip>
            )}
          </div>
          <h1 className="font-display text-2xl font-semibold text-foreground">
            Diagnostic placement
          </h1>
          <p className="text-sm text-muted max-w-2xl">
            Measure your starting proficiency across {catalog.activeCert.name} skills so your
            learning path targets the right gap.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {catalog.currentBandCode && (
            <div className="rounded-lg border border-border bg-surface px-4 py-2 text-right">
              <p className="text-xs text-muted">Current assessed band</p>
              <p className="font-display text-lg font-semibold text-accent font-mono-tabular">
                {catalog.currentBandCode}
                {catalog.currentBandName ? ` · ${catalog.currentBandName}` : ''}
              </p>
            </div>
          )}
          {catalog.creditBalance != null && (
            <div className="rounded-lg border border-border bg-surface px-4 py-2 text-right">
              <p className="text-xs text-muted flex items-center justify-end gap-1">
                <Wallet className="h-3.5 w-3.5" aria-hidden="true" />
                Wallet balance
              </p>
              <p className="font-mono-tabular text-lg font-semibold text-foreground">
                {catalog.creditBalance} cr
              </p>
            </div>
          )}
        </div>
      </header>

      {error && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Action required</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {catalog.activePlacementId && (
        <Alert status="accent">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>In-progress placement sitting detected</Alert.Title>
            <Alert.Description>
              Resume your open sitting or abandon it cleanly before starting a new one.
            </Alert.Description>
          </Alert.Content>
          <div className="flex items-center gap-2 shrink-0">
            <Link
              to="/placement/resume/$attemptId"
              params={{ attemptId: String(catalog.activePlacementId) }}
              className="button button--sm button--primary"
            >
              Resume sitting
            </Link>
          </div>
        </Alert>
      )}

      {!catalog.isRetake && !catalog.activePlacementId && (
        <Card variant="secondary">
          <Card.Header>
            <Card.Title className="text-base font-display flex items-center gap-2">
              <Sparkles className="h-4 w-4 text-success" aria-hidden="true" />
              Choose placement scope
            </Card.Title>
            <Card.Description>
              Your first placement test for {catalog.activeCert.code} is free regardless of scope.
            </Card.Description>
          </Card.Header>
          <Card.Content>
            <RadioGroup
              aria-label="Placement skill scope"
              value={selectedScope}
              onChange={(val) => setSelectedScope(val as PlacementScopeCode)}
              className="grid grid-cols-1 md:grid-cols-2 gap-3"
            >
              <Radio
                value="all_skills"
                className="rounded-lg border border-border bg-surface p-4"
              >
                <Radio.Control>
                  <Radio.Indicator />
                </Radio.Control>
                <Radio.Content>
                  <Label className="font-medium text-foreground">All 4 skills (Recommended)</Label>
                  <p className="text-xs text-muted mt-0.5">
                    Listening, Reading, Writing, and Speaking.
                  </p>
                </Radio.Content>
              </Radio>
              <Radio
                value="listening_and_reading"
                className="rounded-lg border border-border bg-surface p-4"
              >
                <Radio.Control>
                  <Radio.Indicator />
                </Radio.Control>
                <Radio.Content>
                  <Label className="font-medium text-foreground">Listening + Reading only</Label>
                  <p className="text-xs text-muted mt-0.5">
                    Writing and Speaking remain unmeasured until a full sitting.
                  </p>
                </Radio.Content>
              </Radio>
            </RadioGroup>
          </Card.Content>
        </Card>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {catalog.exams.map((exam) => (
          <Card key={exam.examId}>
            <Card.Header className="space-y-1.5">
              <div className="flex items-center justify-between gap-2">
                <Chip size="sm" variant="soft" color="accent">
                  {exam.certCode}
                </Chip>
                {exam.isRecommended && (
                  <Chip size="sm" variant="soft" color="success">
                    Recommended
                  </Chip>
                )}
              </div>
              <Card.Title className="font-display text-lg">{exam.name}</Card.Title>
              {exam.description && <Card.Description>{exam.description}</Card.Description>}
            </Card.Header>

            <Card.Content className="flex flex-wrap items-center gap-4 text-sm text-muted font-mono-tabular">
              <span className="inline-flex items-center gap-1.5">
                <Clock className="h-4 w-4 text-accent" aria-hidden="true" />
                {exam.durationMinutes} min
              </span>
              <span className="inline-flex items-center gap-1.5">
                <Award className="h-4 w-4 text-accent" aria-hidden="true" />
                {exam.questionCount} questions
              </span>
            </Card.Content>

            <Card.Footer className="flex flex-wrap items-center justify-between gap-2 pt-3 border-t border-separator">
              {catalog.isRetake ? (
                <>
                  <span className="text-xs text-muted font-mono-tabular">
                    Full: {catalog.allSkillsRetakeCredits ?? 5} cr · L+R:{' '}
                    {catalog.listeningAndReadingRetakeCredits ?? 3} cr
                  </span>
                  <Link
                    to="/placement/retake/$certId"
                    params={{ certId: String(catalog.exams[0]?.certId ?? catalog.activeCert.code) }}
                    className="button button--sm button--primary"
                  >
                    <RotateCcw className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                    Retake options
                  </Link>
                </>
              ) : (
                <>
                  <Link to="/placement/preflight" className="text-xs text-accent hover:underline">
                    Check audio &amp; reuse options
                  </Link>
                  <Button
                    size="sm"
                    variant="primary"
                    isDisabled={Boolean(catalog.activePlacementId)}
                    isPending={startingExamId === exam.examId}
                    onPress={() => void handleStartFirstPlacement(exam.examId)}
                  >
                    <Play className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                    Start free placement
                  </Button>
                </>
              )}
            </Card.Footer>
          </Card>
        ))}
      </div>
    </div>
  );
}
