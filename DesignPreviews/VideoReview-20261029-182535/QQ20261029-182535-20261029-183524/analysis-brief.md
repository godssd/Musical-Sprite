# Video Analysis Brief

## Source

- Input: `C:/Users/Administrator/Downloads/QQ20261029-182535.mp4`
- Source kind: `local-file`
- Duration: `42.96852` seconds
- Resolution: `1358x1082`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-182535\QQ20261029-182535-20261029-183524\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-182535\QQ20261029-182535-20261029-183524\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-01-00-00-01_719.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-02-00-00-03_437.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-03-00-00-05_156.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-04-00-00-06_875.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-05-00-00-08_594.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-06-00-00-10_312.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-07-00-00-12_031.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-08-00-00-13_750.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-09-00-00-15_469.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-10-00-00-17_187.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-11-00-00-18_906.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-12-00-00-20_625.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-13-00-00-22_344.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-14-00-00-24_062.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-15-00-00-25_781.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-16-00-00-27_500.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-17-00-00-29_219.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-18-00-00-30_937.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-19-00-00-32_656.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-20-00-00-34_375.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-21-00-00-36_094.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-22-00-00-37_812.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-23-00-00-39_531.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261029-182535\\QQ20261029-182535-20261029-183524\\frames\\frame-24-00-00-41_250.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261029-182535\QQ20261029-182535-20261029-183524\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
