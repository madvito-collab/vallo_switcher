namespace Vallo.Core;

/// Тексты слоёв из Фигмы — те же шаги, что у выделенного текста:
/// опечатки → «ё» → запятые (без заглавной и точки — в макете это решение редполитики) → типографика.
/// Одинаковые тексты обрабатываются один раз, ответы помнятся между нажатиями,
/// модель запятых читает несколько текстов сразу. Перенос `Sources/ValloSwitcher/FigmaBatch.swift`.
public sealed class FigmaBatch(CommaService commas)
{
    /// Сколько текстов модель читает одновременно.
    public const int Parallel = 4;
    /// Больше стольких запомненных текстов — память сбрасывается.
    public const int CacheLimit = 5000;

    readonly object gate = new();
    int running;

    /// Текст слоя → результат и что в нём исправлено.
    readonly Dictionary<string, (string Text, FixStats Stats)> cache = new();
    /// Настройки, при которых получены запомненные ответы: поменялись — ответы устарели.
    (bool Yo, HashSet<string> Ignored)? cacheSettings;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    /// Правки только для изменившихся слоёв и общая статистика. null — уже идёт другой пакет.
    public async Task<(List<FigmaTextEdits> Edits, FixStats Stats)?> RunAsync(
        IReadOnlyList<FigmaText> texts, TypoCorrector typos, Yoficator yo, bool yoEnabled)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            DebugLog.Shared.Write("figma: пакет уже идёт — нажатие пропущено");
            return null;
        }
        try
        {
            var started = Environment.TickCount64;
            var todo = new List<string>();
            Dictionary<string, (string Text, FixStats Stats)> known;
            lock (gate)
            {
                var ignored = typos.Ignored;
                if (cacheSettings is not { } s || s.Yo != yoEnabled || !s.Ignored.SetEquals(ignored) || cache.Count > CacheLimit)
                {
                    cache.Clear();
                    cacheSettings = (yoEnabled, new HashSet<string>(ignored));
                }
                // что ещё не обрабатывали — каждый текст по одному разу
                var seen = new HashSet<string>();
                foreach (var item in texts)
                    if (!cache.ContainsKey(item.Text) && seen.Add(item.Text)) todo.Add(item.Text);
                known = new Dictionary<string, (string, FixStats)>(cache);
            }

            // опечатки и «ё» — быстро, по очереди; модель — в несколько потоков
            var fresh = new Dictionary<string, (string Text, FixStats Stats)>();
            using var slots = new SemaphoreSlim(Parallel);
            var jobs = todo.Select(async text =>
            {
                var typoFixed = typos.CorrectPhrase(text);
                var yoFixed = yoEnabled ? yo.FixPhrase(typoFixed) : typoFixed;
                // в одном слове запятой взяться неоткуда — модель не будим
                var needsCommas = HasTwoWords(yoFixed);
                string? withCommas = null;
                if (needsCommas)
                {
                    await slots.WaitAsync().ConfigureAwait(false);
                    try { withCommas = await commas.FixAsync(yoFixed, typos, capitalize: false).ConfigureAwait(false); }
                    finally { slots.Release(); }
                }
                var done = FixPipeline.Finish(text, typoFixed, yoFixed, withCommas);
                lock (gate)
                {
                    fresh[text] = done;
                    // модель опоздала или её нет — ответ не запоминаем: в следующий раз попробуем снова
                    if (withCommas is not null || !needsCommas) cache[text] = done;
                }
            }).ToList();
            await Task.WhenAll(jobs).ConfigureAwait(false);

            var edits = new List<FigmaTextEdits>();
            var total = new FixStats();
            foreach (var item in texts)
            {
                if (!fresh.TryGetValue(item.Text, out var r) && !known.TryGetValue(item.Text, out r)) continue;
                if (r.Text != item.Text) edits.Add(new FigmaTextEdits(item.Id, TextDiff.Edits(item.Text, r.Text)));
                total.Add(r.Stats);
            }
            var ms = Environment.TickCount64 - started;
            DebugLog.Shared.Write(() => $"figma: пакет {texts.Count} текстов, обработано {todo.Count}, из памяти {texts.Count - todo.Count}, изменено {edits.Count}, {ms} мс");
            return (edits, total);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    public static bool HasTwoWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) >= 2;
}
