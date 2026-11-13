import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Chip, ProgressBar, Spinner } from '@heroui/react';
import { Mic, RotateCcw, Square, CheckCircle2, AlertTriangle } from 'lucide-react';
import { api, type AttemptQuestionDto } from '@/lib/api';
import { formatTimerSeconds, toBrowserMediaUrl } from '@/lib/security';

export interface SpeakingResponsePaneProps {
  question: AttemptQuestionDto;
  attemptId: number;
  onAudioSaved: (audioPath: string, durationMs?: number) => void;
  onNextQuestion: () => void;
  onUploadAudio?: (
    questionId: number,
    blob: Blob,
    fileName: string,
    durationMs?: number,
  ) => Promise<{
    audioPath?: string | null;
    responseAudioPath?: string | null;
    durationMs?: number | null;
    savedAt?: string;
  }>;
  onRecordInterruption?: (payload: {
    questionId: number;
    sessionToken: string;
    capturedMs: number;
  }) => Promise<{
    interruptions: number;
    outcome: string;
    replacementQuestionId?: number | null;
  }>;
}

type RecorderPhase =
  | 'idle'
  | 'recording'
  | 'uploading'
  | 'recorded'
  | 'interrupted'
  | 'error';

function makeSessionToken(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `spk-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export function SpeakingResponsePane({
  question,
  attemptId,
  onAudioSaved,
  onUploadAudio,
  onRecordInterruption,
}: SpeakingResponsePaneProps) {
  const prepDuration = Math.max(0, question.prepSeconds ?? 0);
  const maxRecordDuration = Math.max(15, question.responseSeconds ?? 60);

  const [phase, setPhase] = useState<RecorderPhase>(() =>
    question.responseAudioPath ? 'recorded' : 'idle',
  );
  const [prepRemaining, setPrepRemaining] = useState<number>(prepDuration);
  const [recordElapsedSec, setRecordElapsedSec] = useState<number>(0);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [audioPreviewUrl, setAudioPreviewUrl] = useState<string | null>(() =>
    question.responseAudioPath
      ? `/api/learner/speaking/attempts/${attemptId}/questions/${question.questionId}/audio`
      : null,
  );

  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const interruptedRef = useRef(false);
  const startedAtRef = useRef<number>(0);
  const sessionTokenRef = useRef<string>(makeSessionToken());
  const objectUrlRef = useRef<string | null>(null);

  const stopTracks = useCallback(() => {
    const stream = streamRef.current;
    if (stream) {
      for (const track of stream.getTracks()) {
        try {
          track.stop();
        } catch {
          // Ignore track stop failures
        }
      }
      streamRef.current = null;
    }
  }, []);

  // Reset cleanly whenever switching to another question
  useEffect(() => {
    interruptedRef.current = true;
    try {
      if (recorderRef.current && recorderRef.current.state !== 'inactive') {
        recorderRef.current.stop();
      }
    } catch {
      // Ignore stop error on switch
    }
    recorderRef.current = null;
    stopTracks();

    setPhase(question.responseAudioPath ? 'recorded' : 'idle');
    setPrepRemaining(Math.max(0, question.prepSeconds ?? 0));
    setRecordElapsedSec(0);
    setStatusMessage(null);
    sessionTokenRef.current = makeSessionToken();

    if (question.responseAudioPath) {
      setAudioPreviewUrl(
        toBrowserMediaUrl(question.responseAudioPath) ??
          `/api/learner/speaking/attempts/${attemptId}/questions/${question.questionId}/audio`,
      );
    } else {
      setAudioPreviewUrl(null);
    }

    return () => {
      stopTracks();
      if (objectUrlRef.current && typeof URL.revokeObjectURL === 'function') {
        URL.revokeObjectURL(objectUrlRef.current);
        objectUrlRef.current = null;
      }
    };
  }, [question.questionId, question.prepSeconds, question.responseAudioPath, attemptId, stopTracks]);

  // Prep countdown tick while idle
  useEffect(() => {
    if (phase !== 'idle' || prepRemaining <= 0) return;
    const id = window.setInterval(() => {
      setPrepRemaining((prev) => (prev <= 1 ? 0 : prev - 1));
    }, 1000);
    return () => window.clearInterval(id);
  }, [phase, prepRemaining]);

  const handleInterruption = useCallback(
    async (reasonMessage: string) => {
      if (interruptedRef.current) return;
      interruptedRef.current = true;

      const capturedMs = Math.max(0, Date.now() - startedAtRef.current);
      try {
        if (recorderRef.current && recorderRef.current.state !== 'inactive') {
          recorderRef.current.stop();
        }
      } catch {
        // Ignore recorder stop failure
      }
      recorderRef.current = null;
      stopTracks();
      setPhase('interrupted');
      setStatusMessage(reasonMessage);

      if (onRecordInterruption) {
        try {
          await onRecordInterruption({
            questionId: question.questionId,
            sessionToken: sessionTokenRef.current,
            capturedMs,
          });
        } catch {
          // Interruption telemetry must never lock recovery UI (PLC-15)
        }
      }
    },
    [onRecordInterruption, question.questionId, stopTracks],
  );

  // Monitor visibility loss & response duration cap while recording
  useEffect(() => {
    if (phase !== 'recording') return;

    const timer = window.setInterval(() => {
      setRecordElapsedSec((prev) => {
        const next = prev + 1;
        if (next >= maxRecordDuration && recorderRef.current?.state === 'recording') {
          recorderRef.current.stop();
        }
        return next;
      });
    }, 1000);

    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') {
        void handleInterruption(
          'Your recording was interrupted because this tab lost focus. Click "Record again" when ready.',
        );
      }
    };

    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, [phase, maxRecordDuration, handleInterruption]);

  const startRecording = useCallback(async () => {
    setStatusMessage(null);
    interruptedRef.current = false;
    chunksRef.current = [];
    sessionTokenRef.current = makeSessionToken();

    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setPhase('error');
      setStatusMessage(
        'Microphone access is not available in this browser. Check permissions or use a supported browser.',
      );
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;

      const RecorderCtor = window.MediaRecorder;
      if (!RecorderCtor) {
        stopTracks();
        setPhase('error');
        setStatusMessage('Audio recording is not supported in this browser.');
        return;
      }

      const recorder = new RecorderCtor(stream);
      recorderRef.current = recorder;
      startedAtRef.current = Date.now();
      setRecordElapsedSec(0);

      recorder.ondataavailable = (ev) => {
        if (ev.data && ev.data.size > 0) {
          chunksRef.current.push(ev.data);
        }
      };

      recorder.onerror = () => {
        void handleInterruption(
          'Recording stopped unexpectedly due to an audio device error. You can record again safely.',
        );
      };

      recorder.onstop = () => {
        stopTracks();
        if (interruptedRef.current) {
          return;
        }
        const mimeType = recorder.mimeType || 'audio/webm';
        const ext = mimeType.includes('mp4') ? 'm4a' : mimeType.includes('wav') ? 'wav' : 'webm';
        const blob = new Blob(chunksRef.current, { type: mimeType });
        const durationMs = Math.max(100, Date.now() - startedAtRef.current);

        if (objectUrlRef.current && typeof URL.revokeObjectURL === 'function') {
          URL.revokeObjectURL(objectUrlRef.current);
          objectUrlRef.current = null;
        }
        if (typeof URL.createObjectURL === 'function') {
          objectUrlRef.current = URL.createObjectURL(blob);
          setAudioPreviewUrl(objectUrlRef.current);
        }

        setPhase('uploading');
        const uploadFn =
          onUploadAudio ??
          ((qId: number, audioBlob: Blob, fileName: string, ms?: number) =>
            api.speaking.uploadAudio(attemptId, qId, audioBlob, fileName, ms));

        void Promise.resolve(
          uploadFn(question.questionId, blob, `speaking-${question.questionId}.${ext}`, durationMs),
        )
          .then((res) => {
            const savedPath =
              res.audioPath ||
              res.responseAudioPath ||
              `/api/learner/speaking/attempts/${attemptId}/questions/${question.questionId}/audio`;
            onAudioSaved(savedPath, res.durationMs ?? durationMs);
            setPhase('recorded');
          })
          .catch((err) => {
            setPhase('error');
            setStatusMessage(
              err instanceof Error
                ? `Upload failed: ${err.message}. You can record again.`
                : 'Audio upload failed. Click "Record again" to retry.',
            );
          });
      };

      recorder.start();
      setPhase('recording');
    } catch {
      stopTracks();
      setPhase('error');
      setStatusMessage(
        'Microphone access was denied or no microphone was found. Grant permission and try again.',
      );
    }
  }, [attemptId, handleInterruption, onAudioSaved, onUploadAudio, question.questionId, stopTracks]);

  const stopRecording = useCallback(() => {
    const rec = recorderRef.current;
    if (!rec || rec.state === 'inactive') return;
    rec.stop();
  }, []);

  const recordPct = Math.min(100, Math.round((recordElapsedSec / maxRecordDuration) * 100));

  return (
    <div className="rounded-lg border border-border bg-surface-secondary/50 p-4 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Mic className="h-4 w-4 text-accent" aria-hidden="true" />
          <span className="text-sm font-semibold text-foreground">Spoken response</span>
        </div>

        <div className="flex items-center gap-2 font-mono-tabular text-xs">
          {prepDuration > 0 && phase === 'idle' && (
            <Chip size="sm" variant="soft" color="accent">
              Prep {formatTimerSeconds(prepRemaining)}
            </Chip>
          )}
          <Chip size="sm" variant="soft" color="default">
            Limit {formatTimerSeconds(maxRecordDuration)}
          </Chip>
        </div>
      </div>

      {statusMessage && (
        <Alert status={phase === 'interrupted' ? 'warning' : 'danger'}>
          <Alert.Indicator />
          <Alert.Content>
            <Alert.Title>
              {phase === 'interrupted' ? 'Recording interrupted' : 'Microphone check needed'}
            </Alert.Title>
            <Alert.Description>{statusMessage}</Alert.Description>
          </Alert.Content>
        </Alert>
      )}

      {phase === 'recording' && (
        <div className="space-y-2 rounded-md border border-danger/40 bg-danger/5 p-3">
          <div className="flex items-center justify-between text-sm">
            <span className="inline-flex items-center gap-2 font-medium text-danger">
              <span className="h-2.5 w-2.5 rounded-full bg-danger animate-pulse" />
              Recording in progress
            </span>
            <span className="font-mono-tabular font-semibold text-foreground">
              {formatTimerSeconds(recordElapsedSec)} / {formatTimerSeconds(maxRecordDuration)}
            </span>
          </div>
          <ProgressBar
            aria-label="Speaking recording time"
            value={recordPct}
            minValue={0}
            maxValue={100}
            size="sm"
            color="danger"
          >
            <ProgressBar.Track>
              <ProgressBar.Fill />
            </ProgressBar.Track>
          </ProgressBar>
        </div>
      )}

      {phase === 'uploading' && (
        <div className="flex items-center gap-2.5 text-sm text-muted py-2">
          <Spinner size="sm" color="accent" />
          <span>Uploading spoken response...</span>
        </div>
      )}

      {phase === 'recorded' && (
        <div className="space-y-2 rounded-md border border-success/40 bg-success/5 p-3">
          <div className="flex items-center gap-2 text-sm font-medium text-success">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>Spoken response saved</span>
          </div>
          {audioPreviewUrl && (
            <audio controls src={audioPreviewUrl} className="w-full h-9" preload="none" />
          )}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {phase === 'idle' && (
          <Button variant="primary" onPress={() => void startRecording()}>
            <Mic className="h-4 w-4 mr-1.5" aria-hidden="true" />
            Start recording
          </Button>
        )}

        {phase === 'recording' && (
          <Button variant="danger" onPress={stopRecording}>
            <Square className="h-3.5 w-3.5 mr-1.5" aria-hidden="true" />
            Stop recording
          </Button>
        )}

        {(phase === 'recorded' || phase === 'interrupted' || phase === 'error') && (
          <Button variant="secondary" onPress={() => void startRecording()}>
            {phase === 'interrupted' || phase === 'error' ? (
              <AlertTriangle className="h-4 w-4 mr-1.5 text-warning" aria-hidden="true" />
            ) : (
              <RotateCcw className="h-4 w-4 mr-1.5" aria-hidden="true" />
            )}
            Record again
          </Button>
        )}
      </div>
    </div>
  );
}
