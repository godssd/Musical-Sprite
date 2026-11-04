# Video Analysis Brief

## Source

- Input: `C:/Users/Administrator/Downloads/QQ20261030-8958.mp4`
- Source kind: `local-file`
- Duration: `50.82939` seconds
- Resolution: `1358x1082`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-8958\QQ20261030-8958-20261030-082510\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-8958\QQ20261030-8958-20261030-082510\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-01-00-00-02_033.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-02-00-00-04_066.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-03-00-00-06_100.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-04-00-00-08_133.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-05-00-00-10_166.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-06-00-00-12_199.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-07-00-00-14_232.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-08-00-00-16_265.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-09-00-00-18_299.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-10-00-00-20_332.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-11-00-00-22_365.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-12-00-00-24_398.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-13-00-00-26_431.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-14-00-00-28_464.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-15-00-00-30_498.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-16-00-00-32_531.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-17-00-00-34_564.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-18-00-00-36_597.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-19-00-00-38_630.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-20-00-00-40_664.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-21-00-00-42_697.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-22-00-00-44_730.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-23-00-00-46_763.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\VideoReview-20261030-8958\\QQ20261030-8958-20261030-082510\\frames\\frame-24-00-00-48_796.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\VideoReview-20261030-8958\QQ20261030-8958-20261030-082510\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
