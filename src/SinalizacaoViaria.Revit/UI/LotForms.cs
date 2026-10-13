using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Opções do loteamento novo (medidas padrão aplicadas às quadras reconhecidas).</summary>
public sealed class NovoLoteamentoOpcoes
{
    public double Frente { get; set; } = 12;
    public double Profundidade { get; set; } = 30;
    public ModoSobra Sobra { get; set; } = ModoSobra.LoteSobra;
    public ModoMiolo Miolo { get; set; } = ModoMiolo.AosLotes;
    public bool SentidoHorario { get; set; } = true;
    public double AreaMinimaQuadra { get; set; } = 50;
}

/// <summary>Janelas do Loteamento (campos + pré-visualização dos lotes coloridos por categoria).</summary>
public static class LotForms
{
    public static readonly (string Label, ModoSobra Value)[] SobraOptions =
    {
        ("Vira o último lote (lote comum, se atingir a frente e a área mínimas; senão, somada ao último)", ModoSobra.LoteSobra),
        ("Somada ao último lote", ModoSobra.IncorporarUltimo),
        ("Distribuída entre os lotes", ModoSobra.Distribuir),
    };

    public static readonly (string Label, ModoMiolo Value)[] MioloOptions =
    {
        ("Vai para os lotes (fundos até a divisa ou até a fileira oposta)", ModoMiolo.AosLotes),
        ("Área própria (na categoria escolhida)", ModoMiolo.AreaPropria),
    };

    /// <summary>Frente e área mínimas do lote (informadas no projeto – conferir na legislação aplicável).</summary>
    private static FormWindow Minimos(FormWindow w, LoteamentoDefinition lot) => w
        .Section("Lote mínimo", "Valores informados no projeto (padrão 5 m e 125 m²) – conferir na Lei Federal nº 6.766/1979 e na lei municipal [a confirmar]. " +
                                "A sobra de testada só vira lote quando atinge os dois; lotes abaixo deles são avisados.")
        .Number("Frente mínima (m)", () => lot.FrenteMinimaLote, v => lot.FrenteMinimaLote = v, 0, 100)
        .Number("Área mínima (m²)", () => lot.AreaMinimaLote, v => lot.AreaMinimaLote = v, 0, 100000);

    public static readonly (string Label, bool Value)[] SentidoOptions = { ("Horário", true), ("Anti-horário", false) };

    public static readonly (string Label, Hachura Value)[] HatchOptions =
    {
        ("Sem hachura", Hachura.Nenhuma), ("Diagonal", Hachura.Diagonal), ("Diagonal densa", Hachura.DiagonalDensa),
        ("Cruzada", Hachura.Cruzada), ("Pontilhado", Hachura.Pontos), ("Grama", Hachura.Grama),
    };

    private static (string, string)[] CategoryOptions(LoteamentoDefinition? lot) =>
        (lot?.Categorias ?? CategoriaLote.Padrao()).Select(c => (c.Nome, c.Nome)).ToArray();

    /// <summary>Peças de todas as quadras (lotes coloridos) para a pré-visualização.</summary>
    public static MarkingGeometry Preview(IEnumerable<QuadraDefinition> quadras, LoteamentoDefinition? lot)
    {
        var geo = new MarkingGeometry();
        foreach (var q in quadras)
        {
            var g = LotDrawing.Quadra(q, lot);
            geo.Pieces.AddRange(g.Pieces);
            geo.Annotations.AddRange(g.Annotations);
            geo.Warnings.AddRange(g.Warnings);
        }
        return geo;
    }

