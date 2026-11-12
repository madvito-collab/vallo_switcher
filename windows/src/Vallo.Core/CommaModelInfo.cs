namespace Vallo.Core;

/// Что с моделью запятых. Как `ModelStore.State` на Маке.
public abstract record ModelState
{
    public sealed record NotDownloaded : ModelState;
    /// `Progress` — от 0 до 1; 1 — скачано, идёт проверка и распаковка.
    public sealed record Downloading(double Progress) : ModelState;
    public sealed record Ready : ModelState;
    public sealed record Failed(string Message) : ModelState;

    /// Для страницы настроек: состояние, прогресс, текст ошибки.
    public (string State, double Progress, string Message) Wire => this switch
    {
        Downloading d => ("downloading", d.Progress, ""),
        Ready => ("ready", 1, ""),
        Failed f => ("failed", 0, f.Message),
        _ => ("notDownloaded", 0, ""),
    };

    /// Модели нет — предупреждение «Модель запятых не скачана» на «Основном».
    public bool Missing => this is NotDownloaded or Failed;
}

/// Модель запятых SAGE (FRED-T5, ONNX) — та же, что на Маке: тот же адрес и контрольная сумма.
/// Сеть свитчер использует только здесь — по кнопке «Скачать» — и для проверки обновлений.
public static class CommaModelInfo
{
    public const string Name = "sage-fredt5-95m";
    public const string Url = "https://github.com/madvito-collab/vallo-models/releases/download/models-1/sage-fredt5-95m.zip";
    public const string Sha256 = "0c4af0f7f3905600c7d83eaafb27134f5d051fc80813b7be66ea61c0eba09486";
    public const string DownloadSize = "228 МБ";
    public const string DiskSize = "470 МБ";

    /// Куда распаковывается модель: %LOCALAPPDATA%\Vallo Switcher\Models\sage-fredt5-95m —
    /// не в перемещаемый профиль (470 МБ не должны ездить по сети вместе с настройками).
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vallo Switcher", "Models", Name);

    /// Модель на месте: есть оба файла, которые нужны для запуска.
    public static bool IsInstalled(string directory) =>
        File.Exists(Path.Combine(directory, "decoder_model.onnx")) && File.Exists(Path.Combine(directory, "encoder_model.onnx"))
        && File.Exists(Path.Combine(directory, "tokenizer.json"));

    // Тексты ошибок — те же, что на Маке.
    public static string ServerError(int status) => $"Сервер ответил ошибкой {status} — попробуйте позже";
    public const string BadChecksum = "Файл скачался с ошибкой — попробуйте ещё раз";
    public const string UnpackFailed = "Не получилось распаковать модель";
    public const string Broken = "Файлы модели повреждены — скачайте заново";
    public const string NoNetwork = "Не скачалось — проверьте интернет и попробуйте ещё раз";
}
