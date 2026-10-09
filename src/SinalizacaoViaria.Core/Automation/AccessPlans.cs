using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Acesso aberto numa via existente a partir de um clique no eixo (ferramenta Abrir acesso na via).</summary>
public enum TipoAcesso
{
    /// <summary>Retorno em U (abertura no canteiro com ou sem bolsão, alargamento da pista oposta).</summary>
    Retorno,
    /// <summary>Nova via em T, esquinas com o raio pedido.</summary>
    NovaVia,
    /// <summary>Nova via com faixa de conversão livre à direita e ilha triangular física (Tipo III) na esquina de quem sai da via existente.</summary>
    ConversaoLivre,
    /// <summary>Nova via com bolsão de conversão à esquerda (Tipo IV) na via existente – zebrado amarelo no taper.</summary>
    BolsaoEsquerda,
    /// <summary>Nova via saindo em curva (raio e ângulo) com zebrado de canalização na separação dos fluxos.</summary>
    ViaEmCurva,
    /// <summary>Travessia de pedestres sobre o canteiro central.</summary>
    Travessia,
}

/// <summary>Medidas digitadas de um acesso (as que não se aplicam ao tipo são ignoradas).</summary>
public sealed class AcessoSpec
{
    public TipoAcesso Tipo { get; set; } = TipoAcesso.NovaVia;
    /// <summary>Ângulo da nova via com a via existente (°), medido a partir do sentido do eixo para o lado do clique.</summary>
    public double Angulo { get; set; } = 90;
    /// <summary>Comprimento da nova via (m).</summary>
    public double Comprimento { get; set; } = 40;
    /// <summary>Raio das esquinas (face do meio-fio, m).</summary>
    public double RaioEsquina { get; set; } = 6;
    /// <summary>Raio da face externa da faixa de conversão / da saída em curva (m).</summary>
    public double RaioConversao { get; set; } = 25;
    /// <summary>Largura da faixa de conversão livre (m; mínimo 3,50 m).</summary>
    public double LarguraConversao { get; set; } = 5;
    /// <summary>Bolsão de conversão à esquerda: comprimento da faixa de acumulação (m) [a confirmar].</summary>
    public double Espera { get; set; } = 30;
    /// <summary>Bolsão de conversão à esquerda: comprimento do taper (m) [a confirmar].</summary>
    public double Taper { get; set; } = 20;
    /// <summary>Bolsão de conversão à esquerda: largura (m).</summary>
    public double LarguraBolsao { get; set; } = 3.0;

    public AcessoSpec Clone() => (AcessoSpec)MemberwiseClone();

    /// <summary>O acesso cria uma via nova ligada por interseção.</summary>
    public bool CriaVia => Tipo is TipoAcesso.NovaVia or TipoAcesso.ConversaoLivre or TipoAcesso.BolsaoEsquerda or TipoAcesso.ViaEmCurva;
}

/// <summary>Cena de um acesso (pré-visualização e verificação): marcas, eixos, leiaute da interseção e avisos.</summary>
public sealed record AcessoCena(List<MarkingDefinition> Definitions, Dictionary<string, Polyline2> Paths, IntersectionLayout? Layout,
    List<string> Warnings, List<Vec2>? BranchAxis);

public static partial class AcessoVia
{
    /// <summary>
    /// Ajusta a interseção do acesso (via existente <paramref name="hostRoadId"/> como principal): a esquina de quem sai da via
    /// existente para a nova (conversão à direita) recebe a faixa de conversão livre – ilha física ou zebrado – com o raio e a
    /// largura pedidos; o ramo de quem converte à esquerda para a nova via recebe o bolsão com as medidas pedidas.
    /// </summary>
    public static void Configure(IntersectionDefinition d, string hostRoadId, bool clickLeft, AcessoSpec s)
    {
        d.CornerRadius = Math.Max(0, s.RaioEsquina);
        d.MainRoadId = hostRoadId;
        IntersectionLegSettings Leg(int sign)
        {
            var x = d.LegSettings.FirstOrDefault(l => l.RoadId == hostRoadId && l.Sign == sign);
            if (x == null) d.LegSettings.Add(x = new IntersectionLegSettings { RoadId = hostRoadId, Sign = sign });
            return x;
        }
        switch (s.Tipo)
        {
            case TipoAcesso.ConversaoLivre or TipoAcesso.ViaEmCurva:
            {
                // Nova via à direita de quem segue no sentido do eixo (clique à direita): chega pelo ramo de trás (−1) e converte à direita.
                var l = Leg(clickLeft ? 1 : -1);
                l.RightTurnChannel = s.Tipo == TipoAcesso.ConversaoLivre ? TipoIlha.Fisica : TipoIlha.Pintada;
                l.RightTurnRadius = Math.Max(5, s.RaioConversao);
                d.RightTurnLaneWidth = Math.Max(3.5, s.LarguraConversao);
                break;
            }
            case TipoAcesso.BolsaoEsquerda:
            {
                // Quem vem no sentido contrário converte à esquerda para a nova via: bolsão no ramo da frente.
                Leg(clickLeft ? -1 : 1).Treatment = true;
                d.PocketLength = Math.Max(5, s.Espera);
                d.PocketTaper = Math.Max(5, s.Taper);
                d.PocketWidth = Math.Clamp(s.LarguraBolsao, 2.5, 5.0);
                break;
            }
        }
    }