    /// <summary>Loteamento novo: nome, medidas padrão e pré-visualização das quadras reconhecidas.</summary>
    public static FormWindow Novo(LoteamentoDefinition lot, NovoLoteamentoOpcoes opt, ResultadoReconhecimento rec, IReadOnlyList<Polygon2> occupied)
    {
        List<QuadraDefinition> Quadras() => rec.Quadras.Select(r =>
        {
            var q = Reconhecimento.NovaQuadra(r, lot, opt.Frente, opt.Profundidade);
            q.Sobra = opt.Sobra;
            q.Miolo = opt.Miolo;
            q.SentidoHorario = opt.SentidoHorario;
            return q;
        }).ToList();
        var w = new FormWindow("Loteamento", "Novo loteamento",
            $"Gleba de {LotReport.M(rec.Gleba.Area)} m² com {rec.Quadras.Count} quadra(s) reconhecida(s) (a gleba menos as vias). As testadas são os lados das quadras junto às vias; " +
            "depois, em Quadra, você aponta outras testadas, escolhe o lote de esquina e a numeração – e em Lote, personaliza cada lote.",
            lot, () =>
            {
                var qs = Quadras();
                var geo = Preview(qs, lot);
                return new FormPreview(geo, occupied, Info: $"{qs.Count} quadra(s), {LotReport.M(rec.AreaViaria)} m² de sistema viário ({LotReport.P(rec.AreaViaria / Math.Max(1e-9, rec.Gleba.Area) * 100)}).");
            }, showOutput: false, okText: "Criar loteamento", width: 1100, height: 720);
        w.Section("Loteamento")
            .Text("Nome", () => lot.Nome, v => lot.Nome = string.IsNullOrWhiteSpace(v) ? "Loteamento" : v!.Trim())
            .Text("Município/UF (memorial)", () => lot.Municipio, v => lot.Municipio = string.IsNullOrWhiteSpace(v) ? null : v!.Trim())
            .Section("Lotes padrão", "Medidas de todos os lotes – cada quadra e cada lote podem ser personalizados depois.")
            .Number("Frente (m)", () => opt.Frente, v => opt.Frente = v, 1, 200)
            .Number("Profundidade (m)", () => opt.Profundidade, v => opt.Profundidade = v, 1, 500)
            .Choice("Sobra da testada", SobraOptions, () => opt.Sobra, v => opt.Sobra = v)
            .Choice("Miolo da quadra (atrás dos lotes)", MioloOptions, () => opt.Miolo, v => opt.Miolo = v)
            .Choice("Numeração", SentidoOptions, () => opt.SentidoHorario, v => opt.SentidoHorario = v,
                "Cada quadra numerada de 1 a N a partir do lote de esquina mais a noroeste (o lote 1 pode ser escolhido depois).");
        Minimos(w, lot)
            .Section("Relevo")
            .Number("Espessura do lote no Toposolid (m)", () => lot.AlturaLote, v => lot.AlturaLote = v, 0.005, 1, "0.000",
                "Cada lote é uma subdivisão do Toposolid (acompanha o terreno). Sem Toposolid, o lote vira um piso plano na cota abaixo.")
            .Number("Cota sem Toposolid (m)", () => lot.Z, v => lot.Z = v, -10000, 10000);
        return w;
    }

    /// <summary>Ação pedida na janela da quadra (clique no Revit e reabre).</summary>
    public enum AcaoQuadra { Nenhuma, ApontarTestadas, TestadasAutomaticas, LoteInicial }

