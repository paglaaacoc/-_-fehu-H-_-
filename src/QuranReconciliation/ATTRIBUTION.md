# Third-Party Attribution

This private research workstation includes third-party Qur'an data and font resources that remain subject to their own terms and attribution requirements.

## Quran Foundation font

**IndoPak Nastaleeq / AlQuran IndoPak by QuranWBW**  
Bundled filename: `Fonts/indopak-nastaleeq-waqf-lazim-v4.2.1.ttf`

Font file provided through Quran Foundation's documented Qur'an font distribution endpoint and bundled unmodified as an integrated part of this application.

Credit: **Quran fonts provided by Quran Foundation.**

The font is used to render the Quran Foundation IndoPak / IndoPak Nastaleeq text fields with their intended glyph coverage. It is not offered separately by this project.

Source documentation:
- Quran Foundation — Integrating Quran Font Rendering
- Versioned font: `indopak-nastaleeq-waqf-lazim-v4.2.1.ttf`

Qur'an text, translations, tafsir/commentary resources, and other imported source material remain attributed to their respective source providers and are not relicensed by NivareQ.


## Quran Foundation Uthmani Hafs font

**Uthmanic Hafs / KFGQPC HAFS Uthmanic Script**  
Bundled filename: `Fonts/UthmanicHafs1Ver18.ttf`

The font is distributed through Quran Foundation's documented Qur'an font endpoint and is bundled unmodified for the workstation's Unicode Uthmani display. The immutable stored Qur'an text is not rewritten for font rendering.

Credit: **Quran fonts provided by Quran Foundation / King Fahd Glorious Quran Printing Complex.**

Reference:
- Quran Foundation — Integrating Quran Font Rendering
- Versioned font: `UthmanicHafs1Ver18.ttf`

Pinned Uthmani font SHA-256: `a0636e68e375af9552470d67773936f54d536e6586ce2608311b2fe7f9cbec3a`.


## Quran Foundation word-by-word research data

Build 10 can bundle a separate immutable supplementary local database:

`Corpus/word-by-word.sqlite`

It contains Quran Foundation / Quran.com word-level Uthmani text, English word-by-word glosses, and transliteration acquired through the documented verse content API with `words=true`.

Credit: **Quran data provided by Quran Foundation.**

This supplementary resource is used only as a reconciliation aid. Its one-word glosses are not presented as exact character-level alignment to any full-ayah translation. The primary verified `Corpus/corpus.sqlite` remains byte-identical and separate.


## Quran Foundation Juz boundary metadata

v1.1 bundles a separate immutable structural metadata resource:

`Corpus/juz-map.json`

It contains the 30 traditional Juz start/end verse-key boundaries used only for navigation and structural annotation.

Source: **Quran Foundation / Quran.com Content API v4 — List Juzs**.

Pinned source endpoint used for the snapshot:
`https://api.quran.com/api/v4/juzs`

Pinned Juz metadata SHA-256:
`3016bb4f94af9a42c8c1c24728bf6044e5b4ac13130e47d451c0c10ba35dd434`

The Juz resource does not modify the primary Qur'an corpus, translations, tafsir, or owner research database. Context Blocks remain semantic/discourse units and are never forced to split at Juz boundaries.
