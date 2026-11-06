import Foundation

/// Большой список слов в файле: по одному на строку, отсортирован по байтам UTF-8 (`scripts/build-word-forms.py`).
/// Файл не читается в память — он отображается (mmap), слово ищется двоичным поиском (~22 шага на 3 млн слов).
public final class SortedWordFile: @unchecked Sendable {
    private let data: Data

    public init(url: URL) throws {
        data = try Data(contentsOf: url, options: .alwaysMapped)
    }

    /// Есть ли слово (точное совпадение, как записано: строчные, «ё» → «е»).
    public func contains(_ word: String) -> Bool {
        let key = Array(word.utf8)
        guard !key.isEmpty else { return false }
        return data.withUnsafeBytes { raw -> Bool in
            let p = raw.bindMemory(to: UInt8.self)
            let n = p.count
            var lo = 0, hi = n
            while lo < hi {
                let mid = (lo + hi) / 2
                var start = mid
                while start > 0 && p[start - 1] != 10 { start -= 1 }
                var end = start
                while end < n && p[end] != 10 { end += 1 }
                let c = Self.compare(p, start, end, key)
                if c == 0 { return true }
                if c < 0 { lo = end + 1 } else { hi = start }
            }
            return false
        }
    }

    /// Строка [start, end) против ключа: <0 — строка раньше, >0 — позже.
    private static func compare(_ p: UnsafeBufferPointer<UInt8>, _ start: Int, _ end: Int, _ key: [UInt8]) -> Int {
        var i = start, j = 0
        while i < end && j < key.count {
            if p[i] != key[j] { return Int(p[i]) - Int(key[j]) }
            i += 1; j += 1
        }
        return (end - start) - key.count
    }
}
