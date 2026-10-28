using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Forma da equação de chuvas intensas (curva IDF).</summary>
public enum FormaIdf
{
    /// <summary>i = K·T^a / (t + b)^c, em mm/h – Fendrich e Parigot de Souza (Manual SUDERHSA 2002, eq. 4.1 e B.1).</summary>
    Potencia,
    /// <summary>i = (t + b)^(−c) · (A + B·ln(T − d)), em mm/min – como a equação de Barretos/DAEE-SP está escrita no memorial modelo.</summary>
    Logaritmica,
}

/// <summary>
/// Equação de chuvas intensas (intensidade × duração × tempo de retorno) do local do projeto. As predefinições são as das
/// fontes enviadas pelo usuário; qualquer outra equação do mesmo formato pode ser digitada com a sua fonte.
/// </summary>
public sealed class EquacaoChuva
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public string Nome { get; set; } = "";
    public string? Fonte { get; set; }
    public FormaIdf Forma { get; set; }
    /// <summary>K (potência) ou A (logarítmica).</summary>
    public double Coeficiente { get; set; }
    /// <summary>a, expoente de T (potência) ou B, coeficiente de ln (logarítmica).</summary>
    public double CoeficienteRetorno { get; set; }
    /// <summary>b: minutos somados à duração.</summary>
    public double DuracaoSomada { get; set; }
    /// <summary>c: expoente da duração.</summary>
    public double ExpoenteDuracao { get; set; }
    /// <summary>d: anos subtraídos de T dentro do logaritmo (logarítmica).</summary>
    public double RetornoSubtraido { get; set; }

    /// <summary>Intensidade (mm/h) para a duração <paramref name="t"/> (min) e o tempo de retorno <paramref name="tr"/> (anos).</summary>
    public double Intensidade(double t, double tr)
    {
        var tb = Math.Max(1e-6, t + DuracaoSomada);
        return Forma switch
        {
            FormaIdf.Potencia => Coeficiente * Math.Pow(tr, CoeficienteRetorno) / Math.Pow(tb, ExpoenteDuracao),
            _ => tr - RetornoSubtraido > 0
                ? Math.Pow(tb, -ExpoenteDuracao) * (Coeficiente + CoeficienteRetorno * Math.Log(tr - RetornoSubtraido)) * 60
                : double.NaN,
        };
    }

    private static string F(double v) => v.ToString("0.###", Pt);

    /// <summary>A equação escrita como no memorial.</summary>
    public string Texto => Forma == FormaIdf.Potencia
        ? $"i = {F(Coeficiente)} · T^{F(CoeficienteRetorno)} / (t + {F(DuracaoSomada)})^{F(ExpoenteDuracao)}   (i em mm/h; t em min; T em anos)"
        : $"i = (t + {F(DuracaoSomada)})^−{F(ExpoenteDuracao)} × ({F(Coeficiente)} + {F(CoeficienteRetorno)} × ln(T − {F(RetornoSubtraido)}))   (i em mm/min; t em min; T em anos)";

    public EquacaoChuva Clone() => (EquacaoChuva)MemberwiseClone();

    public const string FonteManual = "Manual de Drenagem Urbana – Região Metropolitana de Curitiba (SUDERHSA, versão 1.0, dez. 2002)";
    public const string FonteMemorial = "memorial descritivo modelo enviado pelo usuário (drenagem pluvial, Miguelópolis/SP, set. 2019)";

    /// <summary>Fendrich (1989), estação Prado Velho – Curitiba (Manual SUDERHSA, eq. 4.1 e B.5).</summary>
    public static EquacaoChuva Fendrich() => new()
    {
        Nome = "Fendrich (1989) – Curitiba, estação Prado Velho", Fonte = FonteManual + ", eq. 4.1",
        Forma = FormaIdf.Potencia, Coeficiente = 3221.07, CoeficienteRetorno = 0.258, DuracaoSomada = 26, ExpoenteDuracao = 1.010,
    };

    /// <summary>Parigot de Souza (1959) – Curitiba; válida até 2 h de duração (Manual SUDERHSA, eq. B.1).</summary>
    public static EquacaoChuva ParigotDeSouza() => new()
    {
        Nome = "Parigot de Souza (1959) – Curitiba (durações até 2 h)", Fonte = FonteManual + ", eq. B.1",
        Forma = FormaIdf.Potencia, Coeficiente = 5950, CoeficienteRetorno = 0.217, DuracaoSomada = 26, ExpoenteDuracao = 1.15,
    };

    /// <summary>Barretos/SP (DAEE-SP), como escrita no item 2.2 do memorial modelo – reproduz a planilha dele.</summary>
    public static EquacaoChuva Barretos() => new()
    {
        Nome = "Barretos/SP – DAEE-SP", Fonte = FonteMemorial + ", item 2.2",
        Forma = FormaIdf.Logaritmica, Coeficiente = 19.18, CoeficienteRetorno = 5.37, DuracaoSomada = 20, ExpoenteDuracao = 0.849,
        RetornoSubtraido = 0.5,
    };

    public static IReadOnlyList<EquacaoChuva> Predefinidas() => new[] { Fendrich(), ParigotDeSouza(), Barretos() };
}

