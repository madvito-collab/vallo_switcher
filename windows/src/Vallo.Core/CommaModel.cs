using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Vallo.Core;

/// Модель запятых SAGE (ONNX): энкодер + декодер без кэша, жадная генерация. Перенос `CommaModel.swift` с Мака.
/// Ответ потом проходит через `CommaFilter`. Прогоны можно звать из нескольких потоков сразу.
public sealed class CommaModel : IDisposable
{
    public const int MaxTokens = 128;
    const long DecoderStart = 0;
    const long EndOfText = 2;
    /// Служебные токены, которые не выводим: <pad>, <s>, </s>, <LM>.
    static readonly HashSet<int> Special = [0, 1, 2, 50357];

    readonly Gpt2Tokenizer tokenizer;
    readonly InferenceSession encoder;
    readonly InferenceSession decoder;
    readonly int vocab;

    CommaModel(Gpt2Tokenizer tokenizer, InferenceSession encoder, InferenceSession decoder, int vocab)
    {
        this.tokenizer = tokenizer;
        this.encoder = encoder;
        this.decoder = decoder;
        this.vocab = vocab;
    }

    /// Папка распакованной модели: encoder_model.onnx, decoder_model.onnx, tokenizer.json, config.json.
    public static CommaModel Load(string directory)
    {
        var tokenizer = Gpt2Tokenizer.Load(Path.Combine(directory, "tokenizer.json"));
        var options = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING };
        var encoder = new InferenceSession(Path.Combine(directory, "encoder_model.onnx"), options);
        InferenceSession decoder;
        try { decoder = new InferenceSession(Path.Combine(directory, "decoder_model.onnx"), options); }
        catch { encoder.Dispose(); throw; }
        var vocab = 50364;
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "config.json")));
            if (config.RootElement.TryGetProperty("vocab_size", out var v)) vocab = v.GetInt32();
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new CommaModel(tokenizer, encoder, decoder, vocab);
    }

    static DenseTensor<long> Tensor(IReadOnlyCollection<long> values) => new(values.ToArray(), [1, values.Count]);

    /// Ответ модели на фразу (как есть, без фильтра). Не на потоке перехвата.
    /// Жадная генерация, но продолжение угадывается по входу (`DraftLookup`) и проверяется пачкой:
    /// один прогон декодера принимает сразу несколько токенов — ответ тот же, прогонов в 2–3 раза меньше.
    public string Run(string text)
    {
        var ids = tokenizer.Encode(text);
        var mask = Tensor(Enumerable.Repeat(1L, ids.Length).ToArray());
        using var encoded = encoder.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", Tensor(ids)),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        ], ["last_hidden_state"]);
        var hidden = encoded.First().AsTensor<float>();
        var output = new List<long>();
        while (output.Count < MaxTokens)
        {
            var draft = DraftLookup.Draft(output, ids);
            List<long> input = [DecoderStart, .. output, .. draft];
            using var result = decoder.Run([
                NamedOnnxValue.CreateFromTensor("input_ids", Tensor(input)),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
            ], ["logits"]);
            var logits = result.First().AsTensor<float>() as DenseTensor<float>;
            if (logits is null) break;
            var data = logits.Buffer.Span;
            // строка logits № base + j предсказывает токен ответа № base + j
            var @base = output.Count;
            var stop = false;
            for (var j = 0; j <= draft.Length; j++)
            {
                var next = Argmax(data, @base + j, vocab);
                if (next == EndOfText) { stop = true; break; }
                output.Add(next);
                if (output.Count >= MaxTokens) { stop = true; break; }
                if (j == draft.Length || next != draft[j]) break;
            }
            if (stop) break;
        }
        return tokenizer.Decode(output.Select(t => (int)t).Where(t => !Special.Contains(t)));
    }

    /// Самый вероятный токен в строке `row` таблицы logits.
    static long Argmax(ReadOnlySpan<float> data, int row, int vocab)
    {
        var start = row * vocab;
        var best = 0;
        for (var i = 1; i < vocab; i++) if (data[start + i] > data[start + best]) best = i;
        return best;
    }

    public void Dispose()
    {
        encoder.Dispose();
        decoder.Dispose();
    }
}
