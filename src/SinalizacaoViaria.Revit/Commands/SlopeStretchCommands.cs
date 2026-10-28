using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Taludes por Trecho: na via clicada, taludes laterais por lado e por trecho – talude fixo (corte e aterro, com limite de
/// largura: onde não alcança o terreno, face quase vertical) ou largura fixa –, com transições. A seção numa estaca do trecho
/// mostra o resultado antes de aplicar; ao continuar, o greide da via guarda os trechos e o terreno é refeito.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdTaludesTrecho : CommandBase
{
    private static readonly (string, LadoTalude)[] Lados = { ("Os dois lados", LadoTalude.Ambos), ("Lado esquerdo", LadoTalude.Esquerdo), ("Lado direito", LadoTalude.Direito) };
    private static readonly (string, MetodoTalude)[] Metodos =
    {
        ("Talude fixo (inclinação até o terreno, com limite opcional)", MetodoTalude.TaludeFixo),
        ("Largura fixa (o talude chega ao terreno nessa largura)", MetodoTalude.LarguraFixa),
    };

    private static string Rotulo(TrechoTalude t, Func<double, string> est, double len)
    {
        var lado = t.Lado switch { LadoTalude.Esquerdo => "esquerda", LadoTalude.Direito => "direita", _ => "dois lados" };
        var m = t.Metodo == MetodoTalude.LarguraFixa
            ? $"largura fixa {UiHelpers.F(t.Largura)} m"
            : $"corte {UiHelpers.F(t.Corte)} : 1, aterro {UiHelpers.F(t.Aterro)} : 1" + (t.Limite is { } l ? $", limite {UiHelpers.F(l)} m" : "");
        return $"{lado}, {est(t.A)} a {(t.EstacaB == null ? "fim" : est(t.B(len)))}: {m}";
    }

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var road = RoadWorks.PickRoad(uidoc, "Taludes por trecho: clique a VIA (pista, calçada ou linha)");
        if (road == null) return Result.Cancelled;
        if (road.Pavement.Output.Grade == null)
            throw new UserMessageException("Esta via não tem greide: aplique o Perfil da Via antes – os taludes partem da plataforma no greide.");
        var L = road.Axis.Length;
        var grade = road.Grade.Clone();
        var est = new EstacasDaVia(road.Pavement.Estaqueamento);
        var ground = RoadWorks.Ground(doc, road.BaseZ);
        var hasTerrain = TerrainModel.Hosts(doc).Count > 0;
        var t = new TrechoTalude { EstacaA = Math.Round(L * 0.25, 1), EstacaB = Math.Round(L * 0.75, 1), Corte = grade.CutSlope, Aterro = grade.FillSlope };
        var semLimite = true;
        var ver = (t.A + t.B(L)) / 2;
        var escolha = -1;
        var acao = 0;
        var trechos = grade.Taludes;

        FormPreview? Preview()
        {
            if (!hasTerrain) return new FormPreview(null, null, null, "Sem Toposolid no projeto: a seção precisa do terreno para mostrar o talude.");
            var lista = trechos.Select(x => x.Clone()).ToList();
            if (acao == 0) lista.Add(Montar());
            var tipo = new SecaoTipo(road.Pavement.TotalLeft, road.Pavement.TotalRight, grade.CutSlope, grade.FillSlope, Trechos: lista);
            var sec = EstacasTerraplenagem.Secao(road.Axis, Math.Clamp(ver, 0, L), grade, tipo, ground);
            var geo = EarthworkCharts.Secao(sec, $"Estaca {est.Mostrar(sec.Estaca)}");
            var info = $"Seção na estaca {est.Mostrar(sec.Estaca)}: corte {sec.AreaCorte:0.00} m², aterro {sec.AreaAterro:0.00} m²." +
                       (sec.SemEncontroEsquerdo || sec.SemEncontroDireito ? "\n⚠ O talude não chega ao terreno dentro do alcance num dos lados." : "");
            return new FormPreview(geo, null, null, info, Paper: true);
        }

        TrechoTalude Montar()
        {
            var c = t.Clone();
            if (semLimite) c.Limite = null;
            return c;
        }

        string Lista() => trechos.Count == 0 ? "Nenhum trecho de talude: valem os taludes da via dos dois lados."
            : string.Join("\n", trechos.Select((x, i) => $"{i + 1}. {Rotulo(x, est.Mostrar, L)}"));

        var w = new FormWindow("Taludes por trecho", "Taludes da via por lado e por trecho",
            $"Taludes da via: corte {UiHelpers.F(grade.CutSlope)} : 1 e aterro {UiHelpers.F(grade.FillSlope)} : 1 (Perfil da Via). Entre A e B o lado escolhido " +
            "segue este trecho; as transições voltam aos da via. Valores informados pelo usuário – conferir no projeto geotécnico.",
            null, Preview, false, "Continuar", 1080, 720);
        w.Choice("Lado", Lados, () => t.Lado, v => t.Lado = v)
         .Text($"Início (A) – estaca ({est.Formato})", () => est.Mostrar(t.A), v => t.EstacaA = est.Ler(v) ?? t.EstacaA)
         .Text($"Fim (B) – estaca ({est.Formato}; vazio = até o fim)", () => t.EstacaB == null ? "" : est.Mostrar(t.B(L)),
             v => t.EstacaB = string.IsNullOrWhiteSpace(v) ? null : est.Ler(v) ?? t.EstacaB)
         .Number("Transição antes de A (m)", () => t.TransicaoA, v => t.TransicaoA = Math.Max(0, v), 0, 100)
         .Number("Transição depois de B (m)", () => t.TransicaoB, v => t.TransicaoB = Math.Max(0, v), 0, 100)
         .Choice("Método", Metodos, () => t.Metodo, v => t.Metodo = v)
         .If(() => t.Metodo == MetodoTalude.TaludeFixo, f => f
             .Number("Talude de corte (H : 1 V)", () => t.Corte, v => t.Corte = v, 0.1, 10, "0.0#")
             .Number("Talude de aterro (H : 1 V)", () => t.Aterro, v => t.Aterro = v, 0.1, 10, "0.0#")
             .Check("Sem limite de largura (até o alcance da terraplenagem)", () => semLimite, v => semLimite = v)
             .If(() => !semLimite, g => g.Number("Largura máxima do talude (m)", () => t.Limite ?? 5, v => t.Limite = v, 0.1, 60, "0.0#",
                 "Onde o talude não alcança o terreno dentro dela, o terreno fica natural além do limite (face quase vertical: prever contenção).")))
         .If(() => t.Metodo == MetodoTalude.LarguraFixa, f => f
             .Number("Largura do talude (m)", () => t.Largura, v => t.Largura = v, 0.1, 60, "0.0#"))
         .Section("Seção para conferir")
         .Text($"Estaca da seção ({est.Formato})", () => est.Mostrar(ver), v => ver = est.Ler(v) ?? ver)
         .Section("Trechos gravados nesta via", Lista());
        if (trechos.Count > 0)
            w.Choice("Trecho", new[] { ("O trecho novo (campos acima)", -1) }.Concat(trechos.Select((x, i) => ($"{i + 1}. {Rotulo(x, est.Mostrar, L)}", i))),
                    () => escolha, v => escolha = v)
             .Choice("Ao continuar", new[] { ("Gravar o trecho novo", 0), ("Remover o trecho escolhido", 1) }, () => acao, v => acao = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        if (acao == 1)
        {
            if (escolha < 0) throw new UserMessageException("Escolha um trecho gravado para remover.");
            trechos.RemoveAt(escolha);
        }
        else
        {
            var novo = Montar();
            if (!novo.Valido) throw new UserMessageException("Inclinações, largura e limite precisam ser de pelo menos 0,1.");
            if (novo.A < -0.01 || novo.B(L) > L + 0.01) throw new UserMessageException($"As estacas devem ficar entre 0 e {L:0.00} m (comprimento do eixo).");
            trechos.Add(novo);
        }
        // O greide sem as obras guarda os mesmos trechos (é dele que as obras são recompostas).
        if (grade.WithoutWorks != null) grade.WithoutWorks.Taludes = trechos.Select(x => x.Clone()).ToList();
        var results = RoadWorks.SetGrade(uidoc, road.GroupId, grade);
        // O pavimento gravado com os trechos novos (o lido antes ainda tem o greide anterior).
        var gravado = RoadWorks.RoadByGroup(doc, road.GroupId)?.Pavement ?? road.Pavement;
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { gravado });
        Report("Taludes por trecho", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}
