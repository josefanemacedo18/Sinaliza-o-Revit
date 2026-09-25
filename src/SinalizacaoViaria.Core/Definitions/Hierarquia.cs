namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Classificação funcional das vias (CTB, art. 60).</summary>
public enum HierarquiaViaria
{
    NaoDefinida,
    /// <summary>Urbana – acessos especiais com trânsito livre, sem interseções em nível.</summary>
    TransitoRapido,
    /// <summary>Urbana – interseções em nível, geralmente semaforizadas; liga regiões da cidade.</summary>
    Arterial,
    /// <summary>Urbana – coleta e distribui o trânsito entre as arteriais e as locais.</summary>
    Coletora,
    /// <summary>Urbana – acesso local ou a áreas restritas, sem semáforos.</summary>
    Local,
    /// <summary>Rural pavimentada.</summary>
    Rodovia,
    /// <summary>Rural não pavimentada.</summary>
    Estrada,
}

public static class Hierarquia
{
    public static readonly HierarquiaViaria[] Definidas =
    {
        HierarquiaViaria.TransitoRapido, HierarquiaViaria.Arterial, HierarquiaViaria.Coletora, HierarquiaViaria.Local,
        HierarquiaViaria.Rodovia, HierarquiaViaria.Estrada,
    };

    public static string Label(HierarquiaViaria? h) => h switch
    {
        HierarquiaViaria.TransitoRapido => "Via de trânsito rápido",
        HierarquiaViaria.Arterial => "Via arterial",
        HierarquiaViaria.Coletora => "Via coletora",
        HierarquiaViaria.Local => "Via local",
        HierarquiaViaria.Rodovia => "Rodovia",
        HierarquiaViaria.Estrada => "Estrada",
        _ => "Hierarquia não definida",
    };

    /// <summary>Velocidade máxima quando não sinalizada (CTB, art. 61), km/h.</summary>
    public static double DefaultSpeed(HierarquiaViaria h) => h switch
    {
        HierarquiaViaria.TransitoRapido => 80,
        HierarquiaViaria.Arterial => 60,
        HierarquiaViaria.Coletora => 40,
        HierarquiaViaria.Local => 30,
        HierarquiaViaria.Rodovia => 100,
        HierarquiaViaria.Estrada => 60,
        _ => 60,
    };

    /// <summary>Importância no sistema viário (maior = preferencial nas interseções).</summary>
    public static int Rank(HierarquiaViaria? h) => h switch
    {
        HierarquiaViaria.TransitoRapido => 6,
        HierarquiaViaria.Rodovia => 5,
        HierarquiaViaria.Arterial => 4,
        HierarquiaViaria.Coletora => 3,
        HierarquiaViaria.Estrada => 2,
        HierarquiaViaria.Local => 1,
        _ => 0,
    };

    /// <summary>Raio de esquina recomendado (m, face do meio-fio) para o cruzamento de vias destas hierarquias.</summary>
    public static double CornerRadius(HierarquiaViaria? a, HierarquiaViaria? b)
    {
        var r = Math.Max(Rank(a), Rank(b));
        return r switch { >= 5 => 15, 4 => 10, 3 => 8, 2 => 8, _ => 6 };
    }
}
