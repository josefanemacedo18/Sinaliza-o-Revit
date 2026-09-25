using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Catalog;

// Os nomes das propriedades são em português porque o catálogo (JSON) é editável pelo usuário.

/// <summary>Agrupamento funcional das marcas (MBST Vol. IV).</summary>
public enum GrupoMarca
{
    Longitudinal,
    Transversal,
    Canalizacao,
    Estacionamento,
    Inscricao,
    Dispositivo,
    Acessibilidade,
    Ciclovia,
    /// <summary>Calçadas, meios-fios e canteiros (elementos físicos da seção transversal).</summary>
    Urbanizacao,
    SinalizacaoVertical,
    Mobiliario,
    Moderacao,
    Detalhamento,
}

/// <summary>Como o padrão tracejado é posicionado ao longo do caminho.</summary>
public enum AlinhamentoPadrao
{
    /// <summary>Primeiro traço começa no início do caminho.</summary>
    Inicio,
    /// <summary>Último traço termina no fim do caminho.</summary>
    Fim,
    /// <summary>Padrão simétrico em relação ao meio do caminho.</summary>
    Centro,
    /// <summary>Ajusta os espaçamentos para caber um número inteiro de traços (traço no início e no fim).</summary>
    Ajustar,
}

/// <summary>Uma "faixa" (linha pintada) paralela ao caminho. Linhas duplas têm duas faixas.</summary>
public sealed class FaixaDef
{
    /// <summary>Deslocamento lateral do eixo da faixa (m). Positivo = à esquerda do sentido do caminho.</summary>
    public double Deslocamento { get; set; }

    /// <summary>Largura da faixa (m).</summary>
    public double Largura { get; set; } = 0.10;

    /// <summary>Sequência traço, espaço, traço, espaço... (m). Vazio = linha contínua.</summary>
    public double[] Padrao { get; set; } = Array.Empty<double>();

    /// <summary>Repetir o padrão ao longo do caminho (falso = sequência única, ex.: LRV).</summary>
    public bool Repetir { get; set; } = true;

    /// <summary>Cor específica da faixa (senão usa a cor da marca).</summary>
    public MarkingColor? Cor { get; set; }

    /// <summary>Altura/espessura específica da faixa (m). 0 = a do tipo.</summary>
    public double Espessura { get; set; }

    public bool Continua => Padrao.Length == 0;

    public FaixaDef Clone() => new()
    {
        Deslocamento = Deslocamento, Largura = Largura, Padrao = (double[])Padrao.Clone(), Repetir = Repetir, Cor = Cor, Espessura = Espessura,
    };
}

/// <summary>Variante dimensional de uma marca, normalmente em função da velocidade regulamentada.</summary>
public sealed class VarianteDef
{
    public string Nome { get; set; } = "";

    /// <summary>Velocidade máxima (km/h) para a qual a variante se aplica. 0 = qualquer.</summary>
    public double VelocidadeMax { get; set; }

    public List<FaixaDef> Faixas { get; set; } = new();

    public string? Observacao { get; set; }
}

/// <summary>Tipo de marca linear (longitudinal, transversal, tachas, piso tátil...).</summary>
public sealed class TipoLinearDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public GrupoMarca Grupo { get; set; }
    public MarkingColor Cor { get; set; } = MarkingColor.Branca;
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";
    public AlinhamentoPadrao Alinhamento { get; set; } = AlinhamentoPadrao.Inicio;

    /// <summary>Descarta traços cortados pelos limites do caminho (ex.: barras de faixa de pedestres).</summary>
    public bool DescartarParciais { get; set; }

    /// <summary>Cada peça conta como unidade nos quantitativos (ex.: tachas).</summary>
    public bool Unidades { get; set; }

    /// <summary>Espessura/altura específica (m). 0 = espessura padrão do material.</summary>
    public double Espessura { get; set; }

    /// <summary>Limites de largura do MBST, usados para avisos de conformidade.</summary>
    public double LarguraMin { get; set; }
    public double LarguraMax { get; set; }

    /// <summary>Marca desenhada por dois pontos atravessando a via (retenção, travessias...).</summary>
    public bool Transversal { get; set; }

    public List<VarianteDef> Variantes { get; set; } = new();

    public VarianteDef? Variante(string? nome) =>
        Variantes.FirstOrDefault(v => string.Equals(v.Nome, nome, StringComparison.OrdinalIgnoreCase)) ?? Variantes.FirstOrDefault();

    /// <summary>Seleciona a variante adequada à velocidade regulamentada.</summary>
    public VarianteDef? VariantePorVelocidade(double velocidade)
    {
        var ordered = Variantes.Where(v => v.VelocidadeMax > 0).OrderBy(v => v.VelocidadeMax).ToList();
        var hit = ordered.FirstOrDefault(v => velocidade <= v.VelocidadeMax + 1e-9);
        return hit ?? Variantes.FirstOrDefault(v => v.VelocidadeMax <= 0) ?? ordered.LastOrDefault() ?? Variantes.FirstOrDefault();
    }

    public override string ToString() => $"{Codigo} – {Nome}";
}

