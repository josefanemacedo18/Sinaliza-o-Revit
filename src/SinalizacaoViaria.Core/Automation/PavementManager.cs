using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Estrutura de pavimento guardada para reutilizar (Prompt de Interface §15): fica na biblioteca do usuário, nas
/// configurações do plugin – não no projeto, que guarda só a estrutura de cada via.
/// </summary>
public sealed class ModeloEstruturaPavimento
{
    public string Nome { get; set; } = "";
    public EstruturaPavimento Estrutura { get; set; } = new();
}

/// <summary>De onde vem o material de uma camada: um material do Revit com o mesmo nome, só o texto do plugin, ou nenhum.</summary>
public enum OrigemMaterial { NaoInformado, Revit, SoNoPlugin }

/// <summary>Uma linha do gerenciador: a ordem, a camada, o estado da configuração (problemas ou "ok") e a origem do material.</summary>
public sealed record EstadoCamada(int Ordem, CamadaPavimento Camada, string Estado, OrigemMaterial Material);

/// <summary>Faixa do esquema em corte: rótulo, topo e base (cm abaixo do topo da pista) e a hachura (nula no revestimento).</summary>
public sealed record FaixaEsquema(string Rotulo, double TopoCm, double BaseCm, Hachura? Hachura);

/// <summary>Quantitativo previsto de uma camada: espessura e volume (m³) para a área em planta dada.</summary>
public sealed record QuantidadeCamada(string Camada, double EspessuraCm, double Volume);

/// <summary>
/// Gerenciador de estruturas de pavimento (Etapa 11; Prompt de Interface §15): estado de cada camada, origem do material
/// (cadastrado no Revit, só no plugin ou não informado), esquema em corte, quantitativo previsto só com a área medida e a
/// biblioteca de modelos do usuário. O plugin não dimensiona: espessuras e fonte são do projeto de pavimentação.
/// </summary>
public static class GerenciadorPavimento
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Problemas de uma camada (vazio = ok), os mesmos de <see cref="EstruturaPavimento.Problemas"/>.</summary>
    public static List<string> Problemas(CamadaPavimento c)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(c.Nome)) res.Add("sem nome");
        if (!double.IsFinite(c.EspessuraCm) || c.EspessuraCm <= 0) res.Add("espessura ausente ou nula");
        else if (c.EspessuraCm > 200) res.Add($"espessura de {c.EspessuraCm.ToString("0.#", Pt)} cm (mais de 2 m)");
        return res;
    }

    /// <param name="materialNoRevit">O projeto tem um material do Revit com esse nome (comparação sem diferença de maiúsculas).</param>
    public static List<EstadoCamada> Estados(EstruturaPavimento e, Func<string, bool> materialNoRevit) =>
        e.Camadas.Select((c, i) =>
        {
            var p = Problemas(c);
            var origem = string.IsNullOrWhiteSpace(c.Material) ? OrigemMaterial.NaoInformado
                : materialNoRevit(c.Material!.Trim()) ? OrigemMaterial.Revit : OrigemMaterial.SoNoPlugin;
            return new EstadoCamada(i + 1, c, p.Count == 0 ? "ok" : string.Join("; ", p), origem);
        }).ToList();

    public static string Nome(OrigemMaterial o) => o switch
    {
        OrigemMaterial.Revit => "material do Revit no projeto",
        OrigemMaterial.SoNoPlugin => "só no SinalizaBIM (nenhum material do Revit com esse nome)",
        _ => "não informado",
    };

    /// <summary>Esquema em corte, de cima para baixo: o revestimento e as camadas com espessura.</summary>
    public static List<FaixaEsquema> Esquema(EstruturaPavimento e, double revestimentoCm, string revestimento = "Revestimento")
    {
        var res = new List<FaixaEsquema>();
        var topo = 0.0;
        if (revestimentoCm > 0)
        {
            res.Add(new($"{revestimento} – {revestimentoCm.ToString("0.#", Pt)} cm", 0, revestimentoCm, null));
            topo = revestimentoCm;
        }
        foreach (var c in e.Camadas.Where(c => double.IsFinite(c.EspessuraCm) && c.EspessuraCm > 0))
        {
            var nome = string.IsNullOrWhiteSpace(c.Nome) ? "camada sem nome" : c.Nome;
            res.Add(new($"{nome} – {c.EspessuraCm.ToString("0.#", Pt)} cm" + (string.IsNullOrWhiteSpace(c.Material) ? "" : $" ({c.Material})"), topo, topo + c.EspessuraCm, c.Hachura));
            topo += c.EspessuraCm;
        }
        return res;
    }

    /// <summary>
    /// Volume previsto de cada camada (e do revestimento) para a área em planta medida (m²). Sem área (nula ou não positiva),
    /// nenhum valor: o gerenciador não estima.
    /// </summary>
    public static List<QuantidadeCamada> Quantidades(EstruturaPavimento e, double revestimentoCm, double? areaM2)
    {
        if (areaM2 is not > 0) return new();
        var a = areaM2.Value;
        var res = new List<QuantidadeCamada>();
        if (revestimentoCm > 0) res.Add(new("Revestimento", revestimentoCm, a * revestimentoCm / 100));
        foreach (var c in e.Camadas.Where(c => double.IsFinite(c.EspessuraCm) && c.EspessuraCm > 0))
            res.Add(new(string.IsNullOrWhiteSpace(c.Nome) ? "camada sem nome" : c.Nome, c.EspessuraCm, a * c.EspessuraCm / 100));
        return res;
    }

    /// <summary>Guarda (ou substitui, pelo nome) um modelo na biblioteca; devolve o problema ou nulo.</summary>
    public static string? Salvar(List<ModeloEstruturaPavimento> modelos, string? nome, EstruturaPavimento e)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "informe um nome para o modelo";
        if (e.Camadas.Count == 0) return "o modelo precisa de ao menos uma camada";
        var problemas = e.Problemas();
        if (problemas.Count > 0) return "corrija antes: " + string.Join("; ", problemas);
        modelos.RemoveAll(m => string.Equals(m.Nome, nome.Trim(), StringComparison.OrdinalIgnoreCase));
        modelos.Add(new ModeloEstruturaPavimento { Nome = nome.Trim(), Estrutura = e.Clone() });
        modelos.Sort((x, y) => string.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase));
        return null;
    }
}
