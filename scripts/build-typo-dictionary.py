#!/usr/bin/env python3
"""Собирает Data/wordlists/ru-typos.txt — словарь исправлятора опечаток:
разговорные слова (субтитры, Data/wordlists/ru.txt) + письменные (частоты русской Википедии).
Частота слова = максимум из двух словарей (на миллион слов) × 1000, 150 000 самых частых.

Запуск: python3 scripts/build-typo-dictionary.py путь/к/ruwiki-2022-08-29.txt
Источник Википедии: https://github.com/IlyaSemenov/wikipedia-word-frequency (results/ruwiki-2022-08-29.txt)
"""
import sys

ALPH = set("абвгдежзийклмнопрстуфхцчшщъыьэюя")

def load(path, limit):
    out = {}
    for line in open(path, encoding="utf-8"):
        parts = line.split()
        if len(parts) != 2:
            continue
        w = parts[0].lower().replace("ё", "е")
        if w and all(c in ALPH for c in w) and w not in out:
            out[w] = int(parts[1])
            if len(out) >= limit:
                break
    return out

sub = load("Data/wordlists/ru.txt", 50_000)
wiki = load(sys.argv[1], 300_000)
st, wt = sum(sub.values()), sum(wiki.values())
merged = {w: c / st * 1e6 for w, c in sub.items()}
for w, c in wiki.items():
    merged[w] = max(merged.get(w, 0), c / wt * 1e6)
top = sorted(merged, key=lambda w: -merged[w])[:150_000]
with open("Data/wordlists/ru-typos.txt", "w", encoding="utf-8") as f:
    for w in top:
        f.write(f"{w} {max(1, round(merged[w] * 1000))}\n")
print(f"Data/wordlists/ru-typos.txt: {len(top)} слов")
