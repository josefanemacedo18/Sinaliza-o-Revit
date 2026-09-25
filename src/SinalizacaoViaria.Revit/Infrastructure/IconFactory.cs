using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Desenha os ícones da faixa de opções em tempo de execução (vetoriais, sem arquivos de imagem).
/// </summary>
public static class IconFactory
{
    private static readonly Color Asphalt = Color.FromRgb(70, 74, 82);
    private static readonly Color White = Color.FromRgb(250, 250, 250);
    private static readonly Color Yellow = Color.FromRgb(255, 184, 28);
    private static readonly Color Blue = Color.FromRgb(31, 95, 168);
    private static readonly Color Red = Color.FromRgb(196, 30, 36);
    private static readonly Color Magenta = Color.FromRgb(200, 0, 160);

    public static ImageSource Large(string key) => Render(key, 32);
    public static ImageSource Small(string key) => Render(key, 16);

    private static ImageSource Render(string key, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
            Draw(dc, key);
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen P(Color c, double w) { var p = new Pen(B(c), w) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; p.Freeze(); return p; }

    private static void RoadBackground(DrawingContext dc) =>
        dc.DrawRoundedRectangle(B(Asphalt), null, new Rect(1, 1, 30, 30), 4, 4);

    private static void Draw(DrawingContext dc, string key)
    {
        switch (key)
        {
            case "via":
                RoadBackground(dc);
                dc.DrawLine(P(White, 2), new Point(5, 2), new Point(5, 30));
                dc.DrawLine(P(White, 2), new Point(27, 2), new Point(27, 30));
                for (int y = 2; y < 30; y += 8) dc.DrawLine(P(Yellow, 2), new Point(16, y), new Point(16, y + 4));
                break;
            case "linha":
                RoadBackground(dc);
                for (int y = 3; y < 30; y += 9) dc.DrawLine(P(White, 3), new Point(16, y), new Point(16, y + 5));
                dc.DrawLine(P(Yellow, 2), new Point(8, 2), new Point(8, 30));
                break;
            case "eixo":
                dc.DrawGeometry(null, new Pen(B(Magenta), 2) { DashStyle = DashStyles.Dash }, Poly(new Point(4, 27), new Point(12, 10), new Point(22, 20), new Point(28, 5)));
                foreach (var pt in new[] { new Point(4, 27), new Point(12, 10), new Point(22, 20), new Point(28, 5) })
                    dc.DrawEllipse(B(Blue), null, pt, 2.5, 2.5);
                break;
            case "pedestre":
                RoadBackground(dc);
                for (int x = 4; x < 30; x += 6) dc.DrawRectangle(B(White), null, new Rect(x, 8, 3, 16));
                break;
            case "transversal":
                RoadBackground(dc);
                dc.DrawRectangle(B(White), null, new Rect(3, 18, 26, 5));
                for (int x = 3; x < 29; x += 6) dc.DrawRectangle(B(White), null, new Rect(x, 8, 3, 3));
                break;
            case "zebrado":
                RoadBackground(dc);
                var tri = Poly(new Point(4, 27), new Point(28, 27), new Point(28, 5));
                dc.PushClip(tri);
                for (int i = -30; i < 40; i += 6) dc.DrawLine(P(White, 2), new Point(i, 30), new Point(i + 25, 5));
                dc.Pop();
                dc.DrawGeometry(null, P(White, 1.5), Close(tri));
                break;
            case "seta":
                RoadBackground(dc);
                dc.DrawGeometry(B(White), null, Close(Poly(new Point(14.5, 29), new Point(17.5, 29), new Point(17.5, 13), new Point(23, 13),
                    new Point(16, 3), new Point(9, 13), new Point(14.5, 13))));
                break;
            case "legenda":
                RoadBackground(dc);
                Text(dc, "PARE", 9.5, White, new Point(16, 11), true);
                break;
            case "vaga":
                RoadBackground(dc);
                dc.DrawLine(P(White, 2), new Point(3, 26), new Point(29, 26));
                for (int x = 4; x < 30; x += 12) dc.DrawLine(P(White, 2), new Point(x, 26), new Point(x, 6));
                dc.DrawRectangle(B(Blue), null, new Rect(18, 11, 9, 9));
                Text(dc, "P", 9, White, new Point(22.5, 10.5), true);
                break;
            case "tacha":
                RoadBackground(dc);
                dc.DrawLine(P(Yellow, 2), new Point(16, 2), new Point(16, 30));
                for (int y = 5; y < 30; y += 8) dc.DrawRectangle(B(Color.FromRgb(255, 230, 120)), P(Yellow, 1), new Rect(19, y, 5, 4));
                break;
            case "tatil":
                dc.DrawRoundedRectangle(B(Yellow), null, new Rect(2, 2, 28, 28), 3, 3);
                for (int x = 7; x < 30; x += 6)
                    for (int y = 7; y < 30; y += 6) dc.DrawEllipse(B(Color.FromRgb(200, 140, 0)), null, new Point(x, y), 1.8, 1.8);
                break;
            case "ciclo":
                RoadBackground(dc);
                dc.DrawRectangle(B(Red), null, new Rect(17, 1, 13, 30));
                dc.DrawLine(P(White, 2), new Point(16, 1), new Point(16, 31));
                dc.DrawEllipse(null, P(White, 1.5), new Point(20.5, 20), 2.6, 2.6);
                dc.DrawEllipse(null, P(White, 1.5), new Point(26.5, 20), 2.6, 2.6);
                dc.DrawLine(P(White, 1.5), new Point(20.5, 20), new Point(23.5, 15));
                dc.DrawLine(P(White, 1.5), new Point(23.5, 15), new Point(26.5, 20));
                break;
            case "editar":
                dc.DrawRectangle(B(Color.FromRgb(235, 238, 243)), P(Asphalt, 1), new Rect(3, 6, 20, 23));
                dc.DrawGeometry(B(Yellow), P(Asphalt, 1), Close(Poly(new Point(12, 24), new Point(26, 6), new Point(30, 10), new Point(16, 28), new Point(11, 29))));
                break;
            case "atualizar":
                dc.DrawGeometry(null, P(Blue, 3), Arc(new Point(16, 16), 10, 20, 290));
                dc.DrawGeometry(B(Blue), null, Close(Poly(new Point(22, 3), new Point(29, 9), new Point(20, 11))));
                break;
            case "alternar":
                Text(dc, "2D", 11, Blue, new Point(10, 3), true);
                Text(dc, "3D", 11, Asphalt, new Point(21, 15), true);
                dc.DrawLine(P(Magenta, 1.5), new Point(4, 28), new Point(28, 4));
                break;
            case "selecionar":
                dc.DrawRectangle(null, new Pen(B(Blue), 1.5) { DashStyle = DashStyles.Dash }, new Rect(3, 3, 22, 22));
                dc.DrawGeometry(B(Asphalt), null, Close(Poly(new Point(14, 12), new Point(29, 20), new Point(22, 22), new Point(26, 29), new Point(23, 30), new Point(19, 24), new Point(14, 28))));
                break;
            case "quantitativos":
                dc.DrawRectangle(B(White), P(Asphalt, 1.2), new Rect(3, 3, 26, 26));
                dc.DrawRectangle(B(Blue), null, new Rect(3, 3, 26, 6));
                for (int y = 13; y < 29; y += 5) dc.DrawLine(P(Asphalt, 1), new Point(3, y), new Point(29, y));
                dc.DrawLine(P(Asphalt, 1), new Point(13, 9), new Point(13, 29));
                break;
            case "config":
                for (int i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    dc.DrawLine(P(Asphalt, 4), new Point(16 + 9 * Math.Cos(a), 16 + 9 * Math.Sin(a)), new Point(16 + 13 * Math.Cos(a), 16 + 13 * Math.Sin(a)));
                }
                dc.DrawEllipse(null, P(Asphalt, 4), new Point(16, 16), 8, 8);
                break;
            case "catalogo":
                dc.DrawRectangle(B(Blue), null, new Rect(5, 3, 22, 26));
                dc.DrawRectangle(B(White), null, new Rect(9, 7, 14, 3));
                dc.DrawRectangle(B(Yellow), null, new Rect(9, 13, 14, 2));
                dc.DrawRectangle(B(White), null, new Rect(9, 18, 10, 2));
                break;
            default:
                dc.DrawEllipse(B(Blue), null, new Point(16, 16), 13, 13);
                Text(dc, "i", 16, White, new Point(16, 5), true);
                break;
        }
    }

    private static StreamGeometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, false);
            c.PolyLineTo(pts.Skip(1).ToList(), true, false);
        }
        g.Freeze();
        return g;
    }

    private static StreamGeometry Close(StreamGeometry open)
    {
        var pts = new List<Point>();
        var pg = open.GetFlattenedPathGeometry();
        foreach (var f in pg.Figures)
        {
            pts.Add(f.StartPoint);
            foreach (var s in f.Segments)
                if (s is PolyLineSegment pl) pts.AddRange(pl.Points);
                else if (s is LineSegment ls) pts.Add(ls.Point);
        }
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts.Skip(1).ToList(), true, false);
        }
        g.Freeze();
        return g;
    }

    private static StreamGeometry Arc(Point c, double r, double startDeg, double endDeg)
    {
        var pts = new List<Point>();
        for (double a = startDeg; a <= endDeg; a += 10)
        {
            var rad = a * Math.PI / 180;
            pts.Add(new Point(c.X + r * Math.Cos(rad), c.Y - r * Math.Sin(rad)));
        }
        return Poly(pts.ToArray());
    }

    private static void Text(DrawingContext dc, string text, double size, Color color, Point topCenter, bool bold)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, B(color), 1.0);
        dc.DrawText(ft, new Point(topCenter.X - ft.Width / 2, topCenter.Y));
    }
}
