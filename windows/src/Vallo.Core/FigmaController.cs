namespace Vallo.Core;

/// Разговор с плагином по протоколу: только что запущенный плагин (`hello` с `fresh`) сразу получает `press`,
/// на `mode: layers` — тексты в обработку и `result`, на `mode: text` — исправление выделенного текста.
/// Перенос раздела «Фигма» из `Sources/ValloSwitcher/KeyboardTap.swift`.
public sealed class FigmaController
{
    readonly FigmaBridge bridge;
    readonly FigmaBatch batch;
    readonly object gate = new();
    int pressId;
    /// Только что запущенный плагин, пришедший, пока шёл другой пакет.
    string? queuedFile;

    /// Опечатки и «ё» свитчера (null — словари не загрузились) и включена ли «ё».
    public Func<(TypoCorrector Typos, Yoficator Yo, bool YoEnabled)?> Spelling { get; set; } = () => null;
    /// Человек редактирует текст в Фигме — исправить выделенное, как в любой программе.
    public Action? TextMode { get; set; }

    public FigmaController(FigmaBridge bridge, CommaService commas)
    {
        this.bridge = bridge;
        batch = new FigmaBatch(commas);
        bridge.OnMessage = (m, file) => _ = Handle(m, file);
    }

    public bool IsRunning => batch.IsRunning;

    /// Нажатие для плагина файла. false — плагин этого файла не на связи.
    public bool SendPress(string file)
    {
        int id;
        lock (gate) id = ++pressId;
        return bridge.Send(new FigmaMessage.Press(id), file);
    }

    async Task Handle(FigmaMessage message, string file)
    {
        // плагин только что запущен (нами по сочетанию или человеком из меню) — сразу за работу
        if (message is FigmaMessage.Hello { Fresh: true })
        {
            // свитчер занят другим пакетом — этот файл обслужим следом (плагин ждёт и пишет «занят», а не «не запущен»)
            if (batch.IsRunning) { lock (gate) queuedFile = file; }
            else SendPress(file);
            return;
        }
        if (message is not FigmaMessage.Mode mode) return;
        lock (gate) if (mode.Id != pressId) return;
        switch (mode.Kind)
        {
            case FigmaMode.Text:
                DebugLog.Shared.Write("figma: плагин говорит — редактируется текст, исправляем выделенное");
                TextMode?.Invoke();
                break;
            case FigmaMode.Layers layers:
                if (Spelling() is not { } s)
                {
                    DebugLog.Shared.Write("figma: словари не загружены — слои не обрабатываем");
                    return;
                }
                if (await batch.RunAsync(layers.Texts, s.Typos, s.Yo, s.YoEnabled).ConfigureAwait(false) is not { } done) return;
                bridge.Send(new FigmaMessage.Result(mode.Id, done.Edits, done.Stats, mode.Skipped), file);
                string? queued;
                lock (gate) { queued = queuedFile; queuedFile = null; }
                if (queued is not null && bridge.ConnectedFiles.Contains(queued)) SendPress(queued);
                break;
        }
    }
}
