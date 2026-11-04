# Модель запятых

`sage-fredt5-95m.zip` — релиз `models-1` в публичном репозитории
[madvito-collab/vallo-models](https://github.com/madvito-collab/vallo-models/releases/tag/models-1).

- Модель: [ai-forever/sage-fredt5-distilled-95m](https://huggingface.co/ai-forever/sage-fredt5-distilled-95m) (SAGE, Сбер), лицензия **MIT**.
- Формат: ONNX fp32, энкодер + декодер без кэша (`encoder_model.onnx`, `decoder_model.onnx`), `tokenizer.json`, `config.json`.
- Архив: 228 МБ, распакованная модель: 470 МБ.
- SHA-256 архива: `0c4af0f7f3905600c7d83eaafb27134f5d051fc80813b7be66ea61c0eba09486`
- Собрана `scripts/export-model.py` (часть fp32, без квантования).

Почему без ужатия (int8): на 34 фразах эксперимента ужатая модель после фильтра теряла запятые
примерно в каждой восьмой фразе, полная совпадает с оригиналом 34/34 и отвечает за ~30 мс. Решение автора — только точная.
