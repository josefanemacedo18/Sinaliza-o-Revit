using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Nó vizinho visto por uma interseção (outra interseção ou uma rotatória): onde está, quem é, o peso na escolha de quem fica
/// com a travessia e se ele faz travessias.
/// </summary>
public sealed record NoVizinho(Vec2 No, string Id, int Peso, bool Travessias);

/// <summary>
/// Travessias num trecho curto entre esquinas (Etapa 15, item 18 do pedido de 2026-10-27). Antes, cada interseção fazia a
/// travessia – com as rampas e as extensões do lado contínuo – em todos os ramos, sem olhar o nó vizinho: um trecho de 25 m
/// entre duas esquinas ganhava duas faixas de pedestres e quatro rampas. Agora, quando o trecho até o nó vizinho na mesma
/// via é menor que o mínimo da interseção, só um dos dois faz a travessia sobre essa via: o de maior peso (mais vias,
/// depois o controle mais forte; rotatória sempre fica com a dela), e no empate o de Id menor – a mesma escolha vista dos
/// dois lados, para nunca sumirem as duas nem ficarem as duas.
/// </summary>
public static class EspacamentoTravessias
{
    /// <summary>
    /// Trecho mínimo padrão (m) – padrão do plugin, sem norma citada: abaixo dele, duas travessias na mesma quadra ficam a
    /// menos de meia quadra comum uma da outra e o pedestre atravessa no nó vizinho.
    /// </summary>
    public const double TrechoMinimoPadrao = 50;

    /// <summary>Distância máxima do nó vizinho ao eixo da via para contar como na mesma via (m).</summary>
    private const double NaVia = 6;

    /// <summary>Peso de uma interseção na escolha: 10 por via que se cruza nela mais o controle (semáforo 3, PARE 2, preferência 1).</summary>
    public static int Peso(IntersectionDefinition d) => 10 * Math.Max(2, d.RoadIds.Count) + d.Control switch
    {
        ControleIntersecao.Semaforo => 3,
        ControleIntersecao.Pare => 2,
        ControleIntersecao.DePreferencia => 1,
        _ => 0,
    };

    /// <summary>Peso de uma rotatória: sempre maior que o de uma interseção (a travessia dela fica no anel).</summary>
    public const int PesoRotatoria = 1000;

    /// <summary>Nós vizinhos de <paramref name="d"/> (até 400 m), com o peso e se fazem travessias.</summary>
    public static List<NoVizinho> Vizinhos(IntersectionDefinition d, IEnumerable<MarkingDefinition> all, Vec2 no) =>
        all.OfType<IntersectionDefinition>().Where(o => o.Id != d.Id).Select(o => new NoVizinho(o.Node, o.Id, Peso(o), o.Crosswalks))
            .Concat(all.OfType<RoundaboutDefinition>().Select(o => new NoVizinho(o.Center, o.Id, PesoRotatoria, o.Crosswalks)))
            .Where(n => n.No.DistanceTo(no) > 3 && n.No.DistanceTo(no) < 400).ToList();

    /// <summary>O nó vizinho mais próximo ao longo do ramo e a distância até ele no eixo (m); nulo sem vizinho na via.</summary>
    public static (NoVizinho No, double Trecho)? VizinhoNoRamo(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg)
    {
        if (leg.Road >= L.Roads.Count) return null;
        var axis = L.Roads[leg.Road].Axis;
        (NoVizinho, double)? best = null;
        foreach (var n in d.NeighborCrossings)
        {
            var (st, dist, _) = IntersectionGenerator.Project(axis, n.No);
            if (dist > NaVia) continue;
            var t = (st - leg.NodeStation) * leg.Sign;
            if (t > 1 && (best == null || t < best.Value.Item2)) best = (n, t);
        }
        return best;
    }

    /// <summary>
    /// A travessia deste ramo sai pela regra do trecho mínimo: o vizinho no ramo está mais perto que o mínimo, faz travessias e
    /// ganha a escolha. <paramref name="motivo"/> diz por quê (para a tela da interseção).
    /// </summary>
    public static bool Suprimida(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg, out string? motivo)
    {
        motivo = null;
        if (d.TravessiaTrechoMinimo <= 0 || d.NeighborCrossings.Count == 0) return false;
        if (VizinhoNoRamo(d, L, leg) is not { } v || v.Trecho >= d.TravessiaTrechoMinimo || !v.No.Travessias) return false;
        var meu = Peso(d);
        if (v.No.Peso < meu || v.No.Peso == meu && string.CompareOrdinal(v.No.Id, d.Id) > 0) return false;
        motivo = $"trecho de {v.Trecho:0.0} m até o nó vizinho (mínimo {d.TravessiaTrechoMinimo:0.#} m): a travessia fica no vizinho";
        return true;
    }

    public static bool Suprimida(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) => Suprimida(d, L, leg, out _);
}
