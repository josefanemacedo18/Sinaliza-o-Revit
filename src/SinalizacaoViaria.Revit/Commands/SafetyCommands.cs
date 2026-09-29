using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Formulários dos dispositivos de segurança viária: sonorizador longitudinal e área de escape.</summary>
internal static class SafetyForms
{
    private static MarkingGeometry Build(MarkingDefinition d, Polyline2 path) =>
        MarkingBuilder.Build(d, path, new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs });

    private static string Info(MarkingGeometry g, string extra)
    {
        var w = g.Warnings.Distinct().ToList();
        return extra + (w.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", w) : "");
    }

    private static readonly (string, TipoSonorizador)[] Types =
    {
        ("Fresado no pavimento (ranhuras)", TipoSonorizador.Fresado),
        ("Linha em termoplástico com relevo (perfilada)", TipoSonorizador.TermoplasticoRelevo),
        ("Barras transversais em relevo", TipoSonorizador.BarrasRelevo),
        ("Tachas sonorizadoras", TipoSonorizador.Tachas),
    };

    public static FormWindow RumbleStrip(RumbleStripDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar sonorizador" : "Sonorizador longitudinal", "Sonorizador longitudinal (rumble strip)",
            "Elementos em série ao longo de uma linha – bordo, acostamento ou eixo – que vibram e fazem ruído quando o pneu passa sobre eles, " +
            "alertando o motorista que sai da faixa (sono, distração). Selecione a LBO/LFO existente ou desenhe a linha; o deslocamento põe os " +
            "elementos ao lado dela (+ à esquerda). Valores iniciais de referência (FHWA/DNIT) – confira com o órgão.",
            d, () =>
            {
                var c = (RumbleStripDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(12, 0) }));
                var per = g.UnitCount / Math.Max(1.0, g.PathLength);
                return new FormPreview(g, null, null, Info(g, $"Prévia em 12 m: {g.UnitCount} elementos ({per:0.#} por metro)."));
            }, true, edit ? "Aplicar" : "Inserir", 1000, 700);
        w.Choice("Tipo", Types, () => d.Type, v => d.Type = v, preset: v => d.ApplyPreset(v))
         .Choice("Posição", new[] { ("Acostamento (ao lado da LBO)", PosicaoSonorizador.Acostamento), ("Sobre o bordo (LBO)", PosicaoSonorizador.Bordo),
                 ("Eixo (entre sentidos opostos)", PosicaoSonorizador.Eixo), ("Livre", PosicaoSonorizador.Livre) }, () => d.Position, v => d.Position = v,
             preset: v =>
             {
                 d.Position = v;
                 d.Offset = v == PosicaoSonorizador.Acostamento ? -0.40 : 0;
                 if (v == PosicaoSonorizador.Acostamento) { d.SegmentLength = 12; d.GapLength = 3.6; }
                 else { d.SegmentLength = 0; d.GapLength = 0; }
             })
         .Number("Deslocamento lateral em relação à linha (m, + esquerda)", () => d.Offset, v => d.Offset = v, -5, 5, "0.00",
             "No acostamento, o centro dos elementos fica a ~0,30–0,50 m do bordo; sobre a linha, 0.")
         .Section("Elementos")
         .Number("Comprimento ao longo da via (cm)", () => d.ElementLength * 100, v => d.ElementLength = v / 100, 2, 200, "0")
         .Number("Largura transversal (cm)", () => d.ElementWidth * 100, v => d.ElementWidth = v / 100, 5, 200, "0")
         .Number("Espaçamento centro a centro (cm)", () => d.Spacing * 100, v => d.Spacing = v / 100, 5, 1000, "0",
             "Fresado: 30 cm; termoplástico perfilado: 50 cm; barras: 60 cm; tachas: 100 cm (indicativos).")
         .Number(d.Type == TipoSonorizador.Fresado ? "Profundidade (mm)" : "Altura do relevo (mm)", () => d.Depth * 1000, v => d.Depth = v / 1000, 1, 50, "0",
             "Fresado 10–13 mm; relevo 5–8 mm; tachas 15–20 mm.")
         .Number("Ângulo dos elementos (°, chevron)", () => d.Angle, v => d.Angle = v, -60, 60, "0")
         .Choice("Cor", new[] { ("Automática (fresado escuro, relevo branco)", (MarkingColor?)null), ("Branca", MarkingColor.Branca), ("Amarela", MarkingColor.Amarela),
                 ("Vermelha", MarkingColor.Vermelha), ("Preta", MarkingColor.Preta) }, () => d.Color, v => d.Color = v)
         .Section("Trechos", "Interrupções periódicas deixam o ciclista passar pelo acostamento sem atravessar o sonorizador.")
         .Number("Trecho contínuo (m, 0 = contínuo)", () => d.SegmentLength, v => d.SegmentLength = v, 0, 500, "0.0")
         .Number("Interrupção (m)", () => d.GapLength, v => d.GapLength = v, 0, 100, "0.0")
         .Number("Recuo no início (m)", () => d.StartSetback, v => d.StartSetback = v, 0, 500, "0.0",
             "Deixe livre as entradas, interseções e pontes (onde há frenagem ou travessia de pedestres).")
         .Number("Recuo no fim (m)", () => d.EndSetback, v => d.EndSetback = v, 0, 500, "0.0");
        if (!edit) w.Modes(("Selecionar linhas existentes (LBO, LFO)", PathMode.Linhas), ("Arestas de pisos (bordo da pista)", PathMode.Bordas),
            ("Desenhar por pontos", PathMode.Desenhar), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    public static FormWindow EscapeRamp(EscapeRampDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar área de escape" : "Área de escape", "Área de escape de caminhões (caixa de retenção)",
            "Para descidas longas e íngremes, onde caminhões podem perder o freio. Desenhe o EIXO da caixa começando no bordo da pista, " +
            "no sentido de entrada do veículo (saída tangente, à direita). O comprimento necessário vem de L = V² / 254 (R + G), com a " +
            "resistência ao rolamento do material (AASHTO). Ao lado, a faixa de serviço pavimentada com âncoras para o guincho retirar o veículo.",
            d, () =>
            {
                var c = (EscapeRampDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                var g = Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(c.ActualLength + 15, 0) }));
                return new FormPreview(g, null, null, Info(g,
                    $"Necessário: {c.RequiredLength:0} m (V = {c.EntrySpeed:0} km/h, R = {c.Resistance:0.00}, G = {c.Grade:0.#}%). Caixa: {c.ActualLength:0} × {c.Width:0.#} m; " +
                    $"material ≈ {c.ActualLength * c.Width * c.BedDepth:0} m³."));
            }, false, edit ? "Aplicar" : "Inserir", 1080, 720);
        w.Number("Velocidade de entrada (km/h)", () => d.EntrySpeed, v => d.EntrySpeed = v, 40, 180, "0",
                "Caminhão desgovernado: 130–140 km/h nas descidas longas (AASHTO). Use a velocidade estimada no pé da descida.")
         .Number("Rampa da caixa (%, + aclive)", () => d.Grade, v => d.Grade = v, -10, 20, "0.0",
             "Aclive reduz o comprimento. Em declive, prefira material mais resistente e barreira no fim.")
         .Choice("Material do leito", new[] { ("Seixo rolado uniforme (R = 0,25) – recomendado", MaterialRetencao.Seixo), ("Areia (R = 0,15)", MaterialRetencao.Areia),
                 ("Cascalho solto (R = 0,10)", MaterialRetencao.Cascalho), ("Brita solta (R = 0,05)", MaterialRetencao.Brita) }, () => d.Material, v => d.Material = v)
         .Number("Comprimento (m, 0 = calculado)", () => d.Length ?? 0, v => d.Length = v > 5 ? v : null, 0, 1000, "0")
         .Number("Largura (m)", () => d.Width, v => d.Width = v, 3, 20, "0.0", "8 a 12 m (dois veículos).")
         .Number("Profundidade do leito (m)", () => d.BedDepth, v => d.BedDepth = v, 0.3, 2, "0.00", "AASHTO: 0,90 a 1,10 m.")
         .Number("Transição da profundidade na entrada (m)", () => d.DepthTaper, v => d.DepthTaper = v, 0, 100, "0",
             "De 8 cm a profundidade total ao longo dos primeiros 30 a 60 m – desaceleração progressiva.")
         .Section("Faixa de serviço e acabamento")
         .Number("Largura da faixa de serviço (m, 0 = sem)", () => d.ServiceWidth, v => d.ServiceWidth = v, 0, 6, "0.0")
         .Check("Faixa de serviço à ESQUERDA da caixa (senão, à direita)", () => d.ServiceLeft, v => d.ServiceLeft = v)
         .Number("Âncoras de reboque a cada (m)", () => d.AnchorSpacing, v => d.AnchorSpacing = v, 0, 200, "0")
         .Check("Berma de material no fim da caixa", () => d.EndBerm, v => d.EndBerm = v)
         .Check("Zebrado de entrada e delineadores", () => d.Signage, v => d.Signage = v);
        if (!edit) w.Modes(("Desenhar o eixo por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(MarkingDefinition def)
    {
        var working = MarkingDefinition.FromJson(def.ToJson())!;
        FormWindow? w = working switch
        {
            RumbleStripDefinition rs => RumbleStrip(rs, true),
            EscapeRampDefinition er => EscapeRamp(er, true),
            _ => null,
        };
        return w == null ? null : (w, working);
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdSonorizador : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var template = UiHelpers.Remembered<RumbleStripDefinition>("seg:sonorizador") ?? new RumbleStripDefinition { Offset = -0.40, SegmentLength = 12, GapLength = 3.6 };
        template.Output = PluginContext.Settings.NewOutput();
        var w = SafetyForms.RumbleStrip(template, false);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember("seg:sonorizador", template);
        PluginContext.SaveSettings();
        return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode, template.DisplayCode);
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaEscape : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        InfraRunner.Run(uidoc, "seg:escape", () => new EscapeRampDefinition(), d => SafetyForms.EscapeRamp(d, false));
}
