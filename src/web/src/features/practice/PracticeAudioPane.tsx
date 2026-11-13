import React, { useEffect, useRef, useState } from 'react';
import { Button, Label, Slider } from '@heroui/react';
import { FileText, Headphones, RotateCcw } from 'lucide-react';
import { toBrowserMediaUrl } from '@/lib/security';
import { formatMsTime } from './practiceUtils';

export interface PracticeAudioPaneProps {
  audioPath?: string | null;
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  transcript?: string | null;
  compact?: boolean;
}

export function PracticeAudioPane({
  audioPath,
  audioStartMs,
  audioEndMs,
  transcript,
  compact = false,
}: PracticeAudioPaneProps) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [playbackRate, setPlaybackRate] = useState<number>(1);
  const [showTranscript, setShowTranscript] = useState<boolean>(false);

  const audioUrl = toBrowserMediaUrl(audioPath);

  useEffect(() => {
    setShowTranscript(false);
    const el = audioRef.current;
    if (el) {
      el.playbackRate = playbackRate;
    }
  }, [audioUrl, audioStartMs, audioEndMs, playbackRate]);

  if (!audioUrl && !transcript) return null;

  const startSec = audioStartMs != null ? audioStartMs / 1000 : 0;
  const endSec = audioEndMs != null ? audioEndMs / 1000 : null;

  const handleLoadedMetadata = () => {
    const el = audioRef.current;
    if (!el) return;
    el.playbackRate = playbackRate;
    if (startSec > 0) {
      try {
        el.currentTime = startSec;
      } catch {
        // Ignore seek errors on unloaded media in headless environments
      }
    }
  };

  const handleTimeUpdate = () => {
    const el = audioRef.current;
    if (!el || endSec == null) return;
    if (el.currentTime >= endSec) {
      el.pause();
    }
  };

  const handleRewind = () => {
    const el = audioRef.current;
    if (!el) return;
    el.currentTime = Math.max(startSec, el.currentTime - 5);
  };

  return (
    <div className="space-y-3 rounded-lg border border-border bg-surface-secondary p-3">
      {audioUrl && (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
            <span className="inline-flex items-center gap-1.5 font-medium text-foreground">
              <Headphones className="h-3.5 w-3.5 text-accent" aria-hidden="true" />
              <span>Listening clip</span>
            </span>
            {(audioStartMs != null || audioEndMs != null) && (
              <span className="font-mono-tabular">
                Segment {formatMsTime(audioStartMs ?? 0)}
                {audioEndMs != null ? ` – ${formatMsTime(audioEndMs)}` : ''}
              </span>
            )}
          </div>

          <audio
            ref={audioRef}
            controls
            preload="metadata"
            src={audioUrl}
            onLoadedMetadata={handleLoadedMetadata}
            onTimeUpdate={handleTimeUpdate}
            className="w-full"
          />

          {!compact && (
            <div className="flex flex-wrap items-center justify-between gap-4">
              <Button size="sm" variant="secondary" onPress={handleRewind}>
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                <span>-5s</span>
              </Button>
              <div className="w-48">
                <Slider
                  minValue={0.8}
                  maxValue={1.2}
                  step={0.05}
                  value={playbackRate}
                  onChange={(v) => {
                    if (typeof v === 'number') {
                      setPlaybackRate(v);
                      if (audioRef.current) audioRef.current.playbackRate = v;
                    }
                  }}
                >
                  <div className="flex items-center justify-between text-xs">
                    <Label>Speed</Label>
                    <Slider.Output className="font-mono-tabular" />
                  </div>
                  <Slider.Track>
                    <Slider.Fill />
                    <Slider.Thumb />
                  </Slider.Track>
                </Slider>
              </div>
            </div>
          )}
        </>
      )}

      {transcript && (
        <div className="space-y-2">
          <Button
            size="sm"
            variant="ghost"
            onPress={() => setShowTranscript((prev) => !prev)}
          >
            <FileText className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{showTranscript ? 'Hide transcript' : 'Show transcript'}</span>
          </Button>
          {showTranscript && (
            <div className="whitespace-pre-line rounded-md border border-border bg-surface p-3 text-sm text-foreground">
              {transcript}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
