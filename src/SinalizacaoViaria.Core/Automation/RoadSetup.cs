using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Tratamento do eixo entre os sentidos de uma via de mão dupla.</summary>
public enum CenterTreatment
{
    Nenhum,
    /// <summary>Linha simples contínua (LFO-1).</summary>
    LFO1,
    /// <summary>Linha simples seccionada (LFO-2).</summary>
    LFO2,
    /// <summary>Linha dupla contínua (LFO-3).</summary>
    LFO3,
    /// <summary>Linha contínua/seccionada (LFO-4).</summary>
    LFO4,
    /// <summary>Canteiro central: linhas de bordo nos dois lados do canteiro.</summary>
    Canteiro,
}

/// <summary>
/// Seção transversal de uma via para geração automática da sinalização longitudinal
/// (equivalente ao "sinalizar alinhamento" dos programas de rodovias).
/// </summary>
public sealed class RoadSetup
{
    /// <summary>Mão dupla (eixo = divisão de sentidos) ou mão única (eixo = centro da pista).</summary>
    public bool TwoWay { get; set; } = true;

    /// <summary>Larguras das faixas do lado direito do eixo, do eixo para fora (m).</summary>
    public List<double> RightLanes { get; set; } = new() { 3.50 };

    /// <summary>Larguras das faixas do lado esquerdo do eixo, do eixo para fora (m). Em mão única, ignorado.</summary>
    public List<double> LeftLanes { get; set; } = new() { 3.50 };

    /// <summary>Em mão única: larguras de todas as faixas, da esquerda para a direita.</summary>
    public List<double> OneWayLanes { get; set; } = new() { 3.50, 3.50 };

    public CenterTreatment Center { get; set; } = CenterTreatment.LFO2;

    /// <summary>Largura do canteiro central (m), quando <see cref="CenterTreatment.Canteiro"/>.</summary>
    public double MedianWidth { get; set; } = 2.0;

    /// <summary>Código das linhas entre faixas de mesmo sentido.</summary>
    public string LaneDividerCode { get; set; } = "LMS-2";

    public bool EdgeLines { get; set; } = true;
    public string EdgeCode { get; set; } = "LBO";

    /// <summary>Afastamento do eixo da linha de bordo para dentro da pista (m).</summary>
    public double EdgeInset { get; set; } = 0.10;

    /// <summary>Velocidade regulamentada (km/h) – escolhe a variante de cada linha.</summary>
    public double Speed { get; set; } = 60;

    public double StartSetback { get; set; }
    public double EndSetback { get; set; }

    /// <summary>Tachas sobre o eixo central.</summary>
    public string? CenterStudsCode { get; set; }
    public string? CenterStudsVariant { get; set; }

    public double TotalWidth => TwoWay
        ? RightLanes.Sum() + LeftLanes.Sum() + (Center == CenterTreatment.Canteiro ? MedianWidth : 0)
        : OneWayLanes.Sum();

    /// <summary>Gera as definições lineares (todas associadas ao mesmo caminho).</summary>
    public List<LinearMarkingDefinition> Build(PathReference path, OutputSettings output)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var res = new List<LinearMarkingDefinition>();

        LinearMarkingDefinition Line(string code, double offset, bool invert = false, string? variant = null) => new()
        {
            Code = code,
            Speed = variant == null ? Speed : null,
            Variant = variant,
            Offset = offset,
            InvertSides = invert,
            PathRef = Clone(path),
            StartSetback = StartSetback,
            EndSetback = EndSetback,
            Output = output.Clone(),
            GroupId = groupId,
        };

        if (TwoWay)
        {
            var half = Center == CenterTreatment.Canteiro ? MedianWidth / 2 : 0;
            switch (Center)
            {
                case CenterTreatment.LFO1: res.Add(Line("LFO-1", 0)); break;
                case CenterTreatment.LFO2: res.Add(Line("LFO-2", 0)); break;
                case CenterTreatment.LFO3: res.Add(Line("LFO-3", 0)); break;
                case CenterTreatment.LFO4: res.Add(Line("LFO-4", 0)); break;
                case CenterTreatment.Canteiro:
                    if (EdgeLines)
                    {
                        res.Add(Line(EdgeCode, half + EdgeInset));
                        res.Add(Line(EdgeCode, -(half + EdgeInset)));
                    }
                    break;
            }
            // Lado direito (offset negativo)
            double acc = half;
            for (int i = 0; i < RightLanes.Count; i++)
            {
                acc += RightLanes[i];
                if (i < RightLanes.Count - 1) res.Add(Line(LaneDividerCode, -acc));
                else if (EdgeLines) res.Add(Line(EdgeCode, -(acc - EdgeInset)));
            }
            acc = half;
            for (int i = 0; i < LeftLanes.Count; i++)
            {
                acc += LeftLanes[i];
                if (i < LeftLanes.Count - 1) res.Add(Line(LaneDividerCode, acc));
                else if (EdgeLines) res.Add(Line(EdgeCode, acc - EdgeInset));
            }
            if (!string.IsNullOrWhiteSpace(CenterStudsCode) && Center != CenterTreatment.Canteiro && Center != CenterTreatment.Nenhum)
                res.Add(Line(CenterStudsCode!, 0, variant: CenterStudsVariant ?? ""));
        }
        else
        {
            var total = OneWayLanes.Sum();
            double left = total / 2;
            double pos = left;
            if (EdgeLines) res.Add(Line(EdgeCode, left - EdgeInset));
            for (int i = 0; i < OneWayLanes.Count; i++)
            {
                pos -= OneWayLanes[i];
                if (i < OneWayLanes.Count - 1) res.Add(Line(LaneDividerCode, pos));
            }
            if (EdgeLines) res.Add(Line(EdgeCode, -left + EdgeInset));
        }
        foreach (var r in res.Where(r => r.Variant == "")) r.Variant = null;
        return res;
    }

    private static PathReference Clone(PathReference p) => new()
    {
        ElementIds = new List<string>(p.ElementIds),
        Points = new List<Geometry.Vec2>(p.Points),
        Z = p.Z,
        Closed = p.Closed,
    };

    /// <summary>Lê uma lista de larguras no formato "3,50; 3,30" ou "3.5 3.3".</summary>
    public static List<double> ParseWidths(string text)
    {
        var res = new List<double>();
        foreach (var tok in text.Split(new[] { ';', ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim().Replace(',', '.');
            if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                res.Add(v);
        }
        return res;
    }
}