/// <summary>Como o coeficiente de escoamento C de cada trecho é obtido.</summary>
public enum MetodoCoeficiente
{
    /// <summary>C de cada área (tabela 4.1 do Manual SUDERHSA), ponderado pelas áreas acumuladas.</summary>
    Ponderado,
    /// <summary>Fórmula de Horner (memorial modelo, item 2.5): C = 0,364·log t + 0,0042·p − 0,145, com t em min e p em %.</summary>
    Horner,
}

/// <summary>
/// Parâmetros do projeto de drenagem (gravados no projeto do Revit): equação de chuva, tempo de retorno, coeficiente de
/// escoamento, rugosidade, diâmetros, limites e a identificação do memorial. Sem equação de chuva, só a geometria da rede é
/// conferida. Os valores iniciais são os do Manual SUDERHSA (2002); as predefinições trazem o conjunto de cada fonte.
/// </summary>
public sealed class ProjetoDrenagem
{
    /// <summary>Fonte dos critérios (sai no relatório e no memorial).</summary>
    public string? Fonte { get; set; }

    // ---- Identificação (memorial)
    public string? Obra { get; set; }
    public string? Municipio { get; set; }
    public string? Endereco { get; set; }
    public string? Proprietario { get; set; }
    public string? ProprietarioDocumento { get; set; }
    public string? Autor { get; set; }
    public string? Registro { get; set; }
    public string? Art { get; set; }

    // ---- Chuva e vazão
    /// <summary>Equação de chuvas intensas; nula = a vazão não é calculada.</summary>
    public EquacaoChuva? Chuva { get; set; }
    /// <summary>Tempo de retorno (anos). Manual SUDERHSA, tabela 3.1: 2 anos na microdrenagem residencial.</summary>
    public double TempoRetorno { get; set; } = 2;
    /// <summary>Tempo de concentração inicial nas cabeceiras (min). Manual SUDERHSA, item 6.3.2: usualmente 10 min.</summary>
    public double TempoInicial { get; set; } = 10;
    /// <summary>Duração mínima da chuva (min). Manual SUDERHSA, item 4.2: limitada a 10 min.</summary>
    public double DuracaoMinima { get; set; } = 10;
    public MetodoCoeficiente Coeficiente { get; set; } = MetodoCoeficiente.Ponderado;
    /// <summary>C das áreas sem valor próprio. Manual SUDERHSA, tabela 4.1: 0,95 para concreto, asfalto e telhado.</summary>
    public double CoeficientePadrao { get; set; } = 0.95;
    /// <summary>Área impermeável (%) das áreas sem valor próprio (fórmula de Horner).</summary>
    public double ImpermeabilizacaoPadrao { get; set; } = 80;

    // ---- Galerias
    /// <summary>Rugosidade de Manning das galerias. Manual SUDERHSA, exemplo 6.3: 0,013 (concreto).</summary>
    public double Rugosidade { get; set; } = 0.013;
    /// <summary>Diâmetros comerciais (m). Manual SUDERHSA, item 6.2.1.</summary>
    public List<double> Diametros { get; set; } = new() { 0.40, 0.50, 0.60, 0.80, 1.00, 1.20, 1.50, 1.80, 2.00 };
    /// <summary>Diâmetro mínimo das galerias (m). Manual SUDERHSA: 0,40 m.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DiametroMinimo { get; set; } = 0.40;
    /// <summary>Diâmetro das ligações das bocas de lobo (m). Manual SUDERHSA: 0,40 m.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DiametroLigacao { get; set; } = 0.40;
    /// <summary>Velocidade mínima a seção plena (m/s). Manual SUDERHSA: 0,75 m/s.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? VelocidadeMinima { get; set; } = 0.75;
    /// <summary>Velocidade máxima a seção plena (m/s). Manual SUDERHSA: 5,0 m/s (tubo de concreto).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? VelocidadeMaxima { get; set; } = 5.0;
    /// <summary>Recobrimento mínimo sobre a geratriz superior externa (m). Manual SUDERHSA: 1,00 m.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? RecobrimentoMinimo { get; set; } = 1.00;
    /// <summary>Declividade mínima das galerias (%); nula = não conferir.</summary>
    public double? DeclividadeMinima { get; set; }
    /// <summary>Declividade máxima das galerias (%); nula = não conferir.</summary>
    public double? DeclividadeMaxima { get; set; }
    /// <summary>Declividade mínima das ligações das bocas de lobo (%); nula = não conferir.</summary>
    public double? DeclividadeMinimaLigacao { get; set; }
    /// <summary>Espaçamento máximo entre PVs pela tabela 6.1 do Manual SUDERHSA (DAEE/CETESB, 1980).</summary>
    public bool EspacamentoPv { get; set; } = true;
    /// <summary>Diferença entre o tubo que chega e o que sai a partir da qual o PV é de queda (m). Manual SUDERHSA: 0,70 m.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? QuedaPv { get; set; } = 0.70;

