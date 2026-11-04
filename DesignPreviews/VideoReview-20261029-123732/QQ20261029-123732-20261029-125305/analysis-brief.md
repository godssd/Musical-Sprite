# Video Analysis Brief

## Source

- Input: `C:/Users/Administrator/Downloads/QQ20261029-123732.mp4`
- Source kind: `local-file`
- Duration: `39.23672` seconds
- Resolution: `1358x1082`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-123732\QQ20261029-123732-20261029-125305\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-123732\QQ20261029-123732-20261029-125305\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-01-00-00-01_569.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-02-00-00-03_139.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-03-00-00-04_708.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-04-00-00-06_278.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-05-00-00-07_847.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-06-00-00-09_417.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-07-00-00-10_986.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-08-00-00-12_556.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-09-00-00-14_125.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-10-00-00-15_695.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-11-00-00-17_264.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-12-00-00-18_834.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-13-00-00-20_403.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-14-00-00-21_973.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-15-00-00-23_542.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-16-00-00-25_112.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-17-00-00-26_681.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-18-00-00-28_250.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-19-00-00-29_820.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-20-00-00-31_389.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-21-00-00-32_959.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-22-00-00-34_528.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-23-00-00-36_098.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-123732\\QQ20261029-123732-20261029-125305\\frames\\frame-24-00-00-37_667.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-123732\QQ20261029-123732-20261029-125305\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
