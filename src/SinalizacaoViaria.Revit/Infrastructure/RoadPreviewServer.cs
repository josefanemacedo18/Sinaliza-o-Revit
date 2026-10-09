using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Gráficos temporários (DirectContext3D) da pré-visualização da via enquanto o eixo é desenhado: pista, meio-fio, calçadas,
/// canteiro e linhas principais, refeitos a cada clique e apagados ao concluir ou cancelar. Nada é gravado no modelo.
/// </summary>
/// <remarks>
/// O Revit só redesenha estes gráficos quando a vista é redesenhada; durante <c>Selection.PickPoint</c> a API fica bloqueada
/// (sem eventos Idling e sem chamadas de outra thread), então não há como pedir o redesenho a cada movimento do mouse – a
/// prévia é atualizada a cada clique.
/// </remarks>
public sealed class RoadPreviewServer : IDirectContext3DServer
{
    private static readonly Guid ServerGuid = new("5E3C1F7A-92B4-4D61-A8E0-3B7D2C9F1A64");
    private static RoadPreviewServer? _instance;

    private Document? _doc;
    private ElementId _viewId = ElementId.InvalidElementId;
    private Outline? _box;
    // Preenchimentos (triângulos) e contornos (segmentos), em pés, com a cor de cada peça.
    private readonly List<(XYZ P, ColorWithTransparency C)> _fillVerts = new();
    private readonly List<(int A, int B, int C)> _fillTris = new();
    private readonly List<(XYZ A, XYZ B, ColorWithTransparency C)> _lines = new();
    private bool _active;

    /// <summary>Mostra (ou atualiza) a prévia na vista ativa. <paramref name="zFeet"/> = cota dos pontos clicados.</summary>
    public static void Show(UIDocument uidoc, PreviaVia? previa, IReadOnlyList<Vec2> clicked, double zFeet)
    {
        try
        {
            var s = Ensure();
            if (s == null) return;
            s.Load(uidoc.Document, uidoc.ActiveView, previa, clicked, zFeet);
            uidoc.RefreshActiveView();
        }
        catch (Exception ex)
        {
            Log.Error("Pré-visualização da via", ex);
        }
    }

    /// <summary>Apaga a prévia.</summary>
    public static void Clear(UIDocument? uidoc)
    {
        if (_instance is not { _active: true } s) return;
        s._active = false;
        s._fillVerts.Clear();
        s._fillTris.Clear();
        s._lines.Clear();
        s._box = null;
        try { uidoc?.RefreshActiveView(); } catch { /* vista fechada */ }
    }

