import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useResendCooldown } from '../useResendCooldown';

describe('useResendCooldown (ADR-0018)', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('starts idle so the button is usable immediately', () => {
    const { result } = renderHook(() => useResendCooldown(60));

    expect(result.current.remaining).toBe(0);
    expect(result.current.isCoolingDown).toBe(false);
  });

  it('counts down to zero and frees the button again', () => {
    const { result } = renderHook(() => useResendCooldown(3));

    act(() => result.current.start());

    expect(result.current.isCoolingDown).toBe(true);
    expect(result.current.remaining).toBe(3);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remaining).toBe(2);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remaining).toBe(1);

    // The last tick must clear, not stop at 1: a button stuck on "in 1s" never comes back.
    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remaining).toBe(0);
    expect(result.current.isCoolingDown).toBe(false);
  });

  it('restarting does not stack two timers and count down twice as fast', () => {
    const { result } = renderHook(() => useResendCooldown(10));

    act(() => result.current.start());
    act(() => result.current.start());

    act(() => vi.advanceTimersByTime(1000));

    // Two live intervals would leave 8 here instead of 9.
    expect(result.current.remaining).toBe(9);
  });

  it('stops its timer on unmount so a navigated-away page cannot keep ticking', () => {
    const { result, unmount } = renderHook(() => useResendCooldown(60));

    act(() => result.current.start());
    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});
