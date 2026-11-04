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
return 0;
