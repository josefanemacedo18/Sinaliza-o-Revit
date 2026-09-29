using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

internal sealed partial class AutoTestRunner
{
    // ================================================================== rodada T: largura variável, recuos, calçadas, segurança

    private void RoundT()
    {
        const double Y = 2700;
        Step("Largura e recuos", "Via com largura variável (10,00 → 10,50 m entre meios-fios) e estreitamento do lote", s =>
        {
            var setup = T(1);
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 0 });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 60, BordoDireito = setup.Nominal(false).Bordo });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 120, BordoDireito = setup.Nominal(false).Bordo + 0.50, AlinhamentoDireito = setup.Nominal(false).Alinhamento + 0.50 });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 200, BordoEsquerdo = setup.Nominal(true).Bordo, AlinhamentoEsquerdo = setup.Nominal(true).Alinhamento - 0.80 });
            var road = MakeRoad(s, "largura variável", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (0, Y), (260, Y));
            if (road?.Pavement is { } pav && !pav.HasEdgeVariation) s.Error("O pavimento não recebeu a variação de largura.");
        });
        Step("Largura e recuos", "Baia de ônibus, faixa de desaceleração e de aceleração", s =>
        {
            var setup = T(2);
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 40;
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaDesaceleracao, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 140;
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaAceleracao, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 260;
            setup.Recuos[^1].LadoEsquerdo = true;
            MakeRoad(s, "recuos", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (320, Y), (700, Y));
            var recs = TestDefs().OfType<RecessMarkingDefinition>().Count();
            if (recs < 3) s.Error($"Esperadas 3 marcas de recuo; criadas {recs}.");
        });
        Step("Largura e recuos", "Calçada com inclinação transversal e níveis do alinhamento (edificações antigas mais altas)", s =>
        {
            var setup = T(0);
            foreach (var e in setup.Right.Concat(setup.Left).Where(e => e.Tipo == TipoElementoSecao.Calcada))
            {
                e.InclinacaoTransversal = 2.0;
                e.NiveisAlinhamento = new List<NivelAlinhamento> { new() { Estaca = 0, Nivel = 0 }, new() { Estaca = 60, Nivel = 0.35 }, new() { Estaca = 120, Nivel = 0.10 } };
            }
            MakeRoad(s, "níveis da calçada", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (0, Y + 120), (200, Y + 120));
            if (!TestDefs().OfType<LinearMarkingDefinition>().Any(l => l.LevelProfile.Count > 0))
                s.Error("Nenhuma faixa da calçada recebeu o perfil de níveis.");
        });
        Step("Largura e recuos", "Arterial × coletora com linha de continuidade (LCO) na interseção", s =>
        {
            MakeRoad(s, "LCO arterial", T(2), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (320, Y + 160), (620, Y + 160));
            MakeRoad(s, "LCO coletora", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (470, Y + 160), (470, Y + 300));
            var it = ItNear(P(470, Y + 160), 12);
            if (it == null) s.Error("A interseção não foi criada.");
            else s.Note($"Interseção criada; linha de continuidade: {it.ContinuityLine}, via principal {it.MainRoadId?[..6]}…");
        });
        foreach (var v in Enum.GetValues<TipoSonorizador>())
            Step("Segurança viária", $"Sonorizador longitudinal – {v}", s =>
            {
                var d = new RumbleStripDefinition { Output = Out(), Position = PosicaoSonorizador.Livre, SegmentLength = 12, GapLength = 3.6 };
                d.ApplyPreset(v);
                var y = Y + 360 + 6 * (int)v;
                d.PathRef = Pts((0, y), (80, y));
                Commit(s, new[] { d }, "sonorizador");
            });
        Step("Segurança viária", "Área de escape (caixa de retenção, faixa de serviço, berma, sinalização)", s =>
        {
            var o = InfraRunner.Output3D();
            var d = new EscapeRampDefinition { Output = o, Length = 150, EntrySpeed = 110, Grade = 5 };
            d.PathRef = Pts((120, Y + 380), (290, Y + 380));
            Commit(s, new[] { d }, "área de escape");
            s.Note($"Comprimento necessário {d.RequiredLength:0} m; caixa {d.ActualLength:0} m.");
        });
    }
}
