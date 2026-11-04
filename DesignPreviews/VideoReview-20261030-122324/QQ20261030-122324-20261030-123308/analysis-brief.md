# Video Analysis Brief

## Source

- Input: `C:/Users/Administrator/Downloads/QQ20261030-122324.mp4`
- Source kind: `local-file`
- Duration: `42.2334` seconds
- Resolution: `1362x968`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-122324\QQ20261030-122324-20261030-123308\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-122324\QQ20261030-122324-20261030-123308\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-01-00-00-01_689.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-02-00-00-03_379.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-03-00-00-05_068.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-04-00-00-06_757.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-05-00-00-08_447.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-06-00-00-10_136.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-07-00-00-11_825.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-08-00-00-13_515.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-09-00-00-15_204.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-10-00-00-16_893.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-11-00-00-18_583.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-12-00-00-20_272.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-13-00-00-21_961.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-14-00-00-23_651.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-15-00-00-25_340.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-16-00-00-27_029.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-17-00-00-28_719.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-18-00-00-30_408.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-19-00-00-32_097.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-20-00-00-33_787.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-21-00-00-35_476.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-22-00-00-37_165.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-23-00-00-38_855.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-122324\\QQ20261030-122324-20261030-123308\\frames\\frame-24-00-00-40_544.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-122324\QQ20261030-122324-20261030-123308\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