    // ---- Sarjetas e bocas de lobo
    /// <summary>Altura máxima da lâmina junto à guia (m). Manual SUDERHSA: 0,10 m (só a sarjeta).</summary>
    public double LaminaMaxima { get; set; } = 0.10;
    /// <summary>Declividade transversal da via junto à sarjeta (m/m). Manual SUDERHSA: 3 %.</summary>
    public double DeclividadeTransversal { get; set; } = 0.03;
    /// <summary>Rugosidade de Manning da via. Manual SUDERHSA: 0,017.</summary>
    public double RugosidadeSarjeta { get; set; } = 0.017;
    /// <summary>Aplica os fatores de redução da capacidade das sarjetas (tabela 6.3) e das bocas de lobo (tabela 6.4).</summary>
    public bool FatoresReducao { get; set; } = true;
    /// <summary>Excesso admitido sobre a capacidade de uma boca de lobo, passado à seguinte. Manual SUDERHSA: até 30 %.</summary>
    public double ExcessoBocaDeLobo { get; set; } = 0.30;
    /// <summary>Velocidade mínima na sarjeta (m/s); nula = não conferir.</summary>
    public double? VelocidadeSarjetaMinima { get; set; }
    /// <summary>Velocidade máxima na sarjeta (m/s); nula = não conferir.</summary>
    public double? VelocidadeSarjetaMaxima { get; set; }

    // ---- Serviços (memorial)
    /// <summary>Profundidade a partir da qual a vala é escorada (m).</summary>
    public double ProfundidadeEscoramento { get; set; } = 1.25;
    /// <summary>Largura mínima da vala em diâmetros do tubo.</summary>
    public double LarguraValaDiametros { get; set; } = 2.0;

    // ---- Tubos, valas e linha piezométrica (Etapa 22)
    /// <summary>
    /// Catálogo de tubos do projeto (material, classe, DN, parede, peça, junta, n); vazio = o padrão do plugin
    /// (<see cref="CatalogoTubos.Padrao"/>: concreto ponta e bolsa, peças de 1 m, um por diâmetro comercial).
    /// </summary>
    public List<TuboCatalogo> Tubos { get; set; } = new();
    /// <summary>Espessura do berço sob o tubo (m). Padrão do plugin: 0 (sem berço) – a da especificação do projeto quando houver.</summary>
    public double EspessuraBerco { get; set; }
    /// <summary>Talude das paredes da vala (H : 1 V). Padrão do plugin: 0 (paredes verticais, escoradas acima da profundidade de escoramento).</summary>
    public double TaludeVala { get; set; }
    /// <summary>Perda localizada na entrada de cada trecho, em K·V²/2g (linha piezométrica). Padrão do plugin: 0 (sem perda localizada).</summary>
    public double PerdaPv { get; set; }

    public bool TemChuva => Chuva != null;

    public string FonteTexto => string.IsNullOrWhiteSpace(Fonte) ? "fonte não informada pelo usuário" : Fonte!.Trim();

    public ProjetoDrenagem Clone()
    {
        var c = (ProjetoDrenagem)MemberwiseClone();
        c.Chuva = Chuva?.Clone();
        c.Diametros = new List<double>(Diametros);
        c.Tubos = Tubos.Select(t => t.Clone()).ToList();
        return c;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonConfig.Options);

    public static ProjetoDrenagem FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProjetoDrenagem();
        try { return JsonSerializer.Deserialize<ProjetoDrenagem>(json, JsonConfig.Options) ?? new ProjetoDrenagem(); }
        catch (JsonException) { return new ProjetoDrenagem(); }
    }

    /// <summary>
    /// Copia os critérios de <paramref name="p"/> (predefinição) mantendo a identificação deste projeto e o que as fontes não
    /// definem: o catálogo de tubos, o berço, o talude da vala e a perda nos PVs.
    /// </summary>
    public void AplicarCriterios(ProjetoDrenagem p)
    {
        var (obra, mun, end, prop, doc, autor, reg, art) = (Obra, Municipio, Endereco, Proprietario, ProprietarioDocumento, Autor, Registro, Art);
        var (tubos, berco, talude, perda) = (Tubos, EspessuraBerco, TaludeVala, PerdaPv);
        foreach (var prop0 in typeof(ProjetoDrenagem).GetProperties().Where(x => x.CanWrite)) prop0.SetValue(this, prop0.GetValue(p.Clone()));
        (Obra, Municipio, Endereco, Proprietario, ProprietarioDocumento, Autor, Registro, Art) = (obra, mun, end, prop, doc, autor, reg, art);
        (Tubos, EspessuraBerco, TaludeVala, PerdaPv) = (tubos, berco, talude, perda);
    }

    /// <summary>Todos os critérios do Manual de Drenagem Urbana da SUDERHSA (2002), com a curva de Fendrich.</summary>
    public static ProjetoDrenagem ManualSuderhsa() => new()
    {
        Fonte = EquacaoChuva.FonteManual,
        Chuva = EquacaoChuva.Fendrich(),
    };

    /// <summary>
    /// Os critérios do memorial modelo (Miguelópolis/SP, 2019): curva de Barretos, T = 10 anos, C de Horner, DN 600 a 1200 nas
    /// galerias e 400 nas ligações, recobrimento 0,80 m, velocidades de 0,75 a 6,0 m/s, lâmina de 12 cm e sarjeta de 0,7 a
    /// 3,5 m/s. A área impermeável de 80 % e n = 0,015 reproduzem a planilha do modelo (o memorial não os escreve); a
    /// declividade transversal e a rugosidade da via, que o memorial não dá, ficam as do Manual SUDERHSA.
    /// </summary>
    public static ProjetoDrenagem MemorialModelo() => new()
    {
        Fonte = EquacaoChuva.FonteMemorial,
        Chuva = EquacaoChuva.Barretos(),
        TempoRetorno = 10,
        Coeficiente = MetodoCoeficiente.Horner,
        ImpermeabilizacaoPadrao = 80,
        Rugosidade = 0.015,
        Diametros = new() { 0.40, 0.60, 0.80, 1.00, 1.20 },
        DiametroMinimo = 0.60,
        DiametroLigacao = 0.40,
        VelocidadeMinima = 0.75,
        VelocidadeMaxima = 6.0,
        RecobrimentoMinimo = 0.80,
        DeclividadeMinimaLigacao = 1.0,
        EspacamentoPv = false,
        QuedaPv = null,
        LaminaMaxima = 0.12,
        VelocidadeSarjetaMinima = 0.7,
        VelocidadeSarjetaMaxima = 3.5,
    };
}