    /// <summary>Quadra: medidas, sobra, numeração, testadas (frente, profundidade e nome da via de cada uma) e esquinas.</summary>
    public static FormWindow Quadra(QuadraDefinition q, LoteamentoDefinition? lot, Action<AcaoQuadra> acao)
    {
        var div = LotDivider.Divide(q, lot);
        FormWindow? w = null;
        w = new FormWindow("Loteamento – Quadra", $"Quadra {q.Nome}",
            $"{div.Parcelas.Count(p => p.Categoria == CategoriaLote.Lote)} lote(s), {LotReport.M(div.Quadra.Area)} m². As testadas, em ordem de prioridade, " +
            "definem de onde saem os lotes; em cada esquina escolha quem fica com o lote de esquina (ou dê medida própria).",
            q, () =>
            {
                var geo = LotDrawing.Quadra(q, lot);
                var d = LotDivider.Divide(q, lot);
                var guides = d.Cadeias.Where(c => c.TestadaId != null).Select(c => (IReadOnlyList<Vec2>)c.Pontos).ToList();
                return new FormPreview(geo, null, guides, $"{d.Parcelas.Count(p => p.Categoria == CategoriaLote.Lote)} lote(s); testadas em amarelo.");
            }, showOutput: false, okText: "Aplicar", width: 1100, height: 760);
        w.Section("Quadra")
            .Text("Nome", () => q.Nome, v => q.Nome = string.IsNullOrWhiteSpace(v) ? q.Nome : v!.Trim())
            .Check("Quadra inteira numa área só (praça, área verde, institucional)", () => q.Inteira, v => q.Inteira = v)
            .Choice("Categoria dos lotes", CategoryOptions(lot), () => q.CategoriaPadrao, v => q.CategoriaPadrao = v)
            .Section("Lotes")
            .Number("Frente padrão (m)", () => q.Frente, v => q.Frente = v, 1, 200)
            .Number("Profundidade padrão (m)", () => q.Profundidade, v => q.Profundidade = v, 1, 500)
            .Check("Fundos no meio da quadra entre testadas opostas (quadra rasa, ou com o miolo para os lotes)", () => q.FundoNoMeio, v => q.FundoNoMeio = v)
            .Choice("Sobra da testada", SobraOptions, () => q.Sobra, v => q.Sobra = v)
            .Number("Sobra vira lote a partir de (m, 0 = frente mínima do loteamento)", () => q.FrenteMinima, v => q.FrenteMinima = v, 0, 200)
            .Choice("Miolo da quadra (atrás dos lotes)", MioloOptions, () => q.Miolo, v => q.Miolo = v)
            .Choice("Categoria do miolo (área própria ou o que nenhum lote alcança)", CategoryOptions(lot), () => q.MioloCategoria, v => q.MioloCategoria = v)
            .Section("Numeração", "De 1 a N nesta quadra; institucionais e áreas verdes com prefixo (AI-1, AV-1).")
            .Choice("Sentido", SentidoOptions, () => q.SentidoHorario, v => q.SentidoHorario = v)
            .Hint(q.LoteInicial == null ? "Lote 1: o lote de esquina mais a noroeste." : $"Lote 1: {q.LoteInicial}.")
            .Button("Clicar o lote que recebe o número 1", () => { acao(AcaoQuadra.LoteInicial); w!.DialogResult = true; })
            .Button("Lote 1 automático (noroeste)", () => q.LoteInicial = null);
        if (q.Unioes.Count > 0)
            w.Section("Lotes unidos", $"{q.Unioes.Count} união(ões) nesta quadra (Unir Lotes). Para desfazer uma só, use Lote no lote unido.")
                .Button("Desfazer todas as uniões desta quadra", () => q.Unioes.Clear());

        w.Section("Testadas", "Em ordem de prioridade (a primeira fica com o lote de esquina quando a esquina está em 'Prioridade'). 0 = medida padrão da quadra.")
            .Button("Apontar as testadas (clicar os lados, ESC termina)", () => { acao(AcaoQuadra.ApontarTestadas); w!.DialogResult = true; })
            .Button("Testadas automáticas (lados junto às vias)", () => { acao(AcaoQuadra.TestadasAutomaticas); w!.DialogResult = true; });
        foreach (var t in q.Testadas)
        {
            var tt = t;
            w.Section($"Testada {tt.Id}")
                .Number("Frente (m)", () => tt.Frente ?? 0, v => tt.Frente = v > 0.1 ? v : null, 0, 200)
                .Number("Profundidade (m)", () => tt.Profundidade ?? 0, v => tt.Profundidade = v > 0.1 ? v : null, 0, 500)
                .Text("Nome da via (memorial)", () => tt.Via, v => tt.Via = string.IsNullOrWhiteSpace(v) ? null : v!.Trim(),
                    tooltip: "Vazio = o nome dado em Nomear Vias.");
        }

        if (div.Esquinas.Count > 0)
        {
            w.Section("Esquinas", "Quem fica com o lote de esquina – escolhido em cada esquina.");
            var k = 1;
            foreach (var e in div.Esquinas)
            {
                var ee = q.Esquinas.FirstOrDefault(x => x.Ponto.DistanceTo(e.Ponto) < 5);
                if (ee == null)
                {
                    ee = e.Escolha.Clone();
                    ee.Ponto = e.Ponto;
                    q.Esquinas.Add(ee);
                }
                var esq = ee;
                var opts = new (string, ModoEsquina)[]
                {
                    ("Pela prioridade das testadas", ModoEsquina.Prioridade),
                    ($"Testada {e.Anterior}", ModoEsquina.TestadaAnterior),
                    ($"Testada {e.Posterior}", ModoEsquina.TestadaPosterior),
                    ("Medida própria", ModoEsquina.MedidaPropria),
                };
                w.Section($"Esquina {k++} – testadas {e.Anterior} × {e.Posterior} ({e.Ponto.X:0.0}; {e.Ponto.Y:0.0})")
                    .Choice("Lote de esquina", opts, () => esq.Modo, v => esq.Modo = v)
                    .If(() => esq.Modo == ModoEsquina.MedidaPropria, f => f
                        .Number($"Frente na testada {e.Anterior} (m)", () => esq.FrenteAnterior, v => esq.FrenteAnterior = v, 0.5, 200)
                        .Number($"Frente na testada {e.Posterior} (m)", () => esq.FrentePosterior, v => esq.FrentePosterior = v, 0.5, 200));
            }
        }
        return w;
    }

