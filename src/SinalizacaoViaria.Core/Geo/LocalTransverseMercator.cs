namespace SinalizacaoViaria.Core.Geo;

/// <summary>
/// Projeção Transversa de Mercator local: meridiano central e origem no centro do projeto, fator de escala k0 = 1.
/// Perto do centro a escala é 1 + x²/(2R²) – menos de 0,3 ppm a 5 km – enquanto no UTM ela vai de 0,9996 a 1,0010
/// (até 40 cm/km de diferença). Série de Krüger até n⁴ (Karney, 2011): erro abaixo de 0,1 mm dentro de centenas de km.
/// </summary>
public sealed class LocalTransverseMercator
{
    private readonly Ellipsoid _el;
    private readonly double _lon0;          // rad
    private readonly double _e;             // excentricidade
    private readonly double _rect;          // raio retificante A
    private readonly double[] _alpha;
    private readonly double[] _beta;
    private readonly double[] _delta;
    private readonly double _n0;            // norte da origem (subtraído)

    public LocalTransverseMercator(double lat0Deg, double lon0Deg, Ellipsoid? ellipsoid = null)
    {
        _el = ellipsoid ?? Ellipsoid.Grs80;
        Lat0 = lat0Deg;
        Lon0 = lon0Deg;
        _lon0 = lon0Deg * Math.PI / 180;
        var n = _el.N;
        double n2 = n * n, n3 = n2 * n, n4 = n3 * n;
        _e = Math.Sqrt(_el.E2);
        _rect = _el.A / (1 + n) * (1 + n2 / 4 + n4 / 64);
        _alpha = new[]
        {
            n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180,
            13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440,
            61 * n3 / 240 - 103 * n4 / 140,
            49561 * n4 / 161280,
        };
        _beta = new[]
        {
            n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360,
            n2 / 48 + n3 / 15 - 437 * n4 / 1440,
            17 * n3 / 480 - 37 * n4 / 840,
            4397 * n4 / 161280,
        };
        _delta = new[]
        {
            2 * n - 2 * n2 / 3 - 2 * n3,
            7 * n2 / 3 - 8 * n3 / 5,
            56 * n3 / 15,
        };
        _n0 = 0;
        _n0 = Forward(lat0Deg, lon0Deg).N;
    }

    public double Lat0 { get; }
    public double Lon0 { get; }

    /// <summary>Latitude/longitude (graus) → leste/norte no plano local (m, sobre o elipsoide).</summary>
    public (double E, double N) Forward(double latDeg, double lonDeg)
    {
        var phi = latDeg * Math.PI / 180;
        var dl = lonDeg * Math.PI / 180 - _lon0;
        var sp = Math.Sin(phi);
        var t = Math.Sinh(Math.Atanh(sp) - _e * Math.Atanh(_e * sp));
        var xi1 = Math.Atan2(t, Math.Cos(dl));
        var eta1 = Math.Atanh(Math.Sin(dl) / Math.Sqrt(1 + t * t));
        double xi = xi1, eta = eta1;
        for (int j = 1; j <= 4; j++)
        {
            xi += _alpha[j - 1] * Math.Sin(2 * j * xi1) * Math.Cosh(2 * j * eta1);
            eta += _alpha[j - 1] * Math.Cos(2 * j * xi1) * Math.Sinh(2 * j * eta1);
        }
        return (_rect * eta, _rect * xi - _n0);
    }

    /// <summary>Leste/norte no plano local (m) → latitude/longitude (graus).</summary>
    public (double Lat, double Lon) Inverse(double e, double n)
    {
        var (lat, lon) = InverseSeries(e, n);
        // Refino: a série inversa já fica abaixo de 0,1 mm; duas correções pela direta garantem o ida e volta.
        for (int k = 0; k < 2; k++)
        {
            var (fe, fn) = Forward(lat, lon);
            var m = _el.MeridianRadius(lat);
            var nv = _el.PrimeVerticalRadius(lat) * Math.Cos(lat * Math.PI / 180);
            lat += (n - fn) / m * 180 / Math.PI;
            lon += (e - fe) / nv * 180 / Math.PI;
        }
        return (lat, lon);
    }

    private (double Lat, double Lon) InverseSeries(double e, double n)
    {
        var xi = (n + _n0) / _rect;
        var eta = e / _rect;
        double xi1 = xi, eta1 = eta;
        for (int j = 1; j <= 4; j++)
        {
            xi1 -= _beta[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            eta1 -= _beta[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }
        var chi = Math.Asin(Math.Sin(xi1) / Math.Cosh(eta1));
        var phi = chi;
        for (int j = 1; j <= 3; j++) phi += _delta[j - 1] * Math.Sin(2 * j * chi);
        var lon = _lon0 + Math.Atan2(Math.Sinh(eta1), Math.Cos(xi1));
        return (phi * 180 / Math.PI, lon * 180 / Math.PI);
    }
}
