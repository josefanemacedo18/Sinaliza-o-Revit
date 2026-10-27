using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// As partes da tela da rede (Prompt de Interface §16) – consistência geométrica, conectividade, verificação de parâmetros,
/// cálculo hidráulico e dimensionamento – e a drenagem × vias e terreno (Etapa 6).
/// </summary>
public enum CategoriaRede { Geometria, Conectividade, Parametros, Hidraulica, Dimensionamento, ViasETerreno }

/// <summary>Um problema na tela: a parte, o nível, o elemento, o critério, a explicação, o que fazer e as marcas envolvidas.</summary>
public sealed record ItemRede(CategoriaRede Categoria, NivelSituacao Nivel, string Elemento, string Criterio, string Texto, string Correcao,
    IReadOnlyList<string> Ids);

/// <summary>Uma parte da tela: título, o que ela confere (e com que fonte), a situação, as notas e os problemas.</summary>
public sealed record SecaoRede(CategoriaRede Categoria, string Titulo, string Escopo, EstadoContexto Situacao, IReadOnlyList<EstadoContexto> Notas,
    IReadOnlyList<ItemRede> Itens);

/// <summary>O diâmetro do modelo diante do calculado.</summary>
public enum SituacaoDiametro { Igual, Aumentar, Reduzir, SemDiametro, NaoDimensionavel }

/// <summary>Uma linha do dimensionamento: o trecho, a vazão de projeto, a declividade (%), o diâmetro do modelo e o calculado (m).</summary>
public sealed record LinhaDimensionamento(string Id, string Trecho, string Montante, string Jusante, double Vazao, double Declividade,
    double DiametroModelo, double? DiametroCalculado, SituacaoDiametro Situacao)
{
    public bool Muda => Situacao is SituacaoDiametro.Aumentar or SituacaoDiametro.Reduzir;
}

/// <summary>Conteúdo da tela da rede: o resumo, as partes, os trechos medidos, os dispositivos, o perfil e o dimensionamento.</summary>
public sealed record TelaRedeDrenagem(string Resumo, IReadOnlyList<SecaoRede> Secoes, IReadOnlyList<TrechoRede> Trechos,
    IReadOnlyList<DispositivoRede> Dispositivos, IReadOnlyList<LinhaPerfilRede> Perfil, IReadOnlyList<LinhaDimensionamento> Dimensionamento,
    IReadOnlyList<ProblemaRede> Problemas, ResultadoDrenagem Hidraulica, IReadOnlyList<CaptacaoLida> Captacoes)
{
    public SecaoRede Secao(CategoriaRede c) => Secoes.First(s => s.Categoria == c);
    /// <summary>Trechos cujo diâmetro do modelo difere do calculado (o que "Aplicar os diâmetros calculados" muda).</summary>
    public int Mudam => Dimensionamento.Count(l => l.Muda);
}

