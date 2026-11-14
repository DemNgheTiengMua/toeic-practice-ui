import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import {
  Alert,
  Button,
  Card,
  Chip,
  Label,
  Radio,
  RadioGroup,
  Spinner,
  ToggleButton,
} from '@heroui/react';
import {
  ArrowLeft,
  CheckCircle2,
  Headphones,
  Mic,
  Play,
  RefreshCw,
  Wallet,
} from 'lucide-react';
import {
  api,
  ApiClientError,
  type PlacementPreflightViewDto,
  type PlacementScopeCode,
} from '@/lib/api';
import { computePlacementScopeOffers } from '../sitting/sitting-helpers';

function makeIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `retake-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export function PlacementRetakePage() {
  const navigate = useNavigate();
  const [preflight, setPreflight] = useState<PlacementPreflightViewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedScope, setSelectedScope] = useState<PlacementScopeCode>('all_skills');
  const [reuseValidSkills, setReuseValidSkills] = useState(true);
  const [micStatus, setMicStatus] = useState<'unchecked' | 'ready' | 'failed'>('unchecked');
  const [submitting, setSubmitting] = useState(false);
  const [idempotencyKey] = useState<string>(() => makeIdempotencyKey());

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api.placement
      .preflightGate()
      .then((data) => {
        if (!cancelled) {
          setPreflight(data);
          setReuseValidSkills(data.hasReusableSkills);
          setError(null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unable to load placement preflight.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const scopeOffers = useMemo(
    () => (preflight ? computePlacementScopeOffers(preflight) : []),
    [preflight],
  );

  const activeOffer = useMemo(
    () => scopeOffers.find((o) => o.scope === selectedScope) ?? scopeOffers[0],
    [scopeOffers, selectedScope],
  );
  const retakeCredits = activeOffer?.creditCost ?? 0;

  const reusableOffers = useMemo(
    () => (preflight?.offers ?? []).filter((o) => o.canReuse),
    [preflight],
  );

  const testMicrophone = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setMicStatus('failed');
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      for (const track of stream.getTracks()) {
        track.stop();
      }
      setMicStatus('ready');
    } catch {
      setMicStatus('failed');
    }
  }, []);

  const handleConfirmStart = async () => {
    if (!preflight?.examId || !activeOffer?.canAfford || !preflight.isStartable) {
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const res = await api.placement.start({
        examId: preflight.examId,
        reuseValidSkills: preflight.hasReusableSkills ? reuseValidSkills : false,
        scope: selectedScope,
        idempotencyKey,
      });
      await navigate({
        to: '/placement/take/$attemptId',
        params: { attemptId: String(res.placementId) },
      });
    } catch (err) {
      if (err instanceof ApiClientError && err.code === 'insufficient_credits') {
        setError('Insufficient credits for the selected placement scope.');
      } else if (err instanceof ApiClientError && err.code === 'placement_cooldown') {
        setError('Retake cooldown is still active for this certificate.');
      } else {
        setError(err instanceof Error ? err.message : 'Failed to start placement sitting.');
      }
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="mx-auto max-w-4xl p-6 flex items-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Checking placement eligibility &amp; credits...</span>
      </div>
    );
  }

  if (error && !preflight) {
    return (
      <div className="mx-auto max-w-4xl p-6">
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Preflight check failed</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      </div>
    );
  }

  if (!preflight) return null;

  const isInsufficient = activeOffer ? !activeOffer.canAfford : false;
  const startBlocked =
    !preflight.isStartable || !preflight.examId || isInsufficient || submitting;

  return (
    <div className="mx-auto max-w-4xl p-4 md:p-6 space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4 border-b border-border pb-4">
        <div className="space-y-1">
          <Link
            to="/placement"
            className="inline-flex items-center gap-1 text-xs text-muted hover:text-foreground"
          >
            <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
            Back to placement catalog
          </Link>
          <div className="flex items-center gap-2">
            <h1 className="font-display text-2xl font-semibold text-foreground">
              {preflight.isRetake ? 'Configure placement retake' : 'Placement readiness check'}
            </h1>
            <Chip size="sm" variant="soft" color="accent">
              {preflight.certCode}
            </Chip>
          </div>
        </div>

        <div className="rounded-lg border border-border bg-surface px-4 py-2 text-right">
          <p className="text-xs text-muted flex items-center justify-end gap-1">
            <Wallet className="h-3.5 w-3.5" aria-hidden="true" />
            Credit balance
          </p>
          <p className="font-mono-tabular text-lg font-semibold text-foreground">
            {preflight.creditBalance ?? 0} credits
          </p>
        </div>
      </div>

      {!preflight.isStartable && preflight.cooldownUntil && (
        <Alert status="warning">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Retake cooldown active</Alert.Title>
            <Alert.Description>
              Next placement retake opens at{' '}
              <span className="font-mono-tabular">
                {new Date(preflight.cooldownUntil).toLocaleString()}
              </span>
              .
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {preflight.cooldownWaived && (
        <Alert status="accent">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Cooldown waived</Alert.Title>
            <Alert.Description>
              Your previous sitting experienced a technical or grading issue, so cooldown was
              waived.
            </Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {error && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Cannot start sitting</Alert.Title>
            <Alert.Description>{error}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {/* Scope Selection */}
      <Card>
        <Card.Header>
          <Card.Title className="font-display text-lg">Choose measurement scope</Card.Title>
          <Card.Description>
            Compare skill coverage and credit deduction before starting your sitting.
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <RadioGroup
            aria-label="Placement retake scope"
            value={selectedScope}
            onChange={(val) => setSelectedScope(val as PlacementScopeCode)}
            className="space-y-3"
          >
            {scopeOffers.map((offer) => (
              <Radio
                key={offer.scope}
                value={offer.scope}
                className="rounded-lg border border-border bg-surface-secondary/50 p-4 items-start"
              >
                <Radio.Control className="mt-1">
                  <Radio.Indicator />
                </Radio.Control>
                <Radio.Content className="flex-1 space-y-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <Label className="font-semibold text-foreground cursor-pointer">
                      {offer.title}
                    </Label>
                    <div className="flex items-center gap-2 font-mono-tabular">
                      <Chip
                        size="sm"
                        variant="soft"
                        color={offer.isFree ? 'success' : offer.canAfford ? 'accent' : 'danger'}
                      >
                        {offer.isFree ? 'Free' : `${offer.creditCost} credits`}
                      </Chip>
                      {!offer.canAfford && (
                        <Chip size="sm" variant="soft" color="danger">
                          Insufficient credits
                        </Chip>
                      )}
                    </div>
                  </div>
                  <p className="text-xs text-muted">{offer.description}</p>
                  <div className="text-xs space-y-1">
                    <p className="text-foreground">
                      <span className="font-medium">Measured:</span>{' '}
                      {offer.includedSkills.join(', ')}
                    </p>
                    {offer.excludedSkills.length > 0 && (
                      <p className="text-warning font-medium">
                        Left unmeasured: {offer.excludedSkills.join(', ')}
                      </p>
                    )}
                  </div>
                </Radio.Content>
              </Radio>
            ))}
          </RadioGroup>
        </Card.Content>
      </Card>

      {/* Insufficient Credits Block linking directly to /wallet */}
      {isInsufficient && activeOffer && (
        <Alert status="danger">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Insufficient credits for selected scope</Alert.Title>
            <Alert.Description>
              This option requires{' '}
              <strong className="font-mono-tabular">{activeOffer.creditCost} credits</strong>, and
              your balance is{' '}
              <strong className="font-mono-tabular">{preflight.creditBalance ?? 0}</strong>. No
              attempt or ledger debit will happen until you have enough credits.
            </Alert.Description>
          </Alert.Content>
          <div className="flex items-center gap-2 shrink-0">
            <Link
              to="/checkout"
              search={{ credits: retakeCredits, returnUrl: '/placement/retake' } as any}
              className="button button--sm button--primary"
            >
              Top up wallet
            </Link>
            <Link to="/pricing" className="button button--sm button--outline">
              Credit packs
            </Link>
          </div>
        </Alert>
      )}

      {/* Carry-over reusable skills */}
      {preflight.hasReusableSkills && reusableOffers.length > 0 && (
        <Card variant="secondary">
          <Card.Header className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <Card.Title className="font-display text-base">Carry over valid skill scores</Card.Title>
              <Card.Description>
                Recently measured skills can be carried over with their original measurement date.
              </Card.Description>
            </div>
            <ToggleButton
              size="sm"
              variant="default"
              isSelected={reuseValidSkills}
              onChange={setReuseValidSkills}
            >
              {reuseValidSkills ? 'Carry-over enabled' : 'Re-measure all skills'}
            </ToggleButton>
          </Card.Header>
          <Card.Content className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {reusableOffers.map((skill) => (
              <div
                key={skill.skillId}
                className="flex items-center justify-between rounded-md border border-border bg-surface px-3 py-2 text-sm"
              >
                <div>
                  <p className="font-medium text-foreground">{skill.skillName}</p>
                  <p className="text-xs text-muted font-mono-tabular">
                    Measured{' '}
                    {skill.measuredAt ? new Date(skill.measuredAt).toLocaleDateString() : 'recently'}
                  </p>
                </div>
                {skill.levelCode && (
                  <Chip size="sm" variant="soft" color="accent" className="font-mono-tabular">
                    {skill.levelCode}
                  </Chip>
                )}
              </div>
            ))}
          </Card.Content>
        </Card>
      )}

      {/* Device Readiness */}
      {(preflight.requiresAudio || preflight.requiresMicrophone) && (
        <Card variant="secondary">
          <Card.Header>
            <Card.Title className="font-display text-base">Equipment check</Card.Title>
          </Card.Header>
          <Card.Content className="space-y-3 text-sm">
            {preflight.requiresAudio && (
              <div className="flex items-center gap-2 text-foreground">
                <Headphones className="h-4 w-4 text-accent" aria-hidden="true" />
                <span>Audio playback required for Listening items.</span>
              </div>
            )}
            {preflight.requiresMicrophone && (
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="inline-flex items-center gap-2 text-foreground">
                  <Mic className="h-4 w-4 text-accent" aria-hidden="true" />
                  Microphone required for Speaking items.
                  {micStatus === 'ready' && (
                    <Chip size="sm" color="success" variant="soft">
                      <CheckCircle2 className="h-3 w-3 mr-1 inline" aria-hidden="true" />
                      Ready
                    </Chip>
                  )}
                  {micStatus === 'failed' && (
                    <Chip size="sm" color="danger" variant="soft">
                      Permission needed
                    </Chip>
                  )}
                </span>
                <Button size="sm" variant="outline" onPress={() => void testMicrophone()}>
                  <RefreshCw className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
                  Test microphone
                </Button>
              </div>
            )}
          </Card.Content>
        </Card>
      )}

      <div className="flex items-center justify-end gap-3 pt-2">
        <Link to="/placement" className="button button--md button--outline">
          Cancel
        </Link>
        <Button
          size="md"
          variant="primary"
          isDisabled={startBlocked}
          isPending={submitting}
          onPress={() => void handleConfirmStart()}
        >
          <Play className="h-4 w-4 mr-1.5" aria-hidden="true" />
          {preflight.isRetake ? 'Start placement retake' : 'Start placement test'}
        </Button>
      </div>
    </div>
  );
}
