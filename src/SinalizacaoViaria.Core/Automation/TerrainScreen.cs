using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Um Toposolid principal: nome, se tem o terreno original guardado, se o plugin o criou, as superfícies e a área terraplenada (m²).</summary>
public sealed record ToposolidLido(string Id, string Nome, bool OriginalGuardado, bool CriadoPeloPlugin, ResumoSuperficie Atual, ResumoSuperficie? Original,
    double AreaTerraplenada);

/// <summary>A superfície que um cálculo lê.</summary>
public enum SuperficieUsada
{
    /// <summary>O terreno natural: o original guardado na primeira terraplenagem ou, sem ele, o Toposolid atual.</summary>
    Natural,
    /// <summary>O Toposolid como está agora (depois da terraplenagem).</summary>
    Projetada,
    /// <summary>As vias do plugin e, fora delas, a projetada.</summary>
    Acabada,
}

/// <summary>Um cálculo do plugin, a superfície que ele lê, o detalhe e o comando em que ele está.</summary>
public sealed record UsoSuperficie(string Calculo, SuperficieUsada Superficie, string Detalhe, string Comando);

/// <summary>Projetada × natural por amostragem: passo (m), pontos amostrados, área alterada (m²) e o maior corte e aterro (m).</summary>
public sealed record DiferencaSuperficies(double Passo, int Amostras, double AreaAlterada, double CorteMaximo, double AterroMaximo);

/// <summary>Uma parte da tela do terreno: título e linhas com o nível.</summary>
public sealed record SecaoTerreno(string Titulo, IReadOnlyList<EstadoContexto> Linhas);

/// <summary>O que pode precisar de atualização quando o terreno muda: o módulo, quantas marcas, o que fazer e o comando.</summary>
public sealed record ImpactoTerreno(string Modulo, int Marcas, string Texto, string? Comando);

/// <summary>Conteúdo da tela do terreno.</summary>
public sealed record TelaTerreno(string Resumo, IReadOnlyList<SecaoTerreno> Secoes, IReadOnlyList<ToposolidLido> Toposolids,
    IReadOnlyList<UsoSuperficie> Usos, IReadOnlyList<ImpactoTerreno> Impactos, IReadOnlyList<Achado> Diagnostico, DiferencaSuperficies? Diferenca)
{
    public SecaoTerreno Secao(string titulo) => Secoes.First(s => s.Titulo == titulo);
}

