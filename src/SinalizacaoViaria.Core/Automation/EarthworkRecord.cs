using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Registro da última terraplenagem aplicada (Prompt Master §9 e §14; Motores §8): quando, com que versão, o subleito e os
/// taludes padrão das opções e a assinatura de cada marca que moldou o terreno. Serve para dizer quando a terraplenagem ficou
/// desatualizada (V15) e para conferir o fundo da estrutura sob a pista (K19).
/// </summary>
public sealed class RegistroTerraplenagem
{
    public DateTime Quando { get; set; }
    public string? Versao { get; set; }
    /// <summary>Profundidade do subleito das opções (m) – vale para as vias sem estrutura do pavimento informada.</summary>
    public double Subleito { get; set; }
    public double TaludeCorte { get; set; }
    public double TaludeAterro { get; set; }
    /// <summary>Id → assinatura (SHA-256 do JSON da definição) de cada marca que moldou o terreno.</summary>
    public Dictionary<string, string> Assinaturas { get; set; } = new();
    /// <summary>Id → nome mostrado (para dizer o que foi apagado).</summary>
    public Dictionary<string, string> Nomes { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Lê o registro; texto vazio ou ilegível = sem registro.</summary>
    public static RegistroTerraplenagem? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RegistroTerraplenagem>(json); }
        catch (JsonException) { return null; }
    }
}

public enum SituacaoTerraplenagem
{
    /// <summary>A marca mudou depois da terraplenagem.</summary>
    Alterada,
    /// <summary>A marca moldou o terreno e foi apagada: o terreno ainda tem a terraplenagem dela.</summary>
    Removida,
    /// <summary>A marca existe, mas deixou de moldar o terreno (ex.: "ajustar o terreno" desligado): ele ainda tem a terraplenagem dela.</summary>
    Retirada,
    /// <summary>A marca molda o terreno, mas não estava na terraplenagem aplicada.</summary>
    Nova,
}

public sealed record MudancaTerraplenagem(string Id, string Nome, SituacaoTerraplenagem Situacao);

/// <summary>Quem molda o terreno, a assinatura de cada um e a comparação com a terraplenagem registrada.</summary>
public static class EarthworkRecord
{
    /// <summary>Vias (pavimentos) ligadas a um nó: interseção, rotatória ou balão.</summary>
    public static IEnumerable<string> NodeRoads(MarkingDefinition d) => (d switch
    {
        IntersectionDefinition it => it.RoadIds.Cast<string?>(),
        RoundaboutDefinition rb => rb.Legs.Select(l => l.RoadId),
        CulDeSacDefinition c => new[] { c.RoadId },
        _ => Enumerable.Empty<string?>(),
    }).Where(x => x != null).Cast<string>();

    /// <summary>
    /// Marcas que moldam o terreno: obras que ajustam o terreno, vias com greide que ajusta o terreno, os nós ligados a uma
    /// delas e as caixas de drenagem (escavação). É a mesma escolha da Terraplenagem.
    /// </summary>
    public static List<MarkingDefinition> Participantes(IReadOnlyCollection<MarkingDefinition> all)
    {
        var graded = all.OfType<RoadPavementDefinition>().Where(r => r.Output.Grade is { AdjustTerrain: true }).Select(r => r.Id).ToHashSet();
        return all.Where(d => d switch
        {
            ITerrainAware t => t.AdjustTerrain,
            RoadPavementDefinition r => graded.Contains(r.Id),
            IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition => NodeRoads(d).Any(graded.Contains),
            DrainageDefinition => true,
            _ => false,
        }).ToList();
    }

    /// <summary>
    /// Assinatura da definição: SHA-256 do JSON dela. Qualquer mudança na marca muda a assinatura, menos a linha do terreno
    /// natural guardada pelas obras (<see cref="ITerrainAware.GroundLine"/>), que é leitura do terreno e não escolha do projeto.
    /// </summary>
    public static string Assinatura(MarkingDefinition d)
    {
        var json = d.ToJson();
        if (d is ITerrainAware { GroundLine: not null } && MarkingDefinition.FromJson(json) is ITerrainAware copia)
        {
            copia.GroundLine = null;
            json = ((MarkingDefinition)copia).ToJson();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static RegistroTerraplenagem Gravar(IEnumerable<MarkingDefinition> participantes, double subleito, double taludeCorte, double taludeAterro,
        DateTime quando, string? versao)
    {
        var r = new RegistroTerraplenagem { Quando = quando, Versao = versao, Subleito = subleito, TaludeCorte = taludeCorte, TaludeAterro = taludeAterro };
        foreach (var d in participantes)
        {
            r.Assinaturas[d.Id] = Assinatura(d);
            r.Nomes[d.Id] = d.DisplayCode;
        }
        return r;
    }

    /// <summary>O que mudou desde a terraplenagem registrada, entre as marcas que moldam o terreno agora e as que moldavam.</summary>
    public static List<MudancaTerraplenagem> Comparar(RegistroTerraplenagem registro, IReadOnlyCollection<MarkingDefinition> all)
    {
        var res = new List<MudancaTerraplenagem>();
        var atuais = Participantes(all).ToDictionary(d => d.Id);
        foreach (var (id, d) in atuais)
        {
            if (!registro.Assinaturas.TryGetValue(id, out var antes)) res.Add(new(id, d.DisplayCode, SituacaoTerraplenagem.Nova));
            else if (antes != Assinatura(d)) res.Add(new(id, d.DisplayCode, SituacaoTerraplenagem.Alterada));
        }
        var existem = all.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var id in registro.Assinaturas.Keys.Where(id => !atuais.ContainsKey(id)))
            res.Add(existem.TryGetValue(id, out var d)
                ? new(id, d.DisplayCode, SituacaoTerraplenagem.Retirada)
                : new(id, registro.Nomes.GetValueOrDefault(id, id), SituacaoTerraplenagem.Removida));
        return res;
    }

    /// <summary>Profundidade sob o topo da pista até o fundo da estrutura de uma via (m): a estrutura dela ou o subleito registrado.</summary>
    public static double Profundidade(RoadPavementDefinition via, double subleito) => via.ProfundidadeEstrutura ?? subleito;

    /// <summary>Profundidade sob um nó (interseção, rotatória, balão): a mais funda entre as vias ligadas a ele.</summary>
    public static double ProfundidadeDoNo(MarkingDefinition no, IEnumerable<MarkingDefinition> all, double subleito)
    {
        var ids = NodeRoads(no).ToHashSet();
        return EstruturaPavimento.ProfundidadeNo(all.OfType<RoadPavementDefinition>().Where(r => ids.Contains(r.Id)), subleito);
    }
}
