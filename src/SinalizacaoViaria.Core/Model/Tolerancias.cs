using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Uma tolerância do núcleo: nome, valor, unidade, onde fica e para que serve.</summary>
public sealed record Tolerancia(string Nome, double Valor, string Unidade, string Onde, string Uso);

/// <summary>
/// Registro único das tolerâncias numéricas do núcleo (Prompt Master §5, Motores §4; K9). Os valores continuam definidos
/// onde são usados – cada um justificado ali –, e este registro só os reúne, com unidade e uso, para o relatório, o manual e
/// os testes de consistência. Nenhum valor muda aqui. Unidades internas: metros.
/// </summary>
public static class Tolerancias
{
    public static IReadOnlyList<Tolerancia> Todas { get; } = new[]
    {
        new Tolerancia("Comparação numérica", Vec2.Eps, "m", "Vec2.Eps", "igualdade de coordenadas e vetores nulos"),
        new Tolerancia("Grade das booleanas", Math.Pow(10, -PolygonOps.Precision), "m", "PolygonOps.Precision", "arredondamento dos contornos no Clipper2 (inteiros)"),
        new Tolerancia("Mesmo vértice entre pisos", BoundaryFit.SameVertex, "m", "BoundaryFit.SameVertex", "vértices de pisos diferentes tratados como o mesmo no ajuste topológico"),
        new Tolerancia("Contorno válido", PolygonChecks.DefaultTolerance, "m", "PolygonChecks.DefaultTolerance", "autointerseção e vértices repetidos (V07)"),
        new Tolerancia("Planos coplanares", FloorTiling.Coplanar, "m", "FloorTiling.Coplanar", "faces do greide que viram o mesmo plano de piso"),
        new Tolerancia("Costura dos pisos", FloorTiling.SeamTol, "m", "FloorTiling.SeamTol", "emendas entre pisos vizinhos"),
        new Tolerancia("Ajuste do contorno", BoundaryFit.DefaultTolerance, "m", "BoundaryFit.DefaultTolerance", "desvio máximo do contorno ajustado em retas e arcos (D-016)"),
        new Tolerancia("Retas quase alinhadas", AxisSnap.MaxAngleDeg, "°", "AxisSnap.MaxAngleDeg", "retas que o Revit avisaria como fora do eixo"),
        new Tolerancia("Folga dos recortes", TrimTools.Gap, "m", "TrimTools.Gap", "folga nos recortes manuais"),
        new Tolerancia("Área mínima de sobreposição", PieceOverlaps.MinArea, "m²", "PieceOverlaps.MinArea", "par de peças sobrepostas no núcleo"),
        new Tolerancia("Altura mínima de sobreposição", PieceOverlaps.MinHeight, "m", "PieceOverlaps.MinHeight", "peças empilhadas que só encostam"),
        new Tolerancia("Sem volume em comum", OverlapClassifier.SemVolume, "m³", "OverlapClassifier.SemVolume", "par sem volume (o limite do Autoteste, 1e-4 pé³)"),
        new Tolerancia("Terreno sob os pisos", FloorTerrainCheck.Tolerancia, "m", "FloorTerrainCheck.Tolerancia", "piso enterrado, suspenso ou com o terreno dentro"),
        new Tolerancia("Terreno na borda da seção", FloorTerrainCheck.ToleranciaBorda, "m", "FloorTerrainCheck.ToleranciaBorda", "degrau entre o terreno e a borda"),
        new Tolerancia("Área de ruído entre quadras", ModelValidator.AreaTolerance, "m²", "ModelValidator.AreaTolerance", "quadras que só se encostam (V09, V10)"),
        new Tolerancia("Dispositivo na sarjeta", DrenagemDoModelo.AlcanceMeioFio, "m", "DrenagemDoModelo.AlcanceMeioFio", "distância do dispositivo à face do meio-fio para ler a sarjeta daquela via"),
        new Tolerancia("Captação do ponto baixo", DrenagemDoModelo.ToleranciaPontoBaixo, "m", "DrenagemDoModelo.ToleranciaPontoBaixo", "captação que atende o ponto baixo da sarjeta (V13)"),
        new Tolerancia("Profundidade do ponto baixo", DrenagemDoModelo.ProfundidadeMinima, "m", "DrenagemDoModelo.ProfundidadeMinima", "quanto a sarjeta sobe dos dois lados para ser ponto baixo"),
        new Tolerancia("Declividade informada × greide", DrenagemDoModelo.DiferencaDeclividade, "%", "DrenagemDoModelo.DiferencaDeclividade", "diferença apontada entre a declividade da sarjeta informada e a do greide"),
        new Tolerancia("Aterro sobre o talvegue", DrenagemDoModelo.AterroMinimo, "m", "DrenagemDoModelo.AterroMinimo", "aterro a partir do qual o talvegue precisa de travessia"),
        new Tolerancia("Profundidade do talvegue", DrenagemDoModelo.TalvegueMinimo, "m", "DrenagemDoModelo.TalvegueMinimo", "vale do terreno natural sob a via"),
        new Tolerancia("Alcance da travessia", DrenagemDoModelo.AlcanceTravessia, "m", "DrenagemDoModelo.AlcanceTravessia", "trecho de rede que atravessa a via perto do talvegue"),
    };
}