    /// <summary>Lote: frente, profundidade, divisão em partes e categoria (as medidas valem para o lote inteiro).</summary>
    public static FormWindow Lote(QuadraDefinition q, LoteamentoDefinition? lot, Parcela p)
    {
        var baseKey = p.Chave.Contains('.') ? p.Chave[..p.Chave.IndexOf('.')] : p.Chave;
        var medidas = q.Ajuste(baseKey)?.Clone() ?? new LoteAjuste { Chave = baseKey };
        var cat = q.Ajuste(p.Chave)?.Categoria ?? (p.Chave != baseKey ? q.Ajuste(baseKey)?.Categoria : null) ?? p.Categoria;
        var start = q.LoteInicial == p.Chave;
        var editable = !p.Chave.StartsWith("esq:") && !p.Chave.StartsWith("m:") && p.Chave != "q" && !p.Sobra && p.Unidos == null;
        var desfazer = false;
        // Categoria padrão desta parcela (gravada só quando muda): a do miolo nas áreas de miolo, a da quadra nos lotes.
        string Padrao(QuadraDefinition t) => p.Chave.StartsWith("m:") || p.Chave == "q" && !t.Inteira ? t.MioloCategoria : t.CategoriaPadrao;
        // As edições valem numa cópia para a prévia e na quadra só ao aplicar.
        void ApplyTo(QuadraDefinition target)
        {
            if (editable)
            {
                var a = target.AjusteOuNovo(baseKey);
                a.Frente = medidas.Frente;
                a.Profundidade = medidas.Profundidade;
                a.Partes = Math.Max(1, medidas.Partes);
            }
            target.AjusteOuNovo(p.Chave).Categoria = cat == Padrao(target) && p.Chave == baseKey ? null : cat;
            if (desfazer) target.Unioes.RemoveAll(g => g.Contains(p.Chave));
            if (start) target.LoteInicial = p.Chave;
            else if (target.LoteInicial == p.Chave) target.LoteInicial = null;
            target.Ajustes.RemoveAll(x => x.Frente == null && x.Profundidade == null && x.Partes <= 1 && x.Categoria == null);
        }
        var w = new FormWindow("Loteamento – Lote", $"{LotReport.Rotulo(p)} – Quadra {q.Nome}",
            $"{LotReport.M(p.Area)} m², frente {LotReport.M(p.Frente)} m. Ao mudar a frente, os lotes seguintes da testada se reajustam e a sobra é refeita." +
            (p.Unidos != null ? $" União de {p.Unidos.Count} lotes (Unir Lotes)." : "") +
            (editable ? "" : " (Lote de esquina com medida própria, sobra da testada, miolo ou união: só a categoria muda aqui – as medidas vêm da quadra.)") +
            (p.AbaixoDoMinimo != null ? $"\nAbaixo do mínimo: {p.AbaixoDoMinimo}." : ""),
            q, () =>
            {
                var draft = (QuadraDefinition)MarkingDefinition.FromJson(q.ToJson())!;
                ApplyTo(draft);
                var geo = LotDrawing.Quadra(draft, lot);
                var d = LotDivider.Divide(draft, lot);
                var me = d.Parcelas.FirstOrDefault(x => x.Chave == p.Chave) ?? d.Parcelas.FirstOrDefault(x => x.Chave.StartsWith(baseKey + "."));
                var guides = me == null ? null : new List<IReadOnlyList<Vec2>> { me.Forma.Outer.Append(me.Forma.Outer[0]).ToList() };
                return new FormPreview(geo, null, guides, me == null ? null : $"{LotReport.Rotulo(me)}: {LotReport.M(me.Area)} m².");
            }, showOutput: false, okText: "Aplicar", width: 1000, height: 640);
        if (editable)
        {
            w.Section("Medidas", "0 = medida padrão da testada/quadra.")
                .Number("Frente (m)", () => medidas.Frente ?? 0, v => medidas.Frente = v > 0.1 ? v : null, 0, 200)
                .Number("Profundidade (m)", () => medidas.Profundidade ?? 0, v => medidas.Profundidade = v > 0.1 ? v : null, 0, 500)
                .Integer("Dividir em partes iguais", () => medidas.Partes, v => medidas.Partes = v, 1, 20);
        }
        w.Section("Categoria")
            .Choice("Categoria", CategoryOptions(lot), () => cat, v => cat = v)
            .Check("Este lote recebe o número 1 da quadra", () => start, v => start = v);
        if (p.Unidos != null)
            w.Section("União")
                .Check($"Desfazer a união (volta aos {p.Unidos.Count} lotes)", () => desfazer, v => desfazer = v);
        w.Closed += (_, _) => { if (w.DialogResult == true) ApplyTo(q); };
        return w;
    }

