using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vallo.Core;

/// Токенайзер модели запятых: BPE по байтам, как у GPT-2 (`tokenizer.json` модели: ByteLevel, без пробела в начале).
/// На Маке это делает библиотека swift-transformers; здесь — своё, сверено с официальным токенайзером HuggingFace
/// (`Tests/Data/comma-golden.tsv`).
public sealed class Gpt2Tokenizer
{
    /// Предразбивка GPT-2: сокращения, слова, числа, знаки, пробелы.
    static readonly Regex Pre = new(@"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+", RegexOptions.Compiled);

    /// Байт → «печатный» символ и обратно (таблица GPT-2: служебные байты сдвинуты за 255).
    static readonly char[] ByteToChar = BuildByteMap();
    static readonly Dictionary<char, byte> CharToByte = ByteToChar.Select((c, b) => (c, b)).ToDictionary(p => p.c, p => (byte)p.b);

    static char[] BuildByteMap()
    {
        var map = new char[256];
        var n = 0;
        for (var b = 0; b < 256; b++)
        {
            var printable = b is >= '!' and <= '~' or >= 0xA1 and <= 0xAC or >= 0xAE and <= 0xFF;
            map[b] = printable ? (char)b : (char)(256 + n++);
        }
        return map;
    }

    readonly Dictionary<string, int> vocab;
    readonly Dictionary<int, string> byId;
    readonly Dictionary<(string, string), int> ranks;
    /// Служебный токен в начале входа (TemplateProcessing модели): «<LM>».
    readonly int? prefix;
    readonly Dictionary<string, int[]> cache = new();

    Gpt2Tokenizer(Dictionary<string, int> vocab, Dictionary<int, string> byId, Dictionary<(string, string), int> ranks, int? prefix)
    {
        this.vocab = vocab;
        this.byId = byId;
        this.ranks = ranks;
        this.prefix = prefix;
    }

    public static Gpt2Tokenizer Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var model = root.GetProperty("model");
        var vocab = model.GetProperty("vocab").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var byId = vocab.ToDictionary(p => p.Value, p => p.Key);
        if (root.TryGetProperty("added_tokens", out var added))
            foreach (var t in added.EnumerateArray()) byId[t.GetProperty("id").GetInt32()] = t.GetProperty("content").GetString()!;
        var ranks = new Dictionary<(string, string), int>();
        var i = 0;
        foreach (var m in model.GetProperty("merges").EnumerateArray())
        {
            // старый формат — «a b», новый — ["a", "b"]
            (string, string) pair = m.ValueKind == JsonValueKind.Array
                ? (m[0].GetString()!, m[1].GetString()!)
                : Split(m.GetString()!);
            ranks.TryAdd(pair, i++);
        }
        int? prefix = null;
        if (root.TryGetProperty("post_processor", out var post) && post.ValueKind == JsonValueKind.Object
            && post.TryGetProperty("single", out var single))
            foreach (var part in single.EnumerateArray())
                if (part.TryGetProperty("SpecialToken", out var special) && special.TryGetProperty("id", out var name)
                    && byId.FirstOrDefault(p => p.Value == name.GetString()) is { Value: not null } hit)
                    prefix = hit.Key;
        return new Gpt2Tokenizer(vocab, byId, ranks, prefix);
    }

    static (string, string) Split(string merge)
    {
        var space = merge.IndexOf(' ');
        return (merge[..space], merge[(space + 1)..]);
    }

    /// Номера токенов входа, со служебным в начале — как `tokenizer.encode` у HuggingFace.
    public long[] Encode(string text)
    {
        var ids = new List<long>();
        if (prefix is { } p) ids.Add(p);
        foreach (Match m in Pre.Matches(text))
        {
            var mapped = new string(Encoding.UTF8.GetBytes(m.Value).Select(b => ByteToChar[b]).ToArray());
            foreach (var id in Bpe(mapped)) ids.Add(id);
        }
        return ids.ToArray();
    }

    int[] Bpe(string word)
    {
        lock (cache) if (cache.TryGetValue(word, out var hit)) return hit;
        var parts = word.Select(c => c.ToString()).ToList();
        while (parts.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < parts.Count - 1; i++)
                if (ranks.TryGetValue((parts[i], parts[i + 1]), out var r) && r < bestRank) { bestRank = r; best = i; }
            if (best < 0) break;
            var merged = new List<string>(parts.Count - 1);
            // как в GPT-2: склеиваем все вхождения лучшей пары слева направо
            var (a, b) = (parts[best], parts[best + 1]);
            for (var i = 0; i < parts.Count; i++)
            {
                if (i < parts.Count - 1 && parts[i] == a && parts[i + 1] == b) { merged.Add(a + b); i++; }
                else merged.Add(parts[i]);
            }
            parts = merged;
        }
        var ids = parts.Select(t => vocab.TryGetValue(t, out var id) ? id : 3).ToArray();   // 3 — <unk>
        lock (cache) { if (cache.Count > 50_000) cache.Clear(); cache[word] = ids; }
        return ids;
    }

    /// Текст из номеров токенов (служебные уже убраны).
    public string Decode(IEnumerable<int> ids)
    {
        var bytes = new List<byte>();
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var token)) continue;
            foreach (var c in token)
            {
                if (CharToByte.TryGetValue(c, out var b)) bytes.Add(b);
                else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
