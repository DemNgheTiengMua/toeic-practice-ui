import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { CheckCircle2, Headphones, Mic, Volume2 } from 'lucide-react';
import {
  api,
  ApiClientError,
  type AttemptTakeDto,
  type PreflightStatusDto,
} from '@/lib/api';
import { ExamSittingShell } from '../sitting/ExamSittingShell';

export function MockExamTakePage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const search = useSearch({ strict: false }) as { q?: number | string };
  const navigate = useNavigate();

  const id = Number(attemptId);
  const initialRequestedOrder =
    search?.q != null && Number.isFinite(Number(search.q)) ? Number(search.q) : null;

  const [take, setTake] = useState<AttemptTakeDto | null>(null);
  const [preflightStatus, setPreflightStatus] = useState<PreflightStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<{ code?: string; message: string } | null>(null);
  const [verifyingPreflight, setVerifyingPreflight] = useState(false);

  const loadTake = useCallback(async () => {
    if (!Number.isFinite(id) || id <= 0) {
      setError({ message: 'Invalid mock exam sitting identifier.' });
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const data = await api.exams.take(id, initialRequestedOrder);
      if (data.status === 'done' || data.isAutoSubmitted) {
        await navigate({
          to: '/exams/result/$attemptId',
          params: { attemptId: String(id) },
        });
        return;
      }
      setTake(data);
      setPreflightStatus(null);
    } catch (err) {
      if (err instanceof ApiClientError && err.code === 'preflight_required') {
        try {
          const status = await api.exams.preflightStatus(id);
          setPreflightStatus(status);
        } catch {
          setError({
            code: err.code,
            message: err.detail || err.message,
          });
        }
      } else {
        setError({
          code: err instanceof ApiClientError ? err.code : undefined,
          message:
            err instanceof ApiClientError
              ? err.detail || err.message
              : err instanceof Error
                ? err.message
                : 'Unable to load mock exam sitting.',
        });
      }
    } finally {
      setLoading(false);
    }
  }, [id, initialRequestedOrder, navigate]);

  useEffect(() => {
    void loadTake();
  }, [loadTake]);

  const handleVerifyPreflight = async () => {
    if (!preflightStatus) return;
    setVerifyingPreflight(true);
    try {
      await api.exams.verifyPreflight(preflightStatus.attemptId);
      await loadTake();
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : 'Unable to confirm equipment check.',
      });
    } finally {
      setVerifyingPreflight(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-[60vh] flex items-center justify-center gap-3 text-muted p-6">
        <Spinner size="md" color="accent" />
        <span>Loading mock exam sitting...</span>
      </div>
    );
  }

  // Preflight Equipment Verification Gate
  if (preflightStatus) {
    return (
      <div className="mx-auto max-w-2xl p-4 sm:p-6 space-y-6">
        <div className="space-y-1 text-center">
          <Chip size="sm" color="accent" variant="soft" className="mb-2">
            Equipment verification
          </Chip>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Audio & equipment check
          </h1>
          <p className="text-sm text-muted">
            Before entering {preflightStatus.examName}, verify your headphones and microphone so you
            can hear listening prompts and record speaking responses without interruption.
          </p>
        </div>

        {error && (
          <Alert status="danger">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>Equipment check notice</Alert.Title>
              <Alert.Description>{error.message}</Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        <div className="space-y-4">
          {/* Audio Speakers Check */}
          {preflightStatus.requiresAudio && (
            <Card className="bg-surface border-border">
              <Card.Header className="flex flex-row items-center gap-3 pb-2">
                <div className="p-2 rounded-lg bg-accent/10 text-accent">
                  <Headphones className="size-5" />
                </div>
                <div>
                  <h2 className="text-base font-semibold text-foreground">Sound & headphones</h2>
                  <p className="text-xs text-muted">Ensure sound is audible at a comfortable volume.</p>
                </div>
              </Card.Header>
              <Card.Content className="pt-2 text-sm text-muted space-y-3">
                <p>
                  Click the test button below to ensure your speakers or headphones are playing
                  audio clearly:
                </p>
                <div className="flex items-center gap-3">
                  <audio
                    controls
                    preload="auto"
                    src="/audio/preflight-test.mp3"
                    className="h-10 w-full max-w-xs"
                  >
                    Your browser does not support audio playback.
                  </audio>
                </div>
              </Card.Content>
            </Card>
          )}

          {/* Microphone Check if Required */}
          {preflightStatus.requiresMicrophone && (
            <Card className="bg-surface border-border">
              <Card.Header className="flex flex-row items-center gap-3 pb-2">
                <div className="p-2 rounded-lg bg-accent/10 text-accent">
                  <Mic className="size-5" />
                </div>
                <div>
                  <h2 className="text-base font-semibold text-foreground">Microphone check</h2>
                  <p className="text-xs text-muted">Required for spoken responses in this exam.</p>
                </div>
              </Card.Header>
              <Card.Content className="pt-2 text-sm text-muted">
                <p>
                  Grant microphone permission when prompted by your browser. Speak a few words to
                  verify clear input levels.
                </p>
              </Card.Content>
            </Card>
          )}
        </div>

        <div className="pt-4 flex flex-col sm:flex-row items-center justify-between gap-3">
          <Button
            variant="secondary"
            size="md"
            onClick={() => void navigate({ to: '/exams' })}
          >
            Cancel sitting
          </Button>
          <Button
            variant="primary"
            size="lg"
            onClick={handleVerifyPreflight}
            isDisabled={verifyingPreflight}
            className="w-full sm:w-auto"
          >
            {verifyingPreflight ? (
              <>
                <Spinner size="sm" color="current" />
                <span>Confirming equipment...</span>
              </>
            ) : (
              <>
                <CheckCircle2 className="size-4 mr-1.5" />
                <span>Confirm equipment & start exam</span>
              </>
            )}
          </Button>
        </div>
      </div>
    );
  }

  // Error State
  if (error || !take) {
    return (
      <div className="mx-auto max-w-lg p-6 space-y-4">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Sitting unavailable</Alert.Title>
            <Alert.Description>
              {error?.message || 'Unable to access this mock exam sitting.'}
            </Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="flex items-center gap-3">
          <Button
            variant="primary"
            size="sm"
            onClick={() => void navigate({ to: '/exams' })}
          >
            Back to mock exams
          </Button>
          <Button variant="secondary" size="sm" onClick={() => void loadTake()}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  // Active Sitting Room hosted in ExamSittingShell
  return (
    <ExamSittingShell
      sittingKind="mock"
      sittingId={take.attemptId}
      attemptId={take.attemptId}
      examName={take.examName}
      certCode={take.certCode}
      initialTake={take}
      initialRequestedOrder={initialRequestedOrder}
      basePath={`/exams/take/${take.attemptId}`}
      onFetchQuestion={(order) => api.exams.take(id, order)}
      onSaveAnswer={(questionId, payload) => api.exams.saveAnswer(id, questionId, payload)}
      onSaveDraft={(questionId, responseText) =>
        api.exams.saveDraft(id, { questionId, responseText })
      }
      onUploadAudio={(questionId, blob, fileName, durationMs) =>
        api.speaking.uploadAudio(take.attemptId, questionId, blob, fileName, durationMs)
      }
      onSubmit={async () => {
        await api.exams.submit(id);
        await navigate({
          to: '/exams/result/$attemptId',
          params: { attemptId: String(id) },
        });
      }}
      onSaveExit={() => {
        void navigate({ to: '/exams' });
      }}
    />
  );
}
