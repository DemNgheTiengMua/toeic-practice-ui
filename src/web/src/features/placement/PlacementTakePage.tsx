import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Spinner } from '@heroui/react';
import { Headphones, Mic, CheckCircle2 } from 'lucide-react';
import {
  api,
  ApiClientError,
  type PlacementTakeViewDto,
  type PreflightStatusDto,
} from '@/lib/api';
import { ExamSittingShell } from '../sitting/ExamSittingShell';

export function PlacementTakePage() {
  const { attemptId } = useParams({ strict: false }) as { attemptId?: string };
  const search = useSearch({ strict: false }) as { q?: number | string };
  const navigate = useNavigate();

  const placementId = Number(attemptId);
  const initialRequestedOrder =
    search?.q != null && Number.isFinite(Number(search.q)) ? Number(search.q) : null;

  const [take, setTake] = useState<PlacementTakeViewDto | null>(null);
  const [preflightStatus, setPreflightStatus] = useState<PreflightStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<{ code?: string; message: string } | null>(null);
  const [verifyingPreflight, setVerifyingPreflight] = useState(false);

  const loadTake = useCallback(async () => {
    if (!Number.isFinite(placementId) || placementId <= 0) {
      setError({ message: 'Invalid placement sitting identifier.' });
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const data = await api.placement.take(placementId, initialRequestedOrder);
      if (data.status === 'done' || data.isAutoSubmitted) {
        await navigate({
          to: '/placement/result/$attemptId',
          params: { attemptId: String(placementId) },
        });
        return;
      }
      setTake(data);
      setPreflightStatus(null);
    } catch (err) {
      if (err instanceof ApiClientError && err.code === 'preflight_required') {
        try {
          const status = await api.placement.preflightStatus(placementId);
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
                : 'Unable to load placement sitting.',
        });
      }
    } finally {
      setLoading(false);
    }
  }, [placementId, initialRequestedOrder, navigate]);

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
        <span>Loading placement sitting...</span>
      </div>
    );
  }

  if (preflightStatus) {
    return (
      <div className="mx-auto max-w-2xl p-6 space-y-5">
        <Card>
          <Card.Header>
            <div className="flex items-center gap-2">
              <Chip size="sm" variant="soft" color="accent">
                {preflightStatus.certCode}
              </Chip>
              <Chip size="sm" variant="soft" color="warning">
                Equipment check required
              </Chip>
            </div>
            <Card.Title className="font-display text-xl mt-2">
              {preflightStatus.examName}
            </Card.Title>
            <Card.Description>
              Confirm your audio environment before opening active questions.
            </Card.Description>
          </Card.Header>
          <Card.Content className="space-y-3 text-sm">
            {preflightStatus.requiresAudio && (
              <div className="flex items-center gap-2 text-foreground">
                <Headphones className="h-4 w-4 text-accent" aria-hidden="true" />
                <span>Headphones or speakers checked for Listening sections.</span>
              </div>
            )}
            {preflightStatus.requiresMicrophone && (
              <div className="flex items-center gap-2 text-foreground">
                <Mic className="h-4 w-4 text-accent" aria-hidden="true" />
                <span>Microphone ready for Speaking sections.</span>
              </div>
            )}
          </Card.Content>
          <Card.Footer className="flex items-center justify-between gap-3 pt-4 border-t border-border">
            <Link to="/placement" className="button button--sm button--outline">
              Back
            </Link>
            <Button
              variant="primary"
              isPending={verifyingPreflight}
              onPress={() => void handleVerifyPreflight()}
            >
              <CheckCircle2 className="h-4 w-4 mr-1.5" aria-hidden="true" />
              Confirm ready &amp; begin
            </Button>
          </Card.Footer>
        </Card>
      </div>
    );
  }

  if (error) {
    return (
      <div className="mx-auto max-w-2xl p-6 space-y-4">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Sitting unavailable</Alert.Title>
            <Alert.Description>{error.message}</Alert.Description>
          </Alert.Content>
        </Alert>
        <div className="flex items-center gap-2">
          {error.code === 'placement_not_active' && (
            <Link
              to="/placement/result/$attemptId"
              params={{ attemptId: String(placementId) }}
              className="button button--sm button--primary"
            >
              View placement result
            </Link>
          )}
          <Link to="/placement" className="button button--sm button--outline">
            Placement catalog
          </Link>
        </div>
      </div>
    );
  }

  if (!take) return null;

  return (
    <ExamSittingShell
      sittingKind="placement"
      sittingId={take.placementId}
      attemptId={take.attemptId}
      examName={take.examName}
      certCode={take.certCode}
      initialTake={take}
      initialRequestedOrder={initialRequestedOrder}
      basePath={`/placement/take/${take.placementId}`}
      onFetchQuestion={(displayOrder) => api.placement.take(take.placementId, displayOrder)}
      onSaveAnswer={(questionId, payload) =>
        api.placement.saveAnswer(take.placementId, questionId, payload)
      }
      onSaveDraft={(questionId, responseText) =>
        api.placement.saveDraft(take.placementId, { questionId, responseText })
      }
      onUploadAudio={(questionId, blob, fileName, durationMs) =>
        api.speaking.uploadAudio(take.attemptId, questionId, blob, fileName, durationMs)
      }
      onRecordInterruption={(payload) =>
        api.placement.recordInterruption(take.placementId, payload)
      }
      onSubmit={async (isAutoSubmit) => {
        await api.placement.submit(take.placementId, isAutoSubmit);
        await navigate({
          to: '/placement/result/$attemptId',
          params: { attemptId: String(take.placementId) },
        });
      }}
      onSaveExit={() => {
        void navigate({
          to: '/placement/resume/$attemptId',
          params: { attemptId: String(take.placementId) },
        });
      }}
    />
  );
}
