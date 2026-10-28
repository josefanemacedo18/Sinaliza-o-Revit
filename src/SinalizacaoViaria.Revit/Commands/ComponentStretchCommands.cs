using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Janela do componente por trecho (a mesma na ferramenta e na janela da via): acrescentar, tirar ou mudar a largura de um
/// elemento de um lado da seção entre as estacas A e B, com as transições, e um desenho do trecho numa via reta com a seção.
/// </summary>
internal static class ComponentStretchForm
{
    /// <summary>Elemento de referência: lado e posição (posição = número de elementos: o fim do lado).</summary>
    private static List<(string Label, (bool Left, int Pos) Value)> Refs(RoadSetup s)
    {
        var res = new List<(string, (bool, int))>();
        foreach (var left in new[] { false, true })
        {
            var side = left ? s.Left : s.Right;
            var lado = left ? "Esquerda" : "Direita";
            for (int i = 0; i < side.Count; i++)
                res.Add(($"{lado} {i + 1} – {ElementoSecao.Rotulo(side[i].Tipo)} ({UiHelpers.F(side[i].Largura)} m)", (left, i)));
            res.Add(($"{lado} – fim do lado (depois do elemento {side.Count})", (left, side.Count)));
        }
        return res;
    }

    private static readonly TipoElementoSecao[] Tipos =
    {
        TipoElementoSecao.FaixaRolamento, TipoElementoSecao.FaixaExclusiva, TipoElementoSecao.FaixaPreferencial, TipoElementoSecao.Ciclofaixa,
        TipoElementoSecao.Estacionamento, TipoElementoSecao.Acostamento, TipoElementoSecao.FaixaSeguranca, TipoElementoSecao.CanteiroFisico,
        TipoElementoSecao.CanteiroPintado, TipoElementoSecao.FaixaCaminhada, TipoElementoSecao.Calcada,
    };

