namespace Vallo.Core;

/// Черновик для модели запятых. Модель почти дословно переписывает входную фразу, лишь вставляя запятые,
/// поэтому продолжение ответа угадываем по входу и проверяем пачкой за один прогон декодера.
/// Ответ модели от этого не меняется: принимаются только токены, которые она выбрала бы и так.
/// Перенос `Sources/ValloCore/DraftLookup.swift`.
public static class DraftLookup
{
    /// Следующие токены входа после хвоста ответа. Хвост не нашёлся (в конце вставленная запятая) —
    /// отбрасываем до трёх последних токенов и ищем снова. Ничего не нашлось — пусто (обычный шаг).
    public static long[] Draft(IReadOnlyList<long> output, IReadOnlyList<long> source, int limit = 32)
    {
        if (output.Count == 0) return source.Take(limit).ToArray();
        for (var skip = 0; skip <= 3 && skip < output.Count; skip++)
        {
            var bodyCount = output.Count - skip;
            for (var m = 2; m >= 1; m--)
            {
                if (bodyCount < m) continue;
                for (var i = source.Count - m; i >= 0; i--)
                {
                    var same = true;
                    for (var k = 0; k < m && same; k++) same = source[i + k] == output[bodyCount - m + k];
                    if (same) return source.Skip(i + m).Take(limit).ToArray();
                }
            }
        }
        return [];
    }
}
