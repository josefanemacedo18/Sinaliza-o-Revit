using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Situação de um valor da Central do Projeto (Prompt de Interface §8.2): o que não se sabe nunca aparece como zero.
/// <see cref="NaoCalculado"/>: a operação que o produz não rodou (ou é cara e espera o usuário pedir);
/// <see cref="Indisponivel"/>: faltam os dados para calculá-lo; <see cref="Desatualizado"/>: guardado, mas o modelo mudou
/// depois; <see cref="Invalido"/>: há dado que não pôde entrar (contorno inválido).
/// </summary>
public enum SituacaoValor { Calculado, NaoCalculado, Indisponivel, Desatualizado, Invalido }

/// <summary>Indicador da Central: o valor (nulo quando não há), a situação, o detalhe e o comando que abre o módulo ou relatório.</summary>
public sealed record IndicadorProjeto(string Chave, string Nome, double? Valor, string Unidade, SituacaoValor Situacao, string Detalhe, string? Comando)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor formatado (pt-BR) ou "—" quando não há valor: nunca um zero inventado.</summary>
    public string ValorTexto => Valor is not { } v ? "—"
        : Unidade.Length == 0 ? v.ToString("N0", Pt) : $"{v.ToString(Unidade == "m³" || Unidade == "m²" || Unidade == "m" ? "N1" : "N2", Pt)} {Unidade}";
}

public enum NivelSituacao { Ok, Informacao, Aviso, Erro, NaoVerificado }

/// <summary>Um item da situação do projeto (§8.4): tema, nível, o que se sabe e, quando há, a ação e o comando.</summary>
public sealed record ItemSituacao(string Tema, NivelSituacao Nivel, string Texto, string? Acao, string? Comando);

/// <summary>Módulo da Central (§8.3): nome, descrição, ícone, a operação principal e quantos elementos do plugin ele tem.</summary>
public sealed record ModuloProjeto(string Chave, string Nome, string Descricao, string Icone, string Comando, string Operacao, int? Elementos, string Estado);

/// <summary>Resumo do projeto para a Central: indicadores, situação, situação geral e módulos.</summary>
public sealed record ResumoDoProjeto(IReadOnlyList<IndicadorProjeto> Indicadores, IReadOnlyList<ItemSituacao> Situacao, NivelSituacao NivelGeral,
    string SituacaoGeral, IReadOnlyList<ModuloProjeto> Modulos);

