using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

public enum TipoCiclo
{
    /// <summary>Ciclofaixa de um sentido junto à pista (linha de delimitação no lado da pista).</summary>
    CiclofaixaUnidirecional,
    /// <summary>Ciclofaixa de dois sentidos (linha central amarela).</summary>
    CiclofaixaBidirecional,
    /// <summary>Ciclovia segregada (separada fisicamente – linhas de bordo finas).</summary>
    Ciclovia,
    /// <summary>Faixa de caminhada de pedestres (azul ou verde).</summary>
    FaixaCaminhada,
}

public enum LadoLinha { Direita, Esquerda, Ambos, Nenhum }

/// <summary>
/// Ciclofaixa, ciclovia ou faixa de caminhada completa ao longo do eixo da faixa: fundo colorido contínuo, linhas de
/// delimitação (contínuas por padrão), linha central, símbolos e setas espaçados e segregação física opcional.
/// </summary>
public sealed class BikeLaneSetup
{
    public TipoCiclo Type { get; set; } = TipoCiclo.CiclofaixaUnidirecional;
    public double Width { get; set; } = 1.50;
    public bool Background { get; set; } = true;
    public MarkingColor WalkColor { get; set; } = MarkingColor.Azul;
    /// <summary>Lado da linha de delimitação (em relação ao sentido do eixo).</summary>
    public LadoLinha Lines { get; set; } = LadoLinha.Esquerda;
    public bool DashedLines { get; set; }
    public double LineWidth { get; set; } = 0.20;
    /// <summary>Traço/espaço da linha central da bidirecional (m).</summary>
    public double CenterDash { get; set; } = 1.0;
    public double CenterGap { get; set; } = 1.0;
    public double SymbolSpacing { get; set; } = 30;
    public double SymbolLength { get; set; } = 1.60;
    public bool Arrows { get; set; } = true;
    public double ArrowLength { get; set; } = 1.50;
    /// <summary>Dispositivo de segregação no lado da linha (vazio = nenhum), ex.: SEG-CIC, BAL-FLEX.</summary>
    public string? Segregation { get; set; }
    public double Speed { get; set; } = 30;
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }

    public static double DefaultWidth(TipoCiclo t) => t switch
    {
        TipoCiclo.CiclofaixaBidirecional => 2.50,
        TipoCiclo.Ciclovia => 2.50,
        TipoCiclo.FaixaCaminhada => 1.50,
        _ => 1.50,
    };

    /// <summary>Posição da faixa em relação à linha de referência (centro ou borda sobre a linha).</summary>
    public Justificacao Justify { get; set; }

    public List<MarkingDefinition> Build(PathReference path, OutputSettings output)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var res = new List<MarkingDefinition>();
        var w = Math.Max(0.5, Width);
        var hw = w / 2;
        // Linha de referência no centro da faixa ou numa das bordas (faixa inteira para um dos lados).
        var shift = Justify switch { Justificacao.Esquerda => hw, Justificacao.Direita => -hw, _ => 0.0 };
        T Add<T>(T d) where T : MarkingDefinition
        {
            d.Output = output.Clone();
            d.GroupId = groupId;
            d.SetPath(RoadConnection.CopyPath(path));
            switch (d)
            {
                case LinearMarkingDefinition l: l.Offset += shift; break;
                case RepeatedMarkingDefinition r: r.Offset += shift; break;
                case DeviceMarkingDefinition dv: dv.Offset += shift; break;
            }
            res.Add(d);
            return d;
        }
        LinearMarkingDefinition Line(string code, double offset, double? width = null, string? variant = null, MarkingColor? color = null, double[]? pattern = null) =>
            Add(new LinearMarkingDefinition
            {
                Code = code, Offset = offset, WidthOverride = width, Variant = variant, ColorOverride = color, PatternOverride = pattern,
                StartSetback = StartSetback, EndSetback = EndSetback,
            });

        var walk = Type == TipoCiclo.FaixaCaminhada;
        var lw = walk ? 0.10 : Math.Clamp(LineWidth, 0.05, 0.30);
        var sides = Lines switch
        {
            LadoLinha.Direita => new[] { -1.0 },
            LadoLinha.Esquerda => new[] { 1.0 },
            LadoLinha.Ambos => new[] { -1.0, 1.0 },
            _ => Array.Empty<double>(),
        };
        if (Type == TipoCiclo.Ciclovia && Lines == LadoLinha.Esquerda) sides = new[] { -1.0, 1.0 };
        if (walk) sides = new[] { -1.0, 1.0 };
        var lineW = Type == TipoCiclo.Ciclovia ? Math.Min(lw, 0.10) : lw;

        // Fundo contínuo entre as linhas (sem sobreposição com elas).
        var innerL = sides.Contains(1.0) ? hw - lineW : hw;
        var innerR = sides.Contains(-1.0) ? hw - lineW : hw;
        if (Background)
        {
            var bw = innerL + innerR;
            var bo = (innerL - innerR) / 2;
            if (walk) Line("FCA", bo, bw, color: WalkColor);
            else Line("CIC-FD", bo, bw);
        }
        foreach (var sg in sides)
        {
            var o = sg * (hw - lineW / 2);
            if (walk) Line("FCA-BD", o, lineW);
            else Line("CIC-LD", o, lineW, DashedLines ? "Seccionada 0,20 m (1 × 1 m)" : "Contínua 0,20 m");
        }
        if (Type == TipoCiclo.CiclofaixaBidirecional)
            Line("CIC-LC", 0, 0.10, "Seccionada 0,10 m (1 × 1 m)", pattern: new[] { Math.Max(0.3, CenterDash), Math.Max(0.3, CenterGap) });

        // Símbolos e setas (bicicleta / pedestre), a partir de 5 m.
        if (SymbolSpacing > 0.5)
        {
            var start = Math.Max(5, StartSetback + 3);
            var two = Type is TipoCiclo.CiclofaixaBidirecional or TipoCiclo.Ciclovia;
            var symbol = walk ? "SPE" : "SIC";
            // O símbolo fica atravessado na faixa: nunca maior que a largura útil (entre as linhas).
            var usable = Math.Max(0.5, innerL + innerR - 0.10);
            var symLen = Math.Min(SymbolLength, walk ? w * 0.9 : Math.Max(0.6, Math.Min(SymbolLength, usable)));
            // Faixa única: símbolos centrados entre as linhas (não no eixo da faixa, que inclui a linha).
            foreach (var (off, rev) in two ? new[] { (w / 4, true), (-w / 4, false) } : new[] { ((innerL - innerR) / 2, false) })
            {
                Add(new RepeatedMarkingDefinition
                {
                    SymbolCode = symbol, Length = two ? Math.Min(symLen, w / 2 * 0.9) : symLen, Offset = off, Spacing = SymbolSpacing,
                    Reverse = rev, StartOffset = start, EndSetback = EndSetback + 3,
                });
                if (!walk && Arrows && ArrowLength > 0.1)
                    Add(new RepeatedMarkingDefinition
                    {
                        SymbolCode = "CIC-SETA", Length = ArrowLength, Offset = off, Spacing = SymbolSpacing, Reverse = rev,
                        StartOffset = start + symLen + 1.0, EndSetback = EndSetback + 3,
                    });
            }
        }
        if (!string.IsNullOrEmpty(Segregation) && !walk)
            foreach (var sg in sides.Length > 0 ? new[] { sides[0] } : new[] { 1.0 })
                Add(new DeviceMarkingDefinition { Code = Segregation!, Offset = sg * (hw + 0.25), StartSetback = StartSetback + 1, EndSetback = EndSetback + 1 });
        return res;
    }
}
