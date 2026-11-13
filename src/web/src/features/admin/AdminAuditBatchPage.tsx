import React, { useEffect, useState } from 'react';
import { useParams, Link } from '@tanstack/react-router';
import { Alert, Button, Card, Chip } from '@heroui/react';
import { ArrowLeft, CheckCircle, Eye, FileCheck2, HelpCircle, ShieldAlert } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  api,
  type AuditDecisionDto,
  type AuditKeyRevealDto,
  type AuditQueueDto,
  type AuditSampleDto,
  type AuditStratumDto,
} from '../../lib/api';
import { hasCapability, STAFF_PERMISSIONS } from '../../lib/security';
import { buildAuditPromotionSentence, formatAuditStratumStatusLabel } from './adminHelpers';
import { AdminAccessGuard } from './AdminAccessGuard';

export function AdminAuditBatchPage() {
  const { batchId: batchIdParam } = useParams({ strict: false }) as { batchId?: string };
  const batchId = parseInt(batchIdParam || '7', 10);

  const { user } = useAuth();
  const [queue, setQueue] = useState<AuditQueueDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Per-item solving state: sampleItemId -> selectedLabel ('A'|'B'|'C'|'D')
  const [selectedAnswers, setSelectedAnswers] = useState<Record<number, string>>({});
  // Per-item revealed data: sampleItemId -> AuditKeyRevealDto
  const [revealedKeys, setRevealedKeys] = useState<Record<number, AuditKeyRevealDto>>({});
  // Per-stratum decision result: stratumId -> AuditDecisionDto
  const [decisions, setDecisions] = useState<Record<number, AuditDecisionDto>>({});
  // Loading spinners
  const [processingItem, setProcessingItem] = useState<number | null>(null);
  const [decidingStratum, setDecidingStratum] = useState<number | null>(null);

  async function loadQueue() {
    try {
      setLoading(true);
      setError(null);
      const data = await api.admin.auditQueue(batchId);
      setQueue(data);
    } catch (err: any) {
      setError(err.detail || err.message || 'Failed to load batch audit queue.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (hasCapability(user, STAFF_PERMISSIONS.ContentReview)) {
      void loadQueue();
    }
  }, [user, batchId]);

  async function handleSolve(sampleItemId: number) {
    const label = selectedAnswers[sampleItemId];
    if (!label) return;
    try {
      setProcessingItem(sampleItemId);
      await api.admin.auditSolve(sampleItemId, label);
      // Update local state
      if (queue) {
        setQueue({
          ...queue,
          strata: queue.strata.map((s) => ({
            ...s,
            items: s.items.map((item) =>
              item.sampleItemId === sampleItemId
                ? { ...item, solvedLabel: label, solvedAt: new Date().toISOString() }
                : item,
            ),
          })),
        });
      }
    } catch (err: any) {
      setError(err.detail || err.message || 'Failed to record solve.');
    } finally {
      setProcessingItem(null);
    }
  }

  async function handleReveal(sampleItemId: number) {
    try {
      setProcessingItem(sampleItemId);
      const revealData = await api.admin.auditReveal(sampleItemId);
      setRevealedKeys((prev) => ({ ...prev, [sampleItemId]: revealData }));
      if (queue) {
        setQueue({
          ...queue,
          strata: queue.strata.map((s) => ({
            ...s,
            items: s.items.map((item) =>
              item.sampleItemId === sampleItemId
                ? { ...item, keyRevealed: true, keyRevealedAt: revealData.revealedAt }
                : item,
            ),
          })),
        });
      }
    } catch (err: any) {
      setError(err.detail || err.message || 'Failed to reveal key.');
    } finally {
      setProcessingItem(null);
    }
  }

  async function handleVerdict(sampleItemId: number, verdict: string) {
    try {
      setProcessingItem(sampleItemId);
      await api.admin.auditVerdict(sampleItemId, verdict);
      if (queue) {
        setQueue({
          ...queue,
          strata: queue.strata.map((s) => ({
            ...s,
            items: s.items.map((item) =>
              item.sampleItemId === sampleItemId ? { ...item, verdict } : item,
            ),
          })),
        });
      }
    } catch (err: any) {
      setError(err.detail || err.message || 'Failed to record verdict.');
    } finally {
      setProcessingItem(null);
    }
  }

  async function handleDecide(stratum: AuditStratumDto) {
    try {
      setDecidingStratum(stratum.stratumId);
      const decisionResult = await api.admin.auditDecide(stratum.stratumId);
      setDecisions((prev) => ({ ...prev, [stratum.stratumId]: decisionResult }));
      if (queue) {
        setQueue({
          ...queue,
          strata: queue.strata.map((s) =>
            s.stratumId === stratum.stratumId
              ? { ...s, status: decisionResult.status, decidedAt: new Date().toISOString() }
              : s,
          ),
        });
      }
    } catch (err: any) {
      setError(err.detail || err.message || 'Failed to decide stratum.');
    } finally {
      setDecidingStratum(null);
    }
  }

  return (
    <AdminAccessGuard predicate={(u) => hasCapability(u, STAFF_PERMISSIONS.ContentReview)}>
      <div className="space-y-6">
        {/* Navigation Breadcrumb */}
        <div className="flex items-center gap-2 text-xs text-muted">
          <Link to="/admin/users" className="hover:text-foreground no-underline flex items-center gap-1">
            <ArrowLeft className="h-3.5 w-3.5" />
            Back to people &amp; audit
          </Link>
          <span>/</span>
          <span className="text-foreground font-medium font-mono-tabular">Batch #{batchId}</span>
        </div>

        {/* Page Header */}
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="font-display text-2xl font-bold tracking-tight text-foreground sm:text-3xl">
              Stratified audit: Batch #{batchId}
            </h1>
            <p className="text-sm text-muted">
              Source: <span className="font-mono-tabular font-medium text-foreground">{queue?.sourceName || 'Loading...'}</span> · Blind-solve review protocol (SRS ADM-02).
            </p>
          </div>
          {queue && (
            <div className="flex items-center gap-2">
              <Chip color="accent" variant="soft" className="font-mono-tabular text-xs">
                {queue.sampledTotal} sampled
              </Chip>
              <Chip color="default" variant="soft" className="font-mono-tabular text-xs">
                {queue.unsampledTotal} unsampled
              </Chip>
            </div>
          )}
        </div>

        {error && (
          <Alert status="danger">
            <Alert.Title className="text-sm font-semibold">Audit error</Alert.Title>
            <Alert.Description className="text-xs">{error}</Alert.Description>
          </Alert>
        )}

        {loading ? (
          <div className="py-12 text-center text-xs text-muted">Loading audit queue...</div>
        ) : !queue || queue.strata.length === 0 ? (
          <Card className="border border-border p-8 text-center text-muted text-sm">
            No strata configured for this batch.
          </Card>
        ) : (
          <div className="space-y-8">
            {queue.strata.map((stratum) => {
              const allItemsJudged = stratum.items.length > 0 && stratum.items.every((it) => !!it.verdict);
              const decisionResult = decisions[stratum.stratumId];
              const isDecided = stratum.status === 'approved' || stratum.status === 'quarantined' || !!decisionResult;

              return (
                <Card key={stratum.stratumId} className="border border-border">
                  <Card.Header className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between border-b border-border/60 pb-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-mono-tabular text-xs font-semibold text-accent">
                          {stratum.topicCode}
                        </span>
                        <Chip size="sm" color="default" variant="soft">
                          {stratum.cefrLevel}
                        </Chip>
                        <Chip size="sm" color="default" variant="soft">
                          {stratum.modality}
                        </Chip>
                      </div>
                      <Card.Description className="text-xs text-muted pt-1">
                        Stratum size: <span className="font-mono-tabular font-medium text-foreground">{stratum.itemCount}</span> items ({stratum.sampled} sampled, {stratum.unsampled} unsampled).
                      </Card.Description>
                    </div>

                    <div className="flex items-center gap-2">
                      <Chip
                        color={
                          stratum.status === 'approved'
                            ? 'success'
                            : stratum.status === 'quarantined'
                              ? 'danger'
                              : 'warning'
                        }
                        variant="soft"
                        className="text-xs"
                      >
                        {formatAuditStratumStatusLabel(stratum.status)}
                      </Chip>
                    </div>
                  </Card.Header>

                  <Card.Content className="space-y-6 pt-4">
                    {/* Items List */}
                    <div className="space-y-6">
                      {stratum.items.map((item, idx) => {
                        const revealInfo = revealedKeys[item.sampleItemId];
                        const isSolved = !!item.solvedLabel;
                        const isRevealed = item.keyRevealed || !!revealInfo;
                        const selectedAnswer = selectedAnswers[item.sampleItemId] || item.solvedLabel || '';

                        return (
                          <div
                            key={item.sampleItemId}
                            className="rounded-xl border border-border/70 bg-surface-secondary/20 p-4 space-y-4"
                          >
                            <div className="flex items-center justify-between">
                              <span className="text-xs font-semibold uppercase tracking-wider text-muted font-mono-tabular">
                                Sample #{idx + 1} (Item #{item.itemId})
                              </span>
                              {item.verdict && (
                                <Chip
                                  color={item.verdict === 'pass' ? 'success' : 'danger'}
                                  variant="soft"
                                  className="text-xs"
                                >
                                  Verdict: {item.verdict}
                                </Chip>
                              )}
                            </div>

                            {/* Stem */}
                            <div className="text-sm font-medium text-foreground leading-relaxed">
                              {item.stem}
                            </div>

                            {/* Options */}
                            <div className="grid gap-2 sm:grid-cols-2">
                              {(['A', 'B', 'C', 'D'] as const).map((opt) => {
                                const optionText =
                                  opt === 'A'
                                    ? item.optionA
                                    : opt === 'B'
                                      ? item.optionB
                                      : opt === 'C'
                                        ? item.optionC
                                        : item.optionD;

                                const isChecked = selectedAnswer === opt;

                                return (
                                  <label
                                    key={opt}
                                    className={`flex items-center gap-2.5 p-3 rounded-lg border text-xs cursor-pointer transition-colors ${
                                      isChecked
                                        ? 'border-accent bg-accent/10 text-foreground font-medium'
                                        : 'border-border/60 hover:bg-surface-secondary/40 text-muted'
                                    }`}
                                  >
                                    <input
                                      type="radio"
                                      name={`sample-${item.sampleItemId}`}
                                      value={opt}
                                      checked={isChecked}
                                      disabled={isSolved}
                                      onChange={() =>
                                        setSelectedAnswers((prev) => ({
                                          ...prev,
                                          [item.sampleItemId]: opt,
                                        }))
                                      }
                                      className="accent-accent"
                                    />
                                    <span>
                                      <strong className="font-mono-tabular">{opt}:</strong> {optionText}
                                    </span>
                                  </label>
                                );
                              })}
                            </div>

                            {/* Blind-solve Workflow Actions */}
                            <div className="flex flex-wrap items-center gap-2.5 pt-2 border-t border-border/50">
                              {/* 1. Submit solve */}
                              {!isSolved && (
                                <Button
                                  size="sm"
                                  variant="primary"
                                  isDisabled={!selectedAnswers[item.sampleItemId] || processingItem === item.sampleItemId}
                                  onPress={() => void handleSolve(item.sampleItemId)}
                                >
                                  Submit solve
                                </Button>
                              )}

                              {/* 2. Reveal Key */}
                              <Button
                                size="sm"
                                variant="secondary"
                                isDisabled={!isSolved || isRevealed || processingItem === item.sampleItemId}
                                onPress={() => void handleReveal(item.sampleItemId)}
                              >
                                Reveal key
                              </Button>

                              {/* 3. Verdict Judgement */}
                              {isRevealed && !item.verdict && (
                                <div className="flex items-center gap-2 ml-auto">
                                  <span className="text-xs text-muted">Auditor verdict:</span>
                                  <Button
                                    size="sm"
                                    variant="primary"
                                    onPress={() => void handleVerdict(item.sampleItemId, 'pass')}
                                  >
                                    Pass
                                  </Button>
                                  <Button
                                    size="sm"
                                    variant="danger"
                                    onPress={() => void handleVerdict(item.sampleItemId, 'fail')}
                                  >
                                    Fail
                                  </Button>
                                </div>
                              )}
                            </div>

                            {/* Authored Key & Explanation Reveal Container (Only rendered after reveal!) */}
                            {isRevealed && (
                              <div className="rounded-lg bg-surface border border-accent/40 p-3 space-y-1.5 text-xs">
                                <div className="flex items-center gap-2">
                                  <span className="font-semibold text-accent">Authored key:</span>
                                  <span className="font-mono-tabular font-bold text-foreground">
                                    Option {revealInfo?.keyLabel || 'revealed'}
                                  </span>
                                </div>
                                {(revealInfo?.explanation || item.solvedLabel) && (
                                  <p className="text-muted leading-relaxed">
                                    {revealInfo?.explanation || 'No author commentary provided.'}
                                  </p>
                                )}
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>

                    {/* Stratum Decision Action */}
                    <div className="pt-4 border-t border-border flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
                      <div>
                        {decisionResult ? (
                          <p className="text-xs text-success font-medium">
                            {buildAuditPromotionSentence(
                              decisionResult.sampledPromoted,
                              decisionResult.unsampledPromoted,
                              decisionResult.quarantined,
                            )}
                          </p>
                        ) : isDecided ? (
                          <p className="text-xs text-muted">
                            Stratum has already been finalized ({formatAuditStratumStatusLabel(stratum.status)}).
                          </p>
                        ) : (
                          <p className="text-xs text-muted">
                            All sampled items must be solved, revealed, and judged before deciding the stratum.
                          </p>
                        )}
                      </div>

                      {!isDecided && (
                        <Button
                          variant="primary"
                          isDisabled={!allItemsJudged || decidingStratum === stratum.stratumId}
                          onPress={() => void handleDecide(stratum)}
                        >
                          {decidingStratum === stratum.stratumId
                            ? 'Deciding...'
                            : `Decide stratum (${stratum.sampled} sampled, ${stratum.unsampled} unsampled)`}
                        </Button>
                      )}
                    </div>
                  </Card.Content>
                </Card>
              );
            })}
          </div>
        )}
      </div>
    </AdminAccessGuard>
  );
}
