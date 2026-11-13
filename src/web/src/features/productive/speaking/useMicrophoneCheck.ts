import { useCallback, useEffect, useRef, useState } from 'react';

export type MicrophoneStatus = 'checking' | 'ready' | 'denied' | 'unavailable' | 'error';

export interface MicrophoneDeviceOption {
  deviceId: string;
  label: string;
}

export interface UseMicrophoneCheckResult {
  status: MicrophoneStatus;
  devices: MicrophoneDeviceOption[];
  selectedDeviceId: string | null;
  errorTitle: string | null;
  errorMessage: string | null;
  setSelectedDeviceId: (deviceId: string) => void;
  retry: (overrideDeviceId?: string) => Promise<boolean>;
}

function classifyMediaError(err: unknown): {
  status: MicrophoneStatus;
  title: string;
  message: string;
} {
  const name = err && typeof err === 'object' && 'name' in err ? String((err as { name: unknown }).name) : '';
  if (name === 'NotAllowedError' || name === 'PermissionDeniedError' || name === 'SecurityError') {
    return {
      status: 'denied',
      title: 'Microphone access was blocked',
      message:
        'Allow microphone permission in your browser site settings, pick a microphone below if listed, then click Retry microphone check.',
    };
  }
  if (name === 'NotFoundError' || name === 'DevicesNotFoundError' || name === 'OverconstrainedError') {
    return {
      status: 'unavailable',
      title: 'No working microphone found',
      message:
        'Connect an external microphone or headset, select a different input device if available, and retry.',
    };
  }
  return {
    status: 'error',
    title: 'Could not open microphone',
    message:
      err instanceof Error && err.message
        ? err.message
        : 'Another app or system privacy setting may be holding the microphone.',
  };
}

export function useMicrophoneCheck(autoRun = true): UseMicrophoneCheckResult {
  const [status, setStatus] = useState<MicrophoneStatus>('checking');
  const [devices, setDevices] = useState<MicrophoneDeviceOption[]>([]);
  const [selectedDeviceId, setSelectedDeviceIdState] = useState<string | null>(null);
  const [errorTitle, setErrorTitle] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const refreshDevices = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.enumerateDevices) {
      return [] as MicrophoneDeviceOption[];
    }
    try {
      const list = await navigator.mediaDevices.enumerateDevices();
      const inputs = list
        .filter((d) => d.kind === 'audioinput')
        .map((d, idx) => ({
          deviceId: d.deviceId || `default-${idx}`,
          label: d.label?.trim() || `Microphone ${idx + 1}`,
        }));
      if (mountedRef.current) {
        setDevices(inputs);
        setSelectedDeviceIdState((prev) => {
          if (prev && inputs.some((item) => item.deviceId === prev)) {
            return prev;
          }
          return inputs[0]?.deviceId ?? null;
        });
      }
      return inputs;
    } catch {
      return [];
    }
  }, []);

  const retry = useCallback(
    async (overrideDeviceId?: string): Promise<boolean> => {
      if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
        if (mountedRef.current) {
          setStatus('unavailable');
          setErrorTitle('Microphone capture unavailable in this browser');
          setErrorMessage('Use an up-to-date Chromium, Firefox, or Safari browser over HTTPS or localhost.');
        }
        return false;
      }

      if (mountedRef.current) {
        setStatus('checking');
        setErrorTitle(null);
        setErrorMessage(null);
      }

      const listed = await refreshDevices();
      const targetId = overrideDeviceId ?? selectedDeviceId ?? listed[0]?.deviceId ?? undefined;

      try {
        const audioConstraint: boolean | MediaTrackConstraints = targetId
          ? { deviceId: { exact: targetId } }
          : true;
        const stream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraint });
        for (const track of stream.getTracks()) {
          track.stop();
        }
        await refreshDevices();
        if (mountedRef.current) {
          setStatus('ready');
          setErrorTitle(null);
          setErrorMessage(null);
        }
        return true;
      } catch (err) {
        await refreshDevices();
        const classified = classifyMediaError(err);
        if (mountedRef.current) {
          setStatus(classified.status);
          setErrorTitle(classified.title);
          setErrorMessage(classified.message);
        }
        return false;
      }
    },
    [refreshDevices, selectedDeviceId],
  );

  const setSelectedDeviceId = useCallback(
    (deviceId: string) => {
      setSelectedDeviceIdState(deviceId);
      void retry(deviceId);
    },
    [retry],
  );

  useEffect(() => {
    if (!autoRun) return;
    void retry();
    // Run initial probe once on mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRun]);

  return {
    status,
    devices,
    selectedDeviceId,
    errorTitle,
    errorMessage,
    setSelectedDeviceId,
    retry,
  };
}
