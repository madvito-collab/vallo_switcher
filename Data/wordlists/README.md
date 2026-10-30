# Словари

`en.txt`, `ru.txt` — частотные списки слов из субтитров OpenSubtitles 2018,
проект [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) (`content/2018/{en,ru}/*_50k.txt`).
Лицензия содержимого: CC-BY-SA-4.0.

Формат: «слово количество» на строку, самые частые сверху.
Свитчер берёт первые 20 000 слов своего алфавита: дальше в списках много мусора.

`ru-typos.txt` — словарь исправлятора опечаток: `ru.txt` (разговорные слова) + частоты слов русской Википедии
([IlyaSemenov/wikipedia-word-frequency](https://github.com/IlyaSemenov/wikipedia-word-frequency), `results/ruwiki-2022-08-29.txt`,
код — MIT, текст Википедии — CC BY-SA). Частота = максимум из двух словарей на миллион слов × 1000, 150 000 слов.
Пересобрать: `python3 scripts/build-typo-dictionary.py путь/к/ruwiki-2022-08-29.txt`.