    public static bool Show(RoadSetup setup, ComponenteTrecho t, string title, Action<FormWindow>? extra = null)
    {
        t.Elemento ??= new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = ElementoSecao.LarguraPadrao(TipoElementoSecao.Ciclofaixa) };
        var est = new EstacasDaVia(setup.Estaqueamento);                    // a estaca da via (K20)
        var w = new FormWindow(title, "Componente da via por trecho",
            "Entre o início (A) e o fim (B) um elemento de um lado é acrescentado, tirado ou muda de largura; antes de A e depois de B, as " +
            "transições voltam à seção. Os elementos de fora se deslocam (a via se alarga ou estreita) e a via é refeita a partir do eixo – " +
            "linhas, pisos e conexões acompanham. A baia e a extensão de calçada por trecho têm ferramentas próprias (Recuo na Via, Extensão de Calçada).",
            null, () => Preview(setup, t), false, "Continuar", 1080, 720);
        w.Choice("O que fazer", Enum.GetValues<OperacaoComponente>().Select(o => (ComponenteTrecho.Rotulo(o), o)), () => t.Operacao, v => t.Operacao = v,
                preset: v =>
                {
                    t.Operacao = v;
                    if (v == OperacaoComponente.Largura && (t.LadoEsquerdo ? setup.Left : setup.Right).ElementAtOrDefault(t.Posicao) is { } e) t.Largura = e.Largura;
                })
         .Choice("Elemento de referência", Refs(setup), () => (t.LadoEsquerdo, t.Posicao), v => (t.LadoEsquerdo, t.Posicao) = v,
             tooltip: "Acrescentar: o elemento novo entra ANTES do elemento escolhido (ou no fim do lado). Tirar e Mudar a largura: o elemento escolhido.",
             preset: v =>
             {
                 (t.LadoEsquerdo, t.Posicao) = v;
                 if (t.Operacao == OperacaoComponente.Largura && (v.Item1 ? setup.Left : setup.Right).ElementAtOrDefault(v.Item2) is { } e) t.Largura = e.Largura;
             })
         .If(() => t.Operacao == OperacaoComponente.Inserir, f => f
             .Choice("Elemento acrescentado", Tipos.Select(x => (ElementoSecao.Rotulo(x), x)), () => t.Elemento!.Tipo, v => t.Elemento!.Tipo = v,
                 preset: v => { t.Elemento!.Tipo = v; t.Elemento.Largura = ElementoSecao.LarguraPadrao(v); })
             .Number("Largura do elemento (m)", () => t.Elemento!.Largura, v => t.Elemento!.Largura = Math.Max(0.05, v), 0.05, 30, "0.00"))
         .If(() => t.Operacao == OperacaoComponente.Largura, f => f
             .Number("Nova largura (m)", () => t.Largura, v => t.Largura = Math.Max(0.05, v), 0.05, 30, "0.00",
                 "Calçada: o meio-fio e a faixa de serviço ficam; a faixa livre muda. Faixa: a sarjeta e a linha de bordo vão com o lado de fora."))
         .Text($"Início (A) – estaca ({est.Formato})", () => est.Mostrar(t.A), v => t.EstacaA = est.Ler(v) ?? t.EstacaA)
         .Text($"Fim (B) – estaca ({est.Formato})", () => est.Mostrar(t.B), v => t.EstacaB = est.Ler(v) ?? t.EstacaB)
         .Number("Transição antes de A (m)", () => t.TransicaoA, v => t.TransicaoA = Math.Max(0, v), 0, 500)
         .Number("Transição depois de B (m)", () => t.TransicaoB, v => t.TransicaoB = Math.Max(0, v), 0, 500)
         .Check("Transições em curva S (suave)", () => t.CurvaS, v => t.CurvaS = v);
        extra?.Invoke(w);
        return UiHelpers.ShowModal(w) == true;
    }

    /// <summary>
    /// Confere o trecho antes de gravá-lo e guarda o tipo do elemento tirado ou mudado: se a seção mudar depois, o trecho não
    /// mexe no elemento errado.
    /// </summary>
    public static void Validar(RoadSetup setup, ComponenteTrecho t, double? length = null)
    {
        var side = t.LadoEsquerdo ? setup.Left : setup.Right;
        if (t.Operacao != OperacaoComponente.Inserir && t.Posicao >= side.Count)
            throw new UserMessageException("Para tirar ou mudar a largura, escolha um elemento (não o fim do lado).");
        if (length is { } len && (t.B > len + 0.01 || t.A < -0.01))
            throw new UserMessageException($"As estacas devem ficar entre 0 e {len:0.00} m (comprimento do eixo).");
        if (t.B - t.A < 0.5) throw new UserMessageException("O início (A) e o fim (B) do trecho coincidem.");
        t.TipoNaPosicao = t.Operacao == OperacaoComponente.Inserir ? null : side[t.Posicao].Tipo;
        if (t.Operacao != OperacaoComponente.Inserir) t.Elemento = null;
    }

    /// <summary>Desenho do trecho numa via reta com a mesma seção: A e B marcados, avisos do componente.</summary>
    private static FormPreview? Preview(RoadSetup setup, ComponenteTrecho t)
    {
        var c = setup.Clone();
        c.TrechosLargura.Clear();
        c.LargurasVariaveis.Clear();
        c.Recuos.Clear();
        c.Retornos.Clear();
        c.ExtensoesCalcada.Clear();
        c.TravessiasCanteiro.Clear();
        c.ComponentesTrecho.Clear();
        var tt = t.Clone();
        var side = t.LadoEsquerdo ? setup.Left : setup.Right;
        tt.TipoNaPosicao = t.Operacao == OperacaoComponente.Inserir ? null : side.ElementAtOrDefault(t.Posicao)?.Tipo;
        var shift = 15 - t.Inicio;
        tt.EstacaA = t.A + shift;
        tt.EstacaB = t.B + shift;
        c.ComponentesTrecho.Add(tt);
        var len = tt.Fim + 15;
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
        var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
        var geo = new MarkingGeometry();
        foreach (var d in c.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), PluginContext.Catalog, axis: axis))
            geo.Merge(MarkingBuilder.Build(d, d.Path?.StoredPolyline() ?? axis, ctx));
        var guides = new List<IReadOnlyList<Vec2>>();
        var top = c.Nominal(true).Alinhamento + 3;
        var bottom = -c.Nominal(false).Alinhamento - 3;
        foreach (var (x, label) in new[] { (tt.A, "Início (A)"), (tt.B, "Fim (B)") })
        {
            guides.Add(new[] { new Vec2(x, bottom), new Vec2(x, top) });
            geo.Annotations.Add(new AnnotationText(new Vec2(x, top + 1.2), label, 3.0));
        }
        var info = tt.Descricao(s => setup.Estaca(s - shift));
        var avisos = c.Warnings.Where(x => x.StartsWith("Componente", StringComparison.Ordinal) || x.Contains("NBR 9050")).ToList();
        if (avisos.Count > 0) info += "\n⚠ " + string.Join("\n⚠ ", avisos);
        return new FormPreview(geo, null, guides, info, ViewScale: 500);
    }
}

