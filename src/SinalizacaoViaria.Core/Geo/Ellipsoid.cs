namespace SinalizacaoViaria.Core.Geo;

/// <summary>
/// Elipsoide de referência. O SIRGAS 2000 (referencial geodésico oficial do Brasil) usa o GRS80; o WGS84 das imagens do
/// Google e do OpenStreetMap difere dele em frações de milímetro no achatamento e em poucos centímetros na realização –
/// desprezível perto da precisão das imagens.
/// </summary>
public sealed class Ellipsoid
{
    /// <summary>GRS80 (SIRGAS 2000).</summary>
    public static readonly Ellipsoid Grs80 = new(6378137.0, 1.0 / 298.257222101);

    public Ellipsoid(double a, double f)
    {
        A = a;
        F = f;
        B = a * (1 - f);
        E2 = f * (2 - f);
        N = f / (2 - f);
    }

    /// <summary>Semieixo maior (m).</summary>
    public double A { get; }
    /// <summary>Achatamento.</summary>
    public double F { get; }
    /// <summary>Semieixo menor (m).</summary>
    public double B { get; }
    /// <summary>Primeira excentricidade ao quadrado.</summary>
    public double E2 { get; }
    /// <summary>Terceiro achatamento n = f / (2 − f).</summary>
    public double N { get; }

    /// <summary>Raio de curvatura do meridiano M(φ) (m).</summary>
    public double MeridianRadius(double latDeg)
    {
        var s = Math.Sin(latDeg * Math.PI / 180);
        return A * (1 - E2) / Math.Pow(1 - E2 * s * s, 1.5);
    }

    /// <summary>Raio de curvatura do primeiro vertical N(φ) (m).</summary>
    public double PrimeVerticalRadius(double latDeg)
    {
        var s = Math.Sin(latDeg * Math.PI / 180);
        return A / Math.Sqrt(1 - E2 * s * s);
    }

    /// <summary>Raio médio de Gauss √(M·N) (m) – usado no fator de altitude.</summary>
    public double GaussianRadius(double latDeg) => Math.Sqrt(MeridianRadius(latDeg) * PrimeVerticalRadius(latDeg));

    /// <summary>
    /// Fator de altitude R / (R + h): distância no elipsoide = distância no terreno × fator. A 800 m de altitude o terreno é
    /// ~1,25×10⁻⁴ maior que o elipsoide (12,5 cm por km).
    /// </summary>
    public double ElevationFactor(double latDeg, double altitude)
    {
        var r = GaussianRadius(latDeg);
        return r / (r + altitude);
    }
}
