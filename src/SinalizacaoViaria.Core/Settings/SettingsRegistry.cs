using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Settings;

/// <summary>Onde uma configuração fica: neste computador (config.json do usuário), no projeto (gravada no modelo) ou em cada elemento.</summary>
public enum OndeFica { Usuario, Projeto, Elemento }

/// <summary>Uma configuração no gerenciador: o grupo (Prompt de Interface §19), o nome, onde fica, o valor, o que afeta e o comando que a edita.</summary>
public sealed record ItemConfiguracao(string Grupo, string Nome, OndeFica Onde, string Valor, string Afeta, string? Comando = null);

/// <summary>De onde vem um perfil reutilizável: exemplo do plugin, do usuário sem referência, ou com uma referência citada pelo usuário.</summary>
public enum TipoPerfil { Exemplo, Usuario, ReferenciaCitada }

/// <summary>Um perfil reutilizável (seção-tipo, estrutura de pavimento, limites geométricos) e de onde ele vem.</summary>
public sealed record PerfilReutilizavel(string Categoria, string Nome, TipoPerfil Tipo, string? Fonte, string Resumo);

/// <summary>Limites geométricos guardados pelo usuário com um nome, para reaplicar (perfis de critérios de diagnóstico).</summary>
public sealed class PerfilCriterios
{
    public string Nome { get; set; } = "";
    public GeometricCriteria Criterios { get; set; } = new();
}

