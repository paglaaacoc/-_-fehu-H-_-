# Quranic Arabic Corpus morphological annotations — attribution

The optional `corpus-lemma-positions.sqlite` file is a locally derived, read-only **linguistic word-position annotation index**. It does not replace or modify any canonical Qur’an text, verse, research note, or user data.

- Annotation source: Quranic Arabic Corpus morphological annotations v0.4, copyright © 2011 Kais Dukes, https://corpus.quran.com/download/.
- Source license: GNU General Public License, as stated by the corpus publisher; see the publisher's download page for authoritative license terms. This annotation layer must retain appropriate provenance/attribution when shared. The annotation does **not** imply that unrelated app and original Qur’an sources are licensed under the same terms.
- Exact upstream annotation bytes: `quranic-corpus-morphology-0.4.txt`, Git blob SHA-1 `b91cec6e95d5e0306550b4aedacc7380dc71152a`, mirrored in `taziksh/quran-frequencies` at commit `079cdc74cd494ec1b35011363288d80cae047c51`.
- Reproducible transformation source: `tools/CorpusLemmaIndexBuilder/build_lemma_positions.py` in the THTRP private source repository; transformation anchors each lemma annotation to the accepted `Corpus/word-by-word.sqlite` positional authority.
- Derived index SHA-256: `96ed3f4c1a761ffa7e9be4018fa0c191fe08e20cd11270ca79c6bba599262c2d`.
- The morphology research index is bundled only for this **personal/private workstation candidate**. Reevaluate GPL compliance and source-offer obligations before publishing or distributing the package outside that scope.

Matching types in the app are deliberately distinct: literal/normalized text, annotated lemma identity and grammatical POS, exact adjacent canonical Uthmani word sequences, and exact full-Ayah text. None constitutes fuzzy, semantic, or tafsir search.
