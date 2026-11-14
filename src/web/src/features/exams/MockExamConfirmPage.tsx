import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner, Table } from '@heroui/react';
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Clock,
  FileText,
  Lock,
  Play,
  Sparkles,
  Wallet,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  type MockExamInstructionsDto,
  type MockExamSectionInfoDto,
} from '@/lib/api';

export function MockExamConfirmPage() {
  const { examId } = useParams({ strict: false }) as { examId?: string };
  const navigate = useNavigate();

  const id = Number(examId);

  const [instructions, setInstructions] = useState<MockExamInstructionsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sittingMode, setSittingMode] = useState<'full' | 'section'>('full');
  const [selectedSectionCode, setSelectedSectionCode] = useState<string>('');
  const [isStarting, setIsStarting] = useState(false);

  useEffect(() => {
    if (!Number.isFinite(id) || id <= 0) {
      setError('Invalid mock exam identifier.');
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    api.exams
      .instructions(id)
      .then((data) => {
        if (!cancelled) {
          setInstructions(data);
          if (data.sections.length > 0) {
            setSelectedSectionCode(data.sections[0].sectionCode);
          }
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load exam instructions.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  const handleStartFullExam = async () => {
    if (!instructions) return;
    setIsStarting(true);
    setError(null);

    try {
      const response = await api.exams.start(instructions.examId);
      await navigate({
        to: '/exams/take/$attemptId',
        params: { attemptId: String(response.attemptId) },
      });
    } catch (err) {
      if (err instanceof ApiClientError && (err.code === 'insufficient_credits' || err.status === 402)) {
        setError(
          'Insufficient credits to start this mock exam. Please top up your wallet to continue.',
        );
      } else {
        setError(err instanceof Error ? err.message : 'Failed to start mock exam.');
      }
      setIsStarting(false);
    }
  };

  if (loading) {
    return (
      <div className="mx-auto max-w-4xl p-6 flex items-center justify-center gap-3 text-muted min-h-[50vh]">
        <Spinner size="md" color="accent" />
        <span>Loading exam details...</span>
      </div>
    );
  }

  if (error && !instructions) {
    return (
      <div className="mx-auto max-w-4xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Exam not found</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="mt-4">
          <Button variant="secondary" size="sm" onClick={() => void navigate({ to: '/exams' })}>
            Back to mock exams
          </Button>
        </div>
      </div>
    );
  }

  if (!instructions) return null;

  const hasCredits = instructions.creditBalance >= instructions.creditCost;
  const totalQuestions =
    instructions.questionCount ??
    instructions.sections.reduce((sum, s) => sum + (s.questionCount || s.expectedQuestionCount), 0);

  return (
    <div className="mx-auto max-w-4xl p-4 sm:p-6 space-y-6">
      {/* Breadcrumbs */}
      <nav className="flex items-center gap-2 text-xs text-muted" aria-label="Breadcrumb">
        <Link to="/dashboard" className="hover:text-foreground transition-colors">
          Home
        </Link>
        <span aria-hidden="true">/</span>
        <Link to="/exams" className="hover:text-foreground transition-colors">
          Mock exams
        </Link>
        <span aria-hidden="true">/</span>
        <span className="text-foreground font-medium" aria-current="page">
          Start confirmation
        </span>
      </nav>

      {/* Header */}
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <Chip size="sm" variant="secondary" className="font-semibold">
            {instructions.certCode}
          </Chip>
          <span className="text-xs text-muted">•</span>
          <span className="text-xs text-muted font-mono-tabular">
            {instructions.durationMinutes} minutes
          </span>
          <span className="text-xs text-muted">•</span>
          <span className="text-xs text-muted font-mono-tabular">
            {totalQuestions} questions
          </span>
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-foreground">
          Before you begin: {instructions.examName}
        </h1>
        {instructions.description && (
          <p className="text-sm text-muted mt-1 max-w-2xl">{instructions.description}</p>
        )}
      </div>

      {/* Credit Balance Placard */}
      <Card className="bg-surface border-border shadow-surface">
        <Card.Content className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 p-4">
          <div className="flex items-center gap-3">
            <div
              className={`p-2.5 rounded-lg ${
                hasCredits ? 'bg-accent/10 text-accent' : 'bg-danger/10 text-danger'
              }`}
            >
              <Wallet className="size-6" />
            </div>
            <div>
              <div className="text-xs text-muted font-medium">Your credit balance</div>
              <div className="text-lg font-bold font-mono-tabular text-foreground">
                {instructions.creditBalance}{' '}
                <span className="text-sm font-normal text-muted">
                  {instructions.creditBalance === 1 ? 'credit' : 'credits'} available
                </span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-4 sm:border-l sm:border-separator/40 sm:pl-6">
            <div>
              <div className="text-xs text-muted font-medium">Full exam cost</div>
              <div className="text-lg font-bold font-mono-tabular text-foreground">
                {instructions.creditCost}{' '}
                <span className="text-sm font-normal text-muted">
                  {instructions.creditCost === 1 ? 'credit' : 'credits'}
                </span>
              </div>
            </div>

            {hasCredits ? (
              <Chip color="success" variant="soft" size="md">
                <CheckCircle2 className="size-3.5 mr-1" />
                Credits available
              </Chip>
            ) : (
              <Chip color="danger" variant="soft" size="md">
                <Lock className="size-3.5 mr-1" />
                Insufficient credits
              </Chip>
            )}
          </div>
        </Card.Content>
      </Card>

      {/* Inline Error if starting fails */}
      {error && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Cannot start sitting</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Insufficient Credits Block */}
      {!hasCredits && (
        <Alert status="warning" className="border-warning/30 bg-warning/10 text-foreground">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Top up required for full mock exam</Alert.Title>
            <Alert.Description>
              This mock exam costs {instructions.creditCost} credit(s), but your current balance is{' '}
              {instructions.creditBalance}. Top up your wallet to start the full exam, or practice
              individual sections for free.
            </Alert.Description>
            <div className="flex flex-wrap items-center gap-3 mt-3">
              <Button
                variant="primary"
                size="sm"
                onClick={() => void navigate({ to: '/checkout', search: { credits: 1, returnUrl: '/exams' } as any })}
              >
                <Wallet className="size-4 mr-1.5" />
                Top up wallet
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void navigate({ to: '/practice' })}
              >
                Practice free by section
              </Button>
            </div>
          </Alert.Content>
        </Alert>
      )}

      {/* Sitting Choice Selector: Full Exam vs Single Section */}
      <div className="space-y-3">
        <h2 className="text-base font-semibold text-foreground">Select sitting option</h2>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Full Exam Option */}
          <Card
            className={`cursor-pointer transition-all border-2 ${
              sittingMode === 'full'
                ? 'border-accent bg-accent/5 shadow-surface'
                : 'border-border bg-surface hover:border-border/80'
            }`}
            onClick={() => setSittingMode('full')}
          >
            <Card.Header className="flex flex-row items-start justify-between pb-2">
              <div className="space-y-1">
                <h3 className="font-semibold text-base text-foreground">Full mock exam</h3>
                <p className="text-xs text-muted">
                  Strict exam timing, auto-submission, scaled scoring & CEFR band result.
                </p>
              </div>
              <Chip size="sm" color="accent" variant="soft" className="font-mono-tabular">
                {instructions.creditCost} {instructions.creditCost === 1 ? 'credit' : 'credits'}
              </Chip>
            </Card.Header>
            <Card.Content className="text-xs text-muted space-y-1 pb-4">
              <div>• All sections delivered consecutively</div>
              <div>• Tabular countdown timer with automatic submit on expiry</div>
              <div>• Comprehensive score report and item-by-item review</div>
            </Card.Content>
          </Card>

          {/* Single Section Practice Option */}
          <Card
            className={`cursor-pointer transition-all border-2 ${
              sittingMode === 'section'
                ? 'border-accent bg-accent/5 shadow-surface'
                : 'border-border bg-surface hover:border-border/80'
            }`}
            onClick={() => setSittingMode('section')}
          >
            <Card.Header className="flex flex-row items-start justify-between pb-2">
              <div className="space-y-1">
                <h3 className="font-semibold text-base text-foreground">
                  Single section practice
                </h3>
                <p className="text-xs text-muted">
                  Practice individual sections at your own pace without credit charges.
                </p>
              </div>
              <Chip size="sm" color="success" variant="soft">
                Free
              </Chip>
            </Card.Header>
            <Card.Content className="text-xs text-muted space-y-1 pb-4">
              <div>• Untimed or targeted drill mode</div>
              <div>• Instant feedback, rationale, and question bookmarking</div>
              <div>• Never consumes credit wallet balance</div>
            </Card.Content>
          </Card>
        </div>
      </div>

      {/* Section Selection for Single Section Practice */}
      {sittingMode === 'section' && instructions.sections.length > 0 && (
        <Card className="bg-surface border-border">
          <Card.Header>
            <Card.Title className="text-sm font-semibold">
              Choose section to practice
            </Card.Title>
          </Card.Header>
          <Card.Content className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              {instructions.sections.map((section) => (
                <button
                  type="button"
                  key={section.sectionId}
                  onClick={() => setSelectedSectionCode(section.sectionCode)}
                  className={`p-3 text-left rounded-lg border transition-all text-sm flex items-center justify-between ${
                    selectedSectionCode === section.sectionCode
                      ? 'border-accent bg-accent/10 text-foreground font-medium'
                      : 'border-border bg-surface-secondary text-muted hover:text-foreground hover:bg-surface'
                  }`}
                >
                  <div>
                    <span className="font-semibold mr-1.5">{section.sectionCode}</span>
                    <span>{section.sectionName}</span>
                  </div>
                  <span className="text-xs font-mono-tabular opacity-75">
                    {section.questionCount || section.expectedQuestionCount} Qs
                  </span>
                </button>
              ))}
            </div>

            <div className="pt-2">
              <Button
                variant="primary"
                size="md"
                onClick={() => void navigate({ to: '/practice' })}
              >
                <Sparkles className="size-4 mr-1.5" />
                Go to section practice
              </Button>
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Exam Blueprint Structure Table */}
      <div className="space-y-3">
        <h2 className="text-base font-semibold text-foreground">Exam structure</h2>
        <Card className="bg-surface border-border overflow-hidden">
          <Table.ScrollContainer>
            <Table.Content aria-label="Exam structure sections">
              <Table.Header>
                <Table.Column isRowHeader>Skill</Table.Column>
                <Table.Column>Section</Table.Column>
                <Table.Column>Questions</Table.Column>
                <Table.Column>Duration</Table.Column>
              </Table.Header>
              <Table.Body>
                {instructions.sections.map((section: MockExamSectionInfoDto) => (
                  <Table.Row key={section.sectionId}>
                    <Table.Cell className="font-medium text-foreground text-sm">
                      {section.skillName}
                    </Table.Cell>
                    <Table.Cell className="text-sm text-muted">
                      <span className="font-semibold text-foreground mr-1.5">
                        {section.sectionCode}
                      </span>
                      {section.sectionName}
                    </Table.Cell>
                    <Table.Cell className="font-mono-tabular text-sm text-muted">
                      {section.questionCount || section.expectedQuestionCount}
                    </Table.Cell>
                    <Table.Cell className="font-mono-tabular text-sm text-muted">
                      {section.durationMinutes > 0 ? `${section.durationMinutes} mins` : '--'}
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Content>
          </Table.ScrollContainer>
        </Card>
      </div>

      {/* Important Sitting Notice */}
      <Card className="bg-surface-secondary border-border p-4 text-xs text-muted space-y-2">
        <div className="font-semibold text-foreground text-sm flex items-center gap-1.5">
          <AlertTriangle className="size-4 text-warning" />
          Sitting instructions & conditions
        </div>
        <ul className="list-disc pl-5 space-y-1">
          <li>
            The exam timer starts immediately upon entering the sitting room and counts down in real
            time.
          </li>
          <li>
            When remaining time reaches zero, your answers are automatically locked and submitted.
          </li>
          <li>
            Standard browser keys and navigation are fully supported; there are no intrusive
            proctoring traps or popup blockers.
          </li>
          <li>
            If you lose connection, your answers are preserved locally and synced automatically when
            connectivity resumes.
          </li>
        </ul>
      </Card>

      {/* Action Bar */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-4 pt-2">
        <Button
          variant="secondary"
          size="md"
          onClick={() => void navigate({ to: '/exams' })}
        >
          Back to catalog
        </Button>

        {sittingMode === 'full' ? (
          hasCredits ? (
            <Button
              variant="primary"
              size="lg"
              onClick={handleStartFullExam}
              isDisabled={isStarting}
              className="w-full sm:w-auto"
            >
              {isStarting ? (
                <>
                  <Spinner size="sm" color="current" />
                  <span>Starting sitting...</span>
                </>
              ) : (
                <>
                  <Play className="size-4 mr-1.5" />
                  <span>Start mock exam</span>
                  <ArrowRight className="size-4 ml-1.5" />
                </>
              )}
            </Button>
          ) : (
            <Button
              variant="primary"
              size="lg"
              onClick={() => void navigate({ to: '/checkout', search: { credits: 1, returnUrl: '/exams' } as any })}
              className="w-full sm:w-auto"
            >
              <Wallet className="size-4 mr-1.5" />
              Top up wallet to start
            </Button>
          )
        ) : (
          <Button
            variant="primary"
            size="lg"
            onClick={() => void navigate({ to: '/practice' })}
            className="w-full sm:w-auto"
          >
            <Sparkles className="size-4 mr-1.5" />
            Continue to free practice
          </Button>
        )}
      </div>
    </div>
  );
}