/// <summary>Fórmulas do dimensionamento, com a origem de cada uma.</summary>
public static class Hidraulica
{
    /// <summary>Método racional (Manual SUDERHSA, eq. 4.2): Q = 0,278·C·i·A (m³/s, A em km²) – aqui em L/s com A em ha.</summary>
    public static double Racional(double c, double i, double areaHa) => c * i * areaHa / 0.36;

    /// <summary>Kirpich (Manual SUDERHSA, eq. 4.3): tc = 57·(L³/H)^0,385, tc em min, L em km, H em m.</summary>
    public static double Kirpich(double comprimentoM, double desnivelM)
    {
        var l = comprimentoM / 1000;
        return 57 * Math.Pow(l * l * l / Math.Max(1e-6, desnivelM), 0.385);
    }

    /// <summary>Fórmula de Horner como aplicada no memorial modelo: C = 0,364·log t + 0,0042·p − 0,145 (t em min, p em %).</summary>
    public static double Horner(double t, double p) => Math.Clamp(0.364 * Math.Log10(Math.Max(1e-6, t)) + 0.0042 * p - 0.145, 0, 1);

    /// <summary>Manning a seção plena (Manual SUDERHSA, eq. 6.2 e 6.4, R = D/4): vazão (m³/s) e velocidade (m/s).</summary>
    public static (double Vazao, double Velocidade) SecaoPlena(double diametro, double declividade, double n)
    {
        if (diametro <= 0 || declividade <= 0 || n <= 0) return (0, 0);
        var area = Math.PI * diametro * diametro / 4;
        var v = Math.Pow(diametro / 4, 2.0 / 3) * Math.Sqrt(declividade) / n;
        return (area * v, v);
    }

    /// <summary>Menor diâmetro comercial (≥ mínimo) que escoa a vazão a seção plena; nulo se nenhum da lista atende.</summary>
    public static double? DiametroNecessario(double vazaoM3s, double declividade, double n, IEnumerable<double> lista, double minimo = 0)
    {
        foreach (var d in lista.Where(d => d >= minimo - 1e-9).OrderBy(d => d))
            if (SecaoPlena(d, declividade, n).Vazao >= vazaoM3s - 1e-9) return d;
        return null;
    }

    /// <summary>
    /// Escoamento de uma sarjeta com lâmina <paramref name="y"/> junto à guia (Manual SUDERHSA, item 6.4.1 e exemplo 6.1):
    /// seção triangular de largura y/Sx, perímetro = altura na guia + hipotenusa, Manning. Vazão (m³/s), área (m²).
    /// </summary>
    public static (double Vazao, double Area) Sarjeta(double y, double declividadeTransversal, double n, double declividade)
    {
        if (y <= 0 || declividadeTransversal <= 0 || declividade <= 0) return (0, 0);
        var largura = y / declividadeTransversal;
        var area = y * largura / 2;
        var perimetro = y + Math.Sqrt(y * y + largura * largura);
        return (area * Math.Pow(area / perimetro, 2.0 / 3) * Math.Sqrt(declividade) / n, area);
    }

    /// <summary>Lâmina junto à guia para escoar <paramref name="vazaoM3s"/> (inverte <see cref="Sarjeta"/>).</summary>
    public static double LaminaSarjeta(double vazaoM3s, double declividadeTransversal, double n, double declividade)
    {
        if (vazaoM3s <= 0) return 0;
        double lo = 0, hi = 0.05;
        while (Sarjeta(hi, declividadeTransversal, n, declividade).Vazao < vazaoM3s && hi < 5) hi *= 2;
        for (int k = 0; k < 60; k++)
        {
            var mid = (lo + hi) / 2;
            if (Sarjeta(mid, declividadeTransversal, n, declividade).Vazao < vazaoM3s) lo = mid; else hi = mid;
        }
        return hi;
    }

    private static readonly (double S, double F)[] TabelaSarjeta = { (0.4, 0.50), (1, 0.80), (3, 0.80), (5, 0.50), (6, 0.40), (8, 0.27), (10, 0.20) };

