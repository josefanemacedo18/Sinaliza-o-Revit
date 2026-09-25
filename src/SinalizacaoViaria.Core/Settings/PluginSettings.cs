using System.Text.Json;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Settings;

/// <summary>Preferências do usuário (gravadas em %AppData%\SinalizacaoViaria\config.json).</summary>
public sealed class PluginSettings
{
    public OutputMode DefaultMode { get; set; } = OutputMode.Modelo3D;
    public string DefaultMaterial { get; set; } = "Tinta acrílica base solvente";

    /// <summary>Espessura mínima modelada no 3D (m). Películas de tinta reais (~0,6 mm) são finas demais para sólidos do Revit.</summary>
    public double MinModelThickness { get; set; } = 0.003;

    public double ElevationOffset { get; set; } = 0.001;
    public bool DrapeByDefault { get; set; }

    /// <summary>Comprimento máximo das peças ao acompanhar superfícies (m).</summary>
    public double DrapePieceLength { get; set; } = 2.0;

    /// <summary>Atualiza automaticamente as marcas quando as linhas de referência mudam.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Cria/atualiza interseções automaticamente quando vias passam a se cruzar (criação ou edição de eixos).</summary>
    public bool AutoIntersect { get; set; } = true;
    /// <summary>Ímã de conexão: pontas de eixo soltas sobre outra via vão para o eixo dela (e vias ligadas acompanham).</summary>
    public bool AutoConnect { get; set; } = true;
    public Automation.TipoConexao LastConnection { get; set; } = Automation.TipoConexao.Intersecao;
    public Automation.FimLivre LastFreeEnds { get; set; } = Automation.FimLivre.Nenhum;
    public double LastCurveRadius { get; set; } = 30;
    public bool LastDrawRoad { get; set; } = true;

    public double DefaultSpeed { get; set; } = 60;
    public string FontFamily { get; set; } = "Arial";
    public bool FontBold { get; set; } = true;

    /// <summary>Contorno visível nas regiões preenchidas 2D (importante para marcas brancas em fundo branco).</summary>
    public bool VisibleBoundary2D { get; set; } = true;

    /// <summary>Caminho do catálogo do usuário. Vazio = %AppData%\SinalizacaoViaria\catalogo.json.</summary>
    public string? UserCatalogPath { get; set; }

    /// <summary>Últimas escolhas por janela (lembradas entre sessões).</summary>
    public Dictionary<string, string> LastUsed { get; set; } = new();

    public static PluginSettings Load(string file)
    {
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(file), JsonConfig.Options) ?? new PluginSettings();
        }
        catch
        {
            // Arquivo corrompido: volta aos padrões.
        }
        return new PluginSettings();
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, JsonConfig.Options));
    }

    public string? Get(string key) => LastUsed.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string? value)
    {
        if (value == null) LastUsed.Remove(key); else LastUsed[key] = value;
    }

    public OutputSettings NewOutput(double? thicknessOverride = null) => new()
    {
        Mode = DefaultMode,
        Material = DefaultMaterial,
        ElevationOffset = ElevationOffset,
        Drape = DrapeByDefault,
        Thickness = thicknessOverride ?? 0,
    };
}