/// <summary>
/// Tela do terreno e da topografia (Etapa 11; Prompt de Interface §14): de onde vem o terreno (os Toposolids do projeto), a
/// superfície existente (o natural guardado) e a projetada, a diferença entre elas, os limites e os parâmetros, o diagnóstico
/// (o mesmo V14 do Validar Modelo), qual superfície cada cálculo lê e o que pode precisar de atualização quando o terreno
/// muda. Só lê: nada aqui altera o projeto. O plugin não importa pontos nem gera curvas de nível – o Toposolid é do Revit.
/// </summary>
public static class TerrenoTela
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string fmt = "N2") => v.ToString(fmt, Pt);

    public const string Origem = "Origem dos dados";
    public const string Existente = "Superfície existente (terreno natural)";
    public const string Projetada = "Superfície projetada";
    public const string Limites = "Limites de análise e parâmetros";
    public const string Diagnostico = "Resultados e diagnóstico";

    /// <summary>
    /// A superfície de cada cálculo, como o código lê (TerrainModel.Ground = natural, Current/CurrentMesh = projetada,
    /// DrenagemDoModelo.SuperficieAcabada = acabada). Um teste confere estes comandos contra o código.
    /// </summary>
    public static IReadOnlyList<UsoSuperficie> Usos { get; } = new UsoSuperficie[]
    {
        new("Greide pelo relevo e Perfil da Via (linha do terreno, obras nos trechos que o relevo pede)", SuperficieUsada.Natural,
            "o terreno antes da terraplenagem: o greide não segue o próprio corte", "CmdPerfilVia"),
        new("Obras que acompanham o terreno (pilares, muros, taludes, emboques)", SuperficieUsada.Natural, "lido quando a obra é gerada de novo", "CmdEditar"),
        new("Terraplenagem (o Toposolid é refeito)", SuperficieUsada.Natural,
            "refeita a partir do original guardado; na primeira vez, o Toposolid atual é guardado como o original", "CmdTerraplenagem"),
        new("Quantitativos de terraplenagem (corte e aterro por estaca)", SuperficieUsada.Natural, "calculados na hora contra o original guardado", "CmdQuantitativos"),
        new("Talvegues sob o aterro das vias (drenagem)", SuperficieUsada.Natural, "o vale do terreno natural que a via represa", "CmdRedeDrenagem"),
        new("Diagnóstico do Terreno e V14 do Validar Modelo", SuperficieUsada.Projetada, "o Toposolid como está, com os eixos das vias", "CmdValidarModelo"),
        new("Pisos × terreno no Autoteste (em Configurações)", SuperficieUsada.Projetada, "a pista confere com o Toposolid depois da terraplenagem", "CmdConfiguracoes"),
        new("Recobrimento da rede de drenagem (Relatório da Rede, V13, memorial)", SuperficieUsada.Acabada,
            "as vias do plugin e, fora delas, o Toposolid como está; sem os dois, a reta entre as tampas", "CmdRedeDrenagem"),
    };

    public static string Nome(SuperficieUsada s) => s switch
    {
        SuperficieUsada.Natural => "natural (original guardado)",
        SuperficieUsada.Projetada => "projetada (Toposolid atual)",
        _ => "acabada (vias + Toposolid atual)",
    };

    /// <summary>
    /// Projetada × natural numa grade regular sobre o natural (no máximo <paramref name="maxAmostras"/> pontos por par): a área em
    /// que diferem mais que <see cref="TerrainDiagnostics.CotaDiferente"/> e o maior corte e aterro. Amostragem, não volume:
    /// os volumes são dos Quantitativos. Nulo sem nenhum par com superfície.
    /// </summary>
    public static DiferencaSuperficies? Diferenca(IEnumerable<(TerrainMesh Natural, TerrainMesh Projetada)> pares, int maxAmostras = 40000)
    {
        var (amostras, area, corte, aterro, passo) = (0, 0.0, 0.0, 0.0, 0.0);
        var algum = false;
        foreach (var (nat, proj) in pares)
        {
            if (nat.Count == 0 || proj.Count == 0 || nat.Bounds is not { } b) continue;
            algum = true;
            var (w, h) = (b.Max.X - b.Min.X, b.Max.Y - b.Min.Y);
            var s = Math.Max(0.5, Math.Sqrt(Math.Max(w * h, 1e-6) / maxAmostras));
            passo = Math.Max(passo, s);
            for (var x = b.Min.X + s / 2; x < b.Max.X; x += s)
                for (var y = b.Min.Y + s / 2; y < b.Max.Y; y += s)
                {
                    var p = new Vec2(x, y);
                    if (nat.Z(p) is not { } z0 || proj.Z(p) is not { } z1) continue;
                    amostras++;
                    var d = z1 - z0;
                    if (Math.Abs(d) <= TerrainDiagnostics.CotaDiferente) continue;
                    area += s * s;
                    if (d < 0) corte = Math.Max(corte, -d);
                    else aterro = Math.Max(aterro, d);
                }
        }
        return algum ? new DiferencaSuperficies(passo, amostras, area, corte, aterro) : null;
    }

    /// <summary>O que pode precisar de atualização quando o terreno (o Toposolid ou o levantamento) muda.</summary>
    public static List<ImpactoTerreno> Impactos(IReadOnlyCollection<MarkingDefinition> defs, RegistroTerraplenagem? registro)
    {
        var res = new List<ImpactoTerreno>();
        var moldam = EarthworkRecord.Participantes(defs).Count;
        if (moldam > 0)
        {
            var mudou = registro == null ? (int?)null : EarthworkRecord.Comparar(registro, defs).Count;
            res.Add(new("Terraplenagem", moldam,
                $"{moldam} marca(s) moldam o terreno. Edições à mão no Toposolid dentro da área terraplenada são refeitas a partir do original guardado na próxima " +
                "Terraplenagem; para adotar um novo levantamento, marque nela «Novo levantamento». " +
                (registro == null ? "Sem registro da terraplenagem aplicada." : mudou > 0 ? $"Já há {mudou} marca(s) mudada(s) desde a última aplicação." : "A última aplicação está em dia com as marcas."),
                "CmdTerraplenagem"));
            res.Add(new("Quantitativos", moldam, "Corte e aterro por estaca são recalculados na hora contra o original guardado: um novo levantamento muda os volumes.",
                "CmdQuantitativos"));
        }
        var vias = defs.OfType<RoadPavementDefinition>().ToList();
        var pelo = vias.Count(v => v.Output.Grade != null);
        if (vias.Count > 0)
            res.Add(new("Vias e greides", vias.Count,
                $"{vias.Count} via(s), {pelo} com greide gravado: o greide gravado não acompanha sozinho um terreno novo (Perfil da Via o refaz pelo relevo); " +
                "as demais seguem o plano ou o terreno quando geradas de novo. O V14 confere os eixos sobre a superfície.", "CmdPerfilVia"));
        var obras = defs.Count(d => d is ITerrainAware { FollowTerrain: true });
        if (obras > 0)
            res.Add(new("Obras", obras, $"{obras} obra(s) acompanham o terreno natural: são refeitas com ele quando geradas de novo (edição ou atualização).", "CmdEditar"));
        if (defs.Any(d => d is DrainagePipeDefinition))
            res.Add(new("Drenagem", defs.Count(d => d is DrainagePipeDefinition),
                "Recobrimento da rede (sob a superfície acabada) e talvegues sob aterro (no natural) mudam: rode o Relatório da Rede.", "CmdRedeDrenagem"));
        var lotes = defs.OfType<QuadraDefinition>().Count();
        if (lotes > 0)
            res.Add(new("Quadras e lotes", lotes, "Os lotes são subdivisões do Toposolid e acompanham a superfície do Revit; as áreas em planta não mudam.", null));
        res.Add(new("Validação", 0, "Rode o Validar Modelo para conferir o terreno (V14), a terraplenagem (V15) e a drenagem (V13) de novo.", "CmdValidarModelo"));
        return res;
    }

    private static void Superficie(List<EstadoContexto> l, ResumoSuperficie r)
    {
        l.Add(new(NivelSituacao.Informacao, $"{N(r.Triangulos, "N0")} triângulo(s), {N(r.Pontos, "N0")} ponto(s) distinto(s) em planta, {N(r.AreaPlanta, "N1")} m² em planta."));
        if (r is { CotaMinima: { } zmin, CotaMaxima: { } zmax, Min: { } a, Max: { } b })
            l.Add(new(NivelSituacao.Informacao, $"Cotas de {N(zmin)} a {N(zmax)} m (desnível de {N(zmax - zmin)} m); retângulo em planta de {N(b.X - a.X, "N1")} × {N(b.Y - a.Y, "N1")} m."));
    }

    /// <param name="toposolids">Os Toposolids principais lidos (vazio = projeto sem terreno).</param>
    /// <param name="projetada">Resumo de todos juntos como estão agora; nulo sem faces lidas.</param>
    /// <param name="natural">Resumo dos originais guardados juntos; nulo se nenhum.</param>
    /// <param name="v14">Achados do V14 (<see cref="ModelValidator.Topografia"/>).</param>
    public static TelaTerreno Montar(IReadOnlyList<ToposolidLido> toposolids, ResumoSuperficie? projetada, ResumoSuperficie? natural, DiferencaSuperficies? diferenca,
        IReadOnlyCollection<MarkingDefinition> defs, GeometricCriteria criterios, RegistroTerraplenagem? registro, IReadOnlyList<Achado> v14)
    {
        static EstadoContexto L(NivelSituacao n, string t) => new(n, t);
        var secoes = new List<SecaoTerreno>();
        var guardados = toposolids.Count(t => t.OriginalGuardado);

        // ---------------------------------------------------------------- origem
        var origem = new List<EstadoContexto>();
        if (toposolids.Count == 0)
            origem.Add(L(NivelSituacao.NaoVerificado, "Nenhum Toposolid no projeto: não há terreno. As vias ficam planas; a Terraplenagem pode criar um Toposolid plano sob elas (opções dela)."));
        foreach (var t in toposolids)
            origem.Add(L(NivelSituacao.Informacao, $"{t.Nome}: {N(t.Atual.Pontos, "N0")} ponto(s), {N(t.Atual.AreaPlanta, "N1")} m²" +
                (t.CriadoPeloPlugin ? "; criado plano pela Terraplenagem (sem levantamento)" : "") +
                (t.OriginalGuardado ? "; terreno original guardado" : "; sem original guardado (o atual é o natural)") + "."));
        origem.Add(L(NivelSituacao.Informacao, "O plugin lê o topo de todos os Toposolids principais (as subdivisões, como os lotes, ficam de fora); onde há mais de um, vale o de cima."));
        origem.Add(L(NivelSituacao.Informacao, "Levantamento, pontos e curvas de nível: ferramentas do Revit para o Toposolid. O plugin não importa pontos nem gera curvas de nível."));
        secoes.Add(new(Origem, origem));

        // ---------------------------------------------------------------- existente
        var existente = new List<EstadoContexto>();
        if (toposolids.Count == 0) existente.Add(L(NivelSituacao.NaoVerificado, "Sem Toposolid: sem terreno natural."));
        else if (guardados == 0 || natural == null)
            existente.Add(L(NivelSituacao.Informacao, "Nenhum terreno original guardado: a Terraplenagem ainda não foi aplicada, e o Toposolid atual é tratado como o natural."));
        else
        {
            existente.Add(L(NivelSituacao.Informacao, $"Guardado em {guardados} de {toposolids.Count} Toposolid(s) na primeira Terraplenagem" +
                (guardados < toposolids.Count ? "; nos outros, o atual é o natural." : ".")));
            Superficie(existente, natural);
        }
        existente.Add(L(NivelSituacao.Informacao, "Os cálculos que leem o natural estão em «Superfície de cada cálculo»."));
        secoes.Add(new(Existente, existente));

        // ---------------------------------------------------------------- projetada
        var proj = new List<EstadoContexto>();
        if (projetada == null) proj.Add(L(NivelSituacao.NaoVerificado, toposolids.Count == 0 ? "Sem Toposolid: sem superfície projetada." : "Nenhuma face de topo lida dos Toposolids."));
        else
        {
            proj.Add(L(NivelSituacao.Informacao, "O Toposolid como está agora (depois da terraplenagem), todos juntos:"));
            Superficie(proj, projetada);
        }
        if (diferenca is { } d && guardados > 0)
            proj.Add(L(NivelSituacao.Informacao, d.AreaAlterada <= 0
                ? $"Projetada igual ao natural guardado (amostragem a cada {N(d.Passo, "0.0")} m, {N(d.Amostras, "N0")} pontos)."
                : $"Projetada × natural (amostragem a cada {N(d.Passo, "0.0")} m, {N(d.Amostras, "N0")} pontos): cerca de {N(d.AreaAlterada, "N0")} m² alterados, " +
                  $"corte de até {N(d.CorteMaximo)} m e aterro de até {N(d.AterroMaximo)} m. Os volumes estão nos Quantitativos."));
        else if (toposolids.Count > 0) proj.Add(L(NivelSituacao.Informacao, "Sem original guardado: a projetada é o próprio natural."));
        secoes.Add(new(Projetada, proj));

        // ---------------------------------------------------------------- limites e parâmetros
        var lim = new List<EstadoContexto>();
        var terraplenada = toposolids.Sum(t => t.AreaTerraplenada);
        lim.Add(L(NivelSituacao.Informacao, terraplenada > 0
            ? $"Área terraplenada na última aplicação: {N(terraplenada, "N0")} m² – é ali que a próxima Terraplenagem troca os pontos do Toposolid."
            : "Nenhuma área terraplenada guardada."));
        lim.Add(L(NivelSituacao.Informacao, "Fora dos Toposolids nenhum cálculo lê terreno: o V14 acusa os eixos de vias fora da superfície."));
        lim.Add(criterios.DeclividadeMaximaTerreno is { } dec
            ? L(NivelSituacao.Informacao, $"Declividade máxima das faces: {N(dec, "0.#")} % – {criterios.FonteTexto}.")
            : L(NivelSituacao.NaoVerificado, "Declividade máxima das faces não informada: não conferida (Configurações › Limites geométricos)."));
        lim.Add(registro == null
            ? L(NivelSituacao.Informacao, "Sem registro da terraplenagem aplicada (nunca aplicada, ou por versão anterior à 2.11).")
            : L(NivelSituacao.Informacao, $"Terraplenagem aplicada em {registro.Quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}: subleito {N(registro.Subleito)} m, " +
                $"taludes de corte {N(registro.TaludeCorte)} : 1 e de aterro {N(registro.TaludeAterro)} : 1."));
        lim.Add(L(NivelSituacao.Informacao, $"Tolerâncias do diagnóstico: mesmo ponto a menos de {N(TerrainDiagnostics.MesmoPonto * 1000, "0")} mm, cota diferente acima de " +
            $"{N(TerrainDiagnostics.CotaDiferente * 100, "0")} cm, triângulo sem área abaixo de {N(TerrainDiagnostics.AreaMinima * 10000, "0")} cm²."));
        secoes.Add(new(Limites, lim));

        // ---------------------------------------------------------------- diagnóstico
        var diag = new List<EstadoContexto>();
        static NivelSituacao Nv(Severidade s) => s switch
        {
            Severidade.Erro => NivelSituacao.Erro, Severidade.Aviso => NivelSituacao.Aviso, Severidade.NaoVerificavel => NivelSituacao.NaoVerificado,
            _ => NivelSituacao.Informacao,
        };
        var problemas = v14.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        if (problemas.Count == 0) diag.Add(L(NivelSituacao.Ok, "Nenhum problema nas verificações do terreno que rodaram (V14)."));
        foreach (var a in v14.OrderBy(a => a.Severidade == Severidade.NaoVerificavel)) diag.Add(L(Nv(a.Severidade), $"{a.Mensagem} → {a.AcaoRecomendada}"));
        secoes.Add(new(Diagnostico, diag));

        int C(Severidade s) => v14.Count(a => a.Severidade == s);
        var resumo = toposolids.Count == 0 ? "Terreno: nenhum Toposolid no projeto – nada a diagnosticar."
            : $"Terreno: {toposolids.Count} Toposolid(s), original guardado em {guardados}; " + (problemas.Count == 0
                ? $"nenhum problema nas verificações que rodaram ({C(Severidade.NaoVerificavel)} não verificada(s))."
                : $"{C(Severidade.Erro)} erro(s), {C(Severidade.Aviso)} aviso(s), {C(Severidade.Info)} informação(ões), {C(Severidade.NaoVerificavel)} não verificada(s).");
        return new TelaTerreno(resumo, secoes, toposolids, Usos, Impactos(defs, registro), v14, diferenca);
    }

    /// <summary>Texto para copiar: o resumo, as partes, a superfície de cada cálculo e o que muda com o terreno.</summary>
    public static string Texto(TelaTerreno t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(t.Resumo);
        foreach (var s in t.Secoes)
        {
            sb.AppendLine();
            sb.AppendLine(s.Titulo.ToUpperInvariant());
            foreach (var l in s.Linhas) sb.AppendLine($"  [{CentralDoProjeto.Nome(l.Nivel)}] {l.Texto}");
        }
        sb.AppendLine();
        sb.AppendLine("SUPERFÍCIE DE CADA CÁLCULO");
        foreach (var u in t.Usos) sb.AppendLine($"  • {u.Calculo}: {Nome(u.Superficie)} – {u.Detalhe}.");
        sb.AppendLine();
        sb.AppendLine("SE O TERRENO MUDAR");
        foreach (var i in t.Impactos) sb.AppendLine($"  • {i.Modulo}: {i.Texto}");
        sb.AppendLine();
        sb.Append("Nada foi alterado no projeto.");
        return sb.ToString();
    }
}
