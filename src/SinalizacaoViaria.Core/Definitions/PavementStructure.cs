using System.Globalization;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Função de uma camada do pavimento sob o revestimento.</summary>
public enum FuncaoCamada { Base, SubBase, Reforco, Regularizacao, Outra }

/// <summary>Camada do pavimento sob o revestimento: nome, material, espessura (cm), função e hachura do corte.</summary>
public sealed class CamadaPavimento
{
    public string Nome { get; set; } = "";
    public string? Material { get; set; }
    public double EspessuraCm { get; set; }
    public FuncaoCamada Funcao { get; set; } = FuncaoCamada.Base;
    public Hachura Hachura { get; set; } = Hachura.Brita;

    public CamadaPavimento Clone() => (CamadaPavimento)MemberwiseClone();
}

/// <summary>
/// Estrutura do pavimento de uma via (Prompt Master §10; Motores §9; Interface §15): as camadas **sob o revestimento**, de
/// cima para baixo. O revestimento é a espessura do próprio piso da pista (<see cref="RoadPavementDefinition.ActualThickness"/>),
/// um valor só. A profundidade total (revestimento + camadas) é o fundo da caixa na terraplenagem, as camadas vão ao corte
/// transversal e ao quantitativo por camada. O plugin não dimensiona pavimento: as espessuras e a fonte são do projeto.
/// </summary>
public sealed class EstruturaPavimento
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public List<CamadaPavimento> Camadas { get; set; } = new();

    /// <summary>De onde vem a estrutura (projeto de pavimentação, dimensionamento), escrita pelo usuário.</summary>
    public string? Fonte { get; set; }

    /// <summary>Soma das camadas sob o revestimento (m).</summary>
    public double EspessuraCamadas => Camadas.Where(c => double.IsFinite(c.EspessuraCm) && c.EspessuraCm > 0).Sum(c => c.EspessuraCm) / 100;

    public EstruturaPavimento Clone() => new() { Fonte = Fonte, Camadas = Camadas.Select(c => c.Clone()).ToList() };

    /// <summary>Profundidade sob o topo da pista até o fundo da estrutura (m): revestimento + camadas.</summary>
    public double Profundidade(double revestimento) => revestimento + EspessuraCamadas;

    /// <summary>Camadas para o desenho do corte transversal (as de espessura nula ficam de fora).</summary>
    public List<SectionLayer> ParaCorte() => Camadas.Where(c => c.EspessuraCm > 0.05)
        .Select(c => new SectionLayer { Name = string.IsNullOrWhiteSpace(c.Material) ? c.Nome : $"{c.Nome} ({c.Material})", ThicknessCm = c.EspessuraCm, Hatch = c.Hachura })
        .ToList();

    public List<string> Problemas()
    {
        var res = new List<string>();
        for (int i = 0; i < Camadas.Count; i++)
        {
            var c = Camadas[i];
            var nome = string.IsNullOrWhiteSpace(c.Nome) ? $"camada {i + 1}" : c.Nome;
            if (string.IsNullOrWhiteSpace(c.Nome)) res.Add($"camada {i + 1} sem nome");
            if (!double.IsFinite(c.EspessuraCm) || c.EspessuraCm <= 0) res.Add($"{nome}: espessura ausente ou nula");
            else if (c.EspessuraCm > 200) res.Add($"{nome}: espessura de {c.EspessuraCm.ToString("0.#", Pt)} cm (mais de 2 m)");
        }
        return res;
    }

    public static string Nome(FuncaoCamada f) => f switch
    {
        FuncaoCamada.Base => "base", FuncaoCamada.SubBase => "sub-base", FuncaoCamada.Reforco => "reforço do subleito",
        FuncaoCamada.Regularizacao => "regularização", _ => "outra",
    };

    /// <summary>Uma camada por linha: "nome; espessura (cm); material; função".</summary>
    public string Texto() => string.Join("\n", Camadas.Select(c =>
        $"{c.Nome}; {c.EspessuraCm.ToString("0.##", Pt)}; {c.Material ?? ""}; {Nome(c.Funcao)}"));

    /// <summary>
    /// Lê "nome; espessura (cm); material; função" (uma camada por linha; material e função opcionais). Devolve a estrutura e os
    /// problemas de leitura; texto vazio = sem camadas.
    /// </summary>
    public static (EstruturaPavimento Estrutura, List<string> Problemas) Ler(string? texto)
    {
        var e = new EstruturaPavimento();
        var problemas = new List<string>();
        if (string.IsNullOrWhiteSpace(texto)) return (e, problemas);
        var linhas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var linha in linhas)
        {
            var p = linha.Split(';', StringSplitOptions.TrimEntries);
            if (p.Length < 2 || !double.TryParse(p[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm))
            {
                problemas.Add($"\"{linha}\": use \"nome; espessura em cm; material; função\" (ex.: Base; 15; brita graduada simples; base)");
                continue;
            }
            var funcao = p.Length > 3 ? LerFuncao(p[3]) : FuncaoCamada.Base;
            if (funcao == null) { problemas.Add($"\"{linha}\": função \"{p[3]}\" desconhecida (base, sub-base, reforço do subleito, regularização, outra)"); continue; }
            e.Camadas.Add(new CamadaPavimento
            {
                Nome = p[0], EspessuraCm = cm, Material = p.Length > 2 && p[2].Length > 0 ? p[2] : null, Funcao = funcao.Value,
                Hachura = funcao is FuncaoCamada.Reforco or FuncaoCamada.Regularizacao ? Hachura.Solo : Hachura.Brita,
            });
        }
        problemas.AddRange(e.Problemas());
        return (e, problemas);
    }

    private static FuncaoCamada? LerFuncao(string s)
    {
        var t = s.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");
        return t switch
        {
            "" or "base" => FuncaoCamada.Base,
            "subbase" => FuncaoCamada.SubBase,
            "reforço" or "reforco" or "reforçodosubleito" or "reforcodosubleito" => FuncaoCamada.Reforco,
            "regularização" or "regularizacao" => FuncaoCamada.Regularizacao,
            "outra" => FuncaoCamada.Outra,
            _ => null,
        };
    }

    /// <summary>
    /// Profundidade da estrutura sob um nó (interseção, rotatória, balão): a mais funda entre as vias ligadas, cada uma pela
    /// sua estrutura ou, sem ela, por <paramref name="padrao"/> (o subleito das opções da terraplenagem).
    /// </summary>
    public static double ProfundidadeNo(IEnumerable<RoadPavementDefinition> vias, double padrao)
    {
        var l = vias.ToList();
        return l.Count == 0 ? padrao : l.Max(v => v.ProfundidadeEstrutura ?? padrao);
    }
}
