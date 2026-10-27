using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// O que os formulários de interseção e de rotatória mostram além dos parâmetros (Etapa 11; Prompt de Interface §12): vias
/// conectadas, pavimentação, terraplenagem, drenagem, verificação e resumo – cada linha com o nível escrito.
/// </summary>
public sealed record ContextoNo(IReadOnlyList<EstadoContexto> Vias, IReadOnlyList<EstadoContexto> Pavimentacao, IReadOnlyList<EstadoContexto> Terraplenagem,
    IReadOnlyList<EstadoContexto> Drenagem, IReadOnlyList<EstadoContexto> Verificacao, IReadOnlyList<EstadoContexto> Resumo);

/// <summary>
/// Conteúdo das seções dos formulários de interseção e de rotatória (Etapa 11; Prompt de Interface §12): as vias ligadas (e as
/// que não existem mais), os ângulos entre elas contra o limite do plugin, a estrutura que a caixa do nó usa, o estado na
/// terraplenagem, as captações e os pontos baixos na área do nó, os limites que o gerador confere (com a fonte ou "sem norma
/// citada") e o que acontece ao aplicar. A geometria gerada não é validação operacional: capacidade e nível de serviço são do
/// Simulador de Tráfego, com a demanda estimada ou informada; visibilidade e trajetória de giro dos veículos não são calculadas.
/// </summary>
public static class FormularioNo
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string M(double v, string fmt = "0.00") => v.ToString(fmt, Pt);
    private static EstadoContexto L(NivelSituacao n, string t) => new(n, t);

    /// <summary>O que a geometria gerada não diz (as duas telas mostram em Verificação).</summary>
    public static IReadOnlyList<EstadoContexto> Operacional { get; } = new[]
    {
        L(NivelSituacao.Informacao, "Verificação geométrica: o arranjo é desenhado e conferido contra os limites acima. Isso não é validação operacional de tráfego."),
        L(NivelSituacao.Informacao, "Capacidade, nível de serviço (HCM), filas e semáforos: Simulador de Tráfego, com a demanda estimada pelo plugin ou informada – estudo, não contagem."),
        L(NivelSituacao.NaoVerificado, "Visibilidade nas aproximações e trajetória de giro dos veículos de projeto: não calculadas pelo plugin – confira no projeto geométrico."),
    };

    private static List<EstadoContexto> Limites(string gerador) =>
        LimitesDeConferencia.Todos.Where(l => l.Onde == gerador).Select(l => L(NivelSituacao.Informacao, $"{l.Nome}: {l.Rotulo}.")).ToList();

    /// <summary>A via ligada: código, pista, calçadas, sentido e se tem estrutura informada.</summary>
    private static string Via(RoadPavementDefinition r) =>
        $"{r.DisplayCode}: pista de {M(r.LeftWidth + r.RightWidth, "0.0")} m, calçadas de {M(r.LeftSidewalk, "0.0")} e {M(r.RightSidewalk, "0.0")} m, " +
        (r.TwoWay ? "mão dupla" : "mão única") + (r.Estrutura is { Camadas.Count: > 0 } ? $", estrutura de {M(r.Estrutura.EspessuraCamadas * 100, "0.#")} cm" : ", sem estrutura informada");

    /// <summary>Vias ligadas e as que não existem mais.</summary>
    private static (List<RoadPavementDefinition> Vias, List<string> Faltam) Ligadas(IEnumerable<string> ids, IReadOnlyCollection<MarkingDefinition> all)
    {
        var porId = all.OfType<RoadPavementDefinition>().GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var lista = ids.Distinct().ToList();
        return (lista.Where(porId.ContainsKey).Select(id => porId[id]).ToList(), lista.Where(id => !porId.ContainsKey(id)).ToList());
    }

    /// <summary>A estrutura sob o nó: a da via mais funda (a caixa da terraplenagem do nó vai até ela).</summary>
    private static void Pavimento(List<EstadoContexto> pav, MarkingDefinition no, List<RoadPavementDefinition> vias, IReadOnlyCollection<MarkingDefinition> all,
        RegistroTerraplenagem? registro)
    {
        var com = vias.Where(v => v.ProfundidadeEstrutura is not null).ToList();
        if (vias.Count == 0) pav.Add(L(NivelSituacao.NaoVerificado, "Sem vias ligadas: a estrutura do nó não é conhecida."));
        else if (com.Count == 0)
            pav.Add(L(NivelSituacao.Informacao, "Nenhuma via ligada tem estrutura informada: a caixa do nó usa o subleito das opções da Terraplenagem."));
        else
        {
            var funda = com.MaxBy(v => v.ProfundidadeEstrutura!.Value)!;
            pav.Add(L(NivelSituacao.Informacao, $"Estrutura informada em {com.Count} de {vias.Count} via(s); a caixa do nó vai até a mais funda: {funda.DisplayCode}, " +
                $"{M(funda.ProfundidadeEstrutura!.Value * 100, "0.#")} cm sob o topo da pista."));
        }
        if (registro != null && vias.Count > 0)
            pav.Add(L(NivelSituacao.Informacao, $"Com o subleito da terraplenagem aplicada ({M(registro.Subleito)} m nas vias sem estrutura), a caixa do nó tem " +
                $"{M(EarthworkRecord.ProfundidadeDoNo(no, all, registro.Subleito))} m."));
        pav.Add(L(NivelSituacao.Informacao, "As camadas são das vias (Pavimentação › Estrutura do Pavimento); o nó não tem estrutura própria."));
    }

    private static void Terra(List<EstadoContexto> terra, MarkingDefinition no, bool existente, List<RoadPavementDefinition> vias, IReadOnlyCollection<MarkingDefinition> all,
        RegistroTerraplenagem? registro)
    {
        var moldam = vias.Count(v => v.Output.Grade is { AdjustTerrain: true });
        if (vias.Count > 0)
            terra.Add(L(NivelSituacao.Informacao, moldam == 0 ? "Nenhuma via ligada molda o terreno: o nó não entra na terraplenagem."
                : $"{moldam} de {vias.Count} via(s) ligada(s) moldam o terreno: o nó entra na terraplenagem com elas."));
        if (existente)
        {
            var porId = all.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
            if (PainelContextual.NaTerraplenagem(no, registro, id => porId.GetValueOrDefault(id)) is { } t)
                terra.Add(t with { Texto = char.ToUpper(t.Texto[0], Pt) + t.Texto[1..] + "." });
        }
        else if (moldam > 0) terra.Add(L(NivelSituacao.Informacao, "Depois de criar, rode a Terraplenagem para o Toposolid acompanhar o nó."));
        terra.Add(L(NivelSituacao.Informacao, "Volumes do nó: Quantitativos (corte e aterro de cada via, interseção e obra)."));
    }

    private static void Agua(List<EstadoContexto> dren, Func<Vec2, bool> naArea, IReadOnlyCollection<MarkingDefinition> all, bool elevada = false)
    {
        var captacoes = all.OfType<DrainageDefinition>().Where(d => d.IsInlet && !d.IsLinear && naArea(d.Position)).ToList();
        dren.Add(L(NivelSituacao.Informacao, captacoes.Count == 0 ? "Nenhuma captação (boca de lobo ou grelha) na área do nó."
            : $"{captacoes.Count} captação(ões) na área do nó."));
        var baixos = DrenagemDoModelo.PontosBaixosSemCaptacao(all).Where(pb => naArea(pb.Ponto)).ToList();
        foreach (var pb in baixos.Take(6))
            dren.Add(L(NivelSituacao.Aviso, $"Ponto baixo da sarjeta sem captação na área do nó: {pb.Via}, lado {DrenagemDoModelo.Lado(pb.Lado)}, estaca {pb.EstacaTexto}."));
        if (elevada) dren.Add(L(NivelSituacao.Aviso, "Rotatória elevada: o platô muda o escoamento das sarjetas nas entradas – confira a drenagem."));
        dren.Add(L(NivelSituacao.Informacao, "O nó não cria dispositivos de drenagem: Drenagem › Dispositivos e o Relatório da Rede."));
    }

    /// <param name="d">A interseção (a do formulário).</param>
    /// <param name="existente">A interseção já está no projeto (edição).</param>
    /// <param name="layout">Arranjo calculado com as vias do projeto; nulo = as vias são lidas ao aplicar (todos os cruzamentos).</param>
    public static ContextoNo Intersecao(IntersectionDefinition d, bool existente, IntersectionLayout? layout, IReadOnlyCollection<MarkingDefinition> all,
        RegistroTerraplenagem? registro)
    {
        var vias = new List<EstadoContexto>();
        var ids = layout != null ? layout.Roads.Select(r => r.Def.Id) : d.RoadIds;
        var (ligadas, faltam) = Ligadas(ids, all);
        if (layout == null && !existente)
            vias.Add(L(NivelSituacao.Informacao, "As vias são lidas ao aplicar: cada cruzamento e entroncamento das vias do Sinalizar via (ou só o clicado) vira uma interseção com as vias dele."));
        foreach (var r in ligadas)
            vias.Add(L(NivelSituacao.Informacao, Via(r) + (layout != null && layout.Roads.Count > layout.Main && layout.Roads[layout.Main].Def.Id == r.Id ? " – principal (preferencial)." : ".")));
        foreach (var f in faltam) vias.Add(L(NivelSituacao.Aviso, $"A via {f} não existe mais: o nó aponta para ela (V04) – refaça a interseção ou apague-a."));
        if (layout != null)
        {
            vias.Add(layout.Legs.Count == 0
                ? L(NivelSituacao.Aviso, "O arranjo não montou os ramos com estas vias e medidas: veja os avisos em Verificação.")
                : L(NivelSituacao.Informacao, $"{layout.Legs.Count} ramo(s)" + (layout.IsBend ? " – emenda de duas vias pela ponta (curva concordada)." : ".")));
            var dirs = layout.Legs.Select(l => l.Dir).ToList();
            if (dirs.Count >= 2)
            {
                // O mesmo critério do gerador: o menor ângulo entre as direções dos ramos (0° a 90°).
                var menor = 90.0;
                for (var i = 0; i < dirs.Count; i++)
                    for (var j = i + 1; j < dirs.Count; j++)
                    {
                        var s = Math.Abs(dirs[i].Cross(dirs[j]));
                        if (s > 1e-9) menor = Math.Min(menor, Math.Asin(Math.Min(1, s)) * 180 / Math.PI);
                    }
                var lim = LimitesDeConferencia.AnguloMinimoEntreVias;
                vias.Add(menor < lim.Valor
                    ? L(NivelSituacao.Aviso, $"Menor ângulo entre as vias: {M(menor, "0")}°, abaixo de {lim.Rotulo}.")
                    : L(NivelSituacao.Ok, $"Menor ângulo entre as vias: {M(menor, "0")}° (limite de conferência: {lim.ValorTexto})."));
            }
        }
        else if (existente) vias.Add(L(NivelSituacao.NaoVerificado, "Os ângulos entre os ramos saem do arranjo com as vias do projeto, que não pôde ser calculado aqui."));

        var pav = new List<EstadoContexto>();
        Pavimento(pav, d, ligadas, all, registro);
        var terra = new List<EstadoContexto>();
        Terra(terra, d, existente, ligadas, all, registro);
        var dren = new List<EstadoContexto>();
        if (layout != null) Agua(dren, p => layout.Zone.Contains(p), all);
        else dren.Add(L(NivelSituacao.NaoVerificado, "Captações e pontos baixos da área do nó: conferidos com as vias resolvidas (edição ou Conexão)."));

        var verif = Limites("IntersectionGenerator");
        if (layout != null)
            foreach (var w in layout.Warnings.Distinct().Take(12)) verif.Add(L(NivelSituacao.Aviso, w));
        verif.AddRange(Operacional);

        var resumo = new List<EstadoContexto>
        {
            L(NivelSituacao.Informacao, "Ao aplicar: as vias são recortadas no nó e o nó é refeito com o que está marcado – pavimento contínuo, esquinas, calçadas e canteiros, " +
                "travessias e rampas, sinalização e controle; a sinalização gerada fica ligada ao nó."),
        };
        if (existente)
            resumo.Add(L(NivelSituacao.Informacao, $"{d.ChildIds.Count} marca(s) gerada(s) pelo nó são refeitas; {ligadas.Count} via(s) recortada(s)."));
        resumo.Add(L(NivelSituacao.Informacao, "Tudo numa operação desfazível (Desfazer do Revit)."));
        return new(vias, pav, terra, dren, verif, resumo);
    }

    /// <param name="d">A rotatória (a do formulário).</param>
    /// <param name="existente">A rotatória já está no projeto (edição).</param>
    public static ContextoNo Rotatoria(RoundaboutDefinition d, bool existente, IReadOnlyCollection<MarkingDefinition> all, RegistroTerraplenagem? registro)
    {
        var vias = new List<EstadoContexto>();
        var (ligadas, faltam) = Ligadas(d.Legs.Where(l => l.RoadId != null).Select(l => l.RoadId!), all);
        vias.Add(L(NivelSituacao.Informacao, $"Diâmetro inscrito de {M(2 * d.OuterRadius, "0.0")} m; ilha central de raio {M(d.IslandRadius, "0.0")} m; " +
            $"{d.Lanes} faixa(s) de {M(d.LaneWidth, "0.00")} m no anel."));
        if (d.Legs.Count == 0) vias.Add(L(NivelSituacao.Aviso, "Sem ramos: informe os ângulos ou posicione a rotatória sobre o cruzamento de vias."));
        else
        {
            var angulos = d.Legs.Select(l => (l.AngleDeg % 360 + 360) % 360).OrderBy(a => a).ToList();
            vias.Add(L(NivelSituacao.Informacao, $"{d.Legs.Count} ramo(s) a {string.Join("; ", angulos.Select(a => M(a, "0") + "°"))} (0° = leste, anti-horário)."));
        }
        foreach (var r in ligadas) vias.Add(L(NivelSituacao.Informacao, Via(r) + "."));
        var soltos = d.Legs.Count(l => l.RoadId == null);
        if (soltos > 0 && d.Legs.Count > 0)
            vias.Add(L(NivelSituacao.Informacao, $"{soltos} ramo(s) sem via ligada: a pista do ramo é desenhada pela rotatória (modo Completa)."));
        foreach (var f in faltam) vias.Add(L(NivelSituacao.Aviso, $"A via {f} não existe mais: o ramo aponta para ela (V04) – refaça a rotatória."));

        var pav = new List<EstadoContexto>
        {
            L(NivelSituacao.Informacao, $"Revestimento do anel: {Memorial.PavementName(d.Pavement)}."),
        };
        Pavimento(pav, d, ligadas, all, registro);
        var terra = new List<EstadoContexto>();
        Terra(terra, d, existente, ligadas, all, registro);
        var dren = new List<EstadoContexto>();
        var alcance = d.OuterRadius + Math.Max(0, d.SidewalkWidth);
        Agua(dren, p => p.DistanceTo(d.Center) <= alcance, all, d.Raised);

        var verif = Limites("RoundaboutGenerator");
        foreach (var w in RoundaboutGenerator.Layout(d).Warnings.Distinct().Take(12)) verif.Add(L(NivelSituacao.Aviso, w));
        verif.AddRange(Operacional);

        var resumo = new List<EstadoContexto>
        {
            L(NivelSituacao.Informacao, d.Integration switch
            {
                IntegracaoRotatoria.Completa => "Ao aplicar: remodela as entradas (raios, ilhas separadoras, calçada em volta) e recorta as vias na área da rotatória.",
                IntegracaoRotatoria.Anel => "Ao aplicar: desenha a ilha, a pista giratória e as marcas; as vias são recortadas só dentro do anel.",
                _ => "Ao aplicar: só a ilha central sobre a pista existente, com a pintura interrompida no anel.",
            } + (d.CutRoads ? "" : " As vias ficam como estão (recorte desligado).")),
        };
        if (existente) resumo.Add(L(NivelSituacao.Informacao, $"{d.ChildIds.Count} marca(s) gerada(s) pela rotatória são refeitas."));
        resumo.Add(L(NivelSituacao.Informacao, "Tudo numa operação desfazível (Desfazer do Revit)."));
        return new(vias, pav, terra, dren, verif, resumo);
    }
}
