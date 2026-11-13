import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Chip, Label, Modal, ProgressBar, Spinner } from '@heroui/react';
import { AlertTriangle, CheckCircle2, Clock, Mic, RotateCcw, Send, Square } from 'lucide-react';
import { api } from '@/lib/api';
import { formatTimerSeconds, toBrowserMediaUrl } from '@/lib/security';
import { SpeakingMicrophoneCard } from './SpeakingMicrophoneCard';
import { useMicrophoneCheck } from './useMicrophoneCheck';
import { formatSpeakingAudioUrl, type SpeakingTake } from './types';

interface SpeakingPracticePageProps {
  attemptId: number;
  initialQuestionId?: number;
}

type RecorderPhase = 'idle' | 'prep' | 'recording' | 'uploading' | 'error';

function pickAudioMimeType(): string | undefined {
  if (typeof MediaRecorder === 'undefined' || typeof MediaRecorder.isTypeSupported !== 'function') {
    return undefined;
  }
  const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus'];
  return candidates.find((mime) => MediaRecorder.isTypeSupported(mime));
}

export function SpeakingPracticePage({ attemptId, initialQuestionId }: SpeakingPracticePageProps) {
  const navigate = useNavigate();
  const mic = useMicrophoneCheck(true);

  const [take, setTake] = useState<SpeakingTake | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [remainingSeconds, setRemainingSeconds] = useState<number | null>(null);
  const [phase, setPhase] = useState<RecorderPhase>('idle');
  const [prepLeft, setPrepLeft] = useState<number>(0);
  const [recordElapsedSec, setRecordElapsedSec] = useState<number>(0);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [submitModalOpen, setSubmitModalOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const recordStartedAtRef = useRef<number>(0);
  const pendingUploadRef = useRef<{ blob: Blob; fileName: string; durationMs: number; questionId: number } | null>(null);

  const cleanupMedia = useCallback(() => {
    if (recorderRef.current && recorderRef.current.state === 'recording') {
      try {
        recorderRef.current.stop();
      } catch {
        // ignore recorder shutdown errors
      }
    }
    recorderRef.current = null;
    if (streamRef.current) {
      for (const track of streamRef.current.getTracks()) {
        track.stop();
      }
      streamRef.current = null;
    }
  }, []);

  useEffect(() => {
    return () => {
      cleanupMedia();
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl);
      }
    };
  }, [cleanupMedia, previewUrl]);

  const loadQuestion = useCallback(
    async (targetQuestionId?: number) => {
      setLoading(true);
      setLoadError(null);
      try {
        const data = (await api.speaking.take(attemptId, targetQuestionId)) as SpeakingTake;
        if (data.status === 'submitted' || data.status === 'graded') {
          void navigate({
            to: '/speaking/result/$attemptId',
            params: { attemptId: String(attemptId) },
          });
          return;
        }
        setTake(data);
        setRemainingSeconds(data.remainingSeconds ?? null);
        setPhase('idle');
        setPrepLeft(data.question.prepSeconds ?? 0);
        setRecordElapsedSec(0);
        setUploadError(null);
        pendingUploadRef.current = null;
      } catch (err) {
        setLoadError(err instanceof Error ? err.message : 'Unable to load speaking prompt.');
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

  const uploadBlob = useCallback(
    async (payload: { blob: Blob; fileName: string; durationMs: number; questionId: number }) => {
      setPhase('uploading');
      setUploadError(null);
      try {
        await api.speaking.uploadAudio(
          attemptId,
          payload.questionId,
          payload.blob,
          payload.fileName,
          payload.durationMs,
        );
        pendingUploadRef.current = null;
        await loadQuestion(payload.questionId);
      } catch (err) {
        pendingUploadRef.current = payload;
        setPhase('error');
        setUploadError(err instanceof Error ? err.message : 'Audio upload failed. Your recording is preserved locally.');
      }
    },
    [attemptId, loadQuestion],
  );

  const startRecordingNow = useCallback(async () => {
    if (!take) return;
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setPhase('error');
      setUploadError('Audio recording is not supported in this browser.');
      return;
    }
    setUploadError(null);
    chunksRef.current = [];

    try {
      const constraint: boolean | MediaTrackConstraints = mic.selectedDeviceId
        ? { deviceId: { exact: mic.selectedDeviceId } }
        : true;
      const stream = await navigator.mediaDevices.getUserMedia({ audio: constraint });
      streamRef.current = stream;

      const mimeType = pickAudioMimeType();
      const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
      recorderRef.current = recorder;

      const qId = take.question.questionId;
      const maxSeconds = take.question.responseSeconds ?? 120;

      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
          chunksRef.current.push(event.data);
        }
      };

      recorder.onstop = () => {
        for (const track of stream.getTracks()) {
          track.stop();
        }
        streamRef.current = null;
        const rawElapsed = Date.now() - recordStartedAtRef.current;
        const clampedDurationMs = Math.max(250, Math.min(maxSeconds * 1000, rawElapsed));
        const finalType = recorder.mimeType || 'audio/webm';
        const ext = finalType.includes('mp4') ? 'm4a' : finalType.includes('ogg') ? 'ogg' : 'webm';
        const blob = new Blob(chunksRef.current, { type: finalType });
        const fileName = `speaking-q${qId}.${ext}`;
        const url = URL.createObjectURL(blob);
        setPreviewUrl((prev) => {
          if (prev) URL.revokeObjectURL(prev);
          return url;
        });
        const payload = { blob, fileName, durationMs: clampedDurationMs, questionId: qId };
        pendingUploadRef.current = payload;
        void uploadBlob(payload);
      };

      recordStartedAtRef.current = Date.now();
      setRecordElapsedSec(0);
      setPhase('recording');
      recorder.start();
    } catch {
      void mic.retry();
      setPhase('idle');
    }
  }, [mic, take, uploadBlob]);

  const stopRecording = useCallback(() => {
    if (recorderRef.current && recorderRef.current.state === 'recording') {
      recorderRef.current.stop();
    }
  }, []);

  useEffect(() => {
    if (phase !== 'prep') return;
    if (prepLeft <= 0) {
      void startRecordingNow();
      return;
    }
    const timer = window.setTimeout(() => setPrepLeft((v) => Math.max(0, v - 1)), 1000);
    return () => window.clearTimeout(timer);
  }, [phase, prepLeft, startRecordingNow]);

  useEffect(() => {
    if (phase !== 'recording' || !take) return;
    const maxSec = take.question.responseSeconds ?? 120;
    if (recordElapsedSec >= maxSec) {
      stopRecording();
      return;
    }
    const timer = window.setTimeout(() => setRecordElapsedSec((v) => v + 1), 1000);
    return () => window.clearTimeout(timer);
  }, [phase, recordElapsedSec, stopRecording, take]);

  const handleSubmitAttempt = async () => {
    setSubmitting(true);
    try {
      await api.speaking.submit(attemptId);
      setSubmitModalOpen(false);
      void navigate({
        to: '/speaking/result/$attemptId',
        params: { attemptId: String(attemptId) },
      });
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Unable to submit speaking attempt.');
      setSubmitModalOpen(false);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading && !take) {
    return (
      <div className="flex min-h-[320px] items-center justify-center gap-3 text-muted">
        <Spinner size="md" color="accent" />
        <span>Loading speaking sitting...</span>
      </div>
    );
  }

  if (loadError && !take) {
    return (
      <div className="mx-auto max-w-2xl space-y-4 p-6">
        <Alert status="danger" role="alert">
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>Unable to open speaking sitting</Alert.Title>
            <Alert.Description>{loadError}</Alert.Description>
          </Alert.Content>
        </Alert>
        <Link to="/speaking" className="text-sm font-medium text-accent underline">
          Back to speaking studio
        </Link>
      </div>
    );
  }

  if (!take) return null;

  const q = take.question;
  const maxResponseSec = q.responseSeconds ?? 60;
  const savedAudioUrl = formatSpeakingAudioUrl(attemptId, q.questionId, q.audioPath);
  const imageUrl = toBrowserMediaUrl(q.groupImagePath);
  const groupAudioUrl = toBrowserMediaUrl(q.groupAudioPath);
  const unansweredCount = take.questions.filter((item) => !item.isAnswered).length;
  const micUnavailable = mic.status === 'denied' || mic.status === 'unavailable' || mic.status === 'error';

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-20 border-b border-border bg-surface px-4 py-3">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Link to="/speaking" className="text-xs font-medium text-muted hover:text-foreground">
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
              Task {take.currentQuestionIndex + 1} of {take.totalQuestions} • {take.savedCount} recorded
            </span>
            {remainingSeconds !== null && (
              <Chip color={remainingSeconds < 60 ? 'danger' : 'default'} variant="soft" size="sm">
                <Clock className="size-3.5" aria-hidden="true" />
                <span className="font-mono-tabular">{formatTimerSeconds(remainingSeconds)}</span>
              </Chip>
            )}
            <Button
              variant="primary"
              size="sm"
              isDisabled={phase === 'recording' || phase === 'uploading'}
              onPress={() => setSubmitModalOpen(true)}
            >
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

        {micUnavailable && <SpeakingMicrophoneCard mic={mic} compact />}

        <div className="grid gap-6 lg:grid-cols-12">
          <div className="space-y-4 lg:col-span-7">
            <Card>
              <Card.Header className="flex flex-row items-center justify-between gap-2">
                <Chip color="accent" variant="soft" size="sm">
                  {q.sectionName}
                </Chip>
                <span className="font-mono-tabular text-xs text-muted">
                  Prep {q.prepSeconds ?? 0}s • Max speak {maxResponseSec}s
                </span>
              </Card.Header>

              <Card.Content className="space-y-4">
                <p className="text-base leading-relaxed text-foreground whitespace-pre-line">{q.stem}</p>

                {q.readAloudText && (
                  <div className="rounded-lg border border-border bg-surface-secondary p-4 text-sm leading-relaxed text-foreground">
                    <div className="mb-1 text-xs font-semibold uppercase tracking-wider text-muted">
                      Read aloud passage
                    </div>
                    {q.readAloudText}
                  </div>
                )}

                {imageUrl && (
                  <img
                    src={imageUrl}
                    alt={q.groupImageAlt ?? 'Speaking prompt visual'}
                    className="max-h-72 rounded-lg border border-border object-contain"
                  />
                )}

                {groupAudioUrl && (
                  <div className="space-y-1">
                    <div className="text-xs font-medium text-muted">Reference audio prompt</div>
                    <audio controls src={groupAudioUrl} className="w-full" />
                  </div>
                )}

                {q.hint && (
                  <p className="rounded-lg bg-surface-secondary px-3 py-2 text-xs text-muted">
                    Hint: {q.hint}
                  </p>
                )}
              </Card.Content>
            </Card>
          </div>

          <div className="space-y-4 lg:col-span-5">
            <Card>
              <Card.Header className="flex flex-row items-center justify-between">
                <Card.Title className="text-base">Audio recorder</Card.Title>
                {q.audioPath ? (
                  <Chip color="success" variant="soft" size="sm">
                    <CheckCircle2 className="size-3.5" aria-hidden="true" />
                    Recorded
                  </Chip>
                ) : (
                  <Chip color="default" variant="soft" size="sm">
                    Awaiting response
                  </Chip>
                )}
              </Card.Header>

              <Card.Content className="space-y-4">
                {phase === 'prep' && (
                  <div className="space-y-3">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted">Preparation countdown</span>
                      <span className="font-mono-tabular text-lg font-semibold text-accent">{prepLeft}s</span>
                    </div>
                    <Button variant="primary" size="sm" onPress={() => void startRecordingNow()}>
                      <Mic className="size-4" aria-hidden="true" />
                      Start recording now
                    </Button>
                  </div>
                )}

                {phase === 'recording' && (
                  <div className="space-y-3">
                    <ProgressBar value={recordElapsedSec} maxValue={maxResponseSec} color="danger">
                      <Label>Recording in progress</Label>
                      <ProgressBar.Output>
                        {recordElapsedSec}s / {maxResponseSec}s
                      </ProgressBar.Output>
                      <ProgressBar.Track>
                        <ProgressBar.Fill />
                      </ProgressBar.Track>
                    </ProgressBar>
                    <Button variant="danger" onPress={stopRecording}>
                      <Square className="size-4" aria-hidden="true" />
                      Stop &amp; upload
                    </Button>
                  </div>
                )}

                {phase === 'uploading' && (
                  <div className="flex items-center gap-2 py-2 text-sm text-muted">
                    <Spinner size="sm" color="accent" />
                    <span>Uploading speaking audio...</span>
                  </div>
                )}

                {phase === 'error' && uploadError && (
                  <Alert status="danger" role="alert">
                    <Alert.Indicator />
                    <Alert.Content>
                      <Alert.Title>Upload failed</Alert.Title>
                      <Alert.Description className="space-y-2">
                        <p>{uploadError}</p>
                        {pendingUploadRef.current && (
                          <Button
                            size="sm"
                            variant="primary"
                            onPress={() => {
                              if (pendingUploadRef.current) {
                                void uploadBlob(pendingUploadRef.current);
                              }
                            }}
                          >
                            <RotateCcw className="size-3.5" aria-hidden="true" />
                            Retry upload
                          </Button>
                        )}
                      </Alert.Description>
                    </Alert.Content>
                  </Alert>
                )}

                {(phase === 'idle' || phase === 'error') && (
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="primary"
                      isDisabled={micUnavailable || mic.status === 'checking'}
                      onPress={() => void startRecordingNow()}
                    >
                      <Mic className="size-4" aria-hidden="true" />
                      {q.audioPath ? 'Record again' : 'Start recording'}
                    </Button>
                    {q.prepSeconds && q.prepSeconds > 0 && phase === 'idle' && !q.audioPath && (
                      <Button
                        variant="secondary"
                        isDisabled={micUnavailable || mic.status === 'checking'}
                        onPress={() => {
                          setPrepLeft(q.prepSeconds ?? 5);
                          setPhase('prep');
                        }}
                      >
                        Use {q.prepSeconds}s prep timer
                      </Button>
                    )}
                  </div>
                )}

                {(savedAudioUrl || previewUrl) && (
                  <div className="space-y-1.5 border-t border-border pt-3">
                    <div className="text-xs font-medium text-muted">Saved recording playback</div>
                    <audio controls src={previewUrl ?? savedAudioUrl ?? undefined} className="w-full" />
                  </div>
                )}
              </Card.Content>
            </Card>

            <Card variant="secondary">
              <Card.Header>
                <Card.Title className="text-sm">Task navigator</Card.Title>
              </Card.Header>
              <Card.Content className="flex flex-wrap gap-2">
                {take.questions.map((nav) => {
                  const isCurrent = nav.isCurrent || nav.questionId === take.currentQuestionId;
                  return (
                    <Button
                      key={nav.questionId}
                      size="sm"
                      variant={isCurrent ? 'primary' : nav.isAnswered ? 'secondary' : 'outline'}
                      isDisabled={phase === 'recording' || phase === 'uploading'}
                      onPress={() => void loadQuestion(nav.questionId)}
                    >
                      #{nav.displayOrder} {nav.isAnswered ? 'Recorded' : 'Open'}
                    </Button>
                  );
                })}
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
              <Modal.Heading>Submit speaking attempt</Modal.Heading>
            </Modal.Header>
            <Modal.Body className="space-y-3 text-sm text-muted">
              {unansweredCount > 0 ? (
                <Alert status="warning">
                  <Alert.Indicator />
                  <Alert.Content>
                    <Alert.Title>Unrecorded tasks remain</Alert.Title>
                    <Alert.Description>
                      You still have <span className="font-mono-tabular font-semibold">{unansweredCount}</span>{' '}
                      task(s) without an uploaded recording. Unrecorded tasks receive 0 points.
                    </Alert.Description>
                  </Alert.Content>
                </Alert>
              ) : (
                <p className="flex items-center gap-2 text-foreground">
                  <AlertTriangle className="size-4 text-accent" aria-hidden="true" />
                  All tasks have uploaded audio. Submitting sends your responses for AI grading.
                </p>
              )}
            </Modal.Body>
            <Modal.Footer>
              <Button slot="close" variant="secondary">
                Keep practicing
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