/// <summary>
/// Tela da rede de drenagem (Etapa 11; Prompt de Interface §16): os problemas que o núcleo já encontra – <see cref="DrainageNetwork"/>,
/// <see cref="DrainageHydraulics"/> e <see cref="DrenagemDoModelo"/> – separados em consistência geométrica, conectividade,
/// verificação de parâmetros, cálculo hidráulico e dimensionamento, mais a drenagem × vias e terreno; cada um com o elemento,
/// a explicação e o que fazer. Sem equação de chuva, o cálculo hidráulico e o dimensionamento aparecem como não feitos – a
/// conferência geométrica nunca é apresentada como dimensionamento. Nenhuma regra nova: a tela só organiza e explica.
/// </summary>
public static class RedeDrenagemTela
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string fmt = "0.00") => v.ToString(fmt, Pt);
    private static string Mm(double m) => N(m * 1000, "0");

    /// <summary>Critério do <see cref="ProblemaRede"/> → parte da tela. Todo critério que o núcleo emite está aqui (teste de regressão).</summary>
    private static readonly Dictionary<string, CategoriaRede> Categorias = new()
    {
        ["sentido"] = CategoriaRede.Geometria,
        ["ligação"] = CategoriaRede.Conectividade,
        ["dispositivo"] = CategoriaRede.Conectividade,
        ["ciclo"] = CategoriaRede.Conectividade,
        ["declividade"] = CategoriaRede.Parametros,
        ["cobrimento"] = CategoriaRede.Parametros,
        ["diâmetro"] = CategoriaRede.Parametros,
        ["espaçamento"] = CategoriaRede.Parametros,
        ["queda"] = CategoriaRede.Parametros,
        ["alinhamento"] = CategoriaRede.Parametros,
        ["vazão"] = CategoriaRede.Hidraulica,
        ["capacidade"] = CategoriaRede.Hidraulica,
        ["velocidade"] = CategoriaRede.Hidraulica,
        ["sarjeta"] = CategoriaRede.Hidraulica,
        ["engolimento"] = CategoriaRede.Hidraulica,
        ["sarjeta-modelo"] = CategoriaRede.ViasETerreno,
        ["ponto-baixo-modelo"] = CategoriaRede.ViasETerreno,
        ["ponto-baixo"] = CategoriaRede.ViasETerreno,
        ["talvegue"] = CategoriaRede.ViasETerreno,
    };

    /// <summary>Os critérios que a tela conhece.</summary>
    public static IReadOnlyCollection<string> CriteriosConhecidos => Categorias.Keys;

    /// <summary>A parte de um problema; critério desconhecido: parâmetros se for por limite, senão geometria.</summary>
    public static CategoriaRede Categoria(ProblemaRede p) =>
        Categorias.TryGetValue(p.Criterio, out var c) ? c : p.PorLimite ? CategoriaRede.Parametros : CategoriaRede.Geometria;

    /// <summary>O nível do Validar Modelo (V13): aviso; a divergência entre o informado na captação e o greide é informação.</summary>
    public static NivelSituacao Nivel(ProblemaRede p) => p.Criterio is "sarjeta-modelo" or "ponto-baixo-modelo" ? NivelSituacao.Informacao : NivelSituacao.Aviso;

    /// <summary>O que fazer, pelos comandos que existem (Editar o trecho muda o diâmetro e as cotas de fundo).</summary>
    public static string Correcao(ProblemaRede p) => p.Criterio switch
    {
        "sentido" => "Confira as cotas de fundo (Editar o trecho ou os dispositivos): a de montante fica acima da de jusante; se o sentido está errado, refaça o trecho clicando primeiro no de montante.",
        "ligação" => "Apague o trecho ou refaça-o entre dois dispositivos diferentes (Trecho de Rede).",
        "dispositivo" => "O dispositivo da ponta foi apagado ou é uma canaleta: refaça o trecho entre duas caixas (Trecho de Rede) ou apague-o.",
        "ciclo" => "Apague ou refaça um dos trechos do ciclo para que a água corra até um lançamento.",
        "declividade" => "Ajuste as cotas de fundo do trecho (Editar o trecho ou os dispositivos) ou reveja o critério no Projeto de Drenagem.",
        "cobrimento" => "Aprofunde o trecho (cotas de fundo) ou reveja a superfície sobre ele; o recobrimento é medido sob a superfície acabada.",
        "diâmetro" => "Aumente o diâmetro do trecho (Editar o trecho) ou reveja o mínimo no Projeto de Drenagem.",
        "espaçamento" => "Intercale um PV no trecho (Boca de Lobo / PV e Trecho de Rede) – tabela 6.1 do Manual SUDERHSA.",
        "queda" => "Preveja o PV de queda no detalhamento ou reduza o desnível entre os fundos.",
        "alinhamento" => "Rebaixe o tubo que sai para alinhar as geratrizes superiores (Manual SUDERHSA, fig. 6.2).",
        "vazão" => "A divisão da vazão entre as saídas não é calculada: confira se o dispositivo deve ter mais de uma saída.",
        "capacidade" => "Aumente o diâmetro ou a declividade – a parte 5 (Dimensionamento) mostra o diâmetro calculado.",
        "velocidade" => "Ajuste a declividade (cotas de fundo) ou o diâmetro do trecho.",
        "sarjeta" => "Coloque uma captação antes ou divida a área de contribuição.",
        "engolimento" => "Aumente a captação (tipo ou módulos) ou divida a área com outra captação.",
        "sarjeta-modelo" => "Confira a declividade informada ou use Ler do Modelo.",
        "ponto-baixo-modelo" => "Marque a captação como em ponto baixo (o engolimento muda) ou use Ler do Modelo.",
        "ponto-baixo" => "Coloque uma captação no ponto baixo ou reveja o greide.",
        "talvegue" => "Preveja uma travessia (bueiro) ou confira o terreno natural.",
        _ => "Veja a descrição.",
    };

    public static string Titulo(CategoriaRede c) => c switch
    {
        CategoriaRede.Geometria => "1. Consistência geométrica",
        CategoriaRede.Conectividade => "2. Conectividade da rede",
        CategoriaRede.Parametros => "3. Verificação de parâmetros",
        CategoriaRede.Hidraulica => "4. Cálculo hidráulico",
        CategoriaRede.Dimensionamento => "5. Dimensionamento",
        _ => "Drenagem × vias e terreno",
    };

    public static ItemRede Item(ProblemaRede p) =>
        new(Categoria(p), Nivel(p), p.Elemento, p.Criterio, p.Texto, Correcao(p), p.Ids ?? Array.Empty<string>());

    /// <summary>
    /// Todos os problemas, na ordem do relatório: geometria e critérios da rede, cálculo hidráulico (com a equação de chuva),
    /// captações × greide, pontos baixos da sarjeta sem captação e talvegues do terreno natural sob aterro (com o terreno).
    /// </summary>
    public static List<ProblemaRede> Problemas(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p, Func<Vec2, double?>? superficie,
        Func<Vec2, double?>? terrenoNatural, ResultadoDrenagem? hidraulica = null, IReadOnlyList<CaptacaoLida>? captacoes = null)
    {
        var res = DrainageNetwork.Check(defs, p, superficie);
        res.AddRange((hidraulica ?? DrainageHydraulics.Calcular(defs, p)).Problemas);
        if (!defs.Any(d => d is DrainageDefinition)) return res;
        res.AddRange(DrenagemDoModelo.Divergencias(captacoes ?? DrenagemDoModelo.Captacoes(defs)));
        foreach (var pb in DrenagemDoModelo.PontosBaixosSemCaptacao(defs))
            res.Add(new ProblemaRede(pb.Via, "ponto-baixo", $"Ponto baixo da sarjeta sem captação: {pb.Via}, lado {DrenagemDoModelo.Lado(pb.Lado)}, estaca {pb.EstacaTexto}.",
                false, new[] { pb.ViaId }));
        if (terrenoNatural != null)
            foreach (var tv in DrenagemDoModelo.TalveguesSemTravessia(defs, terrenoNatural))
                res.Add(new ProblemaRede(tv.Via, "talvegue", $"Talvegue sob o aterro da {tv.Via}, estaca {tv.EstacaTexto} (aterro {N(tv.Aterro, "0.0")} m): sem travessia da rede – a via represa a água.",
                    false, new[] { tv.ViaId }));
        return res;
    }

    /// <summary>
    /// Dimensionamento (com a equação de chuva): o cálculo refeito de montante para jusante com os diâmetros calculados – o mesmo
    /// que "Aplicar os diâmetros calculados" grava – diante do diâmetro de cada trecho no modelo.
    /// </summary>
    public static List<LinhaDimensionamento> Dimensionamento(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p)
    {
        if (!p.TemChuva) return new();
        var modelo = defs.OfType<DrainagePipeDefinition>().GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Diameter);
        return DrainageHydraulics.Calcular(defs, p, dimensionar: true).Trechos.Select(t =>
        {
            var d = modelo.GetValueOrDefault(t.Id, t.Diametro);
            var s = t.Declividade <= 0 ? SituacaoDiametro.NaoDimensionavel
                : t.DiametroNecessario is not { } dn ? SituacaoDiametro.SemDiametro
                : Math.Abs(dn - d) <= 1e-6 ? SituacaoDiametro.Igual
                : dn > d ? SituacaoDiametro.Aumentar : SituacaoDiametro.Reduzir;
            return new LinhaDimensionamento(t.Id, t.Nome, t.Montante, t.Jusante, t.Vazao, t.Declividade * 100, d, t.DiametroNecessario, s);
        }).ToList();
    }

    public static string Nome(SituacaoDiametro s) => s switch
    {
        SituacaoDiametro.Igual => "igual ao calculado",
        SituacaoDiametro.Aumentar => "abaixo do calculado (aumenta)",
        SituacaoDiametro.Reduzir => "acima do calculado (reduz)",
        SituacaoDiametro.SemDiametro => "nenhum diâmetro da lista atende",
        _ => "declividade nula ou contrária: não dimensionado",
    };

    /// <param name="defs">Definições do projeto.</param>
    /// <param name="p">Projeto de Drenagem (critérios e, se houver, a equação de chuva).</param>
    /// <param name="superficie">Superfície acabada para o recobrimento (<see cref="DrenagemDoModelo.SuperficieAcabada"/>).</param>
    /// <param name="temTerreno">O projeto tem Toposolid (o terreno projetado entra na superfície fora das vias).</param>
    /// <param name="terrenoNatural">Terreno natural (original) para os talvegues; nulo = não conferidos.</param>
    public static TelaRedeDrenagem Montar(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p, Func<Vec2, double?>? superficie, bool temTerreno,
        Func<Vec2, double?>? terrenoNatural)
    {
        var trechos = DrainageNetwork.Analyze(defs, superficie);
        var hid = DrainageHydraulics.Calcular(defs, p);
        var captacoes = DrenagemDoModelo.Captacoes(defs);
        var problemas = Problemas(defs, p, superficie, terrenoNatural, hid, captacoes);
        var itens = problemas.Select(Item).ToList();
        var dispositivos = PerfilRede.Dispositivos(defs);
        var perfil = PerfilRede.Linhas(defs, superficie);
        var dim = Dimensionamento(defs, p);
        List<ItemRede> De(CategoriaRede c) => itens.Where(i => i.Categoria == c).ToList();
        static EstadoContexto L(NivelSituacao n, string t) => new(n, t);

        var tubos = defs.OfType<DrainagePipeDefinition>().ToList();
        var caixas = defs.OfType<DrainageDefinition>().Where(d => !d.IsLinear).Select(d => d.Id).ToHashSet();
        var extensao = trechos.Sum(t => t.Comprimento);
        var secoes = new List<SecaoRede>();

        // ---------------------------------------------------------------- 1. geometria
        {
            var it = De(CategoriaRede.Geometria);
            var notas = new List<EstadoContexto>();
            if (trechos.Count > 0)
                notas.Add(L(NivelSituacao.Informacao, $"{trechos.Count} trecho(s) medido(s), {N(extensao, "N1")} m; declividade de {N(trechos.Min(t => t.Declividade))} a " +
                    $"{N(trechos.Max(t => t.Declividade))} %; menor recobrimento {N(trechos.Min(t => t.CobrimentoMinimo))} m."));
            notas.Add(L(NivelSituacao.Informacao, temTerreno
                ? "Recobrimento sob a superfície acabada: as vias do plugin e, fora delas, o terreno projetado (Toposolid)."
                : "Recobrimento sob as vias do plugin; fora delas, sem Toposolid, pela reta entre as tampas dos dispositivos."));
            if (tubos.Count > trechos.Count)
                notas.Add(L(NivelSituacao.Aviso, $"{tubos.Count - trechos.Count} trecho(s) não medido(s) – sem as duas caixas (parte 2)."));
            var sit = trechos.Count == 0 ? L(NivelSituacao.NaoVerificado, "Nenhum trecho medido: os trechos não ligam duas caixas existentes.")
                : it.Count == 0 ? L(NivelSituacao.Ok, "Os fundos descem de montante para jusante em todos os trechos medidos (sem contrafluxo).")
                : L(NivelSituacao.Aviso, $"{it.Count} trecho(s) em contrafluxo.");
            secoes.Add(new(CategoriaRede.Geometria, Titulo(CategoriaRede.Geometria),
                "Extensão (centro a centro das caixas), cotas de fundo, declividade e menor recobrimento de cada trecho; sentido do escoamento pelas cotas de fundo. " +
                "Conferido sempre, sem depender de critério.", sit, notas, it));
        }

        // ---------------------------------------------------------------- 2. conectividade
        {
            var it = De(CategoriaRede.Conectividade);
            var notas = new List<EstadoContexto>();
            var ligados = dispositivos.Where(d => caixas.Contains(d.Id)).ToList();
            notas.Add(L(NivelSituacao.Informacao, $"{ligados.Count} dispositivo(s) com caixa, {tubos.Count} trecho(s); {Partes(tubos, caixas)} rede(s) separada(s)."));
            // Lançamentos e bifurcações pelos trechos válidos (entre duas caixas diferentes): o laço e a ponta perdida já são problemas.
            var validos = tubos.Where(t => t.FromId != t.ToId && caixas.Contains(t.FromId) && caixas.Contains(t.ToId)).ToList();
            var chegam = validos.GroupBy(t => t.ToId).ToDictionary(g => g.Key, g => g.Count());
            var saem = validos.GroupBy(t => t.FromId).ToDictionary(g => g.Key, g => g.Count());
            var lancamentos = ligados.Where(d => chegam.GetValueOrDefault(d.Id) > 0 && saem.GetValueOrDefault(d.Id) == 0).Select(d => d.Rotulo).ToList();
            if (lancamentos.Count > 0)
                notas.Add(L(NivelSituacao.Informacao, $"Lançamento(s) – onde a água sai da rede modelada: {string.Join(", ", lancamentos)}."));
            var soltos = ligados.Where(d => d.Entradas == 0 && d.Saidas == 0).Select(d => d.Rotulo).ToList();
            if (soltos.Count > 0)
                notas.Add(L(NivelSituacao.Informacao, $"Sem nenhum trecho: {string.Join(", ", soltos.Take(20))}{(soltos.Count > 20 ? $" e mais {soltos.Count - 20}" : "")} – " +
                    "a água captada ali não segue pela rede modelada; ligue com Trecho de Rede se fazem parte dela."));
            var bifurca = ligados.Where(d => saem.GetValueOrDefault(d.Id) > 1).Select(d => d.Rotulo).ToList();
            if (bifurca.Count > 0)
                notas.Add(L(NivelSituacao.Informacao, $"Mais de uma saída: {string.Join(", ", bifurca)} (a divisão da vazão não é calculada)."));
            var sit = tubos.Count == 0 ? L(NivelSituacao.NaoVerificado, "Nenhum trecho de rede.")
                : it.Count == 0 ? L(NivelSituacao.Ok, "Todos os trechos ligam duas caixas existentes; sem ciclos.")
                : L(NivelSituacao.Aviso, $"{it.Count} problema(s) de ligação.");
            secoes.Add(new(CategoriaRede.Conectividade, Titulo(CategoriaRede.Conectividade),
                "Cada trecho liga duas caixas existentes (PV, boca de lobo, grelha com caixa), nenhum começa e termina no mesmo dispositivo e a rede não tem ciclos. " +
                "Conferido sempre.", sit, notas, it));
        }

        // ---------------------------------------------------------------- 3. parâmetros
        {
            var it = De(CategoriaRede.Parametros);
            var notas = new List<EstadoContexto>();
            void Crit(double? v, string informado, string ausente)
            {
                notas.Add(v is { } ? L(NivelSituacao.Informacao, informado) : L(NivelSituacao.NaoVerificado, ausente));
            }
            Crit(p.DiametroMinimo, $"Diâmetro mínimo nas galerias: {Mm(p.DiametroMinimo ?? 0)} mm.", "Diâmetro mínimo nas galerias não informado: não conferido.");
            Crit(p.DiametroLigacao, $"Diâmetro mínimo nas ligações: {Mm(p.DiametroLigacao ?? 0)} mm.", "Diâmetro mínimo nas ligações não informado: não conferido.");
            Crit(p.RecobrimentoMinimo, $"Recobrimento mínimo: {N(p.RecobrimentoMinimo ?? 0)} m sobre a geratriz superior externa.", "Recobrimento mínimo não informado: não conferido.");
            Crit(p.DeclividadeMinima, $"Declividade mínima nas galerias: {N(p.DeclividadeMinima ?? 0)} %.", "Declividade mínima nas galerias não informada: não conferida.");
            Crit(p.DeclividadeMaxima, $"Declividade máxima nas galerias: {N(p.DeclividadeMaxima ?? 0)} %.", "Declividade máxima nas galerias não informada: não conferida.");
            Crit(p.DeclividadeMinimaLigacao, $"Declividade mínima nas ligações: {N(p.DeclividadeMinimaLigacao ?? 0)} %.", "Declividade mínima nas ligações não informada: não conferida.");
            notas.Add(p.EspacamentoPv
                ? L(NivelSituacao.Informacao, "Espaçamento máximo entre PVs pela tabela 6.1 do Manual SUDERHSA (120, 150 ou 180 m, pelo diâmetro).")
                : L(NivelSituacao.NaoVerificado, "Espaçamento entre PVs desligado no Projeto de Drenagem: não conferido."));
            Crit(p.QuedaPv, $"PV de queda acima de {N(p.QuedaPv ?? 0)} m entre os fundos.", "Queda máxima nos PVs não informada: não conferida.");
            notas.Add(L(NivelSituacao.Informacao, "Alinhamento pela geratriz superior quando o diâmetro aumenta (Manual SUDERHSA, fig. 6.2)."));
            var sit = trechos.Count == 0 ? L(NivelSituacao.NaoVerificado, "Nenhum trecho medido.")
                : it.Count == 0 ? L(NivelSituacao.Ok, "Trechos e PVs dentro dos critérios informados.")
                : L(NivelSituacao.Aviso, $"{it.Count} item(ns) fora dos critérios informados.");
            secoes.Add(new(CategoriaRede.Parametros, Titulo(CategoriaRede.Parametros),
                $"Os trechos e os PVs contra os critérios do Projeto de Drenagem – {p.FonteTexto}. Critério não informado não é conferido.", sit, notas, it));
        }

        // ---------------------------------------------------------------- 4. cálculo hidráulico
        {
            var it = De(CategoriaRede.Hidraulica);
            var notas = new List<EstadoContexto>();
            string escopo;
            EstadoContexto sit;
            if (p.Chuva is not { } chuva)
            {
                escopo = "Vazão de cada trecho pelo método racional e verificação a seção plena (Manning), sarjetas e engolimento das captações – só com a equação de chuva do Projeto de Drenagem.";
                sit = L(NivelSituacao.NaoVerificado, "Sem equação de chuva no Projeto de Drenagem: vazões, capacidade, velocidades, sarjetas e engolimento não calculados.");
                notas.Add(L(NivelSituacao.Informacao, "Informe a equação de chuva do local, com a fonte, no Projeto de Drenagem."));
            }
            else
            {
                escopo = $"Método racional (Manual SUDERHSA, 2002): vazão pela área acumulada a montante e chuva de {chuva.Nome}" +
                         (string.IsNullOrWhiteSpace(chuva.Fonte) ? "" : $" ({chuva.Fonte})") + $", T = {N(p.TempoRetorno, "0.#")} anos, duração igual ao tempo de concentração; " +
                         $"galerias verificadas a seção plena por Manning (n = {N(p.Rugosidade, "0.000")}); sarjeta e engolimento de cada captação com a área local.";
                if (hid.Trechos.Count > 0)
                {
                    var max = hid.Trechos.MaxBy(t => t.Vazao)!;
                    notas.Add(L(NivelSituacao.Informacao, $"Vazão máxima {N(max.Vazao, "N1")} L/s em {max.Nome} ({max.Montante} → {max.Jusante})."));
                }
                var semArea = defs.OfType<DrainageDefinition>().Count(d => d.IsInlet && !d.IsLinear && d.AreaContribuicao <= 0);
                notas.Add(L(NivelSituacao.Informacao, $"{hid.Captacoes.Count} captação(ões) verificada(s)" +
                    (semArea > 0 ? $"; {semArea} sem área de contribuição não verificada(s) (Ler do Modelo preenche a área)." : ".")));
                notas.Add(L(NivelSituacao.Informacao, "Tubos verificados a seção plena: a lâmina nos tubos e o remanso não são calculados."));
                sit = hid.Trechos.Count == 0 ? L(NivelSituacao.NaoVerificado, "Nenhum trecho calculado (os trechos não ligam duas caixas).")
                    : it.Count == 0 ? L(NivelSituacao.Ok, "Vazões dentro da capacidade a seção plena; velocidades, sarjetas e engolimento dentro dos limites informados.")
                    : L(NivelSituacao.Aviso, $"{it.Count} item(ns) do cálculo hidráulico fora dos limites.");
            }
            secoes.Add(new(CategoriaRede.Hidraulica, Titulo(CategoriaRede.Hidraulica), escopo, sit, notas, it));
        }

        // ---------------------------------------------------------------- 5. dimensionamento
        {
            var it = new List<ItemRede>();
            var notas = new List<EstadoContexto>();
            string escopo = "Diâmetro calculado de cada trecho: o menor da lista do Projeto de Drenagem que escoa a vazão de projeto a seção plena, a partir do mínimo de " +
                            "cada tipo, com o cálculo refeito de montante para jusante com os diâmetros calculados. Nada muda no modelo sem Aplicar.";
            EstadoContexto sit;
            if (!p.TemChuva) sit = L(NivelSituacao.NaoVerificado, "Sem equação de chuva: os diâmetros não são dimensionados.");
            else
            {
                notas.Add(L(NivelSituacao.Informacao, $"Diâmetros da lista: {string.Join(", ", p.Diametros.OrderBy(x => x).Select(Mm))} mm."));
                notas.Add(L(NivelSituacao.Informacao, "Resultado do cálculo com os critérios informados: confira antes de aplicar – o projeto e a responsabilidade técnica são do projetista."));
                foreach (var l in dim.Where(l => l.Situacao != SituacaoDiametro.Igual))
                {
                    var texto = l.Situacao switch
                    {
                        SituacaoDiametro.Aumentar or SituacaoDiametro.Reduzir =>
                            $"{l.Trecho} ({l.Montante} → {l.Jusante}): Ø {Mm(l.DiametroModelo)} mm no modelo, {Mm(l.DiametroCalculado!.Value)} mm calculado para {N(l.Vazao, "N1")} L/s.",
                        SituacaoDiametro.SemDiametro => $"{l.Trecho} ({l.Montante} → {l.Jusante}): nenhum diâmetro da lista escoa {N(l.Vazao, "N1")} L/s com {N(l.Declividade)} %.",
                        _ => $"{l.Trecho} ({l.Montante} → {l.Jusante}): declividade {N(l.Declividade)} % – não dimensionado.",
                    };
                    var correcao = l.Situacao switch
                    {
                        SituacaoDiametro.Aumentar => "Aplicar os diâmetros calculados aumenta este trecho (ou edite o trecho).",
                        SituacaoDiametro.Reduzir => "Aplicar os diâmetros calculados reduz este trecho; mantenha o do modelo se ele vem do projeto.",
                        SituacaoDiametro.SemDiametro => "Aumente a declividade, inclua diâmetros maiores na lista do Projeto de Drenagem ou divida a vazão.",
                        _ => "Corrija as cotas de fundo (parte 1) para o trecho descer no sentido do escoamento.",
                    };
                    it.Add(new ItemRede(CategoriaRede.Dimensionamento, l.Situacao == SituacaoDiametro.Reduzir ? NivelSituacao.Informacao : NivelSituacao.Aviso,
                        l.Trecho, "diâmetro calculado", texto, correcao, new[] { l.Id }));
                }
                var (sobe, desce) = (dim.Count(l => l.Situacao == SituacaoDiametro.Aumentar), dim.Count(l => l.Situacao == SituacaoDiametro.Reduzir));
                var falta = dim.Count(l => l.Situacao is SituacaoDiametro.SemDiametro or SituacaoDiametro.NaoDimensionavel);
                sit = dim.Count == 0 ? L(NivelSituacao.NaoVerificado, "Nenhum trecho calculado.")
                    : sobe + desce + falta == 0 ? L(NivelSituacao.Ok, "Os diâmetros do modelo são os calculados.")
                    : L(sobe + falta > 0 ? NivelSituacao.Aviso : NivelSituacao.Informacao,
                        $"{sobe} trecho(s) abaixo do calculado, {desce} acima" + (falta > 0 ? $", {falta} sem diâmetro calculado." : "."));
            }
            secoes.Add(new(CategoriaRede.Dimensionamento, Titulo(CategoriaRede.Dimensionamento), escopo, sit, notas, it));
        }

        // ---------------------------------------------------------------- drenagem × vias e terreno
        {
            var it = De(CategoriaRede.ViasETerreno);
            var notas = new List<EstadoContexto>();
            if (!defs.Any(d => d is RoadPavementDefinition))
                notas.Add(L(NivelSituacao.NaoVerificado, "Nenhuma via do plugin: sarjetas e pontos baixos não lidos."));
            if (captacoes.Count > 0)
            {
                var est = captacoes.GroupBy(DrenagemDoModelo.Estado).ToDictionary(g => g.Key, g => g.Count());
                int C(EstadoLeitura s) => est.GetValueOrDefault(s);
                notas.Add(L(C(EstadoLeitura.Desatualizada) > 0 ? NivelSituacao.Aviso : NivelSituacao.Informacao,
                    $"{captacoes.Count} captação(ões) na sarjeta de uma via: {C(EstadoLeitura.EmDia)} lida(s) e em dia, {C(EstadoLeitura.Desatualizada)} desatualizada(s), " +
                    $"{C(EstadoLeitura.Editada)} editada(s) à mão, {C(EstadoLeitura.NuncaLida)} nunca lida(s)."));
            }
            if (terrenoNatural == null)
                notas.Add(L(NivelSituacao.NaoVerificado, "Terreno natural não lido: os talvegues sob o aterro das vias não foram conferidos."));
            var sit = it.Count == 0
                ? L(NivelSituacao.Ok, "Captações coerentes com o greide e nenhum ponto baixo da sarjeta sem captação" + (terrenoNatural != null ? "; sem talvegue sob aterro sem travessia." : "."))
                : L(it.Any(i => i.Nivel == NivelSituacao.Aviso) ? NivelSituacao.Aviso : NivelSituacao.Informacao, $"{it.Count} item(ns) entre a drenagem, as vias e o terreno.");
            secoes.Add(new(CategoriaRede.ViasETerreno, Titulo(CategoriaRede.ViasETerreno),
                "Captações lidas da via (sarjeta pelo greide, ponto baixo e área), pontos baixos da sarjeta sem captação e talvegues do terreno natural sob o aterro " +
                "das vias sem travessia da rede.", sit, notas, it));
        }

        var mudam = dim.Count(l => l.Muda);
        var resumo = $"Rede de drenagem: {trechos.Count} trecho(s), {N(extensao, "N1")} m, {dispositivos.Count(d => caixas.Contains(d.Id))} dispositivo(s) – " +
                     (problemas.Count == 0 ? "nenhum problema. " : $"{problemas.Count} problema(s). ") +
                     (p.TemChuva
                         ? "Cálculo hidráulico feito pelo método racional; " + (mudam == 0 ? "diâmetros do modelo iguais aos calculados." : $"{mudam} trecho(s) com diâmetro diferente do calculado.")
                         : "Sem equação de chuva: só geometria, conectividade e parâmetros foram conferidos – nenhum cálculo hidráulico.");
        return new TelaRedeDrenagem(resumo, secoes, trechos, dispositivos, perfil, dim, problemas, hid, captacoes);
    }

    /// <summary>Quantas redes separadas: componentes ligados pelos trechos entre caixas (dispositivos soltos não contam).</summary>
    private static int Partes(IReadOnlyList<DrainagePipeDefinition> tubos, HashSet<string> caixas)
    {
        var pai = new Dictionary<string, string>();
        string Raiz(string x)
        {
            while (pai[x] != x) x = pai[x] = pai[pai[x]];
            return x;
        }
        foreach (var t in tubos.Where(t => t.FromId != t.ToId && caixas.Contains(t.FromId) && caixas.Contains(t.ToId)))
        {
            pai.TryAdd(t.FromId, t.FromId);
            pai.TryAdd(t.ToId, t.ToId);
            var (a, b) = (Raiz(t.FromId), Raiz(t.ToId));
            if (a != b) pai[a] = b;
        }
        return pai.Keys.Select(Raiz).Distinct().Count();
    }

    /// <summary>Texto para copiar: o resumo e, em cada parte, a situação, as notas e os problemas com o que fazer.</summary>
    public static string Texto(TelaRedeDrenagem t, DateTime quando)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"REDE DE DRENAGEM – {quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}");
        sb.AppendLine(t.Resumo);
        foreach (var s in t.Secoes)
        {
            sb.AppendLine();
            sb.AppendLine(s.Titulo.ToUpperInvariant());
            sb.AppendLine(s.Escopo);
            sb.AppendLine($"[{CentralDoProjeto.Nome(s.Situacao.Nivel)}] {s.Situacao.Texto}");
            foreach (var n in s.Notas) sb.AppendLine($"  [{CentralDoProjeto.Nome(n.Nivel)}] {n.Texto}");
            foreach (var i in s.Itens) sb.AppendLine($"  • [{CentralDoProjeto.Nome(i.Nivel)}] {i.Texto} → {i.Correcao}");
        }
        return sb.ToString();
    }
}
