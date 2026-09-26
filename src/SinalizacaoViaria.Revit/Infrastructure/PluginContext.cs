using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Settings;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Estado global do plugin: catálogo normativo, preferências e fonte das legendas.</summary>
public static class PluginContext
{
    private static Catalogo? _catalog;
    private static PluginSettings? _settings;

    public static string? CatalogError { get; private set; }

    public static PluginSettings Settings => _settings ??= PluginSettings.Load(PluginPaths.Settings);

    public static Catalogo Catalog
    {
        get
        {
            if (_catalog == null) ReloadCatalog();
            return _catalog!;
        }
    }

    public static IGlyphOutlineProvider Glyphs { get; } = new WpfGlyphProvider();

    public static string UserCatalogPath =>
        string.IsNullOrWhiteSpace(Settings.UserCatalogPath) ? PluginPaths.DefaultUserCatalog : Settings.UserCatalogPath!;

    public static void ReloadCatalog()
    {
        _catalog = CatalogService.Load(UserCatalogPath, out var err);
        CatalogError = err;
        if (err != null) Log.Info(err);
    }

    public static void SaveSettings()
    {
        try { Settings.Save(PluginPaths.Settings); }
        catch (Exception ex) { Log.Error("SaveSettings", ex); }
    }

    public static BuildContext BuildContext(bool drape, double viewScale = 100,
        Func<string, MarkingDefinition?>? lookup = null, Func<IReadOnlyList<MarkingDefinition>>? all = null,
        Func<MarkingDefinition, SinalizacaoViaria.Core.Model.MarkingGeometry?>? geometryOf = null,
        Func<MarkingDefinition, SinalizacaoViaria.Core.Geometry.Polyline2?>? pathOf = null) => new()
    {
        Catalog = Catalog,
        Glyphs = Glyphs,
        MaxPieceLength = 0,
        DeviceMaxPieceLength = drape ? Math.Max(2.0, Settings.DrapePieceLength) : 0,
        ViewScale = viewScale > 0 ? viewScale : 100,
        Lookup = lookup,
        AllDefinitions = all,
        GeometryOf = geometryOf,
        PathOf = pathOf,
    };
}
