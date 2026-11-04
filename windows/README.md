# Vallo for Windows

C# port of the macOS layout switcher.

- `src/Vallo.Core` - cross-platform logic (net10.0)
- `src/Vallo.Platform` - Windows-only P/Invoke (net10.0-windows)
- `tools/DumpLayouts` - dumps Windows layouts (US, RU) to JSON: `DumpLayouts <outDir>`
- `tests/Vallo.Core.Tests` - xUnit tests; `Data/` is copied to the output folder

Build/test: `dotnet test windows/Vallo.sln` (works on macOS too; Windows-only projects compile but do not run).