/// <summary>
/// Central do Projeto (Etapa 10; Prompt de Interface §8): resume o projeto a partir das definições gravadas, do registro da
/// terraplenagem, da última validação e do que a camada Revit mediu – sem inventar valor, data ou estado. O que é caro
/// (medir os pisos) só entra quando o usuário pede; a validação vem do histórico gravado pelo Validar Modelo, com a data,
/// e nunca é tomada como "validado".
/// </summary>
public static class CentralDoProjeto
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static string Nome(SituacaoValor s) => s switch
    {
        SituacaoValor.Calculado => "calculado", SituacaoValor.NaoCalculado => "não calculado", SituacaoValor.Indisponivel => "indisponível",
        SituacaoValor.Desatualizado => "DESATUALIZADO", _ => "inválido",
    };

    public static string Nome(NivelSituacao n) => n switch
    {
        NivelSituacao.Ok => "ok", NivelSituacao.Informacao => "informação", NivelSituacao.Aviso => "AVISO", NivelSituacao.Erro => "ERRO",
        _ => "não verificado",
    };

    private static string Quando(DateTime t) => t.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <param name="defs">Definições do projeto.</param>
    /// <param name="registro">Registro da última terraplenagem aplicada (nulo = sem registro).</param>
    /// <param name="ultimaValidacao">Última validação gravada deste projeto (nula = nunca validado com a 2.9+).</param>
    /// <param name="projetoDrenagem">O Projeto de Drenagem foi preenchido neste documento.</param>
    /// <param name="areaPavimento">Área do pavimento medida nas peças (m²), quando o usuário pediu; nula = não medida.</param>
    /// <param name="falhasPavimento">Marcas que não puderam ser medidas (a área fica parcial e o indicador, inválido).</param>
    public static ResumoDoProjeto Resumir(IReadOnlyCollection<MarkingDefinition> defs, RegistroTerraplenagem? registro, ValidacaoGravada? ultimaValidacao,
        bool projetoDrenagem, double? areaPavimento = null, int falhasPavimento = 0)
    {
        var estados = EstadoDosResultados.Calcular(defs, registro);
        var indicadores = Indicadores(defs, registro, ultimaValidacao, projetoDrenagem, areaPavimento, estados, falhasPavimento);
        var situacao = Situacao(ultimaValidacao, estados);
        var (nivel, geral) = Geral(situacao);
        return new ResumoDoProjeto(indicadores, situacao, nivel, geral, Modulos(defs, registro, ultimaValidacao));
    }

    // ------------------------------------------------------------------ indicadores (§8.2)

    public static List<IndicadorProjeto> Indicadores(IReadOnlyCollection<MarkingDefinition> defs, RegistroTerraplenagem? registro,
        ValidacaoGravada? ultimaValidacao, bool projetoDrenagem, double? areaPavimento, IReadOnlyList<EstadoResultado>? estados = null, int falhasPavimento = 0)
    {
        estados ??= EstadoDosResultados.Calcular(defs, registro);
        var res = new List<IndicadorProjeto>();
        var vias = defs.OfType<RoadPavementDefinition>().ToList();
        res.Add(new("vias", "Vias", vias.Count, "", SituacaoValor.Calculado,
            vias.Count == 0 ? "nenhuma via do plugin no projeto" : "pistas do plugin no projeto", "CmdSinalizarVia"));

        var eixos = vias.Select(v => v.PathRef.StoredPolyline()).ToList();
        var semEixo = eixos.Count(e => e == null);
        var extensao = eixos.Where(e => e != null).Sum(e => e!.Length);
        res.Add(vias.Count == 0 ? new("extensao", "Extensão das vias", 0, "m", SituacaoValor.Calculado, "nenhuma via", "CmdSinalizarVia")
            : semEixo == vias.Count ? new("extensao", "Extensão das vias", null, "m", SituacaoValor.Indisponivel,
                "nenhuma via tem o traçado do eixo guardado", "CmdValidarModelo")
            : new("extensao", "Extensão das vias", extensao, "m", SituacaoValor.Calculado,
                semEixo == 0 ? "medida nos eixos" : $"medida nos eixos; {semEixo} via(s) sem o traçado guardado ficaram de fora", "CmdSinalizarVia"));

        res.Add(new("intersecoes", "Interseções", defs.OfType<IntersectionDefinition>().Count(), "", SituacaoValor.Calculado,
            "interseções em nível do plugin", "CmdIntersecao"));
        res.Add(new("rotatorias", "Rotatórias", defs.OfType<RoundaboutDefinition>().Count(), "", SituacaoValor.Calculado, "rotatórias do plugin", "CmdRotatoria"));

        // Quadras e lotes: as quadras do plugin, divididas como no quadro do loteamento.
        var quadras = defs.OfType<QuadraDefinition>().ToList();
        var lots = defs.OfType<LoteamentoDefinition>().GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
        var validas = quadras.Where(q => q.Polygon is { IsValid: true }).ToList();
        var invalidas = quadras.Count - validas.Count;
        var areaQuadras = validas.Sum(q => Math.Abs(q.Polygon!.Area));
        res.Add(quadras.Count == 0 ? new("quadras", "Área das quadras", 0, "m²", SituacaoValor.Calculado, "nenhuma quadra", "CmdLoteamento")
            : new("quadras", "Área das quadras", areaQuadras, "m²", invalidas > 0 ? SituacaoValor.Invalido : SituacaoValor.Calculado,
                $"{quadras.Count} quadra(s)" + (invalidas > 0 ? $"; {invalidas} com contorno inválido não entram (Validar Modelo, V07)" : ""), "CmdQuadrosLoteamento"));

        int lotes = 0, parcelas = 0;
        double areaParcelada = 0, areaLotes = 0;
        var semLoteamento = 0;
        foreach (var q in validas)
        {
            if (!lots.TryGetValue(q.LoteamentoId, out var lot)) semLoteamento++;
            foreach (var p in LotDivider.Divide(q, lot).Parcelas)
            {
                parcelas++;
                areaParcelada += p.Area;
                if (!string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase)) continue;
                lotes++;
                areaLotes += p.Area;
            }
        }
        var nota = semLoteamento > 0 ? $"; {semLoteamento} quadra(s) sem o loteamento, divididas com as categorias padrão" : "";
        res.Add(quadras.Count == 0 ? new("lotes", "Lotes", 0, "", SituacaoValor.Calculado, "nenhuma quadra", "CmdLoteamento")
            : new("lotes", "Lotes", lotes, "", invalidas > 0 ? SituacaoValor.Invalido : SituacaoValor.Calculado,
                $"categoria Lote, em {parcelas} parcela(s) de todas as categorias" + nota, "CmdQuadrosLoteamento"));
        res.Add(quadras.Count == 0 ? new("parcelada", "Área parcelada", 0, "m²", SituacaoValor.Calculado, "nenhuma quadra", "CmdLoteamento")
            : new("parcelada", "Área parcelada", areaParcelada, "m²", invalidas > 0 ? SituacaoValor.Invalido : SituacaoValor.Calculado,
                $"soma das parcelas (todas as categorias); lotes: {areaLotes.ToString("N1", Pt)} m²" + nota, "CmdQuadrosLoteamento"));

        // Terraplenagem: os volumes guardados com a última aplicada, no estado do registro (D-024, D-026).
        foreach (var (chave, nome, valor) in new[] { ("corte", "Volume de corte", registro?.Corte), ("aterro", "Volume de aterro", registro?.Aterro) })
        {
            if (registro == null)
                res.Add(new(chave, nome, null, "m³", SituacaoValor.NaoCalculado,
                    "sem registro da terraplenagem (nunca aplicada, ou aplicada por versão anterior à 2.11)", "CmdTerraplenagem"));
            else if (valor == null)
                res.Add(new(chave, nome, null, "m³", SituacaoValor.NaoCalculado, $"a terraplenagem de {Quando(registro.Quando)} não gravou o volume", "CmdTerraplenagem"));
            else
            {
                var mudou = EarthworkRecord.Comparar(registro, defs).Count;
                res.Add(mudou > 0
                    ? new(chave, nome, valor, "m³", SituacaoValor.Desatualizado,
                        $"da terraplenagem de {Quando(registro.Quando)}; {mudou} marca(s) mudaram depois – rode a Terraplenagem", "CmdTerraplenagem")
                    : new(chave, nome, valor, "m³", SituacaoValor.Calculado, $"terraplenagem aplicada em {Quando(registro.Quando)}, com as marcas atuais", "CmdQuantitativos"));
            }
        }

        // Pavimento: medido nas peças só quando pedido (percorrer os pisos custa); a estrutura informada vem das vias.
        var semEstrutura = vias.Count(v => v.Estrutura is not { Camadas.Count: > 0 });
        var estrutura = vias.Count == 0 ? "" : semEstrutura == 0 ? "; todas as vias com a estrutura do pavimento informada"
            : $"; {semEstrutura} via(s) sem a estrutura do pavimento informada";
        res.Add(areaPavimento is { } a
            ? falhasPavimento > 0
                ? new("pavimento", "Pavimento (área em planta)", a, "m²", SituacaoValor.Invalido,
                    $"parcial: {falhasPavimento} marca(s) não puderam ser medidas (o log tem o erro)" + estrutura, "CmdQuantitativos")
                : new("pavimento", "Pavimento (área em planta)", a, "m²", SituacaoValor.Calculado, "medido agora nas peças geradas (pistas e nós)" + estrutura, "CmdQuantitativos")
            : vias.Count == 0 && !defs.Any(d => d is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition)
                ? new("pavimento", "Pavimento (área em planta)", 0, "m²", SituacaoValor.Calculado, "nenhuma via nem nó", "CmdSinalizarVia")
                : new("pavimento", "Pavimento (área em planta)", null, "m²", SituacaoValor.NaoCalculado,
                    "medido nas peças do modelo: use Calcular ou os Quantitativos" + estrutura, "CmdQuantitativos"));

        // Drenagem: o que há na rede, o Projeto de Drenagem e o estado das captações lidas do modelo (Etapa 8).
        var dispositivos = defs.OfType<DrainageDefinition>().Count();
        var trechos = defs.OfType<DrainagePipeDefinition>().Count();
        if (dispositivos + trechos == 0)
            res.Add(new("drenagem", "Rede de drenagem", 0, "", SituacaoValor.Calculado, "sem drenagem modelada", "CmdBocaDeLobo"));
        else
        {
            var cap = estados.FirstOrDefault(e => e.Modulo == "Drenagem");
            var detalhe = $"{dispositivos} dispositivo(s) e {trechos} trecho(s); " +
                          (projetoDrenagem ? "Projeto de Drenagem informado" : "sem Projeto de Drenagem: vazões e diâmetros não calculados") +
                          (cap != null ? $"; captações lidas do modelo: {cap.Detalhe}" : "");
            res.Add(new("drenagem", "Rede de drenagem", dispositivos, "", cap?.Situacao == SituacaoResultado.Desatualizado ? SituacaoValor.Desatualizado : SituacaoValor.Calculado,
                detalhe, cap?.Situacao == SituacaoResultado.Desatualizado ? "CmdCaptacoesDoModelo" : "CmdRedeDrenagem"));
        }

        // Inconsistências: da última validação gravada, com a data – o projeto pode ter mudado depois.
        if (ultimaValidacao == null)
            res.Add(new("inconsistencias", "Inconsistências", null, "", SituacaoValor.NaoCalculado, "verificação não executada neste projeto", "CmdValidarModelo"));
        else
        {
            int N(Severidade s) => ultimaValidacao.Achados.Count(a => a.Severidade == s);
            res.Add(new("inconsistencias", "Inconsistências", N(Severidade.Erro) + N(Severidade.Aviso), "", SituacaoValor.Calculado,
                $"{N(Severidade.Erro)} erro(s) e {N(Severidade.Aviso)} aviso(s) na verificação de {Quando(ultimaValidacao.Quando)}" +
                (ultimaValidacao.Versao is { } v ? $" (versão {v})" : "") + "; o projeto pode ter mudado depois", "CmdValidarModelo"));
        }
        return res;
    }

    // ------------------------------------------------------------------ situação (§8.4)

    public static List<ItemSituacao> Situacao(ValidacaoGravada? ultimaValidacao, IReadOnlyList<EstadoResultado> estados)
    {
        var res = new List<ItemSituacao>();
        if (ultimaValidacao == null)
            res.Add(new("Verificação", NivelSituacao.NaoVerificado, "verificação não executada neste projeto", "rodar o Validar Modelo", "CmdValidarModelo"));
        else
        {
            var erros = ultimaValidacao.Achados.Count(a => a.Severidade == Severidade.Erro);
            var avisos = ultimaValidacao.Achados.Count(a => a.Severidade == Severidade.Aviso);
            var quando = $"na verificação de {Quando(ultimaValidacao.Quando)}";
            res.Add(erros > 0 ? new("Verificação", NivelSituacao.Erro, $"existem erros: {erros} erro(s) e {avisos} aviso(s) {quando}", "ver o relatório do Validar Modelo", "CmdValidarModelo")
                : avisos > 0 ? new("Verificação", NivelSituacao.Aviso, $"existem avisos: {avisos} {quando}", "ver o relatório do Validar Modelo", "CmdValidarModelo")
                : new("Verificação", NivelSituacao.Ok, $"nenhum erro nem aviso {quando}; o projeto pode ter mudado depois", null, "CmdValidarModelo"));
        }
        foreach (var e in estados)
        {
            var (nivel, texto) = e.Situacao switch
            {
                SituacaoResultado.Desatualizado => (NivelSituacao.Aviso, $"necessita de recálculo – {e.Resultado}: {e.Detalhe}"),
                SituacaoResultado.NaoVerificavel => (NivelSituacao.NaoVerificado, $"{e.Resultado}: {e.Detalhe}"),
                SituacaoResultado.Atualizado => (NivelSituacao.Ok, $"{e.Resultado}: {e.Detalhe}"),
                _ => (NivelSituacao.Informacao, $"{e.Resultado}: {e.Detalhe}"),
            };
            var comando = e.Modulo switch { "Terraplenagem" => "CmdTerraplenagem", "Drenagem" => "CmdCaptacoesDoModelo", _ => null };
            res.Add(new(e.Modulo, nivel, texto, e.Acao, e.Acao != null ? comando : null));
        }
        return res;
    }

    /// <summary>Situação geral: o pior nível dos itens, dito pelo que se sabe – nunca "validado".</summary>
    public static (NivelSituacao Nivel, string Texto) Geral(IReadOnlyList<ItemSituacao> itens)
    {
        if (itens.Any(i => i.Nivel == NivelSituacao.Erro)) return (NivelSituacao.Erro, "Existem erros na última verificação");
        if (itens.Any(i => i.Nivel == NivelSituacao.Aviso))
            return (NivelSituacao.Aviso, itens.Any(i => i.Nivel == NivelSituacao.Aviso && i.Tema != "Verificação")
                ? "Há resultados a refazer" + (itens.Any(i => i.Tema == "Verificação" && i.Nivel == NivelSituacao.Aviso) ? " e avisos na última verificação" : "")
                : "Existem avisos na última verificação");
        if (itens.Any(i => i.Tema == "Verificação" && i.Nivel == NivelSituacao.NaoVerificado)) return (NivelSituacao.NaoVerificado, "Verificação não executada");
        if (itens.Any(i => i.Nivel == NivelSituacao.NaoVerificado)) return (NivelSituacao.NaoVerificado, "Sem erros na última verificação; há resultados não verificados");
        return (NivelSituacao.Ok, "Sem erros nem avisos na última verificação e resultados derivados atualizados");
    }

    // ------------------------------------------------------------------ módulos (§8.3)

    /// <summary>Módulo de cada tipo de definição (a mesma divisão dos painéis da faixa). Nulo = tipo sem módulo (não deve haver).</summary>
    public static string? ModuloDe(MarkingDefinition d) => d switch
    {
        RoadPavementDefinition or RailwayDefinition or RecessMarkingDefinition or CurbExtensionDefinition or SidewalkAreaDefinition or RampDefinition
            or BridgeDefinition or TunnelDefinition or TrenchDefinition => "viario",
        IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or InterchangeDefinition => "intersecoes",
        RetainingWallDefinition or SlopeDefinition => "terraplenagem",
        DrainageDefinition or DrainagePipeDefinition => "drenagem",
        LoteamentoDefinition or QuadraDefinition or UrbanElementDefinition or PlanterDefinition => "urbanismo",
        LinearMarkingDefinition or HatchMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition or ParkingMarkingDefinition
            or RepeatedMarkingDefinition or DeviceMarkingDefinition or SignDefinition or TrafficCalmingDefinition or ChannelizationDefinition
            or TactileRouteDefinition or RumbleStripDefinition or EscapeRampDefinition => "sinalizacao",
        SectionProfileDefinition or SignPlanDetailDefinition or LabelDefinition or LegendDefinition or SectionDimensionDefinition
            or TypicalDetailDefinition or QuantityTableDefinition or NotesDefinition or NorthArrowDefinition or LoteamentoDetalheDefinition
            or QuadroLoteamentoDefinition or SheetSegmentDefinition => "documentacao",
        _ => null,
    };

    public static List<ModuloProjeto> Modulos(IReadOnlyCollection<MarkingDefinition> defs, RegistroTerraplenagem? registro, ValidacaoGravada? ultimaValidacao)
    {
        var por = defs.GroupBy(ModuloDe).Where(g => g.Key != null).ToDictionary(g => g.Key!, g => g.Count());
        int C(string k) => por.GetValueOrDefault(k);
        string Estado(int n, string unidade) => n == 0 ? "sem elementos ainda" : $"{n} {unidade}";
        var vias = defs.OfType<RoadPavementDefinition>().ToList();
        var moldam = EarthworkRecord.Participantes(defs).Count;
        var comEstrutura = vias.Count(v => v.Estrutura is { Camadas.Count: > 0 });
        return new()
        {
            new("viario", "Sistema Viário", "Vias, eixos, perfis, seções, calçadas e obras de arte.", "via", "CmdSinalizarVia", "Nova via",
                C("viario"), Estado(C("viario"), "elemento(s)")),
            new("intersecoes", "Interseções", "Interseções, rotatórias, nós viários e balões.", "intersecao", "CmdIntersecao", "Interseção",
                C("intersecoes"), Estado(C("intersecoes"), "nó(s)")),
            new("terraplenagem", "Terraplenagem", "Toposolid moldado pelas vias e obras; taludes e muros.", "terraplenagem", "CmdTerraplenagem", "Terraplenagem",
                moldam + C("terraplenagem"),
                moldam == 0 && C("terraplenagem") == 0 ? "nada molda o terreno ainda"
                : $"{moldam} marca(s) moldam o terreno; " + (registro == null ? "terraplenagem sem registro" : $"aplicada em {Quando(registro.Quando)}")),
            new("pavimentacao", "Pavimentação", "Estrutura do pavimento de cada via e quantitativo por camada.", "estruturapav", "CmdEstruturaPavimento",
                "Estrutura do Pavimento", comEstrutura, vias.Count == 0 ? "nenhuma via" : $"{comEstrutura} de {vias.Count} via(s) com a estrutura informada"),
            new("drenagem", "Drenagem", "Dispositivos, rede, dimensionamento e memorial.", "rededren", "CmdRedeDrenagem", "Relatório da Rede",
                C("drenagem"), Estado(C("drenagem"), "elemento(s)")),
            new("urbanismo", "Urbanismo", "Loteamento, quadras, lotes, elementos urbanos e masterplan.", "loteamento", "CmdLoteamento", "Loteamento",
                C("urbanismo"), Estado(C("urbanismo"), "elemento(s)")),
            new("sinalizacao", "Sinalização", "Sinalização horizontal, vertical e segurança viária.", "placa", "CmdPlacas", "Placas",
                C("sinalizacao"), Estado(C("sinalizacao"), "marca(s)")),
            new("analise", "Análise", "Validação do modelo, diagnóstico do terreno e simulador de tráfego.", "validar", "CmdValidarModelo", "Validar Modelo",
                null, ultimaValidacao == null ? "verificação não executada" : $"última verificação em {Quando(ultimaValidacao.Quando)}"),
            new("documentacao", "Documentação", "Quantitativos, pranchas, memoriais e detalhes.", "quantitativos", "CmdQuantitativos", "Quantitativos",
                C("documentacao"), Estado(C("documentacao"), "detalhe(s) e quadro(s)")),
        };
    }

    /// <summary>Texto do resumo (copiar da Central): cabeçalho, situação, indicadores e módulos.</summary>
    public static string Texto(ResumoDoProjeto r, string projeto, DateTime quando)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"CENTRAL DO PROJETO – {projeto}");
        sb.AppendLine($"{Quando(quando)} · situação: {r.SituacaoGeral}");
        sb.AppendLine();
        sb.AppendLine("SITUAÇÃO");
        foreach (var i in r.Situacao) sb.AppendLine($"[{Nome(i.Nivel)}] {i.Tema}: {i.Texto}{(i.Acao != null ? $" → {i.Acao}" : "")}");
        sb.AppendLine();
        sb.AppendLine("INDICADORES");
        foreach (var i in r.Indicadores) sb.AppendLine($"{i.Nome}: {i.ValorTexto} ({Nome(i.Situacao)}) – {i.Detalhe}");
        sb.AppendLine();
        sb.AppendLine("MÓDULOS");
        foreach (var m in r.Modulos) sb.AppendLine($"{m.Nome}: {m.Estado}");
        return sb.ToString().TrimEnd();
    }
}
