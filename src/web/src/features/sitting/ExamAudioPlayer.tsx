import { useEffect, useMemo, useRef, useState } from 'react';
import { Button, Chip, ProgressBar } from '@heroui/react';
import { Volume2, Play, Pause, CheckCircle2, Lock } from 'lucide-react';
import { formatTimerSeconds, toBrowserMediaUrl } from '@/lib/security';
import {
  readAudioProgressMap,
  saveAudioProgress,
  type SittingKind,
} from '@/lib/sitting-queue';

export interface ExamAudioPlayerProps {
  sittingKind: SittingKind;
  sittingId: number;
  audioPath: string;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  questionId: number;
  allowReplay?: boolean;
  /** Optional audio segment key used to persist single-play state across reloads */
  audioKey?: string;
}

/**
 * ADR-0010 single-play (or bounded segment) listening audio player.
 * Persists playback offset & completion in localStorage so reloading cannot bypass single-play.
 */
export function ExamAudioPlayer({
  sittingKind,
  sittingId,
  audioPath,
  audioStartMs,
  audioEndMs,
  questionId,
  allowReplay = false,
  audioKey,
}: ExamAudioPlayerProps) {
  const resolvedUrl = toBrowserMediaUrl(audioPath);
  const storageKey = useMemo(
    () => audioKey || `${audioPath}::${audioStartMs ?? 0}-${audioEndMs ?? 'end'}::q${questionId}`,
    [audioKey, audioPath, audioStartMs, audioEndMs, questionId],
  );

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [isCompleted, setIsCompleted] = useState(false);
  const [hasError, setHasError] = useState(false);

  const startSec = audioStartMs != null ? Math.max(0, audioStartMs / 1000) : 0;
  const endSec = audioEndMs != null && audioEndMs > 0 ? audioEndMs / 1000 : null;

  useEffect(() => {
    const map = readAudioProgressMap(sittingKind, sittingId);
    const saved = map[storageKey];
    setIsCompleted(Boolean(saved?.completed && !allowReplay));
    setCurrentTime(saved?.currentTime ?? startSec);
    setIsPlaying(false);
    setHasError(false);
  }, [sittingKind, sittingId, storageKey, allowReplay, startSec]);

  useEffect(() => {
    const el = audioRef.current;
    if (!el || !resolvedUrl) return;

    const handleLoadedMetadata = () => {
      const segDuration = endSec != null ? Math.max(1, endSec - startSec) : el.duration || 0;
      setDuration(segDuration);
      const map = readAudioProgressMap(sittingKind, sittingId);
      const saved = map[storageKey];
      const resumeAt = saved?.currentTime && saved.currentTime > startSec ? saved.currentTime : startSec;
      try {
        el.currentTime = resumeAt;
      } catch {
        // Ignore seek errors in jsdom / pre-buffered audio
      }
    };

    const handleTimeUpdate = () => {
      const pos = el.currentTime || 0;
      setCurrentTime(pos);
      saveAudioProgress(sittingKind, sittingId, storageKey, {
        completed: false,
        currentTime: pos,
      });
      if (endSec != null && pos >= endSec) {
        el.pause();
        setIsPlaying(false);
        if (!allowReplay) {
          setIsCompleted(true);
        }
        saveAudioProgress(sittingKind, sittingId, storageKey, {
          completed: !allowReplay,
          currentTime: endSec,
        });
      }
    };

    const handleEnded = () => {
      setIsPlaying(false);
      if (!allowReplay) {
        setIsCompleted(true);
      }
      saveAudioProgress(sittingKind, sittingId, storageKey, {
        completed: !allowReplay,
        currentTime: el.duration || currentTime,
      });
    };

    const handleError = () => {
      setIsPlaying(false);
      setHasError(true);
    };

    el.addEventListener('loadedmetadata', handleLoadedMetadata);
    el.addEventListener('timeupdate', handleTimeUpdate);
    el.addEventListener('ended', handleEnded);
    el.addEventListener('error', handleError);

    return () => {
      el.removeEventListener('loadedmetadata', handleLoadedMetadata);
      el.removeEventListener('timeupdate', handleTimeUpdate);
      el.removeEventListener('ended', handleEnded);
      el.removeEventListener('error', handleError);
    };
  }, [resolvedUrl, sittingKind, sittingId, storageKey, startSec, endSec, allowReplay, currentTime]);

  if (!resolvedUrl) return null;

  const elapsedInSegment = Math.max(0, currentTime - startSec);
  const progressPercent = duration > 0 ? Math.min(100, Math.round((elapsedInSegment / duration) * 100)) : 0;

  const handleTogglePlay = async () => {
    const el = audioRef.current;
    if (!el || isCompleted) return;
    if (isPlaying) {
      el.pause();
      setIsPlaying(false);
      return;
    }
    try {
      await el.play();
      setIsPlaying(true);
    } catch {
      setHasError(true);
    }
  };

  return (
    <div className="rounded-lg border border-border bg-surface-secondary p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium text-foreground">
          <Volume2 className="h-4 w-4 text-accent" aria-hidden="true" />
          <span>Audio track</span>
          {!allowReplay && (
            <Chip size="sm" variant="soft" color={isCompleted ? 'default' : 'accent'}>
              {isCompleted ? (
                <span className="inline-flex items-center gap-1">
                  <Lock className="h-3 w-3" aria-hidden="true" />
                  Played once
                </span>
              ) : (
                'Single play'
              )}
            </Chip>
          )}
        </div>

        <div className="flex items-center gap-2">
          {isCompleted ? (
            <Chip size="sm" color="success" variant="soft">
              <span className="inline-flex items-center gap-1">
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                Playback complete
              </span>
            </Chip>
          ) : (
            <Button
              size="sm"
              variant="primary"
              isDisabled={hasError}
              onPress={handleTogglePlay}
              aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
            >
              {isPlaying ? (
                <span className="inline-flex items-center gap-1.5">
                  <Pause className="h-3.5 w-3.5" aria-hidden="true" />
                  Pause
                </span>
              ) : (
                <span className="inline-flex items-center gap-1.5">
                  <Play className="h-3.5 w-3.5" aria-hidden="true" />
                  {elapsedInSegment > 0 ? 'Resume audio' : 'Play audio'}
                </span>
              )}
            </Button>
          )}
        </div>
      </div>

      <ProgressBar
        aria-label="Audio playback progress"
        value={isCompleted ? 100 : progressPercent}
        minValue={0}
        maxValue={100}
        size="sm"
        color="accent"
      >
        <ProgressBar.Track>
          <ProgressBar.Fill />
        </ProgressBar.Track>
      </ProgressBar>

      <div className="flex items-center justify-between text-xs text-muted font-mono-tabular">
        <span>{formatTimerSeconds(elapsedInSegment)}</span>
        <span>{duration > 0 ? formatTimerSeconds(duration) : '--:--'}</span>
      </div>

      {hasError && (
        <p className="text-xs text-danger">
          Audio failed to load cleanly. Check your connection or headphones before continuing.
        </p>
      )}

      <audio ref={audioRef} src={resolvedUrl} preload="metadata" className="sr-only" />
    </div>
  );
}
