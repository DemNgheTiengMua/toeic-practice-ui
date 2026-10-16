import os

# Standard MPEG-1 Layer 3 frame: 128 kbps, 44.1 kHz, stereo, padded
# Frame size = 144 * 128000 / 44100 + 1 = 418 bytes
# Header: 0xFF, 0xFB, 0x92, 0x64 (sync 11 bits, MPEG-1, Layer 3, no CRC, 128kbps, 44.1kHz, padded, joint stereo)
HEADER = bytes([0xFF, 0xFB, 0x92, 0x64])
# 32 bytes side info for joint stereo
SIDE_INFO = bytes([0x00] * 32)
# 382 bytes main audio data (silence)
MAIN_DATA = bytes([0x00] * 382)
FRAME = HEADER + SIDE_INFO + MAIN_DATA

# Create 50 frames (~1.3 seconds of audio)
SILENCE_MP3 = FRAME * 50

paths = [
    "src/WebMVC/wwwroot/audio/toeic/placement-p1.mp3",
    "src/WebMVC/wwwroot/audio/toeic/placement-p2.mp3",
    "src/WebMVC/wwwroot/audio/toeic/placement-quick.mp3",
    "src/WebMVC/wwwroot/audio/toeic/test1-p1.mp3",
    "src/WebMVC/wwwroot/audio/toeic/test1-p2.mp3",
    "src/WebMVC/wwwroot/audio/toeic/test1-p3.mp3",
    "src/WebMVC/wwwroot/audio/toeic/test1-p4.mp3",
    "src/WebMVC/wwwroot/audio/ielts/cambridge18-t1-listening.mp3",
    "src/WebMVC/wwwroot/audio/ielts/placement-diagnostic.mp3",
    "src/WebMVC/wwwroot/audio/vstep/sample1-listening.mp3",
    "src/WebMVC/wwwroot/audio/vstep/placement-diagnostic.mp3",
]

for p in paths:
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "wb") as f:
        f.write(SILENCE_MP3)
    print(f"Generated {p} ({len(SILENCE_MP3)} bytes)")