/// <summary>Predefinição de marcação de área (zebrados, área de conflito...).</summary>
public sealed class HachuraDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public GrupoMarca Grupo { get; set; } = GrupoMarca.Canalizacao;
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";
    public MarkingColor CorBarras { get; set; } = MarkingColor.Branca;
    public MarkingColor CorBorda { get; set; } = MarkingColor.Branca;
    public double LarguraBarra { get; set; } = 0.30;
    /// <summary>Espaço livre entre barras (m).</summary>
    public double Espacamento { get; set; } = 1.10;
    /// <summary>Ângulo das barras em relação à direção de referência (graus).</summary>
    public double Angulo { get; set; } = 45;
    /// <summary>Largura da linha de contorno (LCA). 0 = sem contorno.</summary>
    public double LarguraBorda { get; set; } = 0.15;
    /// <summary>Barras em "V" (chevron) espelhadas em relação ao eixo.</summary>
    public bool Chevron { get; set; }
    /// <summary>Segundo conjunto de barras a 90° (quadriculado).</summary>
    public bool Cruzado { get; set; }

    public override string ToString() => $"{Codigo} – {Nome}";
}

/// <summary>Formas paramétricas disponíveis na biblioteca de símbolos.</summary>
public enum FormaSimbolo
{
    SetaFrente,
    SetaDireita,
    SetaEsquerda,
    SetaFrenteDireita,
    SetaFrenteEsquerda,
    SetaDireitaEsquerda,
    SetaRetornoEsquerda,
    SetaRetornoDireita,
    SetaMudancaFaixaEsquerda,
    SetaMudancaFaixaDireita,
    AcessoDeficiente,
    Bicicleta,
    DePreferencia,
    CruzSantoAndre,
    ServicoSaude,
    Pedestre,
}

public sealed class SimboloDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public FormaSimbolo Forma { get; set; }
    public MarkingColor Cor { get; set; } = MarkingColor.Branca;
    /// <summary>Cor de fundo (ex.: azul do SIA). Nulo = sem fundo.</summary>
    public MarkingColor? CorFundo { get; set; }
    /// <summary>Comprimentos padronizados (m) oferecidos na interface.</summary>
    public double[] Comprimentos { get; set; } = { 5.0 };
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";

    public override string ToString() => $"{Codigo} – {Nome}";
}

public sealed class LegendaDef
{
    public string Texto { get; set; } = "";
    public string Descricao { get; set; } = "";
    public override string ToString() => Texto.Replace("\n", " / ");
}

public sealed class TamanhoLetraDef
{
    public string Nome { get; set; } = "";
    public double Altura { get; set; } = 1.60;
    public double FatorLargura { get; set; } = 0.40;
    public double Entrelinha { get; set; } = 1.00;
    public double Espacamento { get; set; } = 0.08;
    public override string ToString() => Nome;
}

public enum TipoVaga
{
    Comum,
    PessoaComDeficiencia,
    Idoso,
    Motocicleta,
    CargaDescarga,
    Onibus,
    Taxi,
    Ambulancia,
    Viatura,
}

public sealed class VagaDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public TipoVaga Tipo { get; set; }
    /// <summary>Ângulo entre o eixo da vaga e o meio-fio (0 = paralela, 90 = perpendicular).</summary>
    public double Angulo { get; set; } = 90;
    /// <summary>Largura da vaga (m).</summary>
    public double Largura { get; set; } = 2.40;
    /// <summary>Comprimento da vaga (m).</summary>
    public double Comprimento { get; set; } = 5.00;
    public double LarguraLinha { get; set; } = 0.10;
    public MarkingColor Cor { get; set; } = MarkingColor.Branca;
    /// <summary>Faixa adicional de circulação zebrada (m). 0 = não possui.</summary>
    public double FaixaAdicional { get; set; }
    public string? Legenda { get; set; }
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";
    public override string ToString() => $"{Codigo} – {Nome}";
}

