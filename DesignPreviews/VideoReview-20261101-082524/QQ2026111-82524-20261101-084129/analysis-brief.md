# Video Analysis Brief

## Source

- Input: `C:/Users/Administrator/Downloads/QQ2026111-82524.mp4`
- Source kind: `local-file`
- Duration: `42.66` seconds
- Resolution: `1358x1080`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261101-082524\QQ2026111-82524-20261101-084129\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261101-082524\QQ2026111-82524-20261101-084129\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-01-00-00-03_282.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-02-00-00-06_563.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-03-00-00-09_845.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-04-00-00-13_126.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-05-00-00-16_408.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-06-00-00-19_689.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-07-00-00-22_971.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-08-00-00-26_252.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-09-00-00-29_534.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-10-00-00-32_815.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-11-00-00-36_097.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261101-082524\\QQ2026111-82524-20261101-084129\\frames\\frame-12-00-00-39_378.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261101-082524\QQ2026111-82524-20261101-084129\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
