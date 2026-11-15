import '@testing-library/jest-dom/vitest';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { studioApi } from '../studioApi';
import { EXAM_MEDIA_EXTENSIONS } from '../types';

/**
 * #359: the media door. The server names the file and answers the path; the client's only job is to
 * send the bytes to the exam's own directory and hand the answered path back. A path composed on the
 * client would be a second writer of a fact the server owns.
 */
function installFetchMock(handler: (url: string, init?: RequestInit) => Response) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    return handler(url, init);
  });
  vi.stubGlobal('fetch', mock);
  return mock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('exam media upload (#359)', () => {
  it('posts the file as multipart to the exam media route and returns the answered path', async () => {
    const fetchMock = installFetchMock((url) => {
      expect(url).toBe('/api/admin/exams/42/media');
      return new Response(
        JSON.stringify({
          url: '/media/exams/42/0123456789abcdef0123456789abcdef.mp3',
          path: 'exams/42/0123456789abcdef0123456789abcdef.mp3',
          fileName: '0123456789abcdef0123456789abcdef.mp3',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    });

    const file = new File(['audio-bytes'], 'part1.mp3', { type: 'audio/mpeg' });
    const result = await studioApi.uploadExamMedia(42, file);

    const [, init] = fetchMock.mock.calls[0];
    expect(init?.method).toBe('POST');
    // The body must be the FormData carrying the file, not JSON: this route reads multipart.
    const body = init?.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get('file')).toBeInstanceOf(File);

    expect(result.path).toBe('exams/42/0123456789abcdef0123456789abcdef.mp3');
    expect(result.url).toBe('/media/exams/42/0123456789abcdef0123456789abcdef.mp3');
  });

  it('records the answered path as the section audio, in that section', async () => {
    const fetchMock = installFetchMock((url) => {
      if (url === '/api/admin/exams/42/media') {
        return new Response(
          JSON.stringify({
            url: '/media/exams/42/abc.mp3',
            path: 'exams/42/abc.mp3',
            fileName: 'abc.mp3',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    const file = new File(['audio-bytes'], 'part1.mp3', { type: 'audio/mpeg' });
    const uploaded = await studioApi.uploadExamMedia(42, file);
    await studioApi.saveExamAudio(42, {
      sectionId: 7,
      audioPath: uploaded.path,
      durationSec: 180,
      sizeBytes: file.size,
    });

    const audioCall = fetchMock.mock.calls.find(([url]) => url === '/api/admin/exams/42/audio');
    expect(audioCall).toBeDefined();
    const sent = JSON.parse(String(audioCall![1]?.body));
    expect(sent).toEqual({
      sectionId: 7,
      audioPath: 'exams/42/abc.mp3',
      durationSec: 180,
      sizeBytes: file.size,
    });
  });

  it('does not offer video, because the schema has no column for it', () => {
    for (const ext of EXAM_MEDIA_EXTENSIONS) {
      expect(['.mp4', '.webm', '.mov', '.avi']).not.toContain(ext);
    }
    // The server's allow-list is the same set; a client-only wider list would promise an upload the
    // route then refuses.
    expect(EXAM_MEDIA_EXTENSIONS).toContain('.mp3');
    expect(EXAM_MEDIA_EXTENSIONS).toContain('.png');
  });
});
