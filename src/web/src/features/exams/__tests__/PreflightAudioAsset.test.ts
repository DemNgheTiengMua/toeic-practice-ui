import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';

describe('preflight-test.mp3 static asset', () => {
  const assetPath = path.resolve(__dirname, '../../../../public/audio/preflight-test.mp3');

  it('exists in public/audio and is non-empty', () => {
    expect(fs.existsSync(assetPath)).toBe(true);
    const stat = fs.statSync(assetPath);
    expect(stat.size).toBeGreaterThan(1024);
  });

  it('contains a valid MP3 header (ID3v2 + MPEG frame sync or raw MPEG frame sync)', () => {
    const buf = fs.readFileSync(assetPath);
    const hasId3Header = buf[0] === 0x49 && buf[1] === 0x44 && buf[2] === 0x33; // "ID3"
    const hasRawFrameSync = buf[0] === 0xff && (buf[1] & 0xe0) === 0xe0;

    expect(hasId3Header || hasRawFrameSync).toBe(true);

    if (hasId3Header) {
      // ID3v2 tag size is stored in bytes 6..9 as a 28-bit synchsafe integer
      const tagSize =
        ((buf[6] & 0x7f) << 21) |
        ((buf[7] & 0x7f) << 14) |
        ((buf[8] & 0x7f) << 7) |
        (buf[9] & 0x7f);
      const frameOffset = 10 + tagSize;
      expect(buf.length).toBeGreaterThan(frameOffset + 2);
      expect(buf[frameOffset]).toBe(0xff);
      expect(buf[frameOffset + 1] & 0xe0).toBe(0xe0);
    }
  });
});
