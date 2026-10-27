using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Propriedade mostrada no painel: grupo, nome e valor já formatado.</summary>
public sealed record PropriedadeContexto(string Grupo, string Nome, string Valor);

/// <summary>Operação compatível com a seleção: o rótulo e o comando da faixa que a executa.</summary>
public sealed record OperacaoContexto(string Rotulo, string Comando);

/// <summary>Uma linha do estado da marca: o nível (a tela mostra o texto do nível, não só a cor) e o que se sabe.</summary>
public sealed record EstadoContexto(NivelSituacao Nivel, string Texto);

/// <summary>O que o painel contextual mostra para a seleção atual.</summary>
public sealed record ContextoSelecao(string Titulo, string Subtitulo, IReadOnlyList<PropriedadeContexto> Propriedades, IReadOnlyList<EstadoContexto> Estado,
    IReadOnlyList<OperacaoContexto> Operacoes, IReadOnlyList<string> Notas);

/// <summary>
/// Painel contextual (Etapa 10; Prompt de Interface §9): identifica o elemento do SinalizaBIM selecionado e mostra só as
/// propriedades dele – via (seção, perfil, estaqueamento, estrutura do pavimento), interseção, rotatória, balão, nó,
/// drenagem, quadra, loteamento, obra ou marca –, o estado que se sabe sem varrer o projeto e as operações compatíveis.
/// Vários elementos: só as propriedades iguais em todos e nenhuma alteração em lote. Nada aqui altera o modelo.
/// </summary>
public static class PainelContextual
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string M(double v, string fmt = "0.00") => v.ToString(fmt, Pt);
    private static string Quando(DateTime t) => t.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    private static string SimNao(bool b) => b ? "sim" : "não";

    private static readonly OperacaoContexto Editar = new("Editar", "CmdEditar");
    private static readonly OperacaoContexto Conjunto = new("Selecionar Conjunto", "CmdSelecionarConjunto");

    /// <summary>Sem elemento do plugin selecionado: orientação e atalhos.</summary>
    public static ContextoSelecao Vazio(int foraDoPlugin = 0) => new(
        "Nenhum elemento do SinalizaBIM selecionado",
        "Selecione uma via, interseção, rotatória, dispositivo de drenagem, quadra ou outra marca do SinalizaBIM para ver aqui as propriedades dela.",
        Array.Empty<PropriedadeContexto>(), Array.Empty<EstadoContexto>(),
        new[]
        {
            new OperacaoContexto("Central do Projeto", "CmdCentralProjeto"), new OperacaoContexto("Validar Modelo", "CmdValidarModelo"),
            new OperacaoContexto("Diagnóstico do Terreno", "CmdDiagnosticoTerreno"), new OperacaoContexto("Nova via", "CmdSinalizarVia"),
        },
        foraDoPlugin > 0 ? new[] { $"{foraDoPlugin} elemento(s) do Revit selecionado(s), fora do SinalizaBIM: as propriedades deles ficam na paleta Propriedades do Revit." }
            : Array.Empty<string>());

    /// <param name="selecionadas">Definições dos elementos selecionados (uma por elemento; a mesma marca pode vir várias vezes).</param>
    /// <param name="foraDoPlugin">Elementos selecionados que não são do SinalizaBIM.</param>
    /// <param name="registro">Registro da terraplenagem aplicada (estado da marca na terraplenagem).</param>
    /// <param name="buscar">Definição pelo Id (o loteamento de uma quadra); nula = sem buscar (quadra com as categorias padrão).</param>
    public static ContextoSelecao De(IReadOnlyList<MarkingDefinition> selecionadas, int foraDoPlugin = 0, RegistroTerraplenagem? registro = null,
        Func<string, MarkingDefinition?>? buscar = null)
    {
        var defs = selecionadas.GroupBy(d => d.Id).Select(g => g.First()).ToList();
        if (defs.Count == 0) return Vazio(foraDoPlugin);
        var notaFora = foraDoPlugin > 0 ? $"{foraDoPlugin} elemento(s) do Revit fora do SinalizaBIM também selecionado(s) – não entram aqui." : null;
        if (defs.Count == 1)
        {
            var c = Uma(defs[0], registro, buscar);
            return notaFora == null ? c : c with { Notas = c.Notas.Append(notaFora).ToList() };
        }
        var notas = new List<string>();
        if (notaFora != null) notas.Add(notaFora);
        var validar = new[] { new OperacaoContexto("Validar Modelo", "CmdValidarModelo") };
        if (defs.Select(d => d.GetType()).Distinct().Count() > 1)
        {
            notas.Add("Selecione elementos de um só tipo para ver as propriedades em comum.");
            return new($"{defs.Count} marcas do SinalizaBIM", "tipos diferentes",
                defs.GroupBy(d => d.KindName).OrderBy(g => g.Key, StringComparer.CurrentCulture)
                    .Select(g => new PropriedadeContexto("Seleção", g.Key, $"{g.Count()} marca(s)")).ToList(),
                Array.Empty<EstadoContexto>(), validar, notas);
        }
        // Mesmo tipo: só o que é igual em todas – o que difere não aparece (nada de valor "médio" ou do primeiro).
        var todas = defs.Select(d => Uma(d, registro, buscar)).ToList();
        var comuns = todas[0].Propriedades.Where(p => todas.Skip(1).All(t => t.Propriedades.Any(q => q == p))).ToList();
        notas.Add("Só as propriedades iguais em todas aparecem. Para alterar, edite uma marca por vez: alteração em lote não é oferecida.");
        return new($"{defs.Count} × {defs[0].KindName}", "seleção múltipla", comuns, Array.Empty<EstadoContexto>(), validar, notas);
    }

    /// <summary>Uma marca: identificação, as propriedades do tipo dela, o estado e as operações.</summary>
    public static ContextoSelecao Uma(MarkingDefinition d, RegistroTerraplenagem? registro = null, Func<string, MarkingDefinition?>? buscar = null)
    {
        var p = new List<PropriedadeContexto>();
        var estado = new List<EstadoContexto>();
        var ops = new List<OperacaoContexto> { Editar, Conjunto };
        var notas = new List<string>();
        void A(string grupo, string nome, string valor) => p.Add(new(grupo, nome, valor));

        A("Identificação", "Tipo", d.KindName);
        A("Identificação", "Código", d.DisplayCode);
        if (!string.IsNullOrWhiteSpace(d.Notes)) A("Identificação", "Observação", d.Notes!.Trim());
        if (d.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida) A("Identificação", "Hierarquia", Hierarquia.Label(h));
        A("Identificação", "Representação", d.Output.Mode == OutputMode.Detalhe2D ? "detalhe 2D (na vista)" : "modelo 3D");
        if (d.EditadoManualmente) estado.Add(new(NivelSituacao.Informacao, "editada à mão no modelo"));
        if (DefinitionVersioning.IsNewer(d)) estado.Add(new(NivelSituacao.Aviso, "gravada por uma versão mais nova do plugin: somente leitura"));

        switch (d)
        {
            case RoadPavementDefinition r:
                Via(r, A);
                ops.AddRange(new OperacaoContexto[] { new("Perfil da Via", "CmdPerfilVia"), new("Estrutura do Pavimento", "CmdEstruturaPavimento"),
                    new("Corte Transversal", "CmdCorteTransversal") });
                break;
            case IntersectionDefinition it:
                A("Interseção", "Controle", Memorial.ControlName(it.Control));
                A("Interseção", "Tipo", Memorial.IntersectionType(it));
                A("Interseção", "Vias ligadas", it.RoadIds.Count.ToString(Pt));
                A("Interseção", "Raio das esquinas", $"{M(it.CornerRadius)} m");
                A("Interseção", "Faixas de pedestres", SimNao(it.Crosswalks));
                if (it.Control == ControleIntersecao.Semaforo) A("Interseção", "Plano semafórico", it.SignalPlan != null ? "gravado" : "não gravado");
                A("Interseção", "Sinalização gerada pelo nó", $"{it.ChildIds.Count} marca(s)");
                ops.Add(new("Trocar Conexão", "CmdConexao"));
                break;
            case RoundaboutDefinition rb:
                A("Rotatória", "Tipo", rb.Type switch
                {
                    TipoRotatoria.Mini => "minirrotatória", TipoRotatoria.Compacta => "compacta", TipoRotatoria.UmaFaixa => "uma faixa",
                    TipoRotatoria.DuasFaixas => "duas faixas", TipoRotatoria.Turbo => "turbo", TipoRotatoria.Oval => "oval",
                    TipoRotatoria.ComBypass => "com by-pass", _ => "personalizada",
                });
                A("Rotatória", "Diâmetro externo", $"{M(2 * rb.OuterRadius)} m");
                A("Rotatória", "Raio da ilha central", $"{M(rb.IslandRadius)} m");
                A("Rotatória", "Anel", $"{rb.Lanes} faixa(s) de {M(rb.LaneWidth)} m");
                A("Rotatória", "Ramos", rb.Legs.Count.ToString(Pt));
                A("Rotatória", "By-pass", SimNao(rb.Bypass));
                ops.Add(new("Trocar Conexão", "CmdConexao"));
                break;
            case CulDeSacDefinition cs:
                A("Balão", "Formato", cs.Type switch
                {
                    TipoCulDeSac.Circular => "circular", TipoCulDeSac.ExcentricoEsquerda => "excêntrico à esquerda", TipoCulDeSac.ExcentricoDireita => "excêntrico à direita",
                    TipoCulDeSac.Martelo => "em T (martelo)", TipoCulDeSac.EmY => "em Y", TipoCulDeSac.EmLEsquerda => "em L à esquerda",
                    TipoCulDeSac.EmLDireita => "em L à direita", _ => "gota",
                });
                A("Balão", "Raio do balão", $"{M(cs.BulbRadius)} m");
                A("Balão", "Ligado a uma via", SimNao(cs.RoadId != null));
                ops.Add(new("Trocar Conexão", "CmdConexao"));
                break;
            case InterchangeDefinition nv:
                A("Nó viário", "Tipo", nv.Type switch
                {
                    TipoNoViario.Diamante => "diamante", TipoNoViario.DiamanteRotatorias => "diamante com rotatórias", TipoNoViario.TrevoCompleto => "trevo completo",
                    TipoNoViario.TrevoParcial => "trevo parcial", TipoNoViario.Trombeta => "trombeta", _ => "rotatória em dois níveis",
                });
                A("Nó viário", "Via principal", $"{nv.MainLanes} faixa(s) de {M(nv.MainLaneWidth)} m por sentido");
                A("Nó viário", "Via principal por baixo", SimNao(nv.MainBelow));
                break;
            case DrainageDefinition dr:
                Drenagem(dr, A, estado);
                ops.Add(new("Ler do Modelo", "CmdCaptacoesDoModelo"));
                ops.Add(new("Relatório da Rede", "CmdRedeDrenagem"));
                break;
            case DrainagePipeDefinition tubo:
                A("Rede", "Diâmetro", $"{M(tubo.Diameter * 1000, "0")} mm");
                A("Rede", "Cota de fundo a montante", tubo.InvertFrom is { } i0 ? $"{M(i0)} m" : "pela caixa de montante");
                A("Rede", "Cota de fundo a jusante", tubo.InvertTo is { } i1 ? $"{M(i1)} m" : "pela caixa de jusante");
                ops.Add(new("Relatório da Rede", "CmdRedeDrenagem"));
                break;
            case QuadraDefinition q:
                Quadra(q, A, notas, buscar);
                ops.AddRange(new OperacaoContexto[] { new("Quadra", "CmdQuadraLote"), new("Lote", "CmdLote"), new("Quadro da Quadra", "CmdQuadroQuadra") });
                break;
            case LoteamentoDefinition lot:
                A("Loteamento", "Nome", string.IsNullOrWhiteSpace(lot.Nome) ? "sem nome" : lot.Nome);
                A("Loteamento", "Município", string.IsNullOrWhiteSpace(lot.Municipio) ? "não informado" : lot.Municipio!);
                if (lot.GlebaPolygon is { } g) A("Loteamento", "Área da gleba", $"{M(Math.Abs(g.Area), "N2")} m²");
                A("Loteamento", "Categorias de lote", lot.Categorias.Count.ToString(Pt));
                A("Loteamento", "Lote mínimo", $"frente {M(lot.FrenteMinimaLote)} m e área {M(lot.AreaMinimaLote)} m²");
                A("Loteamento", "Mínimos da gleba", lot.Minimos.Count == 0 ? "não informados (não conferidos – V16)"
                    : $"{lot.Minimos.Count} informado(s); fonte: {(string.IsNullOrWhiteSpace(lot.FonteMinimos) ? "não informada" : lot.FonteMinimos)}");
                A("Loteamento", "Vias nomeadas", lot.Vias.Count.ToString(Pt));
                ops.AddRange(new OperacaoContexto[] { new("Quadros do Loteamento", "CmdQuadrosLoteamento"), new("Categorias e Filtros", "CmdCategoriasLotes"),
                    new("Masterplan", "CmdMasterplan") });
                break;
            case SignDefinition:
                ops.Add(new("Detalhar Placas", "CmdDetalharPlacas"));
                break;
        }
        if (d is ITerrainAware obra)
        {
            A("Terreno", "Molda o terreno", SimNao(obra.AdjustTerrain));
            ops.Add(new("Terraplenagem", "CmdTerraplenagem"));
        }
        if (NaTerraplenagem(d, registro) is { } t) estado.Add(t);
        return new(d.KindName, d.DisplayCode, p, estado, ops, notas);
    }

    /// <summary>
    /// Estado da marca na terraplenagem aplicada, só pela própria marca e pelo registro (sem varrer o projeto): em dia, mudou,
    /// ou molda o terreno e não estava nela. Nulo quando não se sabe ou não se aplica (um nó só molda o terreno pelas vias).
    /// </summary>
    public static EstadoContexto? NaTerraplenagem(MarkingDefinition d, RegistroTerraplenagem? registro)
    {
        var molda = d switch
        {
            ITerrainAware t => t.AdjustTerrain,
            RoadPavementDefinition r => r.Output.Grade is { AdjustTerrain: true },
            DrainageDefinition => true,
            _ => false,
        };
        if (registro == null) return molda ? new(NivelSituacao.NaoVerificado, "molda o terreno; sem registro da terraplenagem aplicada") : null;
        var quando = Quando(registro.Quando);
        if (registro.Assinaturas.TryGetValue(d.Id, out var sig))
            return sig == EarthworkRecord.Assinatura(d) ? new(NivelSituacao.Ok, $"terraplenagem: em dia com a aplicada em {quando}")
                : new(NivelSituacao.Aviso, $"terraplenagem: a marca mudou depois da aplicada em {quando} – rode a Terraplenagem");
        return molda ? new(NivelSituacao.Aviso, $"terraplenagem: molda o terreno, mas não estava na aplicada em {quando} – rode a Terraplenagem") : null;
    }

    private static void Via(RoadPavementDefinition r, Action<string, string, string> A)
    {
        A("Seção", "Pista", $"{M(r.LeftWidth)} m à esquerda + {M(r.RightWidth)} m à direita do eixo");
        A("Seção", "Calçadas", r.LeftSidewalk + r.RightSidewalk <= 1e-9 ? "sem calçadas" : $"{M(r.LeftSidewalk)} m (esquerda) e {M(r.RightSidewalk)} m (direita)");
        A("Seção", "Sentido", r.TwoWay ? "mão dupla" : "mão única");
        A("Seção", "Revestimento", Memorial.PavementName(r.Material));
        A("Eixo", "Extensão do eixo", r.PathRef.StoredPolyline() is { } eixo ? $"{M(eixo.Length, "N2")} m" : "traçado não guardado");
        var e = r.Estaqueamento;
        A("Eixo", "Estaqueamento", e == null || e.EhPadrao ? "padrão (estaca 0 no início, a cada 20 m)"
            : $"início em {M(e.Inicio)} m, intervalo de {M(e.Intervalo)} m, {e.Equacoes.Count} equação(ões)");
        if (r.Output.Grade is { } g)
        {
            A("Perfil", "Greide", $"{g.Points.Count} PIV(s); curva vertical padrão de {M(g.DefaultCurve, "0.#")} m");
            A("Perfil", "Molda o terreno", SimNao(g.AdjustTerrain));
        }
        else A("Perfil", "Greide", "sem greide (a via segue o plano ou o terreno)");
        if (r.Estrutura is { Camadas.Count: > 0 } est)
        {
            A("Pavimento", "Estrutura", $"{est.Camadas.Count} camada(s) sob o revestimento, {M(est.EspessuraCamadas * 100, "0.#")} cm no total");
            for (var i = 0; i < est.Camadas.Count; i++)
            {
                var c = est.Camadas[i];
                A("Pavimento", $"Camada {i + 1}", $"{(string.IsNullOrWhiteSpace(c.Nome) ? "sem nome" : c.Nome)}: {M(c.EspessuraCm, "0.#")} cm" +
                    (string.IsNullOrWhiteSpace(c.Material) ? "" : $", {c.Material}") + $" ({EstruturaPavimento.Nome(c.Funcao)})");
            }
            A("Pavimento", "Fonte", string.IsNullOrWhiteSpace(est.Fonte) ? "não informada" : est.Fonte!);
        }
        else A("Pavimento", "Estrutura", "não informada (a terraplenagem usa o subleito das opções)");
    }

    private static void Drenagem(DrainageDefinition dr, Action<string, string, string> A, List<EstadoContexto> estado)
    {
        A("Drenagem", "Dispositivo", DrainageDefinition.NomeTipo(dr.Type));
        if (dr.IsInlet)
        {
            A("Captação", "Área de contribuição", dr.AreaContribuicao > 1e-9 ? $"{M(dr.AreaContribuicao, "N1")} m²" : "não informada");
            A("Captação", "Declividade da sarjeta", dr.DeclividadeSarjeta is { } s ? $"{M(s * 100)} %" : "não informada");
            A("Captação", "Ponto baixo", SimNao(dr.PontoBaixo));
            A("Captação", "Coeficiente de escoamento", dr.CoeficienteEscoamento is { } c ? M(c) : "o do Projeto de Drenagem");
            estado.Add(dr.Lida is { } l
                ? new(NivelSituacao.Informacao, $"área e declividade lidas da via em {Quando(l.Quando)}; para saber se ainda estão em dia, use Ler do Modelo ou o Validar Modelo (V17)")
                : new(NivelSituacao.Informacao, "área e declividade informadas à mão (não lidas do modelo)"));
        }
        if (dr.BoxDepth > 1e-9) A("Drenagem", "Caixa", $"{M(dr.BoxLength)} × {M(dr.BoxWidth)} m, {M(dr.BoxDepth)} m de profundidade");
        A("Drenagem", "Tubo de saída", dr.OutletPipe ? $"Ø {M(dr.PipeDiameter * 1000, "0")} mm" : "não");
    }

    private static void Quadra(QuadraDefinition q, Action<string, string, string> A, List<string> notas, Func<string, MarkingDefinition?>? buscar)
    {
        A("Quadra", "Quadra", string.IsNullOrWhiteSpace(q.Nome) ? "sem nome" : q.Nome);
        var poly = q.Polygon;
        A("Quadra", "Área", poly is { IsValid: true } ? $"{M(Math.Abs(poly.Area), "N2")} m²" : "contorno inválido (Validar Modelo, V07)");
        A("Quadra", "Lote padrão", $"{M(q.Frente)} × {M(q.Profundidade)} m");
        A("Quadra", "Divisão", q.Divisao == ModoDivisao.Linhas ? "pelas linhas desenhadas" : "pela testada");
        A("Quadra", "Testadas", q.Testadas.Count.ToString(Pt));
        if (poly is not { IsValid: true }) return;
        var lot = buscar?.Invoke(q.LoteamentoId) as LoteamentoDefinition;
        A("Quadra", "Loteamento", lot != null ? (string.IsNullOrWhiteSpace(lot.Nome) ? "sem nome" : lot.Nome) : buscar == null ? "não consultado" : "não encontrado");
        if (lot == null) notas.Add("Lotes contados com as categorias e os mínimos padrão (loteamento não encontrado).");
        var div = LotDivider.Divide(q, lot);
        var lotes = div.Parcelas.Count(x => string.Equals(x.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase));
        A("Parcelamento", "Lotes", $"{lotes} (categoria Lote) em {div.Parcelas.Count} parcela(s)");
        var abaixo = div.Parcelas.Count(x => x.AbaixoDoMinimo != null);
        if (abaixo > 0) A("Parcelamento", "Abaixo do lote mínimo", $"{abaixo} parcela(s)");
    }
}
