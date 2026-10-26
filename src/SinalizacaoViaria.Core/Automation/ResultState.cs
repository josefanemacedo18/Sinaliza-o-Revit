using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

public enum SituacaoResultado { Atualizado, Desatualizado, NaoVerificavel, Informativo }

/// <summary>Estado de um resultado derivado do modelo: módulo, o resultado, a situação, o detalhe, a ação e as marcas.</summary>
public sealed record EstadoResultado(string Modulo, string Resultado, SituacaoResultado Situacao, string Detalhe, string? Acao, IReadOnlyList<string> Ids);

/// <summary>O que uma edição deixa por refazer ou desatualizado: o resultado, o texto e as marcas.</summary>
public sealed record ImpactoEdicao(string Resultado, string Texto, IReadOnlyList<string> Ids);

/// <summary>
/// Estado dos resultados derivados do modelo (Etapa 8; Prompt Master §14; Motores §14, §15): o que está atualizado, o que
/// ficou desatualizado e o que não se pôde verificar – a terraplenagem aplicada no Toposolid (registro, D-024), as
/// captações lidas do modelo (D-025) e, como informação, o que o plugin refaz sozinho ou calcula na hora. E o impacto de
/// uma edição: o que acompanha a marca editada e o que fica por refazer.
/// </summary>
public static class EstadoDosResultados
{
    public static string Nome(SituacaoResultado s) => s switch
    {
        SituacaoResultado.Atualizado => "atualizado", SituacaoResultado.Desatualizado => "DESATUALIZADO",
        SituacaoResultado.NaoVerificavel => "não verificado", _ => "informação",
    };

    public static List<EstadoResultado> Calcular(IReadOnlyCollection<MarkingDefinition> defs, RegistroTerraplenagem? registro)
    {
        var res = new List<EstadoResultado>();
        var participantes = EarthworkRecord.Participantes(defs);
        if (participantes.Count > 0)
        {
            if (registro == null)
                res.Add(new("Terraplenagem", "Terreno (Toposolid) moldado pelas marcas", SituacaoResultado.NaoVerificavel,
                    $"{participantes.Count} marca(s) moldam o terreno e não há registro da terraplenagem aplicada.", "rodar a Terraplenagem", Array.Empty<string>()));
            else
            {
                var m = EarthworkRecord.Comparar(registro, defs);
                var quando = registro.Quando.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                res.Add(m.Count == 0
                    ? new("Terraplenagem", "Terreno (Toposolid) moldado pelas marcas", SituacaoResultado.Atualizado,
                        $"aplicada em {quando} com as marcas atuais.", null, Array.Empty<string>())
                    : new("Terraplenagem", "Terreno (Toposolid) moldado pelas marcas", SituacaoResultado.Desatualizado,
                        $"{m.Count} marca(s) diferem da terraplenagem de {quando} (V15).", "rodar a Terraplenagem", m.Select(x => x.Id).ToArray()));
            }
        }
        var cap = DrenagemDoModelo.Captacoes(defs);
        if (cap.Count > 0)
        {
            var por = cap.GroupBy(DrenagemDoModelo.Estado).ToDictionary(g => g.Key, g => g.ToList());
            int N(EstadoLeitura e) => por.GetValueOrDefault(e)?.Count ?? 0;
            var detalhe = $"{N(EstadoLeitura.EmDia)} em dia, {N(EstadoLeitura.Desatualizada)} desatualizada(s), {N(EstadoLeitura.Editada)} editada(s) à mão, " +
                          $"{N(EstadoLeitura.NuncaLida)} nunca lida(s) do modelo.";
            res.Add(N(EstadoLeitura.Desatualizada) > 0
                ? new("Drenagem", "Área e declividade das captações lidas da via", SituacaoResultado.Desatualizado, detalhe,
                    "Ler do Modelo › Atualizar só as desatualizadas (as editadas à mão ficam)", por[EstadoLeitura.Desatualizada].Select(c => c.Dispositivo.Id).ToArray())
                : new("Drenagem", "Área e declividade das captações lidas da via",
                    N(EstadoLeitura.EmDia) > 0 ? SituacaoResultado.Atualizado : SituacaoResultado.Informativo, detalhe, null, Array.Empty<string>()));
        }
        if (defs.Any(d => d is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition))
            res.Add(new("Interseções", "Interseções, rotatórias e balões", SituacaoResultado.Informativo,
                "refeitos pela atualização automática quando o eixo, o greide ou a seção de uma via ligada mudam.", null, Array.Empty<string>()));
        res.Add(new("Quantitativos", "Quantitativos, volumes por estaca, perfis e relatórios", SituacaoResultado.Informativo,
            "calculados na hora, a cada abertura: não ficam desatualizados.", null, Array.Empty<string>()));
        return res;
    }

    /// <summary>
    /// Impacto da edição das marcas <paramref name="alteradas"/> (invalidação seletiva): as conexões ligadas às vias delas
    /// (refeitas pela atualização automática), as marcas que dependem delas pelo grafo, a terraplenagem (quando alguma molda o
    /// terreno e o registro não a tem como está) e as captações lidas do modelo que ficaram desatualizadas.
    /// </summary>
    public static List<ImpactoEdicao> Impacto(IReadOnlyCollection<MarkingDefinition> alteradas, IReadOnlyCollection<MarkingDefinition> all,
        RegistroTerraplenagem? registro)
    {
        var res = new List<ImpactoEdicao>();
        var ids = alteradas.Select(d => d.Id).ToHashSet();
        var conexoes = ModelGraph.ConnectionsToRefresh(alteradas, all).Where(c => !ids.Contains(c.Id)).ToList();
        if (conexoes.Count > 0)
            res.Add(new("Interseções", $"{conexoes.Count} conexão(ões) ligada(s) à via acompanham a mudança.", conexoes.Select(c => c.Id).ToArray()));
        var grafo = new ModelGraph(all);
        var dependentes = ids.SelectMany(id => grafo.Dependents(id)).Where(id => !ids.Contains(id)).Distinct().ToList();
        if (dependentes.Count > 0)
            res.Add(new("Dependências", $"{dependentes.Count} marca(s) dependem das editadas (detalhes, sinalização dos nós, trechos de rede).", dependentes));
        var molda = EarthworkRecord.Participantes(all).Where(d => ids.Contains(d.Id)).ToList();
        if (molda.Count > 0)
        {
            var pendentes = registro == null ? molda.Select(d => d.Id).ToList()
                : EarthworkRecord.Comparar(registro, all).Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToList();
            if (pendentes.Count > 0)
                res.Add(new("Terraplenagem", $"o terreno não acompanha {pendentes.Count} marca(s) editada(s): rode a Terraplenagem.", pendentes));
        }
        var vias = alteradas.OfType<RoadPavementDefinition>().Select(r => r.Id).ToHashSet();
        if (vias.Count > 0)
        {
            var velhas = DrenagemDoModelo.Captacoes(all).Where(c => c.Dispositivo.Lida is { } l && vias.Contains(l.ViaId)
                && DrenagemDoModelo.Estado(c) == EstadoLeitura.Desatualizada).ToList();
            if (velhas.Count > 0)
                res.Add(new("Drenagem", $"{velhas.Count} captação(ões) lidas do modelo ficaram desatualizadas: Ler do Modelo › Atualizar só as desatualizadas.",
                    velhas.Select(c => c.Dispositivo.Id).ToArray()));
        }
        return res;
    }
}
