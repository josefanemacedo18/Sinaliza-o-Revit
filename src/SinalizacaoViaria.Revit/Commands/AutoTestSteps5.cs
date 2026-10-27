using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

internal sealed partial class AutoTestRunner
{
    // ================================================================== fluxo integrado (Etapa 13)

    /// <summary>
    /// Fluxo integrado (Etapa 13; Prompt Master §20; Motores §17): duas vias que se cruzam sobre o terreno, a interseção, a
    /// terraplenagem, uma grelha na sarjeta, os quantitativos e a validação – conferindo que cada módulo vê o que o anterior fez
    /// (o registro da terraplenagem com as vias e a interseção, a captação na sarjeta da via certa, o quantitativo por via com
    /// a terraplenagem da via, a V15 depois de editar a via e o terreno refeito). Na área B (x 1330–1560, y 820–980), longe das
    /// vias da Topografia e das obras. As etapas vão juntas: se o tempo do modo rápido acabou antes, o fluxo não começa.
    /// </summary>
    private void Integrado()
    {
        const string G = "Fluxo integrado";
        const double Y = 900, X = 1440;
        var antes = _steps.Count;
        if (_topo == null)
        {
            Step(G, "Terreno de teste (o Toposolid com relevo da Topografia)", TerrenoDeTeste);
            if (_steps.Count == antes || _topo == null) return;
        }
        PickedRoad? leste = null, norte = null;
        IntersectionDefinition? no = null;
        DrainageDefinition? grelha = null;
        var inicio = _steps.Count;
        Step(G, "1/5 Duas vias acompanhando o terreno e a interseção no cruzamento", s =>
        {
            leste = MakeRoad(s, "integrado leste-oeste", T(0), 0, true, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (1330, Y), (1560, Y));
            norte = MakeRoad(s, "integrado norte-sul", T(0), 0, true, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (X, 820), (X, 980));
            if (leste == null || norte == null) return;
            if (ItNear(P(X, Y), 10) is not { } it) { s.Error($"A interseção não foi criada no cruzamento {L(P(X, Y))}."); return; }
            no = it;
            foreach (var v in new[] { leste, norte }.Where(v => !it.RoadIds.Contains(v.Pavement.Id)))
                s.Error($"A interseção não está ligada à via {v.Pavement.DisplayCode} (RoadIds).");
            s.Note($"Interseção {it.DisplayCode} a {it.Z - _z0:0.00} m (terreno natural {Relief(X, Y):0.00} m).");
        });
        // As demais etapas vão sempre juntas com a primeira (uma etapa sozinha não diz nada do fluxo).
        if (_steps.Count == inicio || leste is not { } viaL || norte is not { } viaN || no is not { } no0) return;

        // Marcas do fluxo, relidas do modelo (a Terraplenagem grava o greide das vias e a interseção é refeita com elas).
        var grupos = new[] { viaL.GroupId, viaN.GroupId };
        string NoId() => ItNear(P(X, Y), 10)?.Id ?? no0.Id;
        List<MarkingDefinition> Minhas()
        {
            var noId = NoId();
            return MarkingStorage.Definitions(_doc).Where(d => (d.GroupId != null && grupos.Contains(d.GroupId)) || d.Id == noId || d.Id == grelha?.Id).ToList();
        }
        RoadPavementDefinition Via(PickedRoad r) => RoadWorks.RoadByGroup(_doc, r.GroupId)?.Pavement ?? r.Pavement;
        // Participantes do fluxo que diferem do registro da terraplenagem (V15) – só os do fluxo: o resto do teste tem a sua vida.
        List<MudancaTerraplenagem> Pendentes(RegistroTerraplenagem? reg, IReadOnlyCollection<MarkingDefinition> all)
        {
            var meus = Minhas().Select(d => d.Id).ToHashSet();
            return reg == null ? new() : EarthworkRecord.Comparar(reg, all).Where(m => meus.Contains(m.Id)).ToList();
        }

        Step(G, "2/5 Terraplenagem das vias e da interseção (registro e volumes por elemento)", s =>
        {
            var r = CmdTerraplenagem.Apply(_uidoc, Minhas().Where(TerrainService.Grades).ToList(), new GradingOptions(), false);
            if (r == null) { s.Error("O Revit recusou a terraplenagem (ver mensagens)."); return; }
            var all = MarkingStorage.Definitions(_doc);
            var reg = EarthworkRecordStore.Load(_doc);
            if (reg == null) { s.Error("A terraplenagem não gravou o registro (sem ele a V15 não sabe o que o terreno tem)."); return; }
            foreach (var id in new[] { Via(viaL).Id, Via(viaN).Id, NoId() }.Where(id => !reg.Assinaturas.ContainsKey(id)))
                s.Error($"{all.FirstOrDefault(d => d.Id == id)?.DisplayCode ?? id}: molda o terreno e não está no registro da terraplenagem.");
            foreach (var m in Pendentes(reg, all)) s.Error($"Logo depois da terraplenagem, {m.Nome} já aparece como {m.Situacao} (V15).");
            var (itens, corte, aterro) = TerrainService.Quantities(_doc, new MarkingService(_doc, _uidoc.ActiveView), all);
            foreach (var v in new[] { Via(viaL), Via(viaN) })
            {
                var dela = itens.Where(i => i.Id == v.Id).ToList();
                if (dela.Count == 0) s.Error($"Via {v.DisplayCode}: nenhum volume de terraplenagem por elemento.");
                else s.Note($"Via {v.DisplayCode}: corte {dela.Sum(i => i.Cut):0.0} m³, aterro {dela.Sum(i => i.Fill):0.0} m³.");
            }
            s.Note($"Projeto inteiro: corte {corte:0.0} m³, aterro {aterro:0.0} m³ ({itens.Count} elemento(s)).");
        }, always: true);

        Step(G, "3/5 Grelha na sarjeta da via: captação lida do modelo e a terraplenagem acusa a caixa nova", s =>
        {
            var t = new DrainageDefinition { Type = TipoDrenagem.GrelhaSarjeta };
            t.ApplyDefaults();
            t.Output = InfraRunner.Output3D();
            // Modelo local: faixa 3,00 + estacionamento 2,20 até a face do meio-fio; à direita do eixo, 60 m antes do cruzamento.
            var batch = DrainageCommand.Fitted(t, P(1380, Y - 5.2 + 0.3), GroundLocal(1380, Y), new CurbScanner(_doc, _uidoc.ActiveView), null);
            if (batch is not { Count: 1 }) { s.Error("Nenhum meio-fio encontrado junto à sarjeta da via leste-oeste (o encaixe na guia falhou)."); return; }
            Take(s, DrainageCommand.Place(_uidoc, batch), batch);
            grelha = batch[0];
            var all = MarkingStorage.Definitions(_doc);
            var cap = DrenagemDoModelo.Captacoes(all).FirstOrDefault(c => c.Dispositivo.Id == grelha.Id);
            if (cap == null) { s.Error("A grelha não aparece entre as captações do projeto."); return; }
            if (cap.Sarjeta == null) { s.Error("A grelha não foi reconhecida na sarjeta de nenhuma via."); return; }
            if (cap.Sarjeta.ViaId != Via(viaL).Id) s.Error($"A grelha foi lida na sarjeta de outra via ({cap.Sarjeta.Via}).");
            s.Note($"Sarjeta lida: {cap.Sarjeta.Via}, estaca {cap.Sarjeta.EstacaTexto}, declividade " +
                   $"{cap.Sarjeta.Declividade?.ToString("0.00") ?? "–"} %; área {cap.Area?.Area.ToString("0.0") ?? "–"} m².");
            // Cota da grelha (base da face do meio-fio) × superfície acabada no mesmo ponto – anotada, sem tolerância de norma.
            if (DrenagemDoModelo.SuperficieAcabada(all, TerrainModel.Current(_doc))(grelha.Position) is { } sup)
                s.Note($"Grelha a {grelha.Z - _z0:0.00} m; superfície acabada no ponto {sup - _z0:0.00} m (diferença {grelha.Z - sup:+0.00;-0.00;0.00} m).");
            // Ler do Modelo: grava a leitura na grelha (não muda a assinatura da escavação – Etapa 8).
            var mudou = DrenagemDoModelo.Aplicar(new[] { cap });
            if (mudou.Count > 0) Commit(s, mudou.Select(m => (MarkingDefinition)m.Dispositivo), "leitura da captação");
            var depois = DrenagemDoModelo.Captacoes(MarkingStorage.Definitions(_doc)).FirstOrDefault(c => c.Dispositivo.Id == grelha.Id);
            if (depois == null || DrenagemDoModelo.Estado(depois) != EstadoLeitura.EmDia)
                s.Error($"Depois de ler do modelo, a captação ficou {(depois == null ? "fora do projeto" : DrenagemDoModelo.Estado(depois).ToString())} (esperado: em dia).");
            // A caixa escava o terreno: a terraplenagem aplicada não a tem e tem de dizer isso (V15, "nova").
            var pend = Pendentes(EarthworkRecordStore.Load(_doc), MarkingStorage.Definitions(_doc));
            if (!pend.Any(m => m.Id == grelha.Id && m.Situacao == SituacaoTerraplenagem.Nova))
                s.Error("A terraplenagem aplicada não acusou a grelha nova (a escavação dela não está no terreno).");
            foreach (var m in pend.Where(m => m.Id != grelha.Id)) s.Error($"{m.Nome} ficou {m.Situacao} só por pôr a grelha.");
        }, always: true);

        Step(G, "4/5 Quantitativos: a via com a terraplenagem dela e a grelha com a origem (os mesmos motores)", s =>
        {
            var all = MarkingStorage.Definitions(_doc);
            var sections = new List<SecoesDaVia>();
            var earth = new List<EarthworkItem>();
            var (items, rows) = CmdQuantitativos.Collect(_doc, _uidoc.ActiveView, all, new List<FamilyItem>(), sections, earth);
            var nomes = NomesDasMarcas.Montar(all);
            var porVia = QuantitativosPorVia.Linhas(items, nomes, earth);
            var (itens, _, _) = TerrainService.Quantities(_doc, new MarkingService(_doc, _uidoc.ActiveView), all);
            foreach (var r in new[] { viaL, viaN })
            {
                var v = Via(r);
                var linha = porVia.FirstOrDefault(l => l.Id == v.Id);
                if (linha == null) { s.Error($"Via {v.DisplayCode}: sem linha no resumo por via."); continue; }
                if (linha.Pavimento <= 0) s.Error($"Via {v.DisplayCode}: pavimento de 0 m² no resumo por via.");
                if (Math.Abs(linha.Extensao - r.Axis.Length) > 0.5)
                    s.Error($"Via {v.DisplayCode}: extensão {linha.Extensao:0.00} m no quantitativo, eixo de {r.Axis.Length:0.00} m.");
                var (c, a) = (itens.Where(i => i.Id == v.Id).Sum(i => i.Cut), itens.Where(i => i.Id == v.Id).Sum(i => i.Fill));
                if (Math.Abs((linha.Corte ?? 0) - c) > 0.05 || Math.Abs((linha.Aterro ?? 0) - a) > 0.05)
                    s.Error($"Via {v.DisplayCode}: corte/aterro no quantitativo ({linha.Corte:0.0}/{linha.Aterro:0.0} m³) diferentes da terraplenagem " +
                            $"da mesma via ({c:0.0}/{a:0.0} m³).");
                s.Note($"Via {v.DisplayCode}: {linha.Extensao:0.0} m, pavimento {linha.Pavimento:0.0} m², calçada {linha.Calcada:0.0} m², " +
                       $"corte {linha.Corte:0.0} m³, aterro {linha.Aterro:0.0} m³.");
            }
            if (grelha != null && !rows.Any(r => r.Origens.Any(o => o.Id == grelha.Id)))
                s.Error("A grelha não aparece no quantitativo com a origem (nenhuma linha cita a marca).");
            s.Note($"{rows.Count} linha(s) de quantitativo; {porVia.Count} via(s) no resumo por via.");
        }, always: true);

        Step(G, "5/5 Editar a via: a validação acusa (V15) e o impacto; a terraplenagem refeita limpa", s =>
        {
            var v = Via(viaL);
            var reg = EarthworkRecordStore.Load(_doc);
            // Edição como no formulário da via: o talude de corte do greide de todas as marcas da via (1,0 → 1,5).
            var grupo = MarkingStorage.Definitions(_doc).Where(d => d.GroupId == viaL.GroupId && d.Output.Grade != null).ToList();
            foreach (var d in grupo) d.Output.Grade!.CutSlope = Math.Abs(d.Output.Grade.CutSlope - 1.5) < 1e-6 ? 1.0 : 1.5;
            Commit(s, grupo, "editar o talude da via");
            var all = MarkingStorage.Definitions(_doc);
            if (!Pendentes(reg, all).Any(m => m.Id == v.Id && m.Situacao == SituacaoTerraplenagem.Alterada))
                s.Error("Depois de editar a via, a terraplenagem não aparece como desatualizada para ela.");
            var copias = MarkingStorage.Copies(_doc);
            bool Existe(string uid) => !string.IsNullOrWhiteSpace(uid) && _doc.GetElement(uid) != null;
            var v15 = ModelValidator.Validate(copias, Existe, terraplenagem: reg).Where(a => a.Codigo == "V15").ToList();
            if (!v15.Any(a => a.Ids.Contains(v.Id))) s.Error("A validação (V15) não acusou a via editada.");
            var impacto = EstadoDosResultados.Impacto(grupo.Where(d => d.Id == v.Id).ToList(), all, reg);
            if (!impacto.Any(i => i.Resultado == "Terraplenagem" && i.Ids.Contains(v.Id))) s.Error("O impacto da edição não diz que o terreno ficou por refazer.");
            foreach (var i in impacto) s.Note($"Impacto – {i.Resultado}: {i.Texto}");
            // Terraplenagem refeita: nada do fluxo fica pendente e a V15 não cita mais nenhuma marca dele.
            if (CmdTerraplenagem.Apply(_uidoc, Minhas().Where(TerrainService.Grades).ToList(), new GradingOptions(), false) == null)
            {
                s.Error("O Revit recusou a terraplenagem refeita (ver mensagens).");
                return;
            }
            var reg2 = EarthworkRecordStore.Load(_doc);
            all = MarkingStorage.Definitions(_doc);
            foreach (var m in Pendentes(reg2, all)) s.Error($"Depois de refazer a terraplenagem, {m.Nome} continua {m.Situacao}.");
            var meus = Minhas().Select(d => d.Id).ToHashSet();
            var resto = ModelValidator.Validate(MarkingStorage.Copies(_doc), Existe, terraplenagem: reg2)
                .Where(a => a.Codigo == "V15" && a.Ids.Any(meus.Contains)).ToList();
            foreach (var a in resto) s.Error("V15 depois de refazer a terraplenagem: " + a.Mensagem);
            if (grelha != null && DrenagemDoModelo.Captacoes(all).FirstOrDefault(c => c.Dispositivo.Id == grelha.Id) is { } cap)
                s.Note($"Captação da grelha depois da edição: {DrenagemDoModelo.Estado(cap)} (o talude não muda a sarjeta).");
        }, always: true);
    }
}
