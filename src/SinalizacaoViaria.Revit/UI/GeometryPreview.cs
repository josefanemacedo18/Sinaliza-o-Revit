using System.Windows;
using System.Windows.Media;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Pré-visualização ao vivo: desenha o resultado do gerador do núcleo (as mesmas peças que irão
/// para o Revit) sobre um fundo de asfalto, com escala automática e barra de escala.
/// </summary>
public sealed class GeometryPreview : FrameworkElement
{
    private MarkingGeometry? _geometry;
    private readonly List<IReadOnlyList<Vec2>> _guides = new();
    private readonly List<Polygon2> _pavement = new();
    private string? _message;

    /// <summary>Fundo branco (folha) – usado nas prévias de detalhamento.</summary>
    public bool Paper { get; set; }

    /// <summary>Escala da vista usada para dimensionar os textos de detalhamento na prévia.</summary>
    public double ViewScale { get; set; } = 100;

    private static readonly Brush Asphalt = Freeze(new SolidColorBrush(Color.FromRgb(62, 66, 72)));
    private static readonly Brush Surround = Freeze(new SolidColorBrush(Color.FromRgb(214, 219, 206)));
    private static readonly Pen GuidePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(200, 230, 90, 210)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen OutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 0.5));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void Show(MarkingGeometry? geometry, IEnumerable<IReadOnlyList<Vec2>>? guides = null, IEnumerable<Polygon2>? pavement = null, string? message = null)
    {
        _geometry = geometry;
        _guides.Clear();
        if (guides != null) _guides.AddRange(guides);
        _pavement.Clear();
        if (pavement != null) _pavement.AddRange(pavement);
        _message = message;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;
        dc.DrawRectangle(Paper ? Brushes.White : _pavement.Count > 0 ? Surround : Asphalt, null, new Rect(0, 0, w, h));

        var pts = new List<Vec2>();
        if (_geometry != null) foreach (var p in _geometry.Pieces) pts.AddRange(p.Shape.Outer);
        if (_geometry != null)
            foreach (var a in _geometry.Annotations)
                if (a is AnnotationLine l) pts.AddRange(l.Points);
                else if (a is AnnotationText t) pts.AddRange(TextBox(t));
        foreach (var g in _guides) pts.AddRange(g);
        foreach (var p in _pavement) pts.AddRange(p.Outer);
        if (pts.Count == 0)
        {
            DrawText(dc, _message ?? "Sem pré-visualização", new Point(10, 10), Brushes.White, 12);
            return;
        }

        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        var margin = 16.0;
        var sx = (w - 2 * margin) / Math.Max(0.01, maxX - minX);
        var sy = (h - 2 * margin) / Math.Max(0.01, maxY - minY);
        var s = Math.Min(sx, sy);
        var ox = (w - (maxX - minX) * s) / 2;
        var oy = (h - (maxY - minY) * s) / 2;
        Point P(Vec2 v) => new(ox + (v.X - minX) * s, h - (oy + (v.Y - minY) * s));

        foreach (var pav in _pavement) dc.DrawGeometry(Asphalt, null, ToGeometry(pav, P));
        if (_geometry != null)
        {
            foreach (var piece in _geometry.Pieces)
            {
                var rgb = MarkingColors.Display(piece.Color);
                var brush = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
                brush.Freeze();
                dc.DrawGeometry(brush, OutlinePen, ToGeometry(piece.Shape, P));
            }
        }
        foreach (var g in _guides)
        {
            for (int i = 1; i < g.Count; i++) dc.DrawLine(GuidePen, P(g[i - 1]), P(g[i]));
        }
        if (_geometry != null) DrawAnnotations(dc, s, P);

        if (Paper) return;
        DrawScaleBar(dc, s, h);
        if (_message != null) DrawText(dc, _message, new Point(8, 6), Brushes.White, 11);
        if (_geometry != null)
        {
            var info = $"Área pintada: {UiHelpers.F(_geometry.TotalArea)} m²   Extensão: {UiHelpers.F(_geometry.PaintedLength)} m" +
                       (_geometry.UnitCount > 0 ? $"   Unidades: {_geometry.UnitCount}" : "");
            DrawText(dc, info, new Point(8, h - 20), Brushes.White, 11);
        }
    }

    private double TextModelHeight(AnnotationText t) => t.PaperHeightMm * ViewScale / 1000.0;

    /// <summary>Caixa aproximada do texto (m) para o enquadramento.</summary>
    private IEnumerable<Vec2> TextBox(AnnotationText t)
    {
        var th = TextModelHeight(t);
        var lines = t.Text.Split('\n');
        var tw = lines.Max(l => l.Length) * th * 0.62;
        var x0 = t.Align switch { TextAlign.Left => t.Position.X, TextAlign.Right => t.Position.X - tw, _ => t.Position.X - tw / 2 };
        yield return new Vec2(x0, t.Position.Y);
        yield return new Vec2(x0 + tw, t.Position.Y - lines.Length * th * 1.5);
    }

    private void DrawAnnotations(DrawingContext dc, double s, Func<Vec2, Point> map)
    {
        foreach (var a in _geometry!.Annotations)
        {
            if (a is AnnotationLine l)
            {
                var rgb = MarkingColors.Display(l.Color);
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B)), 1);
                pen.Freeze();
                for (int i = 1; i < l.Points.Count; i++) dc.DrawLine(pen, map(l.Points[i - 1]), map(l.Points[i]));
            }
            else if (a is AnnotationText t)
            {
                var size = Math.Max(4, TextModelHeight(t) * s * 1.35);
                var ft = new FormattedText(t.Text, UiHelpers.PtBr, FlowDirection.LeftToRight, new Typeface("Arial"), size,
                    Paper ? Brushes.Black : Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    TextAlignment = t.Align switch { TextAlign.Left => TextAlignment.Left, TextAlign.Right => TextAlignment.Right, _ => TextAlignment.Center },
                };
                dc.DrawText(ft, map(t.Position));
            }
        }
    }

    private static StreamGeometry ToGeometry(Polygon2 poly, Func<Vec2, Point> map)
    {
        var sg = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = sg.Open())
        {
            void Ring(IReadOnlyList<Vec2> r)
            {
                ctx.BeginFigure(map(r[0]), true, true);
                ctx.PolyLineTo(r.Skip(1).Select(map).ToList(), true, false);
            }
            Ring(poly.Outer);
            foreach (var hole in poly.Holes) Ring(hole);
        }
        sg.Freeze();
        return sg;
    }

    private void DrawScaleBar(DrawingContext dc, double s, double h)
    {
        var candidates = new[] { 0.5, 1, 2, 5, 10, 20, 50, 100 };
        var len = candidates.FirstOrDefault(c => c * s > 50, 100);
        var x0 = ActualWidth - len * s - 14;
        var y0 = h - 30;
        var pen = new Pen(Brushes.White, 2);
        dc.DrawLine(pen, new Point(x0, y0), new Point(x0 + len * s, y0));
        dc.DrawLine(pen, new Point(x0, y0 - 4), new Point(x0, y0 + 4));
        dc.DrawLine(pen, new Point(x0 + len * s, y0 - 4), new Point(x0 + len * s, y0 + 4));
        DrawText(dc, $"{UiHelpers.F(len, "0.#")} m", new Point(x0, y0 - 18), Brushes.White, 11);
    }

    private void DrawText(DrawingContext dc, string text, Point at, Brush brush, double size)
    {
        var ft = new FormattedText(text, UiHelpers.PtBr, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, new Rect(at.X - 3, at.Y - 1, ft.Width + 6, ft.Height + 2));
        dc.DrawText(ft, at);
    }
}