/// <summary>Forma 3D de um dispositivo físico.</summary>
public enum FormaDispositivo
{
    /// <summary>Paralelepípedo (tachão, bloco, separador contínuo).</summary>
    Caixa,
    /// <summary>Cilindro (pilarete, frade, cilindro delimitador).</summary>
    Cilindro,
    /// <summary>Base circular larga + haste (balizador/delineador flexível).</summary>
    Balizador,
    /// <summary>Calota elíptica baixa (segregador "tartaruga").</summary>
    Tartaruga,
    /// <summary>Tronco de pirâmide escalonado (prisma de concreto).</summary>
    Prisma,
    /// <summary>Perfil New Jersey escalonado (barreira de concreto ou plástica).</summary>
    NewJersey,
    /// <summary>Postes a cada espaçamento + lâmina contínua (defensa metálica).</summary>
    Defensa,
    /// <summary>Defensa dupla (lâminas nos dois lados dos postes – canteiros centrais).</summary>
    DefensaDupla,
}

/// <summary>Formato da chapa de uma placa de sinalização vertical.</summary>
public enum FormaPlaca
{
    Circulo,
    Octogono,
    TrianguloInvertido,
    Losango,
    Retangulo,
    Quadrado,
    /// <summary>Cruz de Santo André (A-41) – duas travessas cruzadas.</summary>
    CruzSantoAndre,
}

/// <summary>Categoria da sinalização vertical (MBST Vol. I a III e sinalização de obras).</summary>
public enum CategoriaPlaca
{
    Regulamentacao,
    Advertencia,
    Indicacao,
    Educativa,
    Servicos,
    Turistica,
    Obras,
}

/// <summary>Placa de sinalização vertical.</summary>
public sealed class PlacaDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public CategoriaPlaca Categoria { get; set; }
    public FormaPlaca Forma { get; set; } = FormaPlaca.Circulo;
    /// <summary>Largura / diâmetro / lado (m).</summary>
    public double Largura { get; set; } = 0.50;
    /// <summary>Altura (m) – usada em retângulos.</summary>
    public double Altura { get; set; } = 0.50;
    /// <summary>Cor de fundo (área interna).</summary>
    public MarkingColor CorFundo { get; set; } = MarkingColor.Branca;
    /// <summary>Cor da orla (borda).</summary>
    public MarkingColor CorOrla { get; set; } = MarkingColor.Vermelha;
    /// <summary>Largura da orla em fração da largura da placa.</summary>
    public double Orla { get; set; } = 0.10;
    /// <summary>Legenda aplicada sobre a placa (ex.: PARE, 40). Vazio = sem legenda.</summary>
    public string? Legenda { get; set; }
    public MarkingColor CorLegenda { get; set; } = MarkingColor.Preta;
    /// <summary>Tamanhos alternativos (largura em m) oferecidos na interface.</summary>
    public double[] Tamanhos { get; set; } = Array.Empty<double>();
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";
    /// <summary>Desenho interno (pictograma) em unidades da área útil da placa (−0,5…0,5, Y para cima).</summary>
    public List<PictoItem>? Pictograma { get; set; }
    /// <summary>Tarja diagonal vermelha de proibição sobre o pictograma (R-4a, R-9...).</summary>
    public bool Proibicao { get; set; }
    public override string ToString() => $"{Codigo} – {Nome}";
}

/// <summary>
/// Elemento do pictograma de uma placa. Tipos: "linha" (pts, w), "seta" (pts, w, cabeca), "poligono" (pts),
/// "circulo" (c, r), "anel" (c, r, w), "retangulo" (pts = [min, max]), "texto" (c, texto, h, editavel),
/// "silhueta" (nome, c, k, rot, espelhar). Coordenadas na área útil: −0,5…0,5.
/// </summary>
public sealed class PictoItem
{
    public string Tipo { get; set; } = "linha";
    public double[][]? Pts { get; set; }
    public double[]? C { get; set; }
    public double W { get; set; } = 0.08;
    public double R { get; set; } = 0.1;
    /// <summary>Tamanho da ponta da seta (múltiplo da largura da haste).</summary>
    public double Cabeca { get; set; } = 3.0;
    /// <summary>Ponta também no início (seta dupla).</summary>
    public bool Dupla { get; set; }
    public string? Texto { get; set; }
    public double H { get; set; } = 0.3;
    /// <summary>Texto substituído pela legenda editável da placa (ex.: 3,0 m; 10 t).</summary>
    public bool Editavel { get; set; }
    public string? Nome { get; set; }
    public double K { get; set; } = 1;
    public double Rot { get; set; }
    public bool Espelhar { get; set; }
    /// <summary>Pinta com a cor de fundo (vazado).</summary>
    public bool Vazado { get; set; }
    public MarkingColor? Cor { get; set; }
}

