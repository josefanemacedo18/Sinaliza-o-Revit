using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Nome de cada marca nas telas que listam marcas (Central de Qualidade, origem dos quantitativos): vias e nós pela ordem no
/// projeto ("Via 2", "Interseção 1" – os mesmos rótulos das camadas do pavimento; o código das vias, PAV-ASF…, é o mesmo para
/// todas e não diz qual é) e as demais pelo tipo e pelo código ("Placa R-1").
/// </summary>
public static class NomesDasMarcas
{
    public static Dictionary<string, string> Montar(IEnumerable<MarkingDefinition> marcas)
    {
        var lista = marcas.ToList();
        var res = Quantities.PavementLayerQuantities.Rotulos(lista);
        foreach (var d in lista) res.TryAdd(d.Id, $"{d.KindName} {d.DisplayCode}");
        return res;
    }
}
