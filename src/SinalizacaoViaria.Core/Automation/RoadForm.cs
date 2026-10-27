using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// O que a janela da via mostra além dos campos da seção (Etapa 11; Prompt de Interface §10, §11): perfil, pavimentação,
/// terraplenagem, drenagem, validação e resumo da operação – cada linha com o nível (a tela escreve o nível, não só a cor).
/// </summary>
public sealed record ContextoFormularioVia(IReadOnlyList<EstadoContexto> Perfil, IReadOnlyList<EstadoContexto> Pavimentacao,
    IReadOnlyList<EstadoContexto> Terraplenagem, IReadOnlyList<EstadoContexto> Drenagem, IReadOnlyList<EstadoContexto> Validacao,
    IReadOnlyList<EstadoContexto> Resumo);

/// <summary>
/// Conteúdo das seções da janela da via (Etapa 11; Prompt de Interface §10): para a via sendo editada, o que ela tem e o que a
/// edição pode afetar (conexões, dependentes, terraplenagem, captações lidas dela); para a via nova, o que acontece ao aplicar.
/// Só o que se sabe: o traçado da via nova é indicado depois de aplicar, então a conferência dele fica para o Validar Modelo;
/// a verificação geométrica da drenagem não é dimensionamento hidráulico; estimativa não aparece como resultado.
/// </summary>
public static class FormularioVia
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string M(double v, string fmt = "0.00") => v.ToString(fmt, Pt);
    private static string Quando(DateTime t) => t.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <param name="via">Via sendo editada; nula = via nova.</param>
    /// <param name="all">Definições do projeto (as conexões, os dependentes e as captações da via saem daqui).</param>
    /// <param name="registro">Registro da terraplenagem aplicada.</param>
    /// <param name="criterios">Limites geométricos informados (V12); vazios = traçado e greide não conferidos.</param>
    /// <param name="temToposolid">O projeto tem Toposolid (a via nova pode moldar o terreno).</param>
    public static ContextoFormularioVia Contexto(RoadPavementDefinition? via, IReadOnlyCollection<MarkingDefinition> all, RegistroTerraplenagem? registro,
        GeometricCriteria? criterios, bool temToposolid)
    {
        static EstadoContexto L(NivelSituacao n, string t) => new(n, t);
        var perfil = new List<EstadoContexto>();
        var pav = new List<EstadoContexto>();
        var terra = new List<EstadoContexto>();
        var dren = new List<EstadoContexto>();
        var valid = new List<EstadoContexto>();
        var resumo = new List<EstadoContexto>();

        if (via == null)
        {
            perfil.Add(L(NivelSituacao.Informacao, temToposolid
                ? "O greide da via nova sai do relevo escolhido abaixo (acompanhar o terreno, suavizado ou plano). Depois, Perfil da Via ajusta o greide, com curvas verticais e obras onde o relevo pede."
                : "Sem Toposolid no projeto: a via nova fica plana. Com o terreno, o greide pode acompanhá-lo (Perfil da Via)."));
            perfil.Add(L(NivelSituacao.Informacao, "Estaqueamento: o padrão (estaca 0 no início do eixo, a cada 20 m); estaca inicial e equações no Perfil da Via, depois de criar."));
            pav.Add(L(NivelSituacao.Informacao, "O revestimento e a espessura ficam abaixo. A estrutura em camadas é informada depois de criar a via (Pavimentação › Estrutura do Pavimento) – o plugin não dimensiona."));
            terra.Add(L(NivelSituacao.Informacao, temToposolid
                ? "Com Toposolid sob a via, o terreno é cortado e aterrado ao criar, conforme o relevo e os taludes escolhidos no Perfil."
                : "Sem Toposolid no projeto: nada a terraplenar."));
            terra.Add(L(NivelSituacao.Informacao, "Os volumes saem depois de criar: Quantitativos (malha) e a aba Terraplenagem por estaca."));
            dren.Add(L(NivelSituacao.Informacao, "A via nova não tem dispositivos de drenagem. Depois de criar: Drenagem › Dispositivos e Ler do Modelo (área e declividade pela via)."));
            valid.Add(L(NivelSituacao.NaoVerificado, "O traçado é indicado depois de aplicar (desenho, linhas ou pisos): a conferência do traçado e do greide contra os limites informados (V12) fica para o Validar Modelo."));
            resumo.Add(L(NivelSituacao.Informacao, "Ao aplicar: você indica o eixo; o plugin cria a pista, os elementos de cada lado do eixo e a sinalização da seção, e liga a via às existentes conforme a Conexão escolhida."));
            resumo.Add(L(NivelSituacao.Informacao, "Tudo numa operação desfazível (Desfazer do Revit)."));
            return new(perfil, pav, terra, dren, valid, resumo);
        }

        // ---------------------------------------------------------------- via existente
        if (via.Output.Grade is { } g)
            perfil.Add(L(NivelSituacao.Informacao, $"Greide com {g.Points.Count} PIV(s), curva vertical padrão de {M(g.DefaultCurve, "0.#")} m; " +
                (g.AdjustTerrain ? "molda o terreno." : "não molda o terreno.") + " A edição da seção mantém o greide – para mudá-lo, Perfil da Via."));
        else perfil.Add(L(NivelSituacao.Informacao, "Sem greide: a via segue o plano ou o terreno. Perfil da Via define o greide."));
        var e = via.Estaqueamento;
        perfil.Add(L(NivelSituacao.Informacao, e == null || e.EhPadrao ? "Estaqueamento padrão (estaca 0 no início do eixo, a cada 20 m)."
            : $"Estaqueamento próprio: início em {M(e.Inicio)} m, intervalo de {M(e.Intervalo)} m, {e.Equacoes.Count} equação(ões)."));

        if (via.Estrutura is { Camadas.Count: > 0 } est)
        {
            pav.Add(L(NivelSituacao.Ok, $"Estrutura informada: {est.Camadas.Count} camada(s), {M(est.EspessuraCamadas * 100, "0.#")} cm sob o revestimento; fonte: " +
                (string.IsNullOrWhiteSpace(est.Fonte) ? "não informada." : est.Fonte + ".")));
            foreach (var c in est.Camadas)
                pav.Add(L(NivelSituacao.Informacao, $"{(string.IsNullOrWhiteSpace(c.Nome) ? "camada sem nome" : c.Nome)}: {M(c.EspessuraCm, "0.#")} cm" +
                    (string.IsNullOrWhiteSpace(c.Material) ? "" : $", {c.Material}") + $" ({EstruturaPavimento.Nome(c.Funcao)})"));
            pav.Add(L(NivelSituacao.Informacao, "A estrutura não muda nesta janela: Pavimentação › Estrutura do Pavimento. Quantitativo por camada: Quantitativos › Camadas do pavimento."));
        }
        else pav.Add(L(NivelSituacao.NaoVerificado, "Estrutura do pavimento não informada: a terraplenagem usa o subleito das opções. Informe em Pavimentação › Estrutura do Pavimento."));

        if (PainelContextual.NaTerraplenagem(via, registro) is { } t) terra.Add(t);
        else terra.Add(L(NivelSituacao.Informacao, "A via não molda o terreno."));
        if (registro != null)
            terra.Add(L(NivelSituacao.Informacao, $"Última terraplenagem: {Quando(registro.Quando)} – subleito {M(registro.Subleito)} m, taludes de corte {M(registro.TaludeCorte)} : 1 e aterro {M(registro.TaludeAterro)} : 1."));
        terra.Add(L(NivelSituacao.Informacao, "Volumes por estaca da via: Quantitativos › Terraplenagem por estaca – calculados na hora sobre o terreno original guardado."));

        var minhas = DrenagemDoModelo.Captacoes(all).Where(c => c.Sarjeta?.ViaId == via.Id).ToList();
        if (minhas.Count == 0)
            dren.Add(L(NivelSituacao.Informacao, "Nenhuma captação na sarjeta desta via. Crie com Drenagem › Dispositivos e use Ler do Modelo."));
        else
        {
            var est2 = minhas.GroupBy(DrenagemDoModelo.Estado).ToDictionary(x => x.Key, x => x.Count());
            int N(EstadoLeitura s) => est2.GetValueOrDefault(s);
            dren.Add(L(N(EstadoLeitura.Desatualizada) > 0 ? NivelSituacao.Aviso : NivelSituacao.Informacao,
                $"{minhas.Count} captação(ões) na sarjeta desta via: {N(EstadoLeitura.EmDia)} lida(s) e em dia, {N(EstadoLeitura.Desatualizada)} desatualizada(s), " +
                $"{N(EstadoLeitura.Editada)} editada(s) à mão, {N(EstadoLeitura.NuncaLida)} nunca lida(s)."));
            var ids = minhas.Select(c => c.Dispositivo.Id).ToHashSet();
            var trechos = all.OfType<DrainagePipeDefinition>().Count(p => ids.Contains(p.FromId) || ids.Contains(p.ToId));
            dren.Add(L(NivelSituacao.Informacao, $"{trechos} trecho(s) de rede ligados a elas."));
        }
        dren.Add(L(NivelSituacao.Informacao, "Conferência geométrica e de conectividade: Relatório da Rede. Vazões e diâmetros só com o Projeto de Drenagem – sem ele não há dimensionamento hidráulico."));

        if (criterios is not { IsEmpty: false })
            valid.Add(L(NivelSituacao.NaoVerificado, "Limites geométricos não informados: traçado e greide não conferidos (Configurações › Limites geométricos)."));
        else
        {
            var v = ModelValidator.Tracado(via, criterios);
            valid.Add(v.Count == 0
                ? L(NivelSituacao.Ok, $"Traçado e greide dentro dos limites informados ({criterios.FonteTexto}).")
                : L(NivelSituacao.Aviso, $"{v.Count} elemento(s) do traçado ou do greide fora dos limites informados ({criterios.FonteTexto})."));
            foreach (var x in v.Take(12)) valid.Add(L(NivelSituacao.Aviso, x.Texto));
            if (v.Count > 12) valid.Add(L(NivelSituacao.Aviso, $"… e mais {v.Count - 12} (relatório do Validar Modelo)."));
        }

        resumo.Add(L(NivelSituacao.Informacao, "Ao aplicar, a via é refeita com a seção nova: pista, elementos de cada lado e a sinalização da seção (o que foi editado à mão fica)."));
        var conexoes = ModelGraph.ConnectionsToRefresh(new MarkingDefinition[] { via }, all).Where(c => c.Id != via.Id).ToList();
        resumo.Add(L(NivelSituacao.Informacao, conexoes.Count == 0 ? "Nenhuma conexão ligada à via."
            : $"{conexoes.Count} conexão(ões) ligada(s) à via acompanham a mudança (interseções, rotatórias, balões)."));
        var grafo = new ModelGraph(all);
        var dependentes = grafo.Dependents(via.Id).Distinct().Count(id => id != via.Id);
        if (dependentes > 0) resumo.Add(L(NivelSituacao.Informacao, $"{dependentes} marca(s) dependem da via (detalhes, sinalização dos nós, trechos)."));
        if (EarthworkRecord.Participantes(all).Any(d => d.Id == via.Id))
            resumo.Add(L(NivelSituacao.Aviso, "A via molda o terreno: se a seção mudar, a terraplenagem fica a refazer (rode a Terraplenagem)."));
        var lidas = minhas.Count(c => c.Dispositivo.Lida?.ViaId == via.Id);
        if (lidas > 0)
            resumo.Add(L(NivelSituacao.Aviso, $"{lidas} captação(ões) lida(s) desta via podem ficar desatualizadas: Ler do Modelo › Atualizar só as desatualizadas."));
        resumo.Add(L(NivelSituacao.Informacao, "Tudo numa operação desfazível (Desfazer do Revit)."));
        return new(perfil, pav, terra, dren, valid, resumo);
    }
}
