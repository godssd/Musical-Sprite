# Video Analysis Brief

## Source

- Input: `C:\Users\Administrator\Desktop\QQ20261018-92920.mp4`
- Source kind: `local-file`
- Duration: `11.71017` seconds
- Resolution: `1368x800`
- Video codec: `h264`
- Audio present: `True`

## Evidence Artifacts

- `ffprobe_json`: `D:\unity\plan go\Musical Sprite\DesignPreviews\cult-fog-video-analysis\QQ20261018-92920-20261022-142009\ffprobe.json`
- `quality_gates`: `D:\unity\plan go\Musical Sprite\DesignPreviews\cult-fog-video-analysis\QQ20261018-92920-20261022-142009\quality-gates.json`
- `frames`: `['D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-01-00-00-00_689.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-02-00-00-01_378.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-03-00-00-02_067.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-04-00-00-02_755.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-05-00-00-03_444.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-06-00-00-04_133.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-07-00-00-04_822.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-08-00-00-05_511.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-09-00-00-06_200.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-10-00-00-06_888.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-11-00-00-07_577.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-12-00-00-08_266.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-13-00-00-08_955.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-14-00-00-09_644.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-15-00-00-10_333.jpg', 'D:\\unity\\plan go\\Musical Sprite\\DesignPreviews\\cult-fog-video-analysis\\QQ20261018-92920-20261022-142009\\frames\\frame-16-00-00-11_021.jpg']`
- `contact_sheet`: `D:\unity\plan go\Musical Sprite\DesignPreviews\cult-fog-video-analysis\QQ20261018-92920-20261022-142009\contact-sheet.jpg`

## Evidence Rules

- Metadata supports file, stream, duration, and codec claims.
- Sampled frames and the contact sheet support visible-scene claims only for sampled timestamps.
- OCR is weaker than subtitles or ASR and may contain recognition errors.
- Spoken wording requires subtitle or ASR evidence.
- If a claim is not supported by an artifact, label it as unsupported.

## Initial Conclusion

A reusable artifact set exists. Review `contact-sheet.jpg`, `frames/`, subtitles or ASR outputs, and `quality-gates.json` before making content claims.