    /// <summary>Categorias (nome, cor, hachura, pública, prefixo, mínimo), dados do loteamento e filtros da vista ativa.</summary>
    public sealed class CategoriasOpcoes
    {
        public string NovaCategoria { get; set; } = "";
        public bool AplicarNaVista { get; set; } = true;
        public bool Cores { get; set; } = true;
        public Dictionary<string, bool> Visivel { get; } = new();
        public HashSet<string> Excluir { get; } = new();
        public Dictionary<string, string> Renomear { get; } = new();
    }

    public static FormWindow Categorias(LoteamentoDefinition lot, CategoriasOpcoes opt)
    {
        var w = new FormWindow("Loteamento – Categorias e filtros", "Categorias, cores e filtros",
            "Cada categoria tem cor (planta e 3D), hachura e filtro de vista próprios. Mínimos em % da gleba: só os que a legislação aplicável exigir " +
            "(Lei Federal nº 6.766/1979 e lei municipal – a confirmar); 0 = sem mínimo.",
            lot, () =>
            {
                var geo = new MarkingGeometry();
                var y = 0.0;
                foreach (var c in lot.Categorias)
                {
                    geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, y), new Vec2(8, y + 5)), MarkingColor.Terra) { Rgb = c.Cor, Hatch = c.Hachura });
                    geo.Annotations.Add(new AnnotationText(new Vec2(10, y + 4), $"{c.Nome}{(string.IsNullOrWhiteSpace(c.Prefixo) ? "" : $" ({c.Prefixo}-1)")}", 3.5, TextAlign.Left));
                    y -= 7;
                }
                return new FormPreview(geo);
            }, showOutput: false, okText: "Aplicar", width: 1050, height: 760);
        w.Section("Loteamento")
            .Text("Nome", () => lot.Nome, v => lot.Nome = string.IsNullOrWhiteSpace(v) ? lot.Nome : v!.Trim())
            .Text("Município/UF", () => lot.Municipio, v => lot.Municipio = string.IsNullOrWhiteSpace(v) ? null : v!.Trim())
            .Number("Espessura do lote no Toposolid (m)", () => lot.AlturaLote, v => lot.AlturaLote = v, 0.005, 1, "0.000");
        Minimos(w, lot);
        double Min(string key) => lot.Minimos.TryGetValue(key, out var m) ? m : 0;
        void SetMin(string key, double v) { if (v > 1e-6) lot.Minimos[key] = v; else lot.Minimos.Remove(key); }
        w.Section("Mínimos gerais (% da gleba)")
            .Number(ResumoLoteamento.SistemaViario, () => Min(ResumoLoteamento.SistemaViario), v => SetMin(ResumoLoteamento.SistemaViario, v), 0, 100)
            .Number(ResumoLoteamento.AreasPublicas, () => Min(ResumoLoteamento.AreasPublicas), v => SetMin(ResumoLoteamento.AreasPublicas, v), 0, 100);
        foreach (var c in lot.Categorias)
        {
            var cc = c;
            var original = c.Nome;
            opt.Visivel[original] = true;
            w.Section($"Categoria: {original}")
                .Text("Nome", () => cc.Nome, v => { var n = string.IsNullOrWhiteSpace(v) ? original : v!.Trim(); if (n != original) opt.Renomear[original] = n; else opt.Renomear.Remove(original); cc.Nome = n; })
                .Text("Cor (#RRGGBB)", () => cc.Cor, v => { if (Rgb.TryParse(v, out var rgb)) cc.Cor = rgb.Hex; else throw new FormatException($"Cor inválida em {original}: use #RRGGBB."); })
                .Choice("Hachura", HatchOptions, () => cc.Hachura, v => cc.Hachura = v)
                .Check("Área pública (entra nas áreas públicas do quadro)", () => cc.Publica, v => cc.Publica = v)
                .Text("Prefixo da numeração (vazio = numerada como lote)", () => cc.Prefixo, v => cc.Prefixo = (v ?? "").Trim())
                .Number("Mínimo (% da gleba, 0 = sem)", () => Min(original), v => SetMin(original, v), 0, 100)
                .Check("Mostrar na vista ativa", () => opt.Visivel[original], v => opt.Visivel[original] = v);
            if (original != CategoriaLote.Lote)
                w.Check("Excluir esta categoria (os lotes dela voltam a Lote)", () => opt.Excluir.Contains(original), v => { if (v) opt.Excluir.Add(original); else opt.Excluir.Remove(original); });
        }
        w.Section("Nova categoria")
            .Text("Nome (vazio = nenhuma)", () => opt.NovaCategoria, v => opt.NovaCategoria = (v ?? "").Trim())
            .Section("Vista ativa")
            .Check("Aplicar os filtros das categorias na vista ativa", () => opt.AplicarNaVista, v => opt.AplicarNaVista = v)
            .Check("Com as cores e hachuras das categorias", () => opt.Cores, v => opt.Cores = v);
        return w;
    }

    /// <summary>Opções do detalhamento dos lotes na vista.</summary>
    public static FormWindow Detalhe(LoteamentoDetalheDefinition d, Func<MarkingGeometry?> preview, double scale)
    {
        var w = new FormWindow("Loteamento – Detalhar lotes", "Detalhamento dos lotes",
            "Textos e cotas desenhados na vista ativa (mm de papel × escala) – acompanham as mudanças dos lotes.",
            d, () => new FormPreview(preview(), Paper: false, ViewScale: scale), showOutput: false, okText: "Detalhar", width: 1050, height: 640);
        w.Section("Mostrar")
            .Check("Número do lote", () => d.Numero, v => d.Numero = v)
            .Check("Área (m²)", () => d.Area, v => d.Area = v)
            .Check("Cotas dos lados (frente, laterais e fundos)", () => d.Cotas, v => d.Cotas = v)
            .Check("Categoria (institucional, área verde...)", () => d.Categoria, v => d.Categoria = v)
            .Check("Letra da quadra (num círculo no meio da quadra)", () => d.Quadra, v => d.Quadra = v)
            .Check("Nome das vias", () => d.Vias, v => d.Vias = v)
            .Check("Cores das categorias como regiões (vistas sem os lotes coloridos)", () => d.Cores, v => d.Cores = v)
            .Check("Divisas e limite da gleba como linhas", () => d.Divisas, v => d.Divisas = v)
            .Section("Texto")
            .Number("Altura do texto (mm de papel)", () => d.TextMm, v => d.TextMm = v, 1, 10);
        return w;
    }

    /// <summary>Nome da via escolhida.</summary>
    public static FormWindow NomeVia(Func<string> get, Action<string> set, IEnumerable<string> existentes)
    {
        var list = existentes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var w = new FormWindow("Loteamento – Nomear via", "Nome da via",
            "O nome entra nas confrontações do memorial (frente dos lotes), no detalhamento e no masterplan." +
            (list.Count > 0 ? "\nJá nomeadas: " + string.Join(", ", list) + "." : ""),
            null, null, showOutput: false, okText: "Nomear", width: 560, height: 300);
        w.Text("Nome", () => get(), v => set((v ?? "").Trim()));
        return w;
    }
}
