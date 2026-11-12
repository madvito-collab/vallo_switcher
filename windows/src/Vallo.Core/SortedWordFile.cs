using System.IO.MemoryMappedFiles;
using System.Text;

namespace Vallo.Core;

/// Большой список слов: по одному на строку, отсортирован по байтам UTF-8 (`scripts/build-word-forms.py`).
/// Файл не читается в память — он отображается, слово ищется двоичным поиском (~22 шага на 3 млн слов).
/// Перенос `Sources/ValloCore/SortedWordFile.swift`.
public sealed unsafe class SortedWordFile : IDisposable
{
    readonly byte[]? bytes;
    readonly MemoryMappedFile? file;
    readonly MemoryMappedViewAccessor? view;
    readonly byte* pointer;
    readonly long length;

    /// Для тестов — список прямо в памяти.
    public SortedWordFile(byte[] data)
    {
        bytes = data;
        length = data.Length;
    }

    SortedWordFile(string path)
    {
        length = new FileInfo(path).Length;
        if (length == 0) { bytes = []; return; }
        file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        view = file.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
        byte* p = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        pointer = p + view.PointerOffset;
    }

    public static SortedWordFile Load(string path) => new(path);

    ReadOnlySpan<byte> Data => bytes is not null ? bytes : new ReadOnlySpan<byte>(pointer, checked((int)length));

    /// Есть ли слово (точное совпадение, как записано: строчные, «ё» → «е»).
    public bool Contains(string word)
    {
        var key = Encoding.UTF8.GetBytes(word);
        if (key.Length == 0) return false;
        var p = Data;
        int n = p.Length, lo = 0, hi = n;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            var start = mid;
            while (start > 0 && p[start - 1] != 10) start--;
            var end = start;
            while (end < n && p[end] != 10) end++;
            var c = p[start..end].SequenceCompareTo(key);
            if (c == 0) return true;
            if (c < 0) lo = end + 1; else hi = start;
        }
        return false;
    }

    public void Dispose()
    {
        if (view is not null)
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
            view.Dispose();
        }
        file?.Dispose();
    }
}
