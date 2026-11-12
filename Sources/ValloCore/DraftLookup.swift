/// Черновик для модели запятых. Модель почти дословно переписывает входную фразу, лишь вставляя запятые,
/// поэтому продолжение ответа угадываем по входу и проверяем пачкой за один прогон декодера.
/// Ответ модели от этого не меняется: принимаются только токены, которые она выбрала бы и так.
public enum DraftLookup {
    /// Следующие токены входа после хвоста ответа. Хвост не нашёлся (в конце вставленная запятая) —
    /// отбрасываем до трёх последних токенов и ищем снова. Ничего не нашлось — пусто (обычный шаг).
    public static func draft(output: [Int64], source: [Int64], limit: Int = 32) -> [Int64] {
        guard !output.isEmpty else { return Array(source.prefix(limit)) }
        for skip in 0...3 where skip < output.count {
            let body = output.dropLast(skip)
            for m in stride(from: 2, through: 1, by: -1) where body.count >= m {
                let tail = Array(body.suffix(m))
                var i = source.count - m
                while i >= 0 {
                    if Array(source[i..<(i + m)]) == tail {
                        return Array(source[(i + m)...].prefix(limit))
                    }
                    i -= 1
                }
            }
        }
        return []
    }
}