    private static RoadPreviewServer? Ensure()
    {
        if (_instance != null) return _instance;
        if (ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) is not MultiServerService service)
            return null;
        var s = new RoadPreviewServer();
        service.AddServer(s);
        var active = service.GetActiveServerIds();
        active.Add(s.GetServerId());
        service.SetActiveServers(active);
        return _instance = s;
    }

    // Cores (0 = opaco na transparência).
    private static ColorWithTransparency Fill(ClassePrevia c) => c switch
    {
        ClassePrevia.Pista => new ColorWithTransparency(70, 74, 82, 70),
        ClassePrevia.MeioFio => new ColorWithTransparency(150, 150, 150, 60),
        ClassePrevia.Calcada => new ColorWithTransparency(214, 205, 186, 80),
        ClassePrevia.Canteiro => new ColorWithTransparency(98, 158, 74, 80),
        _ => new ColorWithTransparency(250, 250, 250, 20),
    };

    private static ColorWithTransparency Edge(ClassePrevia c, Core.Model.MarkingColor cor) => c switch
    {
        ClassePrevia.Linha => cor == Core.Model.MarkingColor.Amarela ? new ColorWithTransparency(255, 184, 28, 0) : new ColorWithTransparency(250, 250, 250, 0),
        ClassePrevia.MeioFio => new ColorWithTransparency(60, 60, 60, 0),
        ClassePrevia.Canteiro => new ColorWithTransparency(60, 120, 50, 0),
        ClassePrevia.Calcada => new ColorWithTransparency(150, 140, 120, 0),
        _ => new ColorWithTransparency(31, 95, 168, 0),
    };

    private void Load(Document doc, View view, PreviaVia? previa, IReadOnlyList<Vec2> clicked, double zFeet)
    {
        _doc = doc;
        _viewId = view.Id;
        _fillVerts.Clear();
        _fillTris.Clear();
        _lines.Clear();
        // Um pouco acima do plano dos cliques (dentro da faixa da vista em planta).
        var z = zFeet + 0.05;
        XYZ P(Vec2 v, double dz = 0) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z + dz);
        if (previa != null)
        {
            // Ordem de desenho: pista, canteiro, calçada, meio-fio, linhas (cada camada um pouco acima).
            var order = new[] { ClassePrevia.Pista, ClassePrevia.Canteiro, ClassePrevia.Calcada, ClassePrevia.MeioFio, ClassePrevia.Linha };
            for (int layer = 0; layer < order.Length; layer++)
            {
                var dz = layer * 0.01;
                foreach (var pc in previa.Da(order[layer]))
                {
                    AddFill(pc.Contorno, Fill(pc.Classe), z + dz);
                    if (pc.Classe != ClassePrevia.Pista || pc.Contorno.Area > 0)
                        foreach (var ring in new[] { pc.Contorno.Outer }.Concat(pc.Contorno.Holes))
                            for (int i = 0; i < ring.Count; i++)
                                _lines.Add((P(ring[i], dz + 0.005), P(ring[(i + 1) % ring.Count], dz + 0.005), Edge(pc.Classe, pc.Cor)));
                }
            }
            // Eixo concordado (como será criado).
            for (int i = 0; i + 1 < previa.Eixo.Count; i++)
                _lines.Add((P(previa.Eixo[i], 0.06), P(previa.Eixo[i + 1], 0.06), new ColorWithTransparency(200, 0, 160, 0)));
        }
        // Pontos clicados: pequenas cruzes.
        foreach (var c in clicked)
        {
            _lines.Add((P(c + new Vec2(-0.6, 0), 0.07), P(c + new Vec2(0.6, 0), 0.07), new ColorWithTransparency(31, 95, 168, 0)));
            _lines.Add((P(c + new Vec2(0, -0.6), 0.07), P(c + new Vec2(0, 0.6), 0.07), new ColorWithTransparency(31, 95, 168, 0)));
        }
        var pts = _lines.SelectMany(l => new[] { l.A, l.B }).Concat(_fillVerts.Select(v => v.P)).ToList();
        _box = pts.Count == 0 ? null : new Outline(new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z) - 1),
            new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z) + 1));
        _active = _box != null;
    }

    /// <summary>Triangula o contorno (com furos) pela face superior de uma extrusão fina do Revit; sem ela, fica só o contorno.</summary>
    private void AddFill(Polygon2 poly, ColorWithTransparency color, double z)
    {
        try
        {
            var simple = poly.Simplified(0.01) ?? poly;
            var loops = new List<CurveLoop>();
            foreach (var ring in new[] { simple.Outer }.Concat(simple.Holes))
            {
                var loop = new CurveLoop();
                var pts = ring.Select(v => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z)).ToList();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    if (a.DistanceTo(b) < 0.01) continue;
                    loop.Append(Line.CreateBound(a, b));
                }
                loops.Add(loop);
            }
            var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, 0.01);
            foreach (Face f in solid.Faces)
            {
                if (f is not PlanarFace pf || pf.FaceNormal.Z < 0.99) continue;   // face de cima (vista de cima)
                var mesh = f.Triangulate();
                var baseIndex = _fillVerts.Count;
                for (int i = 0; i < mesh.Vertices.Count; i++) _fillVerts.Add((mesh.Vertices[i], color));
                for (int t = 0; t < mesh.NumTriangles; t++)
                {
                    var tr = mesh.get_Triangle(t);
                    _fillTris.Add(((int)tr.get_Index(0) + baseIndex, (int)tr.get_Index(1) + baseIndex, (int)tr.get_Index(2) + baseIndex));
                }
                break;
            }
        }
        catch
        {
            // Contorno que o Revit não aceita como laço (pontas finíssimas): só as bordas aparecem.
        }
    }

    // ------------------------------------------------------------------ IExternalServer / IDirectContext3DServer

    public Guid GetServerId() => ServerGuid;
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetName() => "SinalizaBIM – pré-visualização da via";
    public string GetVendorId() => "SinalizaBIM";
    public string GetDescription() => "Pista, meio-fio, calçadas, canteiro e linhas da via enquanto o eixo é desenhado.";
    public string GetApplicationId() => "";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool UseInTransparentPass(View view) => true;

    public bool CanExecute(View view)
    {
        try { return _active && _doc != null && view.Id == _viewId && view.Document.Equals(_doc); }
        catch { return false; }
    }

    public Outline GetBoundingBox(View view) => _box ?? new Outline(XYZ.Zero, XYZ.Zero);

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (!_active) return;
            if (DrawContext.IsTransparentPass()) DrawFills();
            else DrawLines();
        }
        catch (Exception ex)
        {
            Log.Error("Pré-visualização da via – desenho", ex);
        }
    }

    // Índices de 16 bits: lotes de até 60 000 vértices.
    private const int MaxVerts = 60000;

    private void DrawFills()
    {
        var start = 0;
        while (start < _fillTris.Count)
        {
            var map = new Dictionary<int, int>();
            var tris = new List<(int, int, int)>();
            var k = start;
            for (; k < _fillTris.Count && map.Count < MaxVerts - 3; k++)
            {
                var (a, b, c) = _fillTris[k];
                int M(int i) => map.TryGetValue(i, out var j) ? j : map[i] = map.Count;
                tris.Add((M(a), M(b), M(c)));
            }
            start = k;
            var verts = map.OrderBy(x => x.Value).Select(x => _fillVerts[x.Key]).ToList();
            var vSize = VertexPositionColored.GetSizeInFloats() * verts.Count;
            using var vb = new VertexBuffer(vSize);
            vb.Map(vSize);
            var vs = vb.GetVertexStreamPositionColored();
            foreach (var (p, col) in verts) vs.AddVertex(new VertexPositionColored(p, col));
            vb.Unmap();
            var iSize = IndexTriangle.GetSizeInShortInts() * tris.Count;
            using var ib = new IndexBuffer(iSize);
            ib.Map(iSize);
            var istream = ib.GetIndexStreamTriangle();
            foreach (var (a, b, c) in tris) istream.AddTriangle(new IndexTriangle(a, b, c));
            ib.Unmap();
            using var vf = new VertexFormat(VertexFormatBits.PositionColored);
            using var effect = new EffectInstance(VertexFormatBits.PositionColored);
            DrawContext.FlushBuffer(vb, verts.Count, ib, iSize, vf, effect, PrimitiveType.TriangleList, 0, tris.Count);
        }
    }

    private void DrawLines()
    {
        for (int start = 0; start < _lines.Count; start += MaxVerts / 2)
        {
            var chunk = _lines.Skip(start).Take(MaxVerts / 2).ToList();
            var n = chunk.Count * 2;
            var vSize = VertexPositionColored.GetSizeInFloats() * n;
            using var vb = new VertexBuffer(vSize);
            vb.Map(vSize);
            var vs = vb.GetVertexStreamPositionColored();
            foreach (var (a, b, c) in chunk)
            {
                vs.AddVertex(new VertexPositionColored(a, c));
                vs.AddVertex(new VertexPositionColored(b, c));
            }
            vb.Unmap();
            var iSize = IndexLine.GetSizeInShortInts() * chunk.Count;
            using var ib = new IndexBuffer(iSize);
            ib.Map(iSize);
            var istream = ib.GetIndexStreamLine();
            for (int i = 0; i < chunk.Count; i++) istream.AddLine(new IndexLine(2 * i, 2 * i + 1));
            ib.Unmap();
            using var vf = new VertexFormat(VertexFormatBits.PositionColored);
            using var effect = new EffectInstance(VertexFormatBits.PositionColored);
            DrawContext.FlushBuffer(vb, n, ib, iSize, vf, effect, PrimitiveType.LineList, 0, chunk.Count);
        }
    }
}
