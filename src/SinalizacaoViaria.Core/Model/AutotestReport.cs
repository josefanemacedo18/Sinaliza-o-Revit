using System.Globalization;
using System.Text.RegularExpressions;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Uma etapa do Autoteste: bloco, nome, situação (OK, AVISO, ERRO), duração (ms), elementos criados, erros e avisos (a primeira linha de cada).</summary>
public sealed record EtapaAutoteste(string Bloco, string Nome, string Situacao, long Ms, int Elementos, IReadOnlyList<string> Erros, IReadOnlyList<string> Avisos);

/// <summary>
/// O que o relatório de um Autoteste diz: data, duração, modo (completo ou rápido, com as etapas não executadas), versões do
/// Revit e do plugin, projeto e as etapas. Os totais são os das etapas lidas; o resumo do próprio relatório fica em
/// <see cref="ResumoDoRelatorio"/> para conferência.
/// </summary>
public sealed record ResumoAutoteste(string Arquivo, DateTime? Data, string? Duracao, string Modo, int? NaoExecutadas, string? Revit, string? Plugin,
    string? Projeto, string? ResumoDoRelatorio, IReadOnlyList<EtapaAutoteste> Etapas)
{
    public int Ok => Etapas.Count(e => e.Situacao == "OK");
    public int ComAviso => Etapas.Count(e => e.Situacao == "AVISO");
    public int ComErro => Etapas.Count(e => e.Situacao == "ERRO");
}

/// <summary>
/// Leitura do relatório do Autoteste (Etapa 12; Prompt de Interface §17.5): testes executados, aprovados, com aviso, com erro e
/// não executados, duração, versão e contexto, e as mensagens de cada etapa. São os <b>testes automatizados do plugin</b> no
/// Revit – a Central de Qualidade os mostra separados do diagnóstico do modelo (Validar Modelo). Lê o TXT gravado pelo
/// Autoteste (o mesmo que o usuário envia); linhas que não reconhece são ignoradas.
/// </summary>
public static class RelatorioAutoteste
{
    private static readonly Regex Etapa = new(@"^\[(OK|AVISO|ERRO)\] (.+?)\s+\((\d+) ms, (\d+) elemento\(s\)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex Data = new(@"^Data: (\d{2}/\d{2}/\d{4} \d{2}:\d{2})\s+Duração: ([\d:]+)", RegexOptions.Compiled);
    private static readonly Regex NaoExec = new(@"(\d+) etapa\(s\) não executadas", RegexOptions.Compiled);

    /// <summary>O relatório lido; nulo se o texto não é um relatório do Autoteste.</summary>
    public static ResumoAutoteste? Ler(string arquivo, string texto)
    {
        var linhas = texto.Replace("\r", "").TrimStart('﻿').Split('\n');
        if (linhas.Length == 0 || !linhas[0].StartsWith("SINALIZABIM – RELATÓRIO DO AUTOTESTE", StringComparison.Ordinal)) return null;
        DateTime? data = null;
        string? duracao = null, modo = null, revit = null, plugin = null, projeto = null, resumo = null;
        int? naoExec = null;
        var etapas = new List<EtapaAutoteste>();
        string bloco = "";
        var detalhes = false;
        (string Bloco, string Nome, string Situacao, long Ms, int Elementos, List<string> Erros, List<string> Avisos)? atual = null;
        void Fecha()
        {
            if (atual is { } a) etapas.Add(new EtapaAutoteste(a.Bloco, a.Nome, a.Situacao, a.Ms, a.Elementos, a.Erros, a.Avisos));
            atual = null;
        }
        foreach (var l in linhas)
        {
            if (!detalhes)
            {
                if (Data.Match(l) is { Success: true } m)
                {
                    if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) data = d;
                    duracao = m.Groups[2].Value;
                }
                else if (l.StartsWith("Modo: ", StringComparison.Ordinal))
                {
                    modo = l.Contains("COMPLETO", StringComparison.Ordinal) ? "completo" : "rápido";
                    if (NaoExec.Match(l) is { Success: true } n) naoExec = int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture);
                    else if (modo == "completo") naoExec = 0;
                }
                else if (l.StartsWith("Revit: ", StringComparison.Ordinal)) revit = l[7..].Trim();
                else if (l.StartsWith("Plugin: ", StringComparison.Ordinal)) plugin = l[8..].Split(" – ")[0].Trim();
                else if (l.StartsWith("Projeto: ", StringComparison.Ordinal)) projeto = l[9..].Split("   ")[0].Trim();
                else if (l.StartsWith("RESUMO: ", StringComparison.Ordinal)) resumo = l[8..].Trim();
                else if (l == "DETALHES") detalhes = true;
                continue;
            }
            // Fim dos detalhes: as tabelas de tempo do fim do relatório.
            if (l.StartsWith("TEMPO ", StringComparison.Ordinal) || l.StartsWith("ETAPAS MAIS LENTAS", StringComparison.Ordinal)) break;
            if (l.StartsWith("=== ", StringComparison.Ordinal) && l.EndsWith(" ===", StringComparison.Ordinal))
            {
                Fecha();
                bloco = l[4..^4];
                continue;
            }
            if (Etapa.Match(l) is { Success: true } e)
            {
                Fecha();
                atual = (bloco, e.Groups[2].Value, e.Groups[1].Value, long.Parse(e.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(e.Groups[4].Value, CultureInfo.InvariantCulture), new List<string>(), new List<string>());
                continue;
            }
            if (atual is not { } at) continue;
            var t = l.TrimStart();
            if (t.StartsWith("✖ ", StringComparison.Ordinal)) at.Erros.Add(t[2..].Trim());
            else if (t.StartsWith("⚠ ", StringComparison.Ordinal)) at.Avisos.Add(t[2..].Trim());
        }
        Fecha();
        return new ResumoAutoteste(arquivo, data, duracao, modo ?? "não informado", naoExec, revit, plugin, projeto, resumo, etapas);
    }

    /// <summary>
    /// O relatório mais recente da pasta, pelo nome autoteste-AAAAMMDD-HHMMSS.txt (sem diferença de maiúsculas); os dados
    /// (autoteste-…-dados.jsonl) ficam fora. Nulo se não há nenhum.
    /// </summary>
    public static string? MaisRecente(IEnumerable<string> arquivos) =>
        arquivos.Where(f => Path.GetFileName(f) is { } n && n.StartsWith("autoteste-", StringComparison.OrdinalIgnoreCase) &&
                            n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
}