/// <summary>
/// Gerenciador de configurações (Etapa 12; Prompt de Interface §19 e §26): cada configuração com o grupo, onde fica (usuário,
/// projeto ou elemento), o valor e o que ela afeta; a validação de todas antes de aplicar (faixas e coerência entre campos); o
/// que muda ao salvar (nada é alterado nos elementos existentes sem um comando explícito); e os perfis reutilizáveis, rotulados
/// como exemplo, do usuário ou com referência citada – o plugin não traz perfis que aparentem ser norma.
/// </summary>
public static class RegistroConfiguracoes
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Grupos do gerenciador (§19), na ordem; só aparecem os que têm configuração no plugin.</summary>
    public static IReadOnlyList<string> Grupos { get; } = new[]
    {
        "Geral", "Unidades", "Geometria", "Topografia", "Sistema viário", "Seções-tipo", "Pavimentação", "Terraplenagem", "Drenagem", "Urbanismo",
        "Diagnósticos", "Documentação", "Aparência", "Desempenho",
    };

    private const string MarcasNovas = "marcas novas – as existentes ficam como estão";
    private const string NovasEAtualizadas = "marcas novas e as que forem atualizadas (Atualizar, editar) – as existentes só mudam quando atualizadas";
    private const string Proximas = "comportamento das próximas edições – nada muda agora";

    private static string N(double v, string f = "0.###") => v.ToString(f, Pt);
    private static string S(bool b) => b ? "sim" : "não";

    public static string Nome(OndeFica o) => o switch
    {
        OndeFica.Usuario => "usuário (este computador)",
        OndeFica.Projeto => "projeto (no modelo)",
        _ => "cada elemento (no modelo)",
    };

    public static string Nome(TipoPerfil t) => t switch
    {
        TipoPerfil.Exemplo => "exemplo do plugin (não é norma)",
        TipoPerfil.ReferenciaCitada => "referência citada pelo usuário (o plugin não confere)",
        _ => "do usuário (sem referência)",
    };

    private static string Relevo(RelevoVia r) => r switch
    {
        RelevoVia.Plana => "plana",
        RelevoVia.GreideSuavizado => "greide suavizado",
        _ => "acompanhando o terreno",
    };

    /// <summary>Os limites geométricos em texto: os informados e a fonte; "nenhum" quando vazio.</summary>
    public static string Criterios(GeometricCriteria? c)
    {
        if (c == null || c.IsEmpty) return "nenhum limite informado (nada é conferido)";
        var partes = new List<string>();
        void P(string nome, double? v, string un) { if (v is { } x) partes.Add($"{nome} {N(x)}{un}"); }
        P("raio mín.", c.RaioMinimo, " m");
        P("tangente mín.", c.TangenteMinima, " m");
        P("deflexão máx. sem curva", c.DeflexaoMaximaSemCurva, "°");
        P("rampa máx.", c.RampaMaxima, " %");
        P("rampa mín.", c.RampaMinima, " %");
        P("K convexa", c.KMinimoConvexa, "");
        P("K côncava", c.KMinimoConcava, "");
        P("curva vertical mín.", c.CurvaVerticalMinima, " m");
        return string.Join("; ", partes) + $" – {c.FonteTexto}";
    }

    private sealed record Descritor(string Grupo, string Nome, Func<PluginSettings, string> Valor, string Afeta);

    private static readonly Descritor[] DoUsuarioDescritores =
    {
        new("Geral", "Representação padrão das marcas", s => s.DefaultMode == OutputMode.Modelo3D ? "Modelo 3D" : "Detalhe 2D", MarcasNovas),
        new("Geral", "Velocidade padrão das vias (km/h)", s => N(s.DefaultSpeed, "0"), "vias novas (Sinalizar Via)"),
        new("Geral", "Últimas escolhas das janelas", s => $"{s.LastUsed.Keys.Count(k => !k.StartsWith(TamanhosDeJanela.Prefixo, StringComparison.Ordinal))} lembrada(s)",
            "só a abertura das janelas – não é dado do modelo"),
        new("Unidades", "Unidades e números", _ => "SI (m, m², m³, un) e padrão brasileiro (vírgula decimal)", "telas, CSV e planilhas – fixo, não configurável"),
        new("Geometria", "Limites do traçado e do greide", s => Criterios(s.CriteriosGeometricos), "Perfil da Via (verificação) e Validar Modelo (V12) – a próxima verificação"),
        new("Geometria", "Desvio máximo dos contornos (mm)", s => N(s.BoundaryTolerance * 1000, "0.##"), NovasEAtualizadas),
        new("Topografia", "Declividade máxima do terreno (%)", s => s.CriteriosGeometricos?.DeclividadeMaximaTerreno is { } d ? N(d) : "não conferida",
            "Validar Modelo (V14) e Diagnóstico do Terreno – a próxima verificação"),
        new("Topografia", "Relevo da via nova", s => $"{Relevo(s.RoadRelief)}; rampa máxima {N(s.RoadReliefMaxGrade * 100, "0.#")} %", "vias novas"),
        new("Topografia", "Acompanhar superfícies por padrão", s => S(s.DrapeByDefault), MarcasNovas),
        new("Sistema viário", "Atualizar as marcas quando as linhas mudam", s => S(s.AutoUpdate), Proximas),
        new("Sistema viário", "Interseções automáticas", s => S(s.AutoIntersect), Proximas),
        new("Sistema viário", "Faixas de pedestres nas interseções automáticas", s => S(s.AutoCrosswalks), Proximas),
        new("Sistema viário", "Ímã de conexão", s => S(s.AutoConnect), Proximas),
        new("Seções-tipo", "Modelos de via do usuário", s => $"{s.CustomRoadTemplates.Count} modelo(s)", "Sinalizar Via e janela da via (escolher o modelo)"),
        new("Seções-tipo", "Última seção usada", s => string.IsNullOrWhiteSpace(s.LastRoadSetup) ? "nenhuma" : "guardada", "próximo Sinalizar Via (reabre a seção)"),
        new("Pavimentação", "Estruturas de pavimento guardadas", s => $"{s.ModelosEstruturaPavimento.Count} estrutura(s)", "Estrutura do Pavimento – só quando aplicada a uma via"),
        new("Terraplenagem", "Taludes da via nova (H : 1 V)", s => $"corte {N(s.RoadCutSlope)}; aterro {N(s.RoadFillSlope)}", "vias novas em greide suavizado"),
        new("Diagnósticos", "Perfis de limites geométricos", s => $"{s.PerfisCriterios.Count} perfil(is)", "só ao aplicar um perfil aos limites"),
        new("Diagnósticos", "Histórico do Validar Modelo", _ => "um arquivo por projeto na pasta do usuário", "comparação novo × persistente × resolvido – fica neste computador, não no modelo"),
        new("Documentação", "Material de pintura padrão", s => s.DefaultMaterial, "marcas novas e, nos Quantitativos, o consumo das marcas sem material próprio"),
        new("Documentação", "Fonte das legendas", s => s.FontFamily + (s.FontBold ? ", negrito" : ""), NovasEAtualizadas),
        new("Documentação", "Catálogo normativo do usuário", s => string.IsNullOrWhiteSpace(s.UserCatalogPath) ? "o padrão (pasta do usuário)" : s.UserCatalogPath!,
            "tipos, dimensões, cores e materiais – as marcas existentes só mudam com Atualizar Todas"),
        new("Aparência", "Contorno visível nas regiões 2D", s => S(s.VisibleBoundary2D), NovasEAtualizadas),
        new("Aparência", "Materiais com a aparência realista da biblioteca do Revit", s => S(s.RealisticAppearance), "materiais do plugin ainda sem aparência, na próxima geração de cada cor"),
        new("Aparência", "Tamanho das janelas lembrado", s => $"{s.LastUsed.Keys.Count(k => k.StartsWith(TamanhosDeJanela.Prefixo, StringComparison.Ordinal))} janela(s)",
            "só a abertura das janelas – \"Restaurar o tamanho das janelas\" volta ao padrão"),
        new("Desempenho", "Pavimento, calçada, meio-fio e grama como Piso", s => S(s.PhysicalAsFloors), NovasEAtualizadas),
        new("Desempenho", "Rampas como Piso (fundo plano, topo inclinado)", s => S(s.RampsAsFloors), NovasEAtualizadas),
        new("Desempenho", "Pintura como Piso fino", s => S(s.PaintAsFloors), NovasEAtualizadas),
        new("Desempenho", "Espessura mínima modelada (m)", s => N(s.MinModelThickness, "0.####"), NovasEAtualizadas),
        new("Desempenho", "Elevação adicional padrão (m)", s => N(s.ElevationOffset, "0.####"), MarcasNovas),
        new("Desempenho", "Comprimento máximo das peças na projeção (m)", s => N(s.DrapePieceLength, "0.##"), NovasEAtualizadas),
    };

    /// <summary>As configurações do usuário (config.json neste computador), na ordem dos grupos.</summary>
    public static List<ItemConfiguracao> DoUsuario(PluginSettings s) =>
        DoUsuarioDescritores.Select(d => new ItemConfiguracao(d.Grupo, d.Nome, OndeFica.Usuario, d.Valor(s), d.Afeta, "CmdConfiguracoes")).ToList();

    /// <summary>
    /// As configurações do projeto (gravadas no modelo – Projeto de Drenagem, terraplenagem aplicada) e as de cada elemento
    /// (estaqueamento, estrutura do pavimento, mínimos do loteamento), com o comando que as edita.
    /// </summary>
    public static List<ItemConfiguracao> DoProjeto(ProjetoDrenagem drenagem, RegistroTerraplenagem? terraplenagem, IReadOnlyCollection<MarkingDefinition> defs)
    {
        const string rede = "Rede de Drenagem, dimensionamento, Validar Modelo (V13) e memorial – a próxima verificação ou cálculo";
        var p = drenagem;
        string Faixa(double? a, double? b, string un) => a == null && b == null ? "não conferida" : $"{(a is { } x ? N(x) : "—")} a {(b is { } y ? N(y) : "—")} {un}";
        var res = new List<ItemConfiguracao>
        {
            new("Drenagem", "Equação de chuvas intensas", OndeFica.Projeto,
                p.Chuva == null ? "não informada – a vazão não é calculada (só a geometria da rede é conferida)"
                    : $"{(string.IsNullOrWhiteSpace(p.Chuva.Nome) ? "informada" : p.Chuva.Nome)}{(string.IsNullOrWhiteSpace(p.Chuva.Fonte) ? "" : $" ({p.Chuva.Fonte})")}; TR {N(p.TempoRetorno)} anos",
                rede, "CmdProjetoDrenagem"),
            new("Drenagem", "Critérios da rede", OndeFica.Projeto,
                $"C padrão {N(p.CoeficientePadrao)}; Manning {N(p.Rugosidade)}; Ø mínimo {(p.DiametroMinimo is { } d ? N(d) + " m" : "—")}; " +
                $"recobrimento mínimo {(p.RecobrimentoMinimo is { } r ? N(r) + " m" : "—")}; velocidade {Faixa(p.VelocidadeMinima, p.VelocidadeMaxima, "m/s")}; " +
                $"declividade {Faixa(p.DeclividadeMinima, p.DeclividadeMaxima, "%")}", rede, "CmdProjetoDrenagem"),
            new("Drenagem", "Fonte dos critérios", OndeFica.Projeto, string.IsNullOrWhiteSpace(p.Fonte) ? "não informada" : p.Fonte!.Trim(),
                "relatório da rede e memorial", "CmdProjetoDrenagem"),
            new("Terraplenagem", "Última terraplenagem aplicada", OndeFica.Projeto,
                terraplenagem is not { } t ? "nenhuma – os Quantitativos usam o fator de homogeneização padrão (1,25)"
                    : $"{t.Quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}; subleito {N(t.Subleito)} m; taludes corte {N(t.TaludeCorte)} e aterro {N(t.TaludeAterro)}" +
                      (t.Homogeneizacao is { } h ? $"; homogeneização {N(h, "0.00")}" : ""),
                "Quantitativos (balanço de massas), Validar Modelo (V15) e Diagnóstico do Terreno", "CmdTerraplenagem"),
        };
        var vias = defs.OfType<RoadPavementDefinition>().ToList();
        if (vias.Count > 0)
        {
            res.Add(new("Seções-tipo", "Seção de cada via", OndeFica.Elemento, $"{vias.Count} via(s), cada uma com a sua seção guardada",
                "refazer a via pela janela da via (a seção fica na via)", "CmdEditar"));
            res.Add(new("Sistema viário", "Estaqueamento das vias", OndeFica.Elemento,
                $"{vias.Count(v => v.Estaqueamento != null)} de {vias.Count} via(s) com estaqueamento próprio (as demais: distância desde o início)",
                "estacas nas telas, perfis e relatórios da via", "CmdEditar"));
            res.Add(new("Pavimentação", "Estrutura do pavimento das vias", OndeFica.Elemento,
                $"{vias.Count(v => v.Estrutura is { Camadas.Count: > 0 })} de {vias.Count} via(s) com estrutura informada",
                "Terraplenagem (corte pelo fundo da estrutura) e camadas nos Quantitativos", "CmdEstruturaPavimento"));
        }
        var lots = defs.OfType<LoteamentoDefinition>().ToList();
        if (lots.Count > 0)
            res.Add(new("Urbanismo", "Mínimos e lote mínimo dos loteamentos", OndeFica.Elemento,
                $"{lots.Count(l => l.Minimos.Count > 0)} de {lots.Count} loteamento(s) com mínimos informados",
                "Validar Modelo (V16), quadros e memorial do loteamento", "CmdLoteamento"));
        return res;
    }

    /// <summary>
    /// Confere as configurações do usuário antes de aplicar: faixas, valores não numéricos, coerência entre campos (rampa mínima
    /// maior que a máxima), material fora do catálogo, catálogo do usuário que não existe e limites sem fonte. Sem problema, uma
    /// linha "ok".
    /// </summary>
    /// <param name="materiais">Nomes dos materiais do catálogo.</param>
    /// <param name="existe">Se um arquivo existe (catálogo do usuário).</param>
    public static List<EstadoContexto> Validar(PluginSettings s, IEnumerable<string> materiais, Func<string, bool>? existe = null)
    {
        var res = new List<EstadoContexto>();
        void Erro(string t) => res.Add(new(NivelSituacao.Erro, t));
        void Faixa(string nome, double v, double min, double max, string un = "")
        {
            if (!double.IsFinite(v) || v < min || v > max) Erro($"{nome}: {(double.IsFinite(v) ? N(v) : "inválido")}{un} – use de {N(min)} a {N(max)}{un}.");
        }
        Faixa("Velocidade padrão", s.DefaultSpeed, 10, 200, " km/h");
        Faixa("Espessura mínima modelada", s.MinModelThickness, 0.001, 0.1, " m");
        Faixa("Elevação adicional padrão", s.ElevationOffset, -10, 10, " m");
        Faixa("Comprimento das peças na projeção", s.DrapePieceLength, 0.5, 50, " m");
        Faixa("Desvio máximo dos contornos", s.BoundaryTolerance * 1000, 0, 50, " mm");
        // As mesmas faixas da janela da via (relevo da via nova).
        Faixa("Rampa máxima da via nova", s.RoadReliefMaxGrade * 100, 0.5, 30, " %");
        Faixa("Talude de corte da via nova", s.RoadCutSlope, 0.2, 5);
        Faixa("Talude de aterro da via nova", s.RoadFillSlope, 0.5, 5);
        if (string.IsNullOrWhiteSpace(s.FontFamily)) Erro("Fonte das legendas vazia.");
        if (!materiais.Contains(s.DefaultMaterial, StringComparer.Ordinal))
            Erro($"Material de pintura padrão \"{s.DefaultMaterial}\" não está no catálogo – a pintura usaria o primeiro material do catálogo.");
        if (!string.IsNullOrWhiteSpace(s.UserCatalogPath) && existe != null && !existe(s.UserCatalogPath!))
            res.Add(new(NivelSituacao.Aviso, $"Catálogo do usuário não encontrado ({s.UserCatalogPath}): vale o catálogo padrão do plugin."));
        var c = s.CriteriosGeometricos ?? new GeometricCriteria();
        void Limite(string nome, double? v, double max, string un)
        {
            if (v is { } x && (!double.IsFinite(x) || x < 0 || x > max)) Erro($"{nome}: {(double.IsFinite(x) ? N(x) : "inválido")}{un} – use de 0 a {N(max)}{un}.");
        }
        Limite("Raio mínimo", c.RaioMinimo, 100000, " m");
        Limite("Tangente mínima", c.TangenteMinima, 100000, " m");
        Limite("Deflexão máxima sem curva", c.DeflexaoMaximaSemCurva, 180, "°");
        Limite("Rampa máxima", c.RampaMaxima, 100, " %");
        Limite("Rampa mínima", c.RampaMinima, 100, " %");
        Limite("K mínimo da curva convexa", c.KMinimoConvexa, 100000, "");
        Limite("K mínimo da curva côncava", c.KMinimoConcava, 100000, "");
        Limite("Comprimento mínimo de curva vertical", c.CurvaVerticalMinima, 100000, " m");
        Limite("Declividade máxima do terreno", c.DeclividadeMaximaTerreno, 1000, " %");
        if (c.RampaMinima is { } rmin && c.RampaMaxima is { } rmax && rmin > rmax)
            Erro($"Rampa mínima ({N(rmin)} %) maior que a máxima ({N(rmax)} %): nenhuma rampa passaria.");
        var temLimite = !c.IsEmpty || c.DeclividadeMaximaTerreno != null;
        if (temLimite && string.IsNullOrWhiteSpace(c.Fonte))
            res.Add(new(NivelSituacao.Aviso, "Limites geométricos sem fonte: os relatórios dizem \"fonte não informada pelo usuário\" – informe a norma, a edição, a tabela e a classe."));
        if (!temLimite && !string.IsNullOrWhiteSpace(c.Fonte))
            res.Add(new(NivelSituacao.Informacao, "Fonte dos limites informada sem nenhum limite: nada é conferido."));
        if (res.Count == 0) res.Add(new(NivelSituacao.Ok, "Todas as configurações do usuário são válidas."));
        return res;
    }

    /// <summary>
    /// O que muda ao salvar (§19.1: informar quando a alteração afeta cálculos existentes; evitar alterações silenciosas): cada
    /// configuração alterada, de quanto para quanto e o que ela afeta. Nada é aplicado aos elementos existentes sem um comando.
    /// </summary>
    public static List<string> Impacto(PluginSettings antes, PluginSettings depois) =>
        DoUsuarioDescritores.Select(d => (d, A: d.Valor(antes), B: d.Valor(depois))).Where(x => x.A != x.B)
            .Select(x => $"{x.d.Nome}: {x.A} → {x.B} – {x.d.Afeta}.").ToList();

    /// <summary>
    /// Perfis reutilizáveis (§19.2): as seções-tipo do plugin (exemplos – larguras típicas para começar, não norma) e as do
    /// usuário, as estruturas de pavimento e os limites geométricos guardados, cada um com a referência citada quando há.
    /// </summary>
    public static List<PerfilReutilizavel> Perfis(PluginSettings s, IEnumerable<string> secoesDoPlugin)
    {
        static TipoPerfil Tipo(string? fonte) => string.IsNullOrWhiteSpace(fonte) ? TipoPerfil.Usuario : TipoPerfil.ReferenciaCitada;
        var res = secoesDoPlugin.Select(n => new PerfilReutilizavel("Seções-tipo", n, TipoPerfil.Exemplo, null,
            "seção de exemplo do plugin: larguras típicas para começar – não representa norma")).ToList();
        res.AddRange(s.CustomRoadTemplates.Select(t => new PerfilReutilizavel("Seções-tipo", t.Name, TipoPerfil.Usuario, null, "seção salva pelo usuário")));
        res.AddRange(s.ModelosEstruturaPavimento.Select(m => new PerfilReutilizavel("Estruturas de pavimento", m.Nome, Tipo(m.Estrutura.Fonte), m.Estrutura.Fonte,
            $"{m.Estrutura.Camadas.Count} camada(s), {N(m.Estrutura.Camadas.Where(c => double.IsFinite(c.EspessuraCm)).Sum(c => c.EspessuraCm), "0.#")} cm")));
        res.AddRange(s.PerfisCriterios.Select(p => new PerfilReutilizavel("Limites geométricos", p.Nome, Tipo(p.Criterios.Fonte), p.Criterios.Fonte, Criterios(p.Criterios))));
        return res;
    }
}

/// <summary>
/// Tamanho das janelas lembrado entre sessões (Prompt de Interface §26), em <see cref="PluginSettings.LastUsed"/>: "largura x altura"
/// por tipo de janela. Ilegível ou menor que o mínimo da janela = o tamanho padrão dela (estado previsível); maior que a área
/// da tela = limitado à tela.
/// </summary>
public static class TamanhosDeJanela
{
    public const string Prefixo = "janela:";

    public static string Chave(string tipo) => Prefixo + tipo;

    public static string Escrever(double largura, double altura) =>
        $"{Math.Round(largura).ToString(CultureInfo.InvariantCulture)}x{Math.Round(altura).ToString(CultureInfo.InvariantCulture)}";

    public static (double Largura, double Altura)? Ler(string? texto, double minLargura, double minAltura, double maxLargura, double maxAltura)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var p = texto.Split('x');
        if (p.Length != 2 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) return null;
        if (!double.IsFinite(w) || !double.IsFinite(h) || w < Math.Max(1, minLargura) || h < Math.Max(1, minAltura)) return null;
        return (Math.Min(w, maxLargura), Math.Min(h, maxAltura));
    }

    /// <summary>Esquece o tamanho de todas as janelas; devolve quantas eram.</summary>
    public static int Limpar(Dictionary<string, string> lembradas)
    {
        var chaves = lembradas.Keys.Where(k => k.StartsWith(Prefixo, StringComparison.Ordinal)).ToList();
        foreach (var k in chaves) lembradas.Remove(k);
        return chaves.Count;
    }
}