    /// <summary>
    /// Cena do acesso num trecho da via existente em volta do clique (± <paramref name="window"/> m): a via com o retorno, ou
    /// a via e a nova via ligadas pela interseção ajustada por <see cref="Configure"/>.
    /// </summary>
    public static AcessoCena Cena(RoadSetup host, Polyline2 hostAxis, Vec2 click, AcessoSpec s, Catalogo cat, RoadSetup? branch = null,
        RetornoVia? retorno = null, IntersectionDefinition? template = null, double window = 160)
    {
        var defs = new List<MarkingDefinition>();
        var paths = new Dictionary<string, Polyline2>();
        var warn = new List<string>();
        var st = hostAxis.Project(click).Station;
        var s0 = Math.Max(0, st - window);
        var s1 = Math.Min(hostAxis.Length, st + window);
        var local = new Polyline2(hostAxis.SubPoints(s0, s1));
        var h = host.Clone();
        h.Retornos.Clear();
        if (retorno != null && s.Tipo == TipoAcesso.Retorno)
        {
            var r = retorno.Clone();
            r.Estaca -= s0;
            h.Retornos.Add(r);
        }
        var groups = new List<List<MarkingDefinition>>();
        var roads = new List<IntersectionRoad>();
        void Road(RoadSetup setup, Polyline2 axis)
        {
            var pr = PathReference.FromPoints(axis.Points, 0);
            var g = setup.Build(pr, new OutputSettings(), cat, axis: axis);
            warn.AddRange(setup.Warnings);
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) paths[m.Id] = axis;
            defs.AddRange(g);
            groups.Add(g);
            if (g.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav) roads.Add(new IntersectionRoad(pav, axis));
        }
        Road(h, local);
        if (!s.CriaVia || branch == null || roads.Count == 0) return new AcessoCena(defs, paths, null, warn, null);

        var clickLeft = hostAxis.Project(click).Signed >= 0;
        var pts = BranchAxis(local, click, s.Angulo, s.Comprimento);
        var b = branch.Clone();
        b.CornerRadius = s.RaioEsquina;
        Road(b, new Polyline2(pts));
        var d = (IntersectionDefinition)(template ?? new IntersectionDefinition { Control = ControleIntersecao.Pare }).CloneWithNewId();
        Configure(d, roads[0].Def.Id, clickLeft, s);
        IntersectionLayout? layout = null;
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(roads))
        {
            var rs = ids.Select(i => roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            d.Node = node;
            var scene = IntersectionDemo.Create(d, rs, ids.Select(i => groups[i]).ToList(), paths, defs, cat);
            layout = scene.Layout;
            warn.AddRange(layout.Warnings);
            // Faixa de conversão sem espaço para a ilha/zebrado: raio sugerido para o ângulo pedido.
            if (s.Tipo is TipoAcesso.ConversaoLivre or TipoAcesso.ViaEmCurva
                && (s.Tipo == TipoAcesso.ConversaoLivre ? layout.Islands.Count : layout.PaintedIslands.Count) == 0)
                warn.Add($"Raio de {s.RaioConversao:0.0} m pequeno para a {(s.Tipo == TipoAcesso.ViaEmCurva ? "separação zebrada" : "ilha triangular")} " +
                         $"com a nova via a {s.Angulo:0}°: use pelo menos {Math.Ceiling(RaioMinimoSeparacao(s.Angulo, s.LarguraConversao, s.RaioEsquina)):0} m.");
            break;
        }
        return new AcessoCena(defs, paths, layout, warn, pts);
    }

    /// <summary>
    /// Raio sugerido (m) para a curva de saída comportar a ilha/zebrado de canalização: o arco fica a R·k do vértice da
    /// esquina (k = 1/sen(φ/2) − 1, φ = 180° − ângulo da nova via – a esquina de quem sai) e a ilha precisa da faixa de
    /// conversão, de 0,60 m de afastamento das pistas, da curva da esquina comum (raio <paramref name="raioEsquina"/>) e de
    /// ~6 m de folga para ter área útil (conservador; conferido na geração).
    /// </summary>
    public static double RaioMinimoSeparacao(double anguloDeg, double larguraConversao, double raioEsquina = 6)
    {
        var phi = Math.Clamp(180 - anguloDeg, 20, 160) * Math.PI / 180;
        var k = 1 / Math.Sin(phi / 2) - 1;
        return (Math.Max(3.5, larguraConversao) + 0.6 + Math.Max(0, raioEsquina) * k + 6.0) / k;
    }

    /// <summary>Geometria da cena (placas de fora quando <paramref name="signs"/> é falso).</summary>
    public static MarkingGeometry Geometry(AcessoCena c, BuildContext ctx, bool signs = false) =>
        IntersectionDemo.Build(new IntersectionDemo.Scene(c.Definitions, c.Paths, c.Layout ?? new IntersectionLayout()), ctx, signs);
}