/// <summary>Tipos de mobiliário/elementos urbanísticos viários.</summary>
public enum FormaMobiliario
{
    Banco,
    Lixeira,
    PosteIluminacao,
    Arvore,
    AbrigoOnibus,
    Paraciclo,
    Hidrante,
    Floreira,
    PlacaLogradouro,
    Semaforo,
    /// <summary>Poste baixo de iluminação de pedestres.</summary>
    PostePedestre,
    Palmeira,
    Arbusto,
}

/// <summary>Elemento urbanístico viário (mobiliário urbano).</summary>
public sealed class MobiliarioDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public FormaMobiliario Forma { get; set; }
    public double Comprimento { get; set; } = 1.0;
    public double Largura { get; set; } = 0.5;
    public double Altura { get; set; } = 1.0;
    public MarkingColor Cor { get; set; } = MarkingColor.Metal;
    /// <summary>Espaçamento padrão ao distribuir ao longo de um caminho (m).</summary>
    public double Espacamento { get; set; } = 10;
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";
    public override string ToString() => $"{Codigo} – {Nome}";
}

/// <summary>Dispositivo físico de bloqueio, segregação ou canalização implantado junto à sinalização horizontal.</summary>
public sealed class DispositivoDef
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public FormaDispositivo Forma { get; set; }
    public MarkingColor Cor { get; set; } = MarkingColor.Amarela;
    /// <summary>Dimensão ao longo do caminho (m). Cilindros usam a largura como diâmetro.</summary>
    public double Comprimento { get; set; } = 0.25;
    /// <summary>Dimensão transversal (m) – diâmetro nos cilindros.</summary>
    public double Largura { get; set; } = 0.15;
    public double Altura { get; set; } = 0.05;
    /// <summary>Distância entre centros (m). 0 = contínuo.</summary>
    public double Espacamento { get; set; } = 2.0;
    public string Descricao { get; set; } = "";
    public string Referencia { get; set; } = "";

    public bool Continuo => Espacamento <= 0;
    public override string ToString() => $"{Codigo} – {Nome}";
}

public sealed class MaterialDef
{
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    /// <summary>Espessura de aplicação (mm) – usada no modelo 3D.</summary>
    public double EspessuraMm { get; set; } = 0.6;
    /// <summary>Consumo estimado por m².</summary>
    public double Consumo { get; set; }
    public string UnidadeConsumo { get; set; } = "L/m²";
    /// <summary>Microesferas de vidro (kg/m²) aplicadas por aspersão.</summary>
    public double MicroesferasKgM2 { get; set; }
    public string Referencia { get; set; } = "";
    public override string ToString() => Nome;
}

public sealed class NormaDef
{
    public string Sigla { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Aplicacao { get; set; } = "";
}

/// <summary>Catálogo normativo completo.</summary>
public sealed class Catalogo
{
    public string Versao { get; set; } = "1.0";
    public string Aviso { get; set; } = "";
    public List<NormaDef> Normas { get; set; } = new();
    public List<TipoLinearDef> Lineares { get; set; } = new();
    public List<HachuraDef> Hachuras { get; set; } = new();
    public List<SimboloDef> Simbolos { get; set; } = new();
    public List<LegendaDef> Legendas { get; set; } = new();
    public List<TamanhoLetraDef> TamanhosLetra { get; set; } = new();
    public List<VagaDef> Vagas { get; set; } = new();
    public List<MaterialDef> Materiais { get; set; } = new();
    public List<DispositivoDef> Dispositivos { get; set; } = new();
    public List<PlacaDef> Placas { get; set; } = new();
    public List<MobiliarioDef> Mobiliario { get; set; } = new();

    public PlacaDef? Placa(string? codigo) =>
        Placas.FirstOrDefault(d => string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public MobiliarioDef? Movel(string? codigo) =>
        Mobiliario.FirstOrDefault(d => string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public DispositivoDef? Dispositivo(string? codigo) =>
        Dispositivos.FirstOrDefault(d => string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public TipoLinearDef? Linear(string? codigo) =>
        Lineares.FirstOrDefault(l => string.Equals(l.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public HachuraDef? Hachura(string? codigo) =>
        Hachuras.FirstOrDefault(l => string.Equals(l.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public SimboloDef? Simbolo(string? codigo) =>
        Simbolos.FirstOrDefault(l => string.Equals(l.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public VagaDef? Vaga(string? codigo) =>
        Vagas.FirstOrDefault(l => string.Equals(l.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

    public MaterialDef? Material(string? nome) =>
        Materiais.FirstOrDefault(m => string.Equals(m.Nome, nome, StringComparison.OrdinalIgnoreCase)) ?? Materiais.FirstOrDefault();

    public IEnumerable<TipoLinearDef> LinearesDoGrupo(params GrupoMarca[] grupos) =>
        Lineares.Where(l => grupos.Length == 0 || grupos.Contains(l.Grupo));
}
