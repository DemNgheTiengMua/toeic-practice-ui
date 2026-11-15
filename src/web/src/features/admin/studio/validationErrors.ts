import { ApiClientError } from '../../../lib/api';

/**
 * Extracts a flat camelCase-keyed field error map from an RFC 7807 ApiClientError.
 */
export function extractFieldErrors(err: unknown): Record<string, string> {
  if (!(err instanceof ApiClientError) || !err.errors) {
    return {};
  }

  const mapped: Record<string, string> = {};
  for (const [rawKey, messages] of Object.entries(err.errors)) {
    if (!messages || messages.length === 0) continue;
    const key = rawKey.length > 0 ? rawKey[0].toLowerCase() + rawKey.slice(1) : rawKey;
    mapped[key] = messages[0];
  }
  return mapped;
}

export function extractErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof ApiClientError) {
    return err.detail || err.title || fallback;
  }
  if (err instanceof Error && err.message) {
    return err.message;
  }
  return fallback;
}
