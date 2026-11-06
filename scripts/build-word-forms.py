#!/usr/bin/env python3
"""Все формы русских слов (3 млн) — чтобы исправлятор опечаток не трогал правильные слова.

Источник — морфологический словарь OpenCorpora (CC BY-SA 3.0) в упаковке pymorphy3-dicts-ru.
Результат — Data/wordlists/ru-forms.txt.gz: слова строчными, «ё» → «е», только кириллица, по одному на строку,
отсортированы по байтам UTF-8 (свитчер ищет в файле двоичным поиском, не загружая его в память).
Слово, которое бывает только именем собственным (Тамбов, Фрейдом, Славка), записано со «*» в конце: его не исправляем,
но и опечатку в него не превращаем («табов» ≠ «тамбов»).

Запуск (один раз, если словарь надо обновить):
    python3 -m venv /tmp/morph && /tmp/morph/bin/pip install pymorphy3 pymorphy3-dicts-ru
    /tmp/morph/bin/python scripts/build-word-forms.py
"""
import gzip
import pathlib

import pymorphy3

out = pathlib.Path(__file__).resolve().parent.parent / "Data/wordlists/ru-forms.txt.gz"
morph = pymorphy3.MorphAnalyzer()
PROPER = {"Name", "Surn", "Patr", "Geox", "Orgn", "Trad"}
common, proper = set(), set()
for word, tag, *_ in morph.dictionary.iter_known_words():
    w = word.lower().replace("ё", "е")
    if not w or not all("а" <= c <= "я" for c in w):
        continue
    (proper if PROPER & set(tag.grammemes) else common).add(w)
words = common | {w + "*" for w in proper - common}
data = "\n".join(sorted(words, key=lambda s: s.encode("utf-8"))).encode("utf-8") + b"\n"
# mtime=0 — одинаковый файл при одинаковом словаре, без лишних изменений в git
with open(out, "wb") as f, gzip.GzipFile(fileobj=f, mode="wb", compresslevel=9, mtime=0, filename="") as gz:
    gz.write(data)
print(f"{out}: {len(words)} форм")
