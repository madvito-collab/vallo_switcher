namespace Vallo.Core;

/// Пара раскладок, между которыми переводим.
public sealed record LayoutPair(IKeyLayout En, IKeyLayout Ru)
{
    public IKeyLayout Layout(Language language) => language == Language.En ? En : Ru;
}
