import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * ADR-0018: the activation and reset links are sent from endpoints that cost real mail quota, and the
 * learner-facing buttons sit right next to each other — a double click, or "it did not arrive, try
 * again" five times in a row, is the normal thing a person does. The server already rate-limits
 * (`mail` policy), but a 429 arriving a minute later is a worse explanation than a button that counts
 * down. This is the client half of the same rule.
 *
 * Kept as a hook rather than one useState per page so all three send buttons (resend activation,
 * forgot password, the fixed-address check-email card) cannot drift apart in duration or wording.
 */
export function useResendCooldown(seconds = 60) {
  const [remaining, setRemaining] = useState(0);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(
    () => () => {
      if (timerRef.current) clearInterval(timerRef.current);
    },
    [],
  );

  const start = useCallback(() => {
    setRemaining(seconds);
    if (timerRef.current) clearInterval(timerRef.current);
    timerRef.current = setInterval(() => {
      setRemaining((current) => {
        if (current <= 1) {
          if (timerRef.current) clearInterval(timerRef.current);
          timerRef.current = null;
          return 0;
        }
        return current - 1;
      });
    }, 1000);
  }, [seconds]);

  return { remaining, isCoolingDown: remaining > 0, start };
}
