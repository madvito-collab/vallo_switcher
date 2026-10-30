#!/usr/bin/env python3
"""Разовый экспорт модели запятых SAGE (ai-forever/sage-fredt5-distilled-95m, MIT) в ONNX int8.
Запускается в отдельном Python-окружении с optimum[onnxruntime] (не входит в сборку приложения).

    python export-model.py <папка-вывода>

Результат: encoder_model.onnx, decoder_model.onnx, decoder_with_past_model.onnx (int8), tokenizer.json, config.json.
"""
import sys, shutil, pathlib
from optimum.onnxruntime import ORTModelForSeq2SeqLM, ORTQuantizer
from optimum.onnxruntime.configuration import AutoQuantizationConfig
from transformers import AutoTokenizer

MODEL = "ai-forever/sage-fredt5-distilled-95m"
out = pathlib.Path(sys.argv[1])
fp32 = out / "fp32"
int8 = out / "int8"

model = ORTModelForSeq2SeqLM.from_pretrained(MODEL, export=True)
model.save_pretrained(fp32)
AutoTokenizer.from_pretrained(MODEL).save_pretrained(fp32)

qconfig = AutoQuantizationConfig.arm64(is_static=False, per_channel=False)
int8.mkdir(parents=True, exist_ok=True)
for name in ["encoder_model.onnx", "decoder_model.onnx", "decoder_with_past_model.onnx"]:
    if (fp32 / name).exists():
        q = ORTQuantizer.from_pretrained(fp32, file_name=name)
        q.quantize(save_dir=int8, quantization_config=qconfig)
for f in fp32.iterdir():
    if f.suffix != ".onnx" and not (int8 / f.name).exists():
        shutil.copy(f, int8 / f.name)
# квантизатор дописывает «_quantized» к имени — возвращаем обычные имена
for f in int8.glob("*_quantized.onnx"):
    f.rename(int8 / f.name.replace("_quantized", ""))
print("готово:", int8)