    /// <summary>
    /// Fator de redução da capacidade da sarjeta (Manual SUDERHSA, tabela 6.3, DAEE/CETESB 1980), interpolado linearmente
    /// entre as declividades da tabela; fora dela, o valor da ponta.
    /// </summary>
    public static double FatorSarjeta(double declividadePercent)
    {
        var t = TabelaSarjeta;
        if (declividadePercent <= t[0].S) return t[0].F;
        for (int k = 1; k < t.Length; k++)
            if (declividadePercent <= t[k].S)
                return t[k - 1].F + (t[k].F - t[k - 1].F) * (declividadePercent - t[k - 1].S) / (t[k].S - t[k - 1].S);
        return t[^1].F;
    }

    /// <summary>
    /// Boca de lobo de guia (Manual SUDERHSA): vertedor Q = 1,7·L·y^1,5 (eq. 6.5); com lâmina acima do dobro da abertura,
    /// orifício Q = 3,01·L·h^1,5·(y1/h)^0,5 com y1 = y − h/2 (eq. 6.6). Entre 1 e 2 vezes a abertura, o manual deixa a escolha
    /// ao projetista: aqui, o vertedor. Vazão em m³/s.
    /// </summary>
    public static double BocaDeGuia(double comprimento, double y, double abertura)
    {
        if (comprimento <= 0 || y <= 0) return 0;
        if (abertura > 0 && y > 2 * abertura)
            return 3.01 * comprimento * Math.Pow(abertura, 1.5) * Math.Sqrt((y - abertura / 2) / abertura);
        return 1.7 * comprimento * Math.Pow(y, 1.5);
    }

    /// <summary>
    /// Grelha (Manual SUDERHSA): vertedor Q = 1,7·P·y^1,5 até 12 cm de lâmina (P = perímetro sem o lado junto à guia);
    /// orifício Q = 2,91·A·y^0,5 a partir de 42 cm (A = área livre). Na transição, que o manual deixa ao projetista, o menor
    /// dos dois. Vazão em m³/s.
    /// </summary>
    public static double Grelha(double perimetro, double areaLivre, double y)
    {
        if (y <= 0) return 0;
        var vertedor = 1.7 * perimetro * Math.Pow(y, 1.5);
        var orificio = 2.91 * areaLivre * Math.Sqrt(y);
        if (y <= 0.12) return vertedor;
        if (y >= 0.42) return orificio;
        return Math.Min(vertedor, orificio);
    }

    /// <summary>Fração permitida da capacidade teórica da boca de lobo (Manual SUDERHSA, tabela 6.4, DAEE/CETESB 1980).</summary>
    public static double FatorBocaDeLobo(DrainageDefinition d)
    {
        var transversal = d.Bars != OrientacaoBarras.Longitudinal;
        return d.Type switch
        {
            TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla => 0.80,
            TipoDrenagem.BocaDeLoboCombinada => d.PontoBaixo ? 0.65 : transversal ? 0.55 : 0.66,
            _ => d.PontoBaixo ? 0.50 : transversal ? 0.50 : 0.60,
        };
    }

    /// <summary>Espaçamento máximo entre PVs (Manual SUDERHSA, tabela 6.1, DAEE/CETESB 1980): 120, 150 ou 180 m.</summary>
    public static double EspacamentoPv(double diametro) => diametro < 0.50 - 1e-9 ? 120 : diametro < 1.0 - 1e-9 ? 150 : 180;
}

/// <summary>Uma linha da planilha de dimensionamento das galerias (as colunas do memorial modelo e as do cálculo).</summary>
public sealed record TrechoHidraulico(string Id, string Nome, string Montante, string Jusante, bool Ligacao, double Extensao,
    double Duracao, double Intensidade, double Coeficiente, double AreaHa, double Vazao, double Diametro, double VazaoMaxima,
    double Velocidade, double TerrenoMontante, double TerrenoJusante, double Declividade, double FundoMontante, double FundoJusante,
    double? DiametroNecessario)
{
    public double ProfundidadeMontante => TerrenoMontante - FundoMontante;
    public double ProfundidadeJusante => TerrenoJusante - FundoJusante;
    /// <summary>Vazão de projeto sobre a capacidade a seção plena.</summary>
    public double Ocupacao => VazaoMaxima > 1e-9 ? Vazao / VazaoMaxima : double.PositiveInfinity;
}

/// <summary>Verificação de uma captação: a vazão da área local na sarjeta, a capacidade da sarjeta e o engolimento.</summary>
public sealed record CaptacaoHidraulica(string Id, string Nome, double Vazao, double? CapacidadeSarjeta, double? Lamina,
    double? VelocidadeSarjeta, double Engolimento, double Fator)
{
    /// <summary>Vazão que passa da captação para a seguinte (L/s).</summary>
    public double Excesso => Math.Max(0, Vazao - Engolimento);
}

/// <summary>Resultado do cálculo da rede.</summary>
public sealed class ResultadoDrenagem
{
    public List<TrechoHidraulico> Trechos { get; } = new();
    public List<CaptacaoHidraulica> Captacoes { get; } = new();
    public List<ProblemaRede> Problemas { get; } = new();
    /// <summary>Os diâmetros da planilha são os calculados (dimensionamento), não os do modelo.</summary>
    public bool Dimensionado { get; init; }
}

