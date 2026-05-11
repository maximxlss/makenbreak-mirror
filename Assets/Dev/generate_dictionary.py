"""Generate a Russian Erudit/Scrabble-style dictionary from pymorphy3.

The output contains all dictionary forms of common Russian nouns, uppercase,
without YO, punctuation, hyphens, digits, proper names, geography,
abbreviations, and stylistically marked non-classic entries.
"""

from __future__ import annotations

import re
from pathlib import Path

import pymorphy3


FORMS_OUTPUT_PATH = Path("words.txt")
LEMMAS_OUTPUT_PATH = Path("lemmas.txt")
CYRILLIC_WORD_RE = re.compile(r"^[а-яё]+$")
BANNED_GRAMMEMES = {
    "Abbr",
    "Arch",
    "Dist",
    "Erro",
    "Name",
    "Surn",
    "Patr",
    "Geox",
    "Infr",
    "Orgn",
    "Slng",
    "Trad",
    "Vulg",
}


def is_allowed_noun_form(word: str, tag: object) -> bool:
    tag_grammemes = set(tag.grammemes)

    if tag.POS != "NOUN":
        return False
    if tag_grammemes & BANNED_GRAMMEMES:
        return False
    if len(word) < 2:
        return False
    return bool(CYRILLIC_WORD_RE.fullmatch(word))


def is_allowed_noun_lemma(normal_form: str, tag: object) -> bool:
    if "nomn" not in set(tag.grammemes):
        return False
    return is_allowed_noun_form(normal_form, tag)


def normalize_word(word: str) -> str:
    return word.replace("ё", "е").upper()


def generate_words() -> tuple[list[str], list[str]]:
    morph = pymorphy3.MorphAnalyzer()
    forms: set[str] = set()
    lemmas: set[str] = set()

    for word, tag, normal_form, _para_id, _idx in morph.dictionary.iter_known_words():
        if is_allowed_noun_form(word, tag):
            normalized = normalize_word(word)
            if re.fullmatch(r"^[А-Я]+$", normalized):
                forms.add(normalized)

        if is_allowed_noun_lemma(normal_form, tag):
            normalized = normalize_word(normal_form)
            if re.fullmatch(r"^[А-Я]+$", normalized):
                lemmas.add(normalized)

    return sorted(forms), sorted(lemmas)


def validate_words(words: list[str]) -> None:
    if len(words) != len(set(words)):
        raise ValueError("duplicate words found")
    if words != sorted(words):
        raise ValueError("words are not sorted")

    invalid = [word for word in words if not re.fullmatch(r"^[А-Я]+$", word)]
    if invalid:
        sample = ", ".join(invalid[:10])
        raise ValueError(f"invalid output words: {sample}")


def main() -> None:
    forms, lemmas = generate_words()
    validate_words(forms)
    validate_words(lemmas)

    FORMS_OUTPUT_PATH.write_text("\n".join(forms) + "\n", encoding="utf-8")
    LEMMAS_OUTPUT_PATH.write_text("\n".join(lemmas) + "\n", encoding="utf-8")

    print(f"Wrote {len(forms)} word forms to {FORMS_OUTPUT_PATH}")
    print(f"Wrote {len(lemmas)} lemmas to {LEMMAS_OUTPUT_PATH}")


if __name__ == "__main__":
    main()
