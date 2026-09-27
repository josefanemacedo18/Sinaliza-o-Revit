using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    /// <summary>Vista 3D (isométrica) em vez de planta – alternada pelo botão no canto da prévia.</summary>
    public bool Iso { get; set; }

    /// <summary>Miniatura: sem seletor, barra de escala e textos.</summary>
    public bool Compact { get; set; }

    /// <summary>Miniatura (bitmap) de uma geometria – desenhada sem janela (DrawingVisual), usada no quantitativo e na planilha.</summary>
    public static BitmapSource Snapshot(MarkingGeometry geo, int width, int height, bool iso, bool paper = false)
    {
        var pv = new GeometryPreview { Iso = iso, Compact = true, Paper = paper };
        pv.Show(geo);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) pv.Draw(dc, width, height);
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    public static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private Rect _togglePlan, _toggle3D;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragStart;
    private Vector _panStart;

    public GeometryPreview()
    {
        ClipToBounds = true;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (_togglePlan.Contains(p)) { Iso = false; InvalidateVisual(); return; }
            if (_toggle3D.Contains(p)) { Iso = true; InvalidateVisual(); return; }
            if (e.ClickCount == 2) { _zoom = 1; _pan = new Vector(); InvalidateVisual(); return; }
            _dragStart = p;
            _panStart = _pan;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (_dragStart is not { } d0) return;
            _pan = _panStart + (e.GetPosition(this) - d0);
            InvalidateVisual();
        };
        MouseLeftButtonUp += (_, _) => { _dragStart = null; ReleaseMouseCapture(); };
        MouseWheel += (_, e) =>
        {
            // Zoom com a roda do mouse em torno do cursor (duplo clique volta ao enquadramento).
            var f = e.Delta > 0 ? 1.2 : 1 / 1.2;
            var nz = Math.Clamp(_zoom * f, 0.2, 40);
            f = nz / _zoom;
            var c = e.GetPosition(this);
            var center = new Point(_w / 2, _h / 2);
            _pan = new Vector(c.X - center.X - (c.X - center.X - _pan.X) * f, c.Y - center.Y - (c.Y - center.Y - _pan.Y) * f);
            _zoom = nz;
            InvalidateVisual();
            e.Handled = true;
        };
    }

    private TransformGroup ViewTransform()
    {
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(_zoom, _zoom, _w / 2, _h / 2));
        tg.Children.Add(new TranslateTransform(_pan.X, _pan.Y));
        return tg;
    }

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

    private double _w, _h;

    protected override void OnRender(DrawingContext dc) => Draw(dc, ActualWidth, ActualHeight);

    private void Draw(DrawingContext dc, double w, double h)
    {
        _w = w;
        _h = h;
        if (w < 10 || h < 10) return;
        dc.DrawRectangle(Paper ? Brushes.White : _pavement.Count > 0 ? Surround : Asphalt, null, new Rect(0, 0, w, h));
        if (Iso && !Paper && _geometry != null && _geometry.Pieces.Count > 0)
        {
            RenderIso(dc, w, h);
            if (!Compact) DrawToggle(dc, w);
            return;
        }

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

        dc.PushTransform(ViewTransform());
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
        dc.Pop();

        if (Paper || Compact) return;
        DrawToggle(dc, w);
        DrawScaleBar(dc, s * _zoom, h);
        if (_message != null) DrawText(dc, _message, new Point(8, 6), Brushes.White, 11);
        if (_geometry != null)
        {
            var info = $"Área pintada: {UiHelpers.F(_geometry.TotalArea)} m²   Extensão: {UiHelpers.F(_geometry.PaintedLength)} m" +
                       (_geometry.UnitCount > 0 ? $"   Unidades: {_geometry.UnitCount}" : "");
            DrawText(dc, info, new Point(8, h - 20), Brushes.White, 11);
        }
    }

    /// <summary>Seletor "Planta | 3D" no canto superior direito (o modo ativo fica destacado).</summary>
    private void DrawToggle(DrawingContext dc, double w)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ftP = new FormattedText("Planta", UiHelpers.PtBr, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, dpi);
        var ft3 = new FormattedText("3D", UiHelpers.PtBr, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, dpi);
        var hgt = ftP.Height + 6;
        _toggle3D = new Rect(w - ft3.Width - 20, 6, ft3.Width + 14, hgt);
        _togglePlan = new Rect(_toggle3D.X - ftP.Width - 14, 6, ftP.Width + 14, hgt);
        var on = new SolidColorBrush(Color.FromArgb(230, 30, 90, 160));
        var off = new SolidColorBrush(Color.FromArgb(110, 40, 40, 40));
        dc.DrawRoundedRectangle(Iso ? off : on, new Pen(Brushes.White, 1), _togglePlan, 4, 4);
        dc.DrawRoundedRectangle(Iso ? on : off, new Pen(Brushes.White, 1), _toggle3D, 4, 4);
        dc.DrawText(ftP, new Point(_togglePlan.X + 7, _togglePlan.Y + 3));
        dc.DrawText(ft3, new Point(_toggle3D.X + 7, _toggle3D.Y + 3));
        var hint = new FormattedText("roda = zoom · arrastar = mover · 2 cliques = enquadrar", UiHelpers.PtBr, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
            new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), dpi);
        dc.DrawText(hint, new Point(_togglePlan.X - hint.Width - 10, 9));
    }

    /// <summary>
    /// Vista isométrica das mesmas peças (sólidos, perfis extrudados e peças planas com espessura), com ordenação por
    /// profundidade e sombreamento simples – dá a noção do volume real do elemento (tachão, balizador, placa, poste...).
    /// </summary>
    private void RenderIso(DrawingContext dc, double w, double h)
    {
        var faces = new List<(List<Vec3> Pts, MarkingColor C)>();
        foreach (var pav in _pavement) faces.Add((pav.Outer.Select(v => Vec3.At(v, -0.02)).ToList(), MarkingColor.Asfalto));
        foreach (var p in _geometry!.Pieces)
        {
            if (p.Solid != null) { foreach (var f in p.Solid.Faces) faces.Add((f, p.Color)); continue; }
            if (p.Profile != null)
            {
                var pr = p.Profile;
                var ring = pr.Profile.Outer.Select(q => Vec3.At(pr.Origin + pr.XDir * q.X, q.Y)).ToList();
                var dz = pr.ExtrudeDir * pr.Depth;
                var ring2 = ring.Select(v => new Vec3(v.X + dz.X, v.Y + dz.Y, v.Z)).ToList();
                faces.Add((ring, p.Color));
                faces.Add((ring2, p.Color));
                for (int i = 0; i < ring.Count; i++)
                {
                    var j = (i + 1) % ring.Count;
                    faces.Add((new List<Vec3> { ring[i], ring[j], ring2[j], ring2[i] }, p.Color));
                }
                continue;
            }
            var z0 = p.Elevation;
            var z1 = p.Elevation + Math.Max(0.003, p.Thickness);
            var outer = p.Shape.Outer;
            faces.Add((outer.Select(v => Vec3.At(v, z1)).ToList(), p.Color));
            if (p.Thickness > 0.004)
                for (int i = 0; i < outer.Count; i++)
                {
                    var a = outer[i];
                    var b = outer[(i + 1) % outer.Count];
                    faces.Add((new List<Vec3> { Vec3.At(a, z0), Vec3.At(b, z0), Vec3.At(b, z1), Vec3.At(a, z1) }, p.Color));
                }
        }
        if (faces.Count == 0) return;
        const double az = -35 * Math.PI / 180, el = 30 * Math.PI / 180;
        double ca = Math.Cos(az), sa = Math.Sin(az), ce = Math.Cos(el), se = Math.Sin(el);
        (double X, double Y, double D) Pr(Vec3 v)
        {
            var x = v.X * ca - v.Y * sa;
            var y = v.X * sa + v.Y * ca;
            return (x, v.Z * ce + y * se, y * ce - v.Z * se);
        }
        var light = new Vec3(-0.4, -0.6, 0.7);
        var ll = Math.Sqrt(light.Dot(light));
        light = light * (1 / ll);
        var list = faces.Select(f => (P: f.Pts.Select(Pr).ToList(), f.C, N: Polyhedron.Normal(f.Pts))).ToList();
        var all = list.SelectMany(f => f.P).ToList();
        double minX = all.Min(q => q.X), maxX = all.Max(q => q.X), minY = all.Min(q => q.Y), maxY = all.Max(q => q.Y);
        var margin = 20.0;
        var s = Math.Min((w - 2 * margin) / Math.Max(0.01, maxX - minX), (h - 2 * margin) / Math.Max(0.01, maxY - minY));
        var ox = (w - (maxX - minX) * s) / 2;
        var oy = (h - (maxY - minY) * s) / 2;
        dc.PushTransform(ViewTransform());
        foreach (var f in list.OrderByDescending(f => f.P.Average(q => q.D)))
        {
            var rgb = MarkingColors.Display(f.C);
            var nl = Math.Sqrt(f.N.Dot(f.N));
            var shade = nl < 1e-12 ? 0.8 : 0.55 + 0.45 * Math.Abs(f.N.Dot(light) / nl);
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb.R * shade), (byte)(rgb.G * shade), (byte)(rgb.B * shade)));
            brush.Freeze();
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                var pts = f.P.Select(q => new Point(ox + (q.X - minX) * s, h - (oy + (q.Y - minY) * s))).ToList();
                ctx.BeginFigure(pts[0], true, true);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, false);
            }
            sg.Freeze();
            dc.DrawGeometry(brush, null, sg);
        }
        dc.Pop();
        if (!Compact) DrawText(dc, "Vista 3D (isométrica)", new Point(8, h - 20), Brushes.White, 11);
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
                var at = map(t.Position);
                if (Math.Abs(t.Rotation) > 1e-6) dc.PushTransform(new RotateTransform(-t.Rotation * 180 / Math.PI, at.X, at.Y));
                dc.DrawText(ft, at);
                if (Math.Abs(t.Rotation) > 1e-6) dc.Pop();
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
        var x0 = _w - len * s - 14;
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