/// <summary>
/// Dimensionamento da rede pelo método racional (F8), segundo o Manual SUDERHSA (2002) e o memorial modelo enviados pelo
/// usuário: a vazão de cada trecho vem da área acumulada a montante, com a chuva de duração igual ao maior tempo de
/// concentração que chega ao dispositivo de montante (tempo inicial + percurso nos trechos a seção plena, eq. 6.1 e 6.3); a
/// galeria é verificada a seção plena por Manning e o menor diâmetro comercial que a escoa é indicado. As captações têm a
/// sarjeta e o engolimento verificados com a área local.
/// </summary>
public static class DrainageHydraulics
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string fmt = "0.00") => v.ToString(fmt, Pt);

    /// <summary>Tempo de concentração da área local de um dispositivo (min).</summary>
    public static double TempoLocal(DrainageDefinition d, ProjetoDrenagem p) =>
        d.TempoConcentracao is double t && t > 0 ? t
        : d.TalvegueComprimento is > 0 && d.TalvegueDesnivel is > 0 ? Hidraulica.Kirpich(d.TalvegueComprimento.Value, d.TalvegueDesnivel.Value)
        : p.TempoInicial;

    /// <param name="dimensionar">Usa o menor diâmetro comercial que atende (o tempo de percurso e a capacidade passam a ser os dele).</param>
    public static ResultadoDrenagem Calcular(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p, bool dimensionar = false)
    {
        var res = new ResultadoDrenagem { Dimensionado = dimensionar };
        if (p.Chuva is not { } chuva) return res;
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var labels = DrainageNetwork.Labels(defs);
        var names = DrainageNetwork.PipeNames(defs);
        var dev = defs.OfType<DrainageDefinition>().Where(d => !d.IsLinear).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

        // Acumulados por dispositivo: área (ha), C·A, p·A, vazão externa (L/s) e o tempo de concentração (min).
        var area = new Dictionary<string, double>();
        var ca = new Dictionary<string, double>();
        var pa = new Dictionary<string, double>();
        var ext = new Dictionary<string, double>();
        var tc = new Dictionary<string, double>();
        foreach (var d in dev.Values)
        {
            var a = Math.Max(0, d.AreaContribuicao) / 10000;
            area[d.Id] = a;
            ca[d.Id] = a * (d.CoeficienteEscoamento ?? p.CoeficientePadrao);
            pa[d.Id] = a * (d.Impermeabilizacao ?? p.ImpermeabilizacaoPadrao);
            ext[d.Id] = Math.Max(0, d.VazaoExterna);
            if (a > 0 || d.VazaoExterna > 0) tc[d.Id] = TempoLocal(d, p);
        }

        var saidas = defs.OfType<DrainagePipeDefinition>().Where(x => x.FromId != x.ToId).GroupBy(x => x.FromId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var pipe in DrainageNetwork.PipeOrder(defs))
        {
            if (pipe.FromId == pipe.ToId || DrainageNetwork.Ends(pipe, id => byId.GetValueOrDefault(id)) is not { } e) continue;
            var nome = names[pipe.Id];
            var u = pipe.FromId;
            var ligacao = dev.TryGetValue(u, out var du) && du.IsInlet;
            var t = Math.Max(p.DuracaoMinima, tc.TryGetValue(u, out var tu) ? tu : p.TempoInicial);
            var i = chuva.Intensidade(t, p.TempoRetorno);
            var a = area.GetValueOrDefault(u);
            var c = a <= 1e-12 ? 0 : p.Coeficiente == MetodoCoeficiente.Horner ? Hidraulica.Horner(t, pa[u] / a) : ca[u] / a;
            var q = (a > 0 && !double.IsNaN(i) ? Hidraulica.Racional(c, i, a) : 0) + ext.GetValueOrDefault(u);
            var len = e.A.DistanceTo(e.B);
            var s = len > 1e-9 ? (e.InvertA - e.InvertB) / len : 0;
            var dmin = (ligacao ? p.DiametroLigacao : p.DiametroMinimo) ?? 0;
            // Rugosidade: a do tubo do catálogo, quando informada; senão a do projeto.
            var rug = CatalogoTubos.Do(pipe, p).Tubo.Rugosidade ?? p.Rugosidade;
            var dNec = s > 0 ? Hidraulica.DiametroNecessario(q / 1000, s, rug, p.Diametros, dmin) : null;
            var diam = dimensionar && dNec is { } dn ? dn : pipe.Diameter;
            var (qmax, v) = Hidraulica.SecaoPlena(diam, s, rug);
            var (ma, ju) = (labels.GetValueOrDefault(pipe.FromId, e.CodeA), labels.GetValueOrDefault(pipe.ToId, e.CodeB));
            res.Trechos.Add(new TrechoHidraulico(pipe.Id, nome, ma, ju, ligacao, len, t, i, c, a, q, diam, qmax * 1000, v, e.RimA, e.RimB, s,
                e.InvertA, e.InvertB, dNec));

            var id = $"{nome} ({ma} → {ju})";
            var ids = new[] { pipe.Id };
            if (saidas.GetValueOrDefault(u) > 1)
                res.Problemas.Add(new(id, "vazão", $"{id}: o dispositivo {ma} tem mais de uma saída – a vazão total foi atribuída a cada uma (a divisão não é calculada).", false, ids));
            if (s > 0)
            {
                if (q > qmax * 1000 + 1e-6)
                    res.Problemas.Add(new(id, "capacidade", $"{id}: vazão de projeto {N(q)} L/s acima da capacidade a seção plena {N(qmax * 1000)} L/s " +
                        $"(+{N((q / Math.Max(1e-9, qmax * 1000) - 1) * 100, "0.0")} %) com Ø {N(diam * 1000, "0")} mm" +
                        (dNec is { } d2 ? $" – o menor diâmetro da lista que atende é {N(d2 * 1000, "0")} mm." : " – nenhum diâmetro da lista atende."), true, ids));
                if (p.VelocidadeMinima is { } vmin && v < vmin - 1e-9)
                    res.Problemas.Add(new(id, "velocidade", $"{id}: velocidade a seção plena {N(v)} m/s abaixo do mínimo de {N(vmin)} m/s (autolimpeza).", true, ids));
                if (p.VelocidadeMaxima is { } vmax && v > vmax + 1e-9)
                    res.Problemas.Add(new(id, "velocidade", $"{id}: velocidade a seção plena {N(v)} m/s acima do máximo de {N(vmax)} m/s.", true, ids));
            }

            // Acumula no dispositivo de jusante; o tempo de percurso usa a velocidade a seção plena (como no memorial modelo).
            var w = pipe.ToId;
            var tw = t + (v > 1e-9 ? len / v / 60 : 0);
            area[w] = area.GetValueOrDefault(w) + a;
            ca[w] = ca.GetValueOrDefault(w) + ca.GetValueOrDefault(u);
            pa[w] = pa.GetValueOrDefault(w) + pa.GetValueOrDefault(u);
            ext[w] = ext.GetValueOrDefault(w) + ext.GetValueOrDefault(u);
            tc[w] = Math.Max(tc.TryGetValue(w, out var t0) ? t0 : 0, tw);
        }

        foreach (var d in dev.Values.Where(d => d.IsInlet && d.AreaContribuicao > 0)) res.Captacoes.Add(Captacao(d, p, chuva, labels, res.Problemas));
        return res;
    }

    /// <summary>Sarjeta e engolimento de uma captação com a vazão da sua área local.</summary>
    private static CaptacaoHidraulica Captacao(DrainageDefinition d, ProjetoDrenagem p, EquacaoChuva chuva, Dictionary<string, string> labels,
        List<ProblemaRede> problemas)
    {
        var nome = labels.GetValueOrDefault(d.Id, d.DisplayCode);
        var a = d.AreaContribuicao / 10000;
        var t = Math.Max(p.DuracaoMinima, TempoLocal(d, p));
        var i = chuva.Intensidade(t, p.TempoRetorno);
        var c = p.Coeficiente == MetodoCoeficiente.Horner ? Hidraulica.Horner(t, d.Impermeabilizacao ?? p.ImpermeabilizacaoPadrao)
            : d.CoeficienteEscoamento ?? p.CoeficientePadrao;
        var q = double.IsNaN(i) ? 0 : Hidraulica.Racional(c, i, a);
        var ids = new[] { d.Id };

        double? cap = null, lamina = null, vel = null;
        if (d.DeclividadeSarjeta is double sl && sl > 0 && d.Type != TipoDrenagem.GrelhaQuadrada)
        {
            var s = sl / 100;
            var f = p.FatoresReducao ? Hidraulica.FatorSarjeta(sl) : 1;
            cap = Hidraulica.Sarjeta(p.LaminaMaxima, p.DeclividadeTransversal, p.RugosidadeSarjeta, s).Vazao * 1000 * f;
            var y = Hidraulica.LaminaSarjeta(q / 1000, p.DeclividadeTransversal, p.RugosidadeSarjeta, s);
            lamina = y;
            var area = Hidraulica.Sarjeta(y, p.DeclividadeTransversal, p.RugosidadeSarjeta, s).Area;
            vel = area > 1e-12 ? q / 1000 / area : 0;
            if (q > cap + 1e-6)
                problemas.Add(new(nome, "sarjeta", $"{nome}: vazão na sarjeta {N(q)} L/s acima da capacidade {N(cap.Value)} L/s (lâmina de {N(p.LaminaMaxima * 100, "0")} cm" +
                    (p.FatoresReducao ? $", fator {N(f)}" : "") + ") – a captação precisa vir antes ou a área precisa ser dividida.", true, ids));
            if (q > 0 && p.VelocidadeSarjetaMinima is { } vmin && vel < vmin - 1e-9)
                problemas.Add(new(nome, "sarjeta", $"{nome}: velocidade na sarjeta {N(vel.Value)} m/s abaixo de {N(vmin)} m/s.", true, ids));
            if (p.VelocidadeSarjetaMaxima is { } vmax && vel > vmax + 1e-9)
                problemas.Add(new(nome, "sarjeta", $"{nome}: velocidade na sarjeta {N(vel.Value)} m/s acima de {N(vmax)} m/s.", true, ids));
        }

        // Engolimento com a lâmina de projeto (a da sarjeta na captação, limitada à máxima).
        var yb = Math.Min(p.LaminaMaxima, lamina ?? p.LaminaMaxima);
        var guia = d.Type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboCombinada;
        var grelha = d.Type is not (TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla);
        var mod = Math.Max(1, d.Modules);
        double eng = 0;
        if (guia)
            eng += Hidraulica.BocaDeGuia(d.OpeningLength * mod * (d.Type == TipoDrenagem.BocaDeLoboDupla ? 2 : 1), yb, d.OpeningHeight);
        if (grelha)
        {
            var (gl, gw) = (d.GrateLength * mod, d.GrateWidth);
            var perimetro = d.Type == TipoDrenagem.GrelhaQuadrada ? 2 * (gl + gw) : gl + 2 * gw;
            var livre = gl * gw * d.BarGap / Math.Max(1e-6, d.BarGap + d.BarWidth);
            eng += Hidraulica.Grelha(perimetro, livre, yb);
        }
        var fator = p.FatoresReducao ? Hidraulica.FatorBocaDeLobo(d) : 1;
        eng *= 1000 * fator;
        if (q > eng * (1 + p.ExcessoBocaDeLobo) + 1e-6)
            problemas.Add(new(nome, "engolimento", $"{nome}: chegam {N(q)} L/s e a captação engole {N(eng)} L/s (lâmina {N(yb * 100, "0")} cm" +
                (p.FatoresReducao ? $", fator {N(fator)}" : "") + $") – o excesso de {N(q - eng)} L/s passa de {N(p.ExcessoBocaDeLobo * 100, "0")} % do engolido.", true, ids));
        return new CaptacaoHidraulica(d.Id, nome, q, cap, lamina, vel, eng, fator);
    }

    /// <summary>CSV da planilha (as colunas do memorial modelo e as do cálculo) e das captações.</summary>
    public static string Csv(ResultadoDrenagem r, ProjetoDrenagem p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PLANILHA DE DIMENSIONAMENTO DE GALERIAS – método racional");
        sb.AppendLine($"Critérios: {p.FonteTexto.Replace(';', ',')}");
        if (p.Chuva is { } ch)
            sb.AppendLine($"Chuva: {ch.Nome.Replace(';', ',')} – {ch.Texto.Replace(';', ',')}; T = {N(p.TempoRetorno, "0.#")} anos; " +
                          $"C: {(p.Coeficiente == MetodoCoeficiente.Horner ? "Horner" : "ponderado pelas áreas")}; n = {N(p.Rugosidade, "0.000")}");
        sb.AppendLine(r.Dimensionado ? "Diâmetros: os calculados (menor diâmetro comercial que escoa a vazão a seção plena)." : "Diâmetros: os do modelo.");
        sb.AppendLine("Trecho;Montante;Jusante;Tipo;Extensão (m);t (min);i (mm/h);C;Área (ha);Vazão (L/s);Diâm. (mm);Vazão máxima (L/s);" +
                      "Cota terreno M (m);Cota terreno J (m);Declividade (m/m);Cota galeria M (m);Cota galeria J (m);Prof. M (m);Prof. J (m);Veloc. (m/s);Diâm. necessário (mm)");
        foreach (var t in r.Trechos)
            sb.AppendLine(string.Join(";", t.Nome, t.Montante, t.Jusante, t.Ligacao ? "ligação" : "galeria", N(t.Extensao), N(t.Duracao), N(t.Intensidade, "0.000"),
                N(t.Coeficiente, "0.000"), N(t.AreaHa, "0.0000"), N(t.Vazao), N(t.Diametro * 1000, "0"), N(t.VazaoMaxima), N(t.TerrenoMontante),
                N(t.TerrenoJusante), N(t.Declividade, "0.0000"), N(t.FundoMontante), N(t.FundoJusante), N(t.ProfundidadeMontante),
                N(t.ProfundidadeJusante), N(t.Velocidade), t.DiametroNecessario is { } dn ? N(dn * 1000, "0") : "nenhum da lista"));
        if (r.Captacoes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CAPTAÇÕES (área local de cada boca de lobo ou grelha)");
            sb.AppendLine("Dispositivo;Vazão na sarjeta (L/s);Capacidade da sarjeta (L/s);Lâmina (cm);Velocidade na sarjeta (m/s);Engolimento (L/s);Fator de redução;Excesso (L/s)");
            foreach (var c in r.Captacoes)
                sb.AppendLine(string.Join(";", c.Nome, N(c.Vazao), c.CapacidadeSarjeta is { } cs ? N(cs) : "não verificada", c.Lamina is { } y ? N(y * 100, "0.0") : "",
                    c.VelocidadeSarjeta is { } vs ? N(vs) : "", N(c.Engolimento), N(c.Fator), N(c.Excesso)));
        }
        sb.AppendLine("Vazão: Q = 0,278·C·i·A (método racional); galerias a seção plena por Manning; tempo de percurso com a velocidade a seção plena.");
        return sb.ToString();
    }
}
