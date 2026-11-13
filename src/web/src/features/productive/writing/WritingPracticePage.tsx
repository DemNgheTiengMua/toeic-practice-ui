import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Label, Meter, Modal, Spinner, TextArea, TextField } from '@heroui/react';
import { AlertTriangle, CheckCircle2, Clock, Save, Send } from 'lucide-react';
import { api } from '@/lib/api';
import { countWords, formatTimerSeconds, toBrowserMediaUrl } from '@/lib/security';
import type { WritingTake } from './types';

interface WritingPracticePageProps {
  attemptId: number;
  initialQuestionId?: number;
}

type DraftSaveState = 'idle' | 'dirty' | 'saving' | 'saved' | 'error';

export function WritingPracticePage({ attemptId, initialQuestionId }: WritingPracticePageProps) {
  const navigate = useNavigate();

  const [take, setTake] = useState<WritingTake | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [draftText, setDraftText] = useState('');
  const [saveState, setSaveState] = useState<DraftSaveState>('idle');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [remainingSeconds, setRemainingSeconds] = useState<number | null>(null);
  const [submitModalOpen, setSubmitModalOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const lastSavedTextRef = useRef<string>('');
  const autoSaveTimerRef = useRef<number | null>(null);

  const clearAutoSaveTimer = useCallback(() => {
    if (autoSaveTimerRef.current !== null) {
      window.clearTimeout(autoSaveTimerRef.current);
      autoSaveTimerRef.current = null;
    }
  }, []);

  useEffect(() => clearAutoSaveTimer, [clearAutoSaveTimer]);

  const loadQuestion = useCallback(
    async (targetQuestionId?: number) => {
      setLoading(true);
      setLoadError(null);
      try {
        const data = (await api.writing.take(attemptId, targetQuestionId)) as WritingTake;
        if (data.status === 'submitted' || data.status === 'graded') {
          void navigate({
            to: '/writing/result/$attemptId',
            params: { attemptId: String(attemptId) },
          });
          return;
        }
        const initialText = data.question.responseText ?? '';
        setTake(data);
        setDraftText(initialText);
        lastSavedTextRef.current = initialText;
        setSavedAt(data.question.draftSavedAt ?? null);
        setSaveState(initialText.length > 0 ? 'saved' : 'idle');
        setSaveError(null);
        setRemainingSeconds(data.remainingSeconds ?? null);
      } catch (err) {
        setLoadError(err instanceof Error ? err.message : 'Unable to load writing prompt.');
      } finally {
        setLoading(false);
      }
    },
    [attemptId, navigate],
  );

  useEffect(() => {
    void loadQuestion(initialQuestionId);
  }, [initialQuestionId, loadQuestion]);

  useEffect(() => {
    if (remainingSeconds === null || remainingSeconds <= 0) return;
    const timer = window.setInterval(() => {
      setRemainingSeconds((prev) => (prev !== null && prev > 0 ? prev - 1 : 0));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [remainingSeconds]);

  const persistDraft = useCallback(
    async (textToSave: string, questionId: number): Promise<boolean> => {
      clearAutoSaveTimer();
      setSaveState('saving');
      setSaveError(null);
      try {
        const res = await api.writing.saveDraft(attemptId, {
          questionId,
          responseText: textToSave,
        });
        lastSavedTextRef.current = textToSave;
        setSavedAt(res.savedAt);
        setSaveState('saved');
        setTake((prev) => {
          if (!prev) return prev;
          return {
            ...prev,
            questions: prev.questions.map((nav) =>
              nav.questionId === questionId
                ? { ...nav, hasDraft: res.wordCount > 0, wordCount: res.wordCount }
                : nav,
            ),
          };
        });
        return true;
      } catch (err) {
        setSaveState('error');
        setSaveError(err instanceof Error ? err.message : 'Could not save draft.');
        return false;
      }
    },
    [attemptId, clearAutoSaveTimer],
  );

  const handleDraftChange = (nextValue: string) => {
    setDraftText(nextValue);
    if (!take) return;
    setSaveState('dirty');
    clearAutoSaveTimer();
    const qId = take.question.questionId;
    autoSaveTimerRef.current = window.setTimeout(() => {
      void persistDraft(nextValue, qId);
    }, 1200);
  };

  const handleSwitchQuestion = async (targetQuestionId: number) => {
    if (!take || targetQuestionId === take.currentQuestionId) return;
    if (draftText !== lastSavedTextRef.current) {
      await persistDraft(draftText, take.question.questionId);
    }
    await loadQuestion(targetQuestionId);
  };

  const handleOpenSubmitModal = async () => {
    if (take && draftText !== lastSavedTextRef.current) {
      await persistDraft(draftText, take.question.questionId);
    }
    setSubmitModalOpen(true);
  };

  const handleSubmitAttempt = async () => {
    setSubmitting(true);
    try {
      if (take && draftText !== lastSavedTextRef.current) {
        await persistDraft(draftText, take.question.questionId);
      }
      await api.writing.submit(attemptId);
      setSubmitModalOpen(false);
      void navigate({
        to: '/writing/result/$attemptId',
        params: { attemptId: String(attemptId) },
      });
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Unable to submit writing attempt.');
      setSubmitModalOpen(false);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading && !take) {
    return (
      <div className="flex min-h-[320px] items-center justify-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading writing sitting...</span>
      </div>
    );
  }

  if (loadError && !take) {
    return (
      <div className="mx-auto max-w-2xl space-y-4 p-6">
        <Alert status="danger" role="alert">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to open writing sitting</Alert.Title>
            <Alert.Description>{loadError}</Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/writing" className="text-sm font-medium text-accent underline">
          Back to writing studio
        </Link>
      </div>
    );
  }

  if (!take) return null;

  const q = take.question;
  const currentWords = countWords(draftText);
  const currentChars = draftText.length;
  const imageUrl = toBrowserMediaUrl(q.groupImagePath);
  const minTarget = q.minWords ?? null;
  const maxTarget = q.maxWords ?? null;
  const meetsMin = minTarget === null || currentWords >= minTarget;
  const withinMax = maxTarget === null || currentWords <= maxTarget;
  const lengthHealthy = currentWords > 0 && meetsMin && withinMax;
  const meterMax = minTarget ?? maxTarget ?? 250;
  const unansweredCount = take.questions.filter((nav) => {
    if (nav.questionId === q.questionId) {
      return currentWords === 0;
    }
    return nav.wordCount === 0 && !nav.hasDraft;
  }).length;

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-20 border-b border-border bg-surface px-4 py-3">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Link to="/writing" className="text-xs font-medium text-muted hover:text-foreground">
              Exit studio
            </Link>
            <span className="text-border">/</span>
            <Chip color="accent" variant="soft" size="sm">
              {take.certCode}
            </Chip>
            <h1 className="font-display text-base font-semibold text-foreground">{take.examName}</h1>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <span className="font-mono-tabular text-xs text-muted">
              Task {take.currentQuestionIndex + 1} of {take.totalQuestions}
            </span>
            {remainingSeconds !== null && (
              <Chip color={remainingSeconds < 120 ? 'danger' : 'default'} variant="soft" size="sm">
                <Clock className="size-3.5" aria-hidden="true" />
                <span className="font-mono-tabular">{formatTimerSeconds(remainingSeconds)}</span>
              </Chip>
            )}
            <Button
              variant="secondary"
              size="sm"
              isPending={saveState === 'saving'}
              onPress={() => void persistDraft(draftText, q.questionId)}
            >
              <Save className="size-3.5" aria-hidden="true" />
              Save draft
            </Button>
            <Button variant="primary" size="sm" onPress={() => void handleOpenSubmitModal()}>
              <Send className="size-3.5" aria-hidden="true" />
              Submit attempt
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
        {loadError && (
          <Alert status="danger" role="alert">
            <Alert.Indicator />
            <Alert.Content>
              <Alert.Title>Sitting notice</Alert.Title>
              <Alert.Description>{loadError}</Alert.Description>
            </Alert.Content>
          </Alert>
        )}

        <div className="grid gap-6 lg:grid-cols-12">
          <div className="space-y-4 lg:col-span-5">
            <Card>
              <Card.Header className="flex flex-row items-center justify-between gap-2">
                <Chip color="accent" variant="soft" size="sm">
                  {q.sectionName}
                </Chip>
                {q.responseSeconds && (
                  <span className="font-mono-tabular text-xs text-muted">
                    {Math.round(q.responseSeconds / 60)} min guide
                  </span>
                )}
              </Card.Header>
              <Card.Content className="space-y-4">
                <p className="text-sm leading-relaxed text-foreground whitespace-pre-line">{q.stem}</p>

                {imageUrl && (
                  <img
                    src={imageUrl}
                    alt={q.groupImageAlt ?? 'Writing prompt stimulus'}
                    className="max-h-80 w-full rounded-lg border border-border object-contain"
                  />
                )}

                {q.hint && (
                  <p className="rounded-lg bg-surface-secondary px-3 py-2 text-xs text-muted">
                    Tip: {q.hint}
                  </p>
                )}
              </Card.Content>
            </Card>

            <Card variant="secondary">
              <Card.Header>
                <Card.Title className="text-sm">Live task checklist</Card.Title>
              </Card.Header>
              <Card.Content className="space-y-2.5 text-sm">
                <div className="flex items-center justify-between">
                  <span className="text-muted">Word count target</span>
                  <Chip
                    size="sm"
                    variant="soft"
                    color={lengthHealthy ? 'success' : currentWords === 0 ? 'default' : 'warning'}
                  >
                    <span className="font-mono-tabular">
                      {minTarget && maxTarget
                        ? `${minTarget}–${maxTarget} words`
                        : minTarget
                          ? `Min ${minTarget} words`
                          : maxTarget
                            ? `Max ${maxTarget} words`
                            : 'Open length'}
                    </span>
                  </Chip>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-muted">Draft status</span>
                  <span className="inline-flex items-center gap-1 text-xs font-medium text-foreground">
                    {saveState === 'saved' && (
                      <>
                        <CheckCircle2 className="size-3.5 text-success" aria-hidden="true" />
                        Saved {savedAt ? 'to server' : ''}
                      </>
                    )}
                    {saveState === 'saving' && 'Saving draft...'}
                    {saveState === 'dirty' && 'Unsaved changes'}
                    {saveState === 'error' && 'Save retry needed'}
                    {saveState === 'idle' && 'Not started'}
                  </span>
                </div>
              </Card.Content>
            </Card>

            <Card variant="secondary">
              <Card.Header>
                <Card.Title className="text-sm">Task navigator</Card.Title>
              </Card.Header>
              <Card.Content className="flex flex-wrap gap-2">
                {take.questions.map((nav) => {
                  const isCurrent = nav.questionId === take.currentQuestionId;
                  return (
                    <Button
                      key={nav.questionId}
                      size="sm"
                      variant={isCurrent ? 'primary' : nav.hasDraft ? 'secondary' : 'outline'}
                      onPress={() => void handleSwitchQuestion(nav.questionId)}
                    >
                      #{nav.displayOrder} ({nav.questionId === q.questionId ? currentWords : nav.wordCount}w)
                    </Button>
                  );
                })}
              </Card.Content>
            </Card>
          </div>

          <div className="space-y-4 lg:col-span-7">
            <Card>
              <Card.Header className="flex flex-wrap items-center justify-between gap-2">
                <Card.Title className="text-base">Your written response</Card.Title>
                <div className="flex items-center gap-3 font-mono-tabular text-xs text-muted">
                  <span className="font-semibold text-foreground">{currentWords} words</span>
                  <span>{currentChars} characters</span>
                </div>
              </Card.Header>
              <Card.Content className="space-y-4">
                {saveError && (
                  <Alert status="warning" role="alert">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>Draft auto-save notice</Alert.Title>
                      <Alert.Description>{saveError}</Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}

                <Meter
                  value={Math.min(currentWords, meterMax)}
                  minValue={0}
                  maxValue={meterMax > 0 ? meterMax : 1}
                  color={lengthHealthy ? 'success' : !withinMax ? 'danger' : 'accent'}
                >
                  <Label>Length progress</Label>
                  <Meter.Output>
                    {currentWords} / {meterMax} words
                  </Meter.Output>
                  <Meter.Track>
                    <Meter.Fill />
                  </Meter.Track>
                </Meter>

                <TextField className="w-full" value={draftText} onChange={handleDraftChange}>
                  <Label>Essay / response editor</Label>
                  <TextArea
                    rows={16}
                    spellCheck={false}
                    placeholder="Write your response here. Drafts auto-save while you type..."
                    className="w-full font-sans text-sm leading-relaxed"
                  />
                </TextField>
              </Card.Content>
            </Card>
          </div>
        </div>
      </main>

      <Modal.Backdrop isOpen={submitModalOpen} onOpenChange={setSubmitModalOpen}>
        <Modal.Container>
          <Modal.Dialog className="sm:max-w-md">
            <Modal.CloseTrigger />
            <Modal.Header>
              <Modal.Heading>Submit writing attempt</Modal.Heading>
            </Modal.Header>
            <Modal.Body className="space-y-3 text-sm text-muted">
              {unansweredCount > 0 ? (
                <Alert status="warning">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Unanswered tasks remain</Alert.Title>
                    <Alert.Description>
                      You have <span className="font-mono-tabular font-semibold">{unansweredCount}</span>{' '}
                      empty task(s). Submitting now grades only written responses.
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
              ) : (
                <p className="flex items-center gap-2 text-foreground">
                  <AlertTriangle className="size-4 text-accent" aria-hidden="true" />
                  All tasks have saved drafts. Submit to start AI rubric grading.
                </p>
              )}
            </Modal.Body>
            <Modal.Footer>
              <Button slot="close" variant="secondary">
                Continue writing
              </Button>
              <Button
                variant="primary"
                isPending={submitting}
                onPress={() => void handleSubmitAttempt()}
              >
                Confirm submit
              </Button>
            </Modal.Footer>
          </Modal.Dialog>
        </Modal.Container>
      </Modal.Backdrop>
    </div>
  );
}
