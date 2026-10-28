using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Reports;

/// <summary>Quantidade de material ou serviço da rede (memorial e relatório).</summary>
public sealed record ItemDrenagem(string Descricao, string Unidade, double Quantidade, string Formato = "0.##");

/// <summary>
/// Memorial descritivo de drenagem pluvial (F8), na estrutura do memorial modelo enviado pelo usuário: identificação, o
/// projeto, parâmetros de projeto (vazões pelo método racional, chuva, recorrência, tempo de concentração, coeficiente de
/// escoamento, capacidade das vias, captações, tubos, recobrimento, velocidades, ligações e PVs), a planilha de
/// dimensionamento das galerias, o descritivo dos serviços, o quantitativo e as disposições finais. Os números vêm do
/// modelo e do <see cref="ProjetoDrenagem"/>; o texto dos serviços é genérico e pode ser editado no Word.
/// </summary>
public static class DrainageMemorial
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string fmt = "0.00") => v.ToString(fmt, Pt);
    private static string Ou(string? s, string vazio = "(a preencher)") => string.IsNullOrWhiteSpace(s) ? vazio : s!.Trim();

    public const string SecProjeto = "1. DO PROJETO";
    public const string SecParametros = "2. PARÂMETROS DE PROJETO";
    public const string SecPlanilha = "3. PLANILHA DE DIMENSIONAMENTO DAS GALERIAS";
    public const string SecServicos = "4. DESCRITIVO DOS SERVIÇOS";
    public const string SecQuantitativo = "5. QUANTITATIVO DE MATERIAIS E SERVIÇOS";
    public const string SecFinais = "6. DISPOSIÇÕES FINAIS";

    private static string NomeTipo(TipoDrenagem t) => DrainageDefinition.NomeTipo(t);

    /// <summary>
    /// Materiais e serviços da rede. Tubos do catálogo (<see cref="CatalogoTubos"/>) pela extensão entre os centros dos
    /// dispositivos (como na planilha do modelo), as peças e as juntas pelo tubo entre as paredes das caixas e as conexões com as
    /// caixas; valas com a largura de <see cref="ProjetoDrenagem.LarguraValaDiametros"/> diâmetros, até o fundo externo do tubo
    /// mais o berço, com o talude informado; escoramento das duas paredes verticais onde a vala passa da profundidade de
    /// escoramento; reaterro = escavação menos o volume externo dos tubos (material excedente) e o berço. Estimativas – não
    /// incluem PVs e caixas.
    /// </summary>
    public static List<ItemDrenagem> Quantitativo(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p)
    {
        var itens = new List<ItemDrenagem>();
        var trechos = CatalogoTubos.Trechos(defs, p);
        var grupos = trechos.GroupBy(t => t.Tubo.Nome).OrderBy(g => g.First().Tubo.Diametro).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var g in grupos)
            itens.Add(new(g.Key, "m", g.Sum(t => t.Extensao), "0.00"));
        foreach (var g in defs.OfType<DrainageDefinition>().GroupBy(d => d.Type).OrderBy(g => g.Key == TipoDrenagem.PocoDeVisita ? 0 : 1).ThenBy(g => g.Key))
            itens.Add(new(NomeTipo(g.Key), g.Key == TipoDrenagem.GrelhaContinua ? "un (trechos)" : "un", g.Count(), "0"));
        foreach (var g in grupos)
        {
            var t0 = g.First().Tubo;
            itens.Add(new($"Peças – {Minusculo(t0.Nome)}, de {N(t0.ComprimentoPeca, "0.##")} m (entre as paredes das caixas)", "un", g.Sum(t => t.Pecas), "0"));
            if (g.Sum(t => t.Juntas) > 0)
                itens.Add(new($"Juntas{(string.IsNullOrWhiteSpace(t0.Junta) ? "" : " " + t0.Junta.Trim())} – {Minusculo(t0.Nome)}", "un", g.Sum(t => t.Juntas), "0"));
        }
        var conexoes = trechos.Sum(t => t.Conexoes);
        if (conexoes > 0) itens.Add(new("Conexões dos tubos com as caixas (chegadas e saídas)", "un", conexoes, "0"));

        var esc = trechos.Sum(t => t.Vala.Escavacao);
        if (esc > 0)
        {
            var escor = trechos.Sum(t => t.Vala.Escoramento);
            var berco = trechos.Sum(t => t.Vala.Berco);
            itens.Add(new("Escavação mecanizada de valas (estimativa)", "m³", esc, "0.0"));
            if (escor > 0) itens.Add(new($"Escoramento de valas com mais de {N(p.ProfundidadeEscoramento)} m (estimativa)", "m²", escor, "0.0"));
            if (berco > 0) itens.Add(new($"Berço sob os tubos, {N(p.EspessuraBerco * 100, "0")} cm (estimativa)", "m³", berco, "0.0"));
            itens.Add(new("Reaterro compactado de valas (estimativa)", "m³", trechos.Sum(t => t.Vala.Reaterro), "0.0"));
            itens.Add(new("Material excedente a remover (volume dos tubos)", "m³", trechos.Sum(t => t.Vala.VolumeTubo), "0.0"));
        }
        return itens;
    }

    private static string Minusculo(string nome) => nome.Length > 0 ? char.ToLower(nome[0], Pt) + nome[1..] : nome;

    /// <param name="superficie">Superfície acabada para o recobrimento (<see cref="DrenagemDoModelo.SuperficieAcabada"/>); nula =
    /// a reta entre as tampas.</param>
    public static MemorialDocument Build(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p, DateTime data,
        Func<Geometry.Vec2, double?>? superficie = null)
    {
        var header = string.IsNullOrWhiteSpace(p.Autor) ? null
            : p.Autor!.Trim().ToUpper(Pt) + (string.IsNullOrWhiteSpace(p.Registro) ? "" : "\n" + p.Registro!.Trim());
        var doc = new MemorialDocument { Title = "MEMORIAL DESCRITIVO", Header = header, PageNumbers = true };
        var b = doc.Blocks;
        var r = DrainageHydraulics.Calcular(defs, p);
        var hgl = LinhaPiezometrica.Calcular(defs, p, r);
        var problemas = DrainageNetwork.Check(defs, p, superficie).Concat(r.Problemas).Concat(hgl.Problemas).ToList();
        var dispositivos = defs.OfType<DrainageDefinition>().ToList();
        var trechos = DrainageNetwork.Analyze(defs, superficie);
        var galerias = trechos.Where(t => !t.Ligacao).ToList();
        var ligacoes = trechos.Where(t => t.Ligacao).ToList();

        // ---- Capa
        b.Add(new MemParagraph("Projeto de Drenagem Urbana", Bold: true, Center: true));
        if (!string.IsNullOrWhiteSpace(p.Obra)) b.Add(new MemParagraph(p.Obra!.Trim(), Bold: true, Center: true));
        if (!string.IsNullOrWhiteSpace(p.Municipio)) b.Add(new MemParagraph(p.Municipio!.Trim(), Bold: true, Center: true));
        b.Add(new MemTable(new[] { "Identificação", "" }, new[]
        {
            new[] { "Proprietário", Ou(p.Proprietario) },
            new[] { "CNPJ / CPF", Ou(p.ProprietarioDocumento) },
            new[] { "Autor do projeto", Ou(p.Autor) },
            new[] { "Registro profissional (CREA/CAU)", Ou(p.Registro) },
            new[] { "ART / RRT", Ou(p.Art) },
            new[] { "Localização do imóvel", Ou(p.Endereco) },
            new[] { "Município", Ou(p.Municipio) },
        }, new[] { 1.0, 2.0 }));
        b.Add(new MemPageBreak());

        // ---- 1. Do projeto
        b.Add(new MemHeading(1, SecProjeto));
        var local = string.Join(", ", new[] { p.Obra, p.Endereco, p.Municipio }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        b.Add(new MemParagraph("Este memorial refere-se ao sistema de drenagem pluvial urbana" + (local.Length > 0 ? $" de {local}" : "") +
            ": o caminhamento superficial das águas pelas sarjetas até os dispositivos de captação (bocas de lobo e grelhas) e a condução por " +
            "ligações e galerias de concreto até o ponto de lançamento, com o dimensionamento das galerias."));
        b.Add(new MemParagraph($"O sistema projetado tem {dispositivos.Count(d => d.IsInlet && !d.IsLinear)} dispositivo(s) de captação, " +
            $"{dispositivos.Count(d => d.Type == TipoDrenagem.PocoDeVisita)} poço(s) de visita, {galerias.Count} trecho(s) de galeria " +
            $"({N(galerias.Sum(t => t.Comprimento), "0.0")} m) e {ligacoes.Count} ligação(ões) de bocas de lobo ({N(ligacoes.Sum(t => t.Comprimento), "0.0")} m)."));
        b.Add(new MemParagraph("As cotas de terreno, de tampa e de fundo vêm do modelo BIM em que a rede foi desenhada. Podem ocorrer divergências em relação " +
            "à situação encontrada na obra, em razão da movimentação de terra para a abertura e manutenção das vias."));
        b.Add(new MemParagraph("A execução é de responsabilidade da pessoa física ou jurídica contratada e legalmente habilitada, observadas as normas técnicas " +
            "da ABNT pertinentes a cada serviço. O uso de materiais e equipamentos com características diferentes das especificadas pode comprometer a " +
            "funcionalidade e a segurança deste projeto."));

        // ---- 2. Parâmetros
        b.Add(new MemHeading(1, SecParametros));
        b.Add(new MemParagraph($"Critérios adotados: {p.FonteTexto}.", Italic: true));
        b.Add(new MemHeading(2, "2.1. Cálculo das vazões de projeto"));
        b.Add(new MemParagraph("As vazões foram calculadas pelo Método Racional, indicado para bacias pequenas (até 2 km²):"));
        b.Add(new MemParagraph("Q = 0,278 · C · i · A", Bold: true, Center: true));
        b.Add(new MemParagraph("sendo Q a vazão máxima (m³/s), C o coeficiente de escoamento superficial, i a intensidade da chuva (mm/h) e A a área de " +
            "contribuição (km²). A área de cada trecho é a soma das áreas que chegam aos dispositivos a montante, e a duração da chuva é igual ao maior " +
            "tempo de concentração que chega ao trecho." + (r.Trechos.Count > 0
                ? $" Área total drenada: {N(r.Trechos.Max(t => t.AreaHa), "0.0000")} ha; maior vazão de projeto: {N(r.Trechos.Max(t => t.Vazao))} L/s."
                : "")));
        b.Add(new MemHeading(2, "2.2. Chuva de projeto"));
        if (p.Chuva is { } ch)
        {
            b.Add(new MemParagraph($"Adotou-se a equação de chuvas intensas {ch.Nome}" + (string.IsNullOrWhiteSpace(ch.Fonte) ? ":" : $" ({ch.Fonte}):")));
            b.Add(new MemParagraph(ch.Texto, Bold: true, Center: true));
            b.Add(new MemParagraph("sendo i a intensidade de precipitação, t a duração da chuva, igual ao tempo de concentração, e T o tempo de recorrência."));
        }
        else b.Add(new MemParagraph("Equação de chuvas intensas não informada no Projeto de Drenagem: as vazões não foram calculadas."));
        b.Add(new MemHeading(2, "2.3. Período de recorrência"));
        b.Add(new MemParagraph($"Adotou-se o tempo de recorrência de {N(p.TempoRetorno, "0.#")} anos para a microdrenagem."));
        b.Add(new MemHeading(2, "2.4. Tempo de concentração"));
        b.Add(new MemParagraph($"Nas cabeceiras da rede adotou-se o tempo de concentração de {N(p.TempoInicial, "0.#")} min, ou o informado para cada área de " +
            "contribuição; onde o talvegue foi informado, pela fórmula de Kirpich, tc = 57 · (L³/H)^0,385 (tc em min, L em km, H em m). Ao longo da rede, " +
            "o tempo de concentração de cada trecho é o do trecho anterior somado ao tempo de percurso, calculado com a velocidade a seção plena: " +
            $"tc(i) = tc(i−1) + L/v. Duração mínima da chuva: {N(p.DuracaoMinima, "0.#")} min."));
        b.Add(new MemHeading(2, "2.5. Coeficiente de escoamento superficial"));
        b.Add(new MemParagraph(p.Coeficiente == MetodoCoeficiente.Horner
            ? $"O coeficiente de escoamento foi obtido pela fórmula de Horner, C = 0,364 · log t + 0,0042 · p − 0,145 (t em min, p em %), com área impermeável de " +
              $"{N(p.ImpermeabilizacaoPadrao, "0.#")} %, salvo onde outro valor foi informado para a área."
            : $"O coeficiente de escoamento de cada área de contribuição foi ponderado pelas áreas acumuladas a montante de cada trecho; nas áreas sem valor " +
              $"próprio adotou-se C = {N(p.CoeficientePadrao)}."));
        b.Add(new MemHeading(2, "2.6. Capacidade das vias públicas"));
        b.Add(new MemParagraph($"A capacidade de escoamento das sarjetas foi calculada pela equação de Manning na seção triangular formada pela guia e pela declividade " +
            $"transversal de {N(p.DeclividadeTransversal * 100, "0.#")} % da via, com rugosidade n = {N(p.RugosidadeSarjeta, "0.000")}. Adotou-se " +
            $"{N(p.LaminaMaxima * 100, "0")} cm como altura máxima da lâmina junto à guia" +
            (p.FatoresReducao ? ", com os fatores de redução por declividade da sarjeta (DAEE/CETESB, 1980)." : ".")));
        b.Add(new MemHeading(2, "2.7. Critério para adoção de captações e galerias"));
        b.Add(new MemParagraph("A captação foi prevista onde a vazão na sarjeta supera a sua capacidade" +
            (p.VelocidadeSarjetaMinima is { } vsm && p.VelocidadeSarjetaMaxima is { } vsx
                ? $", ou onde a velocidade de escoamento na sarjeta fica abaixo de {N(vsm, "0.0#")} m/s ou acima de {N(vsx, "0.0#")} m/s."
                : ".") + " A partir da primeira captação, as águas seguem por ligações e galerias. A planilha de cálculo das galerias está no item 3."));
        b.Add(new MemHeading(2, "2.8. Tipos de bocas de lobo"));
        var tipos = dispositivos.Where(d => d.IsInlet).GroupBy(d => d.Type).Select(g => $"{NomeTipo(g.Key).ToLower(Pt)} ({g.Count()})").ToList();
        b.Add(new MemParagraph((tipos.Count > 0 ? $"Foram adotadas: {string.Join("; ", tipos)}. " : "Não há dispositivos de captação no modelo. ") +
            "A capacidade de engolimento foi verificada como vertedor ou orifício (abertura na guia e grelha)" +
            (p.FatoresReducao ? ", com os fatores de redução pela localização na sarjeta e pelo tipo de boca de lobo (DAEE/CETESB, 1980)." : ".")));
        b.Add(new MemHeading(2, "2.9. Tipos e diâmetros dos tubos"));
        var dnGal = galerias.Select(t => N(t.Diametro * 1000, "0")).Distinct().ToList();
        var dnLig = ligacoes.Select(t => N(t.Diametro * 1000, "0")).Distinct().ToList();
        // Tubos do catálogo do projeto: o texto do memorial modelo quando todos são de concreto; senão, os tubos usados e as fontes.
        var usados = CatalogoTubos.Trechos(defs, p).Select(t => t.Tubo).GroupBy(t => t.Nome).Select(g => g.First()).ToList();
        var soConcreto = usados.All(t => string.Equals(t.Material.Trim(), "concreto", StringComparison.OrdinalIgnoreCase));
        b.Add(new MemParagraph((soConcreto ? "Na condução das águas pluviais serão utilizados tubos de concreto conforme a NBR 8890. "
                : "Na condução das águas pluviais serão utilizados os tubos do catálogo do projeto: " +
                  string.Join("; ", usados.Select(t => t.Nome + (string.IsNullOrWhiteSpace(t.Fonte) ? "" : $" ({t.Fonte!.Trim()})"))) + ". ") +
            (dnGal.Count > 0 ? $"Diâmetros nominais das galerias: DN {string.Join(", DN ", dnGal)}. " : "") +
            (dnLig.Count > 0 ? $"Diâmetro nominal das ligações: DN {string.Join(", DN ", dnLig)}. " : "") +
            $"Diâmetros comerciais considerados no dimensionamento: {string.Join("; ", p.Diametros.OrderBy(d => d).Select(d => N(d * 1000, "0")))} mm."));
        if (p.RecobrimentoMinimo is { } rec)
        {
            b.Add(new MemHeading(2, "2.10. Recobrimento mínimo das galerias"));
            b.Add(new MemParagraph($"O recobrimento mínimo é de {N(rec)} m sobre a geratriz superior externa da galeria."));
        }
        b.Add(new MemHeading(2, "2.11. Velocidades limites nas galerias"));
        b.Add(new MemParagraph("As galerias foram dimensionadas a seção plena pela equação de Manning, com rugosidade n = " + N(p.Rugosidade, "0.000") +
            (p.VelocidadeMinima is { } vmin && p.VelocidadeMaxima is { } vmax
                ? $", devendo manter velocidades não inferiores a {N(vmin)} m/s (autolimpeza) e não superiores a {N(vmax, "0.0#")} m/s."
                : ".")));
        if (p.DeclividadeMinimaLigacao is { } dlig)
        {
            b.Add(new MemHeading(2, "2.12. Declividade das ligações das bocas de lobo"));
            b.Add(new MemParagraph($"A declividade do conduto de ligação da boca de lobo à galeria deve ser de no mínimo {N(dlig / 100, "0.00#")} m/m."));
        }
        b.Add(new MemHeading(2, "2.13. Poços de visita"));
        b.Add(new MemParagraph("Os poços de visita ficam nas mudanças de direção, de declividade e de diâmetro, nas junções de galerias e nos trechos longos, " +
            "para inspeção e limpeza." + (p.EspacamentoPv ? " O espaçamento máximo entre PVs é de 120, 150 ou 180 m, conforme o diâmetro (DAEE/CETESB, 1980)." : "") +
            (p.QuedaPv is { } qd ? $" Quando a diferença entre o tubo afluente e o efluente supera {N(qd)} m, o PV é de queda." : "") +
            " Nas mudanças de diâmetro, os tubos são alinhados pela geratriz superior."));

        // ---- 3. Planilha (paisagem)
        b.Add(new MemSection(true));
        b.Add(new MemHeading(1, SecPlanilha));
        if (r.Trechos.Count == 0)
            b.Add(new MemParagraph(p.TemChuva ? "Não há trechos de rede ligados a dispositivos no modelo." : "Sem equação de chuva no Projeto de Drenagem: a planilha não foi calculada."));
        else
        {
            b.Add(new MemTable(
                new[] { "Trecho", "Extensão (m)", "i (mm/h)", "Área (ha)", "Vazão (L/s)", "Diâm. (mm)", "Vazão máx. (L/s)", "Terreno M (m)", "Terreno J (m)",
                    "Declividade (m/m)", "Galeria M (m)", "Galeria J (m)", "Prof. M (m)", "Prof. J (m)", "Veloc. (m/s)" },
                r.Trechos.Select(t => (IReadOnlyList<string>)new[]
                {
                    $"{t.Nome} ({t.Montante}–{t.Jusante})", N(t.Extensao), N(t.Intensidade, "0.000"), N(t.AreaHa, "0.0000"), N(t.Vazao), N(t.Diametro * 1000, "0"),
                    N(t.VazaoMaxima), N(t.TerrenoMontante), N(t.TerrenoJusante), N(t.Declividade, "0.0000"), N(t.FundoMontante), N(t.FundoJusante),
                    N(t.ProfundidadeMontante), N(t.ProfundidadeJusante), N(t.Velocidade),
                }).ToList(),
                new[] { 2.2, 1, 1, 1.1, 1.1, 0.8, 1.1, 1, 1, 1.1, 1, 1, 0.8, 0.8, 0.8 }, Enumerable.Range(1, 14).ToList()));
            b.Add(new MemParagraph($"Vazão: método racional, com a duração da chuva igual ao tempo de concentração no início do trecho. Vazão máxima e velocidade: " +
                $"capacidade a seção plena (Manning, n = {N(p.Rugosidade, "0.000")}). Galeria: cota de fundo; Prof.: cota do terreno menos a cota de fundo; " +
                "M: montante; J: jusante.", Italic: true));
            if (r.Captacoes.Count > 0)
            {
                b.Add(new MemHeading(2, "Verificação das captações"));
                b.Add(new MemTable(new[] { "Dispositivo", "Vazão na sarjeta (L/s)", "Capacidade da sarjeta (L/s)", "Lâmina (cm)", "Veloc. na sarjeta (m/s)", "Engolimento (L/s)", "Excesso (L/s)" },
                    r.Captacoes.Select(c => (IReadOnlyList<string>)new[]
                    {
                        c.Nome, N(c.Vazao), c.CapacidadeSarjeta is { } cs ? N(cs) : "não verificada", c.Lamina is { } y ? N(y * 100, "0.0") : "–",
                        c.VelocidadeSarjeta is { } vs ? N(vs) : "–", N(c.Engolimento), N(c.Excesso),
                    }).ToList(), new[] { 1.4, 1, 1, 0.8, 1, 1, 0.9 }, Enumerable.Range(1, 6).ToList()));
            }
            if (hgl.Trechos.Count > 0)
            {
                b.Add(new MemHeading(2, "Linha piezométrica"));
                b.Add(new MemTable(new[] { "Trecho", "Vazão (L/s)", "Diâm. (mm)", "Lâmina normal (y/D)", "Nível d'água M (m)", "Nível d'água J (m)",
                        "Energia M (m)", "Veloc. M (m/s)", "Regime" },
                    hgl.Trechos.Select(t => (IReadOnlyList<string>)new[]
                    {
                        $"{t.Nome} ({t.Montante}–{t.Jusante})", N(t.Vazao), N(t.Diametro * 1000, "0"), t.LaminaNormal is { } y ? N(y / t.Diametro, "0.00") : "acima da seção",
                        N(t.NivelMontante, "0.000"), N(t.NivelJusante, "0.000"), N(t.EnergiaMontante, "0.000"), N(t.Velocidade),
                        t.Afogado ? "sob pressão" : "livre",
                    }).ToList(), new[] { 2.2, 1, 0.8, 1.1, 1.1, 1.1, 1.1, 0.9, 1 }, Enumerable.Range(1, 7).ToList()));
                var lanc = string.Join("; ", hgl.Lancamentos.Select(x => $"{x.Key}: {x.Value}"));
                b.Add(new MemParagraph("Calculada de jusante para montante com as vazões da planilha: lâmina normal por Manning na seção parcialmente cheia; sob pressão, " +
                    "a perda por atrito a seção plena; com remanso abaixo da geratriz superior, o nível de jusante estendido em nível até o montante. " +
                    $"Perda localizada na entrada de cada trecho: K = {N(p.PerdaPv, "0.##")} (Projeto de Drenagem). Lançamento – {lanc}.", Italic: true));
            }
            if (problemas.Count > 0)
            {
                b.Add(new MemHeading(2, "Observações da verificação"));
                b.Add(new MemBullets(problemas.Select(x => x.Texto).ToList()));
            }
        }
        b.Add(new MemSection(false));

        // ---- 4. Serviços
        b.Add(new MemHeading(1, SecServicos));
        b.Add(new MemHeading(2, "Escavação mecanizada de valas"));
        b.Add(new MemParagraph("Escavação das valas para assentamento das galerias e ligações, com marcação planialtimétrica, equipamentos (retroescavadeira, " +
            "pá-carregadeira, caminhão basculante e ferramentas manuais), mão de obra e materiais. Antes do início, a marcação dos alinhamentos é feita " +
            "conforme o projeto e as interferências com as redes de água, esgoto, energia e telefonia são levantadas; mudanças são aprovadas pela " +
            $"fiscalização. A vala terá largura mínima de {N(p.LarguraValaDiametros, "0.#")} vezes o diâmetro do tubo e paredes verticais ou inclinadas " +
            "conforme a estabilidade do solo. O material escavado é depositado ao lado da vala ou transportado para local aprovado; onde o equipamento " +
            "não tiver acesso, a escavação é manual. As áreas escavadas são isoladas e sinalizadas para veículos e pedestres."));
        b.Add(new MemHeading(2, "Escoramento de valas"));
        b.Add(new MemParagraph($"Para profundidades superiores a {N(p.ProfundidadeEscoramento)} m, em solos instáveis ou alagadiços, ou sujeitos à trepidação do " +
            "trânsito, a vala é escorada (contínuo ou descontínuo), com estroncas, longarinas e pranchas de madeira, garantindo a segurança dos operários."));
        b.Add(new MemHeading(2, "Fornecimento e assentamento de tubos de concreto (ponta e bolsa)"));
        b.Add(new MemParagraph("Fornecimento e assentamento dos tubos de concreto tipo ponta e bolsa, com ou sem lastro de brita e esgotamento do fundo da vala " +
            "quando necessário. O assentamento acompanha a abertura das valas, de jusante para montante, com a bolsa voltada para montante. Os tubos são " +
            "vistoriados quanto à integridade e à limpeza; as juntas são executadas com argamassa de cimento e areia (traço 1:3), com as pontas centradas " +
            "nas bolsas, e os tubos de DN 600 ou maiores recebem rejuntamento interno. O aterro de um trecho só é liberado após a conferência do " +
            "alinhamento, das cotas e das declividades."));
        b.Add(new MemHeading(2, "Poços de visita e caixas de captação"));
        b.Add(new MemParagraph("Execução dos poços de visita e das caixas das bocas de lobo com marcação planialtimétrica, equipamentos, mão de obra e materiais: " +
            "fundo em concreto armado sobre lastro de brita apiloado, paredes em blocos de concreto preenchidos ou em concreto, revestimento interno com " +
            "argamassa desempenada e tampão de ferro fundido compatível com o tráfego da via. O poço é executado junto com a rede e os ramais, evitando " +
            "reescavação; o reaterro lateral é compactado em camadas."));
        b.Add(new MemHeading(2, "Reaterro compactado de valas"));
        b.Add(new MemParagraph("Reaterro com solo de boa qualidade, isento de pedras e corpos estranhos, proveniente da escavação ou importado a critério da " +
            "fiscalização. Do fundo da vala até a geratriz superior do tubo, camadas de no máximo 20 cm compactadas manualmente; daí até a superfície, " +
            "camadas de no máximo 20 cm compactadas mecanicamente até 95 % da densidade máxima do ensaio de Proctor Normal. O material excedente é " +
            "removido para local indicado pela fiscalização. Nas vias pavimentadas, a recomposição do pavimento é feita logo após o reaterro; não devem " +
            "permanecer trechos escavados sem reaterro de um dia para o outro."));

        // ---- 5. Quantitativo
        b.Add(new MemHeading(1, SecQuantitativo));
        if (!string.IsNullOrWhiteSpace(p.Obra)) b.Add(new MemParagraph($"Empreendimento: {p.Obra!.Trim()}"));
        var itens = Quantitativo(defs, p);
        b.Add(new MemTable(new[] { "Item", "Descrição", "Unidade", "Quantidade" },
            itens.Select((it, k) => (IReadOnlyList<string>)new[] { (k + 1).ToString(Pt), it.Descricao, it.Unidade, N(it.Quantidade, it.Formato) }).ToList(),
            new[] { 0.5, 4, 1.1, 1.2 }, new[] { 3 }));
        var porTrecho = CatalogoTubos.Trechos(defs, p, superficie);
        b.Add(new MemParagraph("Tubos pela extensão entre os centros dos dispositivos; peças e juntas pelo tubo entre as paredes das caixas. Escavação, escoramento, " +
            "berço e reaterro estimados pela largura mínima da vala e pela profundidade média até o fundo externo do tubo mais o berço" +
            (p.TaludeVala > 0 ? $", com paredes em talude {N(p.TaludeVala, "0.##")} : 1" : ", com paredes verticais") + ", sem os poços de visita e as caixas." +
            (porTrecho.Any(t => !t.DoCatalogo) ? " Trechos sem tubo do catálogo com o diâmetro deles: tubo de concreto do padrão do plugin." : ""), Italic: true));
        if (porTrecho.Count > 0)
        {
            b.Add(new MemHeading(2, "Tubos, peças e valas por trecho"));
            b.Add(new MemTable(new[] { "Trecho", "Tubo", "Extensão (m)", "Tubo entre caixas (m)", "Peças", "Juntas", "Largura da vala (m)", "Prof. média (m)", "Escavação (m³)" },
                porTrecho.Select(t => (IReadOnlyList<string>)new[]
                {
                    $"{t.Nome} ({t.Montante}–{t.Jusante})", t.Tubo.Nome, N(t.Extensao), N(t.ExtensaoTubo), t.Pecas.ToString(Pt), t.Juntas.ToString(Pt),
                    N(t.Vala.Largura), N(t.Vala.ProfundidadeMedia), N(t.Vala.Escavacao, "0.0"),
                }).ToList(), new[] { 1.6, 2.6, 0.9, 1, 0.6, 0.6, 0.9, 0.9, 0.9 }, Enumerable.Range(2, 7).ToList()));
        }

        // ---- 6. Disposições finais
        b.Add(new MemHeading(1, SecFinais));
        b.Add(new MemParagraph("Os casos omissos e as alterações de projeto durante a obra serão resolvidos com a fiscalização e o autor do projeto."));
        b.Add(new MemParagraph($"{Ou(p.Municipio, "Local")}, {data.ToString("MMMM 'de' yyyy", Pt)}.", Center: false));
        b.Add(new MemTable(new[] { "Autor do projeto", "Proprietário" }, new[]
        {
            new[] { "\n\n______________________________", "\n\n______________________________" },
            new[] { Ou(p.Autor, "Nome"), Ou(p.Proprietario, "Nome") },
            new[] { "Registro: " + Ou(p.Registro, ""), "CNPJ/CPF: " + Ou(p.ProprietarioDocumento, "") },
            new[] { "ART/RRT: " + Ou(p.Art, ""), "" },
        }, new[] { 1.0, 1.0 }));
        return doc;
    }
}
