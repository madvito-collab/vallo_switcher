#!/usr/bin/env python3
"""Словарь «ё» для Vallo Switcher: однозначные слова из eyo-kernel (MIT), все формы по одной на строку.

    python3 scripts/build-yo-dictionary.py safe.txt not_safe.txt > Data/wordlists/ru-yo.txt

safe.txt, not_safe.txt — dictionary/ из github.com/e2yo/eyo-kernel.
Строки с «_» в начале и всё после «#» отбрасываем. Форма, которая (через «е») есть и среди двойных
(not_safe: все/всё, Королев/Королёв), выкидывается — сомневаешься, не ставь.
"""
import re
import sys

ENTRY = re.compile(r"^([^()]*)(?:\(([^()]*)\))?([^()]*)$")


def forms(path, keep_marked):
    out = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.split("#", 1)[0].strip()
            if not line:
                continue
            if line.startswith("_"):
                if not keep_marked:
                    continue
                line = line[1:]
            m = ENTRY.match(line)
            if not m:
                raise SystemExit(f"не разобрал строку: {line}")
            head, variants, tail = m.groups()
            for v in (variants.split("|") if variants is not None else [""]):
                out.add(head + v + tail)
    return out


def plain(word):
    return word.lower().replace("ё", "е")


def main():
    safe_path, not_safe_path = sys.argv[1:3]
    safe = forms(safe_path, keep_marked=False)
    # помеченные «_» в safe — тоже сомнительные: их «е»-формы не должны получить «ё» и из других записей
    doubtful = {plain(w) for w in forms(not_safe_path, keep_marked=True)}
    doubtful |= {plain(w) for w in forms(safe_path, keep_marked=True) - safe}
    for word in sorted(w for w in safe if plain(w) not in doubtful and "ё" in w.lower()):
        print(word)


if __name__ == "__main__":
    main()
