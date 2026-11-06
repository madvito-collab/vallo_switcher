using System.Text.Json;
using Vallo.Platform;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: DumpLayouts <outDir>");
    return 1;
}

string outDir = args[0];
Directory.CreateDirectory(outDir);

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

foreach (var (file, klid, language) in new[] { ("win-us.json", "00000409", "en"), ("win-ru.json", "00000419", "ru") })
{
    var entries = LayoutReader.Read(klid)
        .Select(e => new Dictionary<string, object>
        {
            ["keyCode"] = e.KeyCode,
            ["shift"] = e.Shift,
            ["capsLock"] = e.CapsLock,
            ["char"] = e.Char,
        })
        .ToList();
    var doc = new Dictionary<string, object> { ["entries"] = entries, ["language"] = language };
    File.WriteAllText(Path.Combine(outDir, file), JsonSerializer.Serialize(doc, options));
    Console.WriteLine($"{file}: {entries.Count} entries");
}
// «США — международная»: мёртвые клавиши и что они дают со следующей — проверка LayoutReader.ReadDead на настоящей Windows
{
    var hkl = LayoutReader.Load("00020409");
    var dead = LayoutReader.ReadDead(hkl)
        .Select(d => new Dictionary<string, object>
        {
            ["keyCode"] = d.KeyCode, ["shift"] = d.Shift, ["capsLock"] = d.CapsLock, ["spacing"] = d.Spacing,
            // только самое интересное: та же клавиша, «[», «c», «a», «s» и пробел
            ["compose"] = d.Compose.Where(c => !c.CapsLock && (c.KeyCode == d.KeyCode || c.KeyCode is 0xDB or 0x43 or 0x41 or 0x53 or 0x20))
                .Select(c => $"{c.KeyCode:X2}{(c.Shift ? "+shift" : "")} → {c.Char}").ToList(),
            ["composeCount"] = d.Compose.Count,
        })
        .ToList();
    File.WriteAllText(Path.Combine(outDir, "win-us-intl-dead.json"), JsonSerializer.Serialize(dead, options));
    Console.WriteLine($"win-us-intl-dead.json: {dead.Count} dead keys");
}
return 0;