/// <summary>
/// Componentes por Trecho: clique a via, o início (A) e o fim (B) no eixo (ou digite as estacas) e acrescente, tire ou mude a
/// largura de um elemento da seção no trecho, com transições. Os trechos gravados desta via podem ser editados, divididos ou
/// removidos na mesma janela; tudo fica na seção da via e é refeito a partir do eixo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdComponentesTrecho : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var pick = Picking.PickPoint(uidoc, "Componentes por trecho: clique o INÍCIO (A) do trecho sobre a via – ESC cancela");
        if (pick == null) return Result.Cancelled;
        if (RoadExtensionCommand.RoadAt(uidoc, UnitConv.ToVec2(pick)) is not { } road)
            throw new UserMessageException("Nenhuma via do plugin junto ao ponto clicado.");
        var pav = road.Def;
        var setup = RoadSetup.DaVia(pav)
                    ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior): use Editar → A via inteira uma vez e repita.");
        setup.Estaqueamento = pav.Estaqueamento;
        var len = road.Axis.Length;
        var sa = Math.Round(road.Axis.Project(UnitConv.ToVec2(pick)).Station, 2);
        var work = setup.Clone();
        var t = new ComponenteTrecho { EstacaA = sa, EstacaB = Math.Min(len, sa + 40), Posicao = setup.Right.Count > 0 ? setup.Right.Count - 1 : 0 };
        var pb = Picking.PickPoint(uidoc, "Componentes por trecho: clique o FIM (B) do trecho – ESC: digitar as estacas na janela");
        if (pb != null) t.EstacaB = Math.Round(road.Axis.Project(UnitConv.ToVec2(pb)).Station, 2);
        (t.EstacaA, t.EstacaB) = (Math.Min(t.EstacaA, t.EstacaB), Math.Max(t.EstacaA, t.EstacaB));

        // Trechos já gravados: escolher um para editar (ou o novo), dividir ou remover.
        var gravados = work.ComponentesTrecho;
        var escolha = -1;                                   // -1 = o trecho novo
        var dividirEm = (t.A + t.B) / 2;
        string Lista() => gravados.Count == 0 ? "Nenhum componente por trecho gravado nesta via."
            : string.Join("\n", gravados.Select((g, i) => $"{i + 1}. {g.Descricao(work.Estaca)}"));
        var acao = 0;                                       // 0 = gravar o trecho; 1 = remover; 2 = dividir
        var ok = ComponentStretchForm.Show(work, t, "Componentes por trecho", w =>
        {
            w.Section("Componentes gravados nesta via", Lista());
            if (gravados.Count == 0) return;
            w.Choice("Trecho", new[] { ("O trecho novo (campos acima)", -1) }.Concat(gravados.Select((g, i) => ($"{i + 1}. {ComponenteTrecho.Rotulo(g.Operacao)} – {work.Estaca(g.A)} a {work.Estaca(g.B)}", i))),
                    () => escolha, v => escolha = v,
                    tooltip: "Escolha um trecho gravado para removê-lo ou dividi-lo; para mudar um gravado, remova-o e crie o novo no lugar.")
             .Choice("Ao continuar", new[] { ("Gravar o trecho novo", 0), ("Remover o trecho escolhido", 1), ("Dividir o trecho escolhido na estaca abaixo", 2) },
                 () => acao, v => acao = v)
             .Text("Dividir na estaca", () => work.Estaca(dividirEm), v => dividirEm = new EstacasDaVia(work.Estaqueamento).Ler(v) ?? dividirEm);
        });
        if (!ok) return Result.Cancelled;
        switch (acao)
        {
            case 1 when escolha >= 0:
                gravados.RemoveAt(escolha);
                break;
            case 2 when escolha >= 0:
            {
                var parts = gravados[escolha].Dividir(dividirEm)
                            ?? throw new UserMessageException("A estaca da divisão precisa ficar entre A e B do trecho (a mais de 0,5 m das pontas).");
                gravados[escolha] = parts.Antes;
                gravados.Insert(escolha + 1, parts.Depois);
                break;
            }
            case 1 or 2:
                throw new UserMessageException("Escolha um trecho gravado para remover ou dividir.");
            default:
                ComponentStretchForm.Validar(setup, t, len);
                gravados.Add(t);
                break;
        }
        var results = RoadAccessCommand.Regenerate(uidoc, pav, work);
        if (work.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, work.Warnings);
        Report("Componentes por trecho", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}
