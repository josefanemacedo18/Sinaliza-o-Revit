using System.Reflection;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Commands;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit;

/// <summary>Monta a guia "SinalizaBIM" na faixa de opções.</summary>
public static class RibbonBuilder
{
    public const string TabName = "SinalizaBIM";
    public const string InfraTab = "SinalizaBIM Infra";
    private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

    public static void Build(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* já existe */ }

        // ---------------------------------------------------------------- Vias
        var via = app.CreateRibbonPanel(TabName, "Vias");
        Split(via, "SvVias", "Via",
            Data(typeof(CmdSinalizarVia), "Via", "via",
                "Via completa a partir do eixo – desenhado por pontos (curvas concordadas, encaixe nas vias existentes) ou selecionado: faixas, ciclofaixas, " +
                "estacionamento, canteiros, calçadas e toda a sinalização, com modelos prontos e personalizados. Cria a pista e as conexões como a ferramenta Pista e " +
                "acrescenta os demais elementos junto ao bordo."),
            Data(typeof(CmdPista), "Pista", "pista",
                "Só a pista dos veículos (asfalto, bloquete ou concreto), já conectada. Monte o resto passo a passo com Calçadas → 'Junto ao bordo de uma via'."),
            Data(typeof(CmdFerrovia), "Via Férrea", "ferrovia",
                "Via férrea completa e personalizável: lastro com taludes, sublastro, dormentes (concreto, bibloco, madeira, aço), fixações e trilhos " +
                "TR-45/57/68, UIC-60 ou Ri-60 – ou via em laje e via embutida no pavimento (VLT) –, bitola larga, métrica, padrão ou mista, várias linhas e valetas."),
            Data(typeof(CmdDesenharEixo), "Desenhar Eixo", "eixo",
                "Eixo com a ferramenta nativa Linha de modelo (reta, arco, spline, cadeia, snaps) ou por pontos com encaixe nas vias."));
        Split(via, "SvImagem", "Calibrar Imagem",
            Data(typeof(CmdCalibrarImagem), "Calibrar Escala da Imagem", "calibrarimagem",
                "Selecione uma imagem já no projeto (ex.: vista aérea colada ou importada), clique 2 pontos sobre ela e informe a distância real " +
                "(ou a lat/lon dos dois): a imagem é redimensionada – e posicionada/girada para o Norte verdadeiro com lat/lon – na escala exata, medida no modelo."),
            Data(typeof(CmdImportarImagem), "Importar Imagem e Calibrar", "importarimagem",
                "Qualquer imagem aérea (captura, ortofoto, drone): com world file ou GeoTIFF entra na escala e posição certas; " +
                "sem georreferência, é colocada e a calibração por 2 pontos abre em seguida."));
        Split(via, "SvConexoes", "Conexões",
            Data(typeof(CmdRotatoria), "Rotatória", "rotatoria",
                "Clique o centro (encaixa no cruzamento mais próximo; os ramos vêm das vias) ou um ponto livre para uma rotatória isolada. " +
                "Tipos: mini, compacta, 1 faixa, 2 faixas, turbo, oval, com by-pass ou personalizada – tudo editável."),
            Data(typeof(CmdIntersecao), "Interseção", "intersecao",
                "Cria/atualiza a interseção do cruzamento clicado ou todas as do projeto (tipos I a IV, PARE / dê a preferência / semáforo)."),
            Data(typeof(CmdConexao), "Trocar Conexão", "conexao",
                "Clique num encontro de vias, rotatória ou ponta livre e troque o tratamento: interseção, rotatória, cul-de-sac ou nenhum."),
            Data(typeof(CmdDistanciaVias), "Distância entre Vias", "distancia",
                "Clique a via A (fica) e a via B (move): mostra a distância entre eixos, meios-fios e bordos e aceita a nova – a via B se move inteira " +
                "e as interseções são refeitas, numa única operação desfazível."),
            Data(typeof(CmdMoverVia), "Mover Via", "movervia",
                "Desloca uma via inteira (eixo, sinalização, calçadas, extensões, piso tátil e elementos sobre ela) por ΔX/ΔY digitados ou dois cliques, refazendo as interseções."),
            Data(typeof(CmdLarguraTrecho), "Alterar Largura por Trecho", "recuo",
                "Clique o início (A) e o fim (B) no eixo – ou digite as estacas – e mude a largura da calçada, da pista, do estacionamento ou do canteiro, com transição em cada ponta. Setas e cotas na planta antes de confirmar."),
            Data(typeof(CmdRecuoVia), "Recuo na Via (baia / faixa auxiliar)", "recuo",
                "Numa via já criada: clique junto ao bordo e crie baia de ônibus, faixa de desaceleração/aceleração ou recuo de embarque – a via é refeita com meio-fio, calçada e sinalização do recuo. Editar no recuo altera ou remove."));
        Split(via, "SvCalcadas", "Calçadas",
            Data(typeof(CmdCalcadas), "Meio-fio, Calçada e Sarjeta", "calcada",
                "Meios-fios, sarjetas, sarjetões, calçadas, grama e faixas de caminhada ao longo de linhas – ou 'Junto ao bordo de uma via', empilhando a partir do bordo (fazem parte da via e das conexões)."),
            Data(typeof(CmdOrelha), "Extensão de Calçada", "orelha",
                "Avanço de calçada sobre o estacionamento, com cada ponta em curva, chanfro ou acompanhando a calçada; meio-fio, canteiro e árvores."),
            Data(typeof(CmdAreaCalcada), "Área de Calçada", "areacalcada",
                "Contorno livre (esquinas, ilhas, parklets, ciclovia no nível da calçada, faixa de serviço) com meio-fio e cantos arredondados."),
            Data(typeof(CmdCanteiro), "Canteiros", "canteiro",
                "Canteiros gramados, jardineiras e grelhas de árvore na faixa de serviço, com árvores."),
            Data(typeof(CmdCulDeSac), "Cul-de-sac", "culdesac",
                "Balão de retorno na ponta de uma via (ligado a ela) ou avulso: circular, excêntrico, gota, T, Y ou L."),
            Data(typeof(CmdRampa), "Rampas", "rampa",
                "Rebaixamentos de calçada (NBR 9050) com abas e piso tátil, e guias rebaixadas de veículos."));
        Split(via, "SvUrbanos", "Elementos Urbanos",
            Data(typeof(CmdMobiliario), "Elementos Urbanos", "mobiliario", "Bancos, lixeiras, postes, árvores, abrigos, paraciclos, hidrantes, floreiras, semáforos..."),
            Data(typeof(CmdTrocarElementosUrbanos), "Trocar Elementos", "mobiliario",
                "Selecione vários elementos urbanos e troque o tipo de uma vez (ex.: lixeiras por paraciclos): posição, direção, caminho e espaçamento ficam; " +
                "medidas e cor próprias ficam ou voltam às do novo tipo."),
            Data(typeof(CmdElementoFamilia), "Famílias do Revit", "familia",
                "Use suas próprias famílias de componente (mobiliário, luminárias, plantio, modelo genérico...) como elementos urbanos – por cliques ou ao longo de linhas – " +
                "classificadas por subcategoria (iluminação, arborização, bancos, lixeiras...) no Quantitativo. Também classifica famílias já inseridas."));
        StackTwo(via,
            Data(typeof(CmdHierarquia), "Hierarquia e Esquinas", "hierarquia", "Hierarquia viária (CTB art. 60) e raio de concordância das esquinas de vias existentes – aplica às interseções delas."),
            Data(typeof(CmdEixos), "Mostrar/Ocultar Eixos", "eixos", "Mostra ou oculta na vista ativa as linhas de eixo usadas pelas marcas."));

        // ---------------------------------------------------------------- Sinalização horizontal
        var hor = app.CreateRibbonPanel(TabName, "Sinalização Horizontal");
        Split(hor, "SvLinhas", "Linhas",
            Data(typeof(CmdLinhaLongitudinal), "Linha Longitudinal", "linha", "LFO-1 a LFO-4, LMS, LBO, LCO, faixas exclusivas, LCA e LPP."),
            Data(typeof(CmdLinhaTransversal), "Linha Transversal", "transversal", "LRE, LDP, LRV, FTP e demais marcas transversais."),
            Data(typeof(CmdFaixaPedestres), "Faixa de Pedestres", "pedestre", "FTP-1 zebrada ou FTP-2 paralela com linhas de retenção."));
        Split(hor, "SvZebrado", "Zebrado",
            Data(typeof(CmdZebrado), "Zebrado", "zebrado",
                "Zebrados (ZPA, ZPA-A), chevrons, marcação de área de conflito (MAC), quadriculado (MAE) em qualquer contorno – com furos – ou como faixa ao longo de um anel."),
            Data(typeof(CmdCanalizacao), "Canalização (MTL / MAO / MAP)", "canalizacao",
                "Área neutra de transição de largura, aproximação de obstáculo ou acostamento, dimensionada pela velocidade (l = 0,5·V·d) ao longo de uma linha de referência."));
        Split(hor, "SvInscricoes", "Inscrições",
            Data(typeof(CmdSimbolos), "Setas e Símbolos", "seta", "Setas PEM, mudança de faixa, SIA, bicicleta, dê a preferência..."),
            Data(typeof(CmdLegendas), "Legendas", "legenda", "Legendas alongadas (PARE, ÔNIBUS, ESCOLA...) com qualquer fonte."));
        Large(hor, typeof(CmdVagas), "Vagas", "vaga",
            "Vagas paralelas ou em ângulo, PcD, idoso, carga e descarga, ônibus, táxi...");
        Split(hor, "SvComplementos", "Complementos",
            Data(typeof(CmdPisoTatil), "Piso Tátil", "tatil", "Piso tátil de alerta e direcional (NBR 16537)."),
            Data(typeof(CmdCiclovia), "Ciclovia / Faixa de Caminhada", "ciclo", "Ciclofaixa uni/bidirecional, ciclovia segregada ou faixa de caminhada completa: fundo, linhas, símbolos, setas e segregação."),
            Data(typeof(CmdCicloviaLinhas), "Marcas de Ciclovia Avulsas", "ciclo", "CIC-LD, CIC-FD, CIC-LC e cruzamento rodocicloviário (MCC) ao longo de linhas."),
            Data(typeof(CmdModeracao), "Quebra-mola e Lombadas", "quebramola", "Ondulações transversais, faixa elevada, lombada invertida e almofadas (speed cushion)."),
            Data(typeof(CmdCruzamentoFerroviario), "Cruzamento Rodoferroviário", "ferrovia",
                "Passagem em nível completa: linha de retenção dupla, retângulo de advertência com cruz de Santo André, linha dupla, PARE, LRV e placas A-41/R-1/A-39."));

        // ---------------------------------------------------------------- Sinalização vertical e dispositivos
        var vert = app.CreateRibbonPanel(TabName, "Sinalização Vertical");
        Split(vert, "SvPlacas", "Placas",
            Data(typeof(CmdPlacas), "Placas", "placa", "Placas de regulamentação, advertência, indicação, educativas, turísticas e de obras, com suporte – em 3D."),
            Data(typeof(CmdDetalharPlacas), "Detalhar Placas", "detalheplaca", "Placa ampliada ao lado do suporte, com chamada (reta, cotovelo ou livre), código/nome e número em balão."),
            Data(typeof(CmdMoverChamadaPlaca), "Mover Chamada de Placa", "detalheplaca", "Leve o símbolo detalhado para onde quiser e desenhe a linha de chamada clicando os vértices – ou mude a ponta da chamada."),
            Data(typeof(CmdQuadroPlacas), "Quadro de Placas", "quadroplacas", "Símbolo, numeração, código, descrição, dimensões e quantidade de cada placa – numa Legenda própria (qualquer vista ativa, inclusive folha) ou na vista ativa."),
            Data(typeof(CmdQuadroLegenda), "Quadro de Legenda", "quadrolegenda", "Legenda das placas do projeto, com desenho e descrição – numa Legenda própria que vai para várias folhas e se atualiza sozinha."));
        Split(vert, "SvBloqueios", "Bloqueios Físicos",
            Data(typeof(CmdDispositivos), "Bloqueios Físicos", "bloqueio",
                "Todos os dispositivos físicos numa só ferramenta: tachas e tachões refletivos, segregadores, balizadores, pilaretes e frades, New Jersey, " +
                "defensas metálicas (guard rail simples, dupla e de cabos), gradis, floreiras e canalização provisória de obras."),
            Data(typeof(CmdTachas), "Tachas e Tachões", "tacha", "Tachas e tachões refletivos ao longo de linhas (mono/bidirecionais, cadência pela velocidade) – atalho de Bloqueios Físicos."),
            Data(typeof(CmdSonorizador), "Sonorizador Longitudinal", "sonorizador",
                "Sonorizador (rumble strip) no bordo, acostamento ou eixo: fresado, termoplástico com relevo, barras ou tachas – dimensões, espaçamento, ângulo, cor e interrupções para ciclistas."),
            Data(typeof(CmdAreaEscape), "Área de Escape", "escape",
                "Caixa de retenção para caminhões sem freio em descidas: comprimento pela velocidade, rampa e material (AASHTO), profundidade com transição, faixa de serviço com âncoras, berma, zebrado e delineadores."));

        var det = app.CreateRibbonPanel(TabName, "Detalhamento");
        Split(det, "SvDetalhar", "Detalhar",
            Data(typeof(CmdAnotar), "Anotar", "anotar", "Chamada com texto automático para qualquer sinalização."),
            Data(typeof(CmdCotarSecao), "Cotar Seção", "cotasecao", "Um clique sobre a via: perfil transversal em corte (camadas, níveis, caimento, cotas horizontais e verticais, materiais) + cotas e marcas do corte na planta."),
            Data(typeof(CmdCorteTransversal), "Corte Transversal", "cotasecao", "Só o perfil transversal em corte: um clique sobre a via e outro onde colocar o perfil – sem gerar antes a cota de seção (na planta ficam a linha de corte e as marcas A–A)."),
            Data(typeof(CmdDetalheTipico), "Detalhe Típico", "detalhetipico", "Detalhe ampliado e cotado de uma marca ou placa."),
            Data(typeof(CmdQuadroQuantitativos), "Quadro de Quantitativos", "quadroqtd", "Quadro de quantidades do projeto – numa Legenda própria (colocada direto na folha escolhida) ou na vista ativa."),
            Data(typeof(CmdNotas), "Notas Gerais", "notas", "Bloco de notas numeradas do projeto."),
            Data(typeof(CmdNorte), "Norte", "norte", "Indicação de norte."));
        Large(det, typeof(CmdPranchas), "Pranchas", "pranchas",
            "Com a folha (carimbo carregado no projeto) e a escala escolhidas, divide o eixo da via em trechos que cabem na folha – com sobreposição e " +
            "linhas de corte \"continua na prancha X\" – ou usa trechos por estacas ou cortes clicados; cria uma vista girada por trecho e monta as " +
            "folhas com a legenda de placas e o quadro de quantidades. Editar um trecho atualiza a prancha.");
        Large(det, typeof(CmdMemorial), "Memorial Descritivo", "memorial",
            "Gera o memorial descritivo (.docx) do modelo: objetivo e normas, vias, interseções, sinalização horizontal e vertical por código com as " +
            "quantidades do quantitativo, acessibilidade, drenagem e obras – com nome do projeto, responsável técnico e ART em branco para preencher.");
        Large(det, typeof(CmdQuantitativos), "Quantitativos", "quantitativos",
            "Quantidades por categoria e hierarquia viária (m², m, un, consumo), exportação CSV e tabelas do Revit.");

        var traf = app.CreateRibbonPanel(TabName, "Tráfego");
        Large(traf, typeof(CmdSimuladorTrafego), "Simulador de Tráfego", "trafego",
            "Lê todo o projeto (vias, faixas, sentidos, hierarquia, velocidades, interseções e seus controles, rotatórias, balões, placas, " +
            "faixas de pedestres, vagas, lombadas e greide), estima a demanda, calcula capacidade e nível de serviço (HCM) de cada cruzamento " +
            "e trecho, otimiza os semáforos, anima a microssimulação dos veículos e gera o diagnóstico com recomendações (capacidade, segurança, " +
            "sinalização, CTB, acessibilidade) – com mapa de níveis de serviço na planta e relatório.");
        Large(traf, typeof(CmdSemaforos), "Semáforos", "semaforos",
            "Abre o Simulador direto na aba Semáforos: lista de todos os semáforos, criar (clique no mapa ou escolha um elemento do projeto), " +
            "tempos de cada um (ciclo, verdes, entreverdes, defasagem), recomendação para um, todos ou todos os cruzamentos de uma via com onda verde, e gravar no projeto.");

        // ---------------------------------------------------------------- Editar e configurações
        var edit = app.CreateRibbonPanel(TabName, "Editar");
        Large(edit, typeof(CmdEditar), "Editar", "editar", "Edita os parâmetros de uma marca, via, interseção ou rotatória e a regenera.");
        Large(edit, typeof(CmdApagarTrecho), "Apagar Trecho", "apagartrecho",
            "Apaga partes da sinalização HORIZONTAL: escolha a marca e, na planta dela, clique em cada traço, seta ou faixa a apagar " +
            "(Shift + arrastar apaga uma janela). A marca continua a mesma e as peças apagadas podem ser devolvidas depois.");
        Stack(edit,
            Data(typeof(CmdAtualizarTodas), "Atualizar Todas", "atualizar", "Regenera todas as marcas do projeto."),
            Data(typeof(CmdSelecionarConjunto), "Selecionar Conjunto", "selecionar", "Seleciona todos os elementos da mesma marca ou da mesma via."),
            Data(typeof(CmdAlternar2D3D), "Alternar 2D/3D", "alternar", "Converte as marcas selecionadas entre modelo 3D e detalhe 2D."));
        Large(edit, typeof(CmdValidarModelo), "Validar Modelo", "validar",
            "Confere o projeto sem alterar nada: dados ilegíveis ou de versão mais nova, cópias divergentes, referências quebradas " +
            "(detalhes, vias, loteamento), dependências circulares, linhas de referência apagadas, contornos inválidos de quadras e glebas, " +
            "quadras sobrepostas ou fora da gleba e interseções sem duas vias. " +
            "Cada achado traz severidade, confiança e se pede revisão humana; o relatório é gravado em TXT.");
        Stack(edit,
            Data(typeof(CmdConfiguracoes), "Configurações", "config", "Preferências do plugin (ímã de conexão, pisos do Revit, interseções automáticas...)."),
            Data(typeof(CmdCatalogo), "Catálogo", "catalogo", "Abre o catálogo normativo (JSON) para personalização."),
            Data(typeof(CmdSobre), "Normas / Sobre", "sobre", "Normas de referência e informações do plugin."));
        // O Autoteste (diagnóstico) fica em Configurações: é ferramenta de suporte, não de projeto.

        // ================================================================ Guia "SinalizaBIM Infra"
        // Infraestrutura numa guia própria: a guia principal fica com espaço para os nomes das ferramentas.
        try { app.CreateRibbonTab(InfraTab); } catch { /* já existe */ }
        var dren = app.CreateRibbonPanel(InfraTab, "Drenagem");
        Split(dren, "SvDrenagem", "Drenagem",
            Data(typeof(CmdBocaDeLobo), "Boca de Lobo / PV", "bocadelobo",
                "Bocas de lobo simples, dupla, com grelha e combinada (guia chapéu, caixa, tampa, rebaixo da sarjeta) e poços de visita – alinhadas ao meio-fio com um clique."),
            Data(typeof(CmdGrelha), "Grelha de Drenagem", "grelha",
                "Grelhas de sarjeta e de piso com caixa, e canaleta com grelha contínua ao longo de linhas (barras transversais seguras para ciclistas)."));
        Stack(dren,
            Data(typeof(CmdTrechoDrenagem), "Trecho de Rede", "tubodren",
                "Liga dois dispositivos com caixa (PV, boca de lobo, grelha) por um tubo de concreto: clique no de montante e no de jusante; o de jusante vira o montante do próximo trecho (Esc termina)."),
            Data(typeof(CmdProjetoDrenagem), "Projeto de Drenagem", "chuva",
                "Equação de chuva, tempo de retorno, coeficiente de escoamento, rugosidade, diâmetros, limites e identificação do memorial, gravados no projeto – com as predefinições do Manual SUDERHSA (2002) e do memorial modelo."),
            Data(typeof(CmdRedeDrenagem), "Relatório da Rede", "rededren",
                "Planilha de dimensionamento das galerias pelo método racional, captações (sarjeta e engolimento) e conferência da rede (sentido, ligações, ciclos, recobrimento, PVs); aplica os diâmetros calculados (CSV)."));
        Large(dren, typeof(CmdCaptacoesDoModelo), "Ler do Modelo", "dremodelo",
            "Lê da via cada boca de lobo e grelha: declividade da sarjeta pelo greide, ponto baixo e a área que escoa até ela (pista do eixo ao meio-fio e calçada, " +
            "entre o divisor e a captação). Mostra o que leu e preenche só os campos vazios – ou substitui todos, se você escolher.");
        Large(dren, typeof(CmdMemorialDrenagem), "Memorial de Drenagem", "memdren",
            "Memorial descritivo de drenagem pluvial (.docx): parâmetros de projeto, planilha das galerias, descritivo dos serviços, quantitativo e disposições finais.");
        var oa = app.CreateRibbonPanel(InfraTab, "Obras de Arte");
        Split(oa, "SvObrasArte", "Pontes e Viadutos",
            Data(typeof(CmdViaduto), "Viaduto", "viaduto",
                "Viaduto num trecho de uma via existente ou numa via nova: a via (pisos, faixas, calçadas) passa sobre a estrutura – rampas de acesso, encontros, pilares, vigas/caixão/laje, guarda-corpos, juntas e iluminação."),
            Data(typeof(CmdPonte), "Ponte", "ponte",
                "Ponte num trecho de via (existente ou nova): reta ou em curva, em nível, entre margens ou convexa; vigas, caixão, arcos, estaiada ou treliça; apoios esconsos e pilares onde você quiser."),
            Data(typeof(CmdPassarela), "Passarela", "passarela",
                "Passarela de pedestres em treliça com cobertura, rampas ≤ 8,33 % (NBR 9050) e guarda-corpos."));
        Split(oa, "SvTuneis", "Túneis e Trincheiras",
            Data(typeof(CmdTunel), "Túnel", "tunel",
                "Túnel num trecho de via (existente ou nova): ferradura (NATM), circular (TBM) ou retangular, com revestimento, iluminação, ventiladores e emboques – a pista e os passeios são da via."),
            Data(typeof(CmdTrincheira), "Trincheira", "trincheira",
                "Trecho de via rebaixado entre muros (flexão, cortina atirantada ou terra armada), com rampas, guarda-corpos, canaletas e laje de travessia."));
        var cont = app.CreateRibbonPanel(InfraTab, "Contenções");
        Split(cont, "SvContencoes", "Contenções",
            Data(typeof(CmdMuroArrimo), "Muro de Arrimo", "muro",
                "Muros de flexão, gravidade, contrafortes, gabião, terra armada e cortina atirantada – base no terreno, barbacãs, coroamento e reaterro no Toposolid."),
            Data(typeof(CmdTalude), "Talude", "talude",
                "Taludes de corte e aterro com bermas, canaletas de crista, pé e bermas, descidas d'água e revestimento – aplicados ao Toposolid."));
        var rel = app.CreateRibbonPanel(InfraTab, "Relevo e Terreno");
        Large(rel, typeof(CmdPerfilVia), "Perfil da Via", "perfil",
            "Greide (perfil longitudinal) de uma via sobre a topografia: acompanha o terreno com rampa máxima e curvas verticais, rampa constante, nivelado ou por PIVs. " +
            "Onde o terreno pede, a via vira obra automaticamente – viaduto/ponte nos aterros altos, túnel nos cortes profundos, trincheira nos cortes médios – e o resto é aterro e corte no Toposolid.");
        var nos = app.CreateRibbonPanel(InfraTab, "Nós Viários");
        Large(nos, typeof(CmdNoViario), "Nó Viário", "noviario",
            "Interseção em desnível: diamante, diamante com rotatórias, trevo completo, trevo parcial, trombeta e rotatória em dois níveis – viadutos, rampas e laços com greide.");
        Large(rel, typeof(CmdEstruturaPavimento), "Estrutura do Pavimento", "estruturapav",
            "Camadas do pavimento de uma via (revestimento, base, sub-base, reforço), com material, espessura, função e fonte: o fundo da caixa na terraplenagem, " +
            "as camadas no corte transversal e o quantitativo por camada. O plugin não dimensiona – as espessuras são do projeto de pavimentação.");
        Large(rel, typeof(CmdTerraplenagem), "Terraplenagem", "terraplenagem",
            "Ajusta o Toposolid (Massa e terreno) às vias, conexões e obras: plataformas, taludes de corte e aterro até o terreno natural, reaterro de muros e escavação – com volumes de corte e aterro.");

        // ---------------------------------------------------------------- Loteamento
        var lote = app.CreateRibbonPanel(InfraTab, "Loteamento");
        Large(lote, typeof(CmdLoteamento), "Loteamento", "loteamento",
            "Gleba (linha de propriedade, linhas, Toposolid ou cliques) → quadras reconhecidas (a gleba menos as vias) → lotes pela testada com medida padrão (ex.: 12 × 30), " +
            "numerados de 1 a N em cada quadra. Cada lote é uma subdivisão do Toposolid: acompanha o relevo em planta e no 3D (sem Toposolid, piso plano).");
        Split(lote, "SvLoteEdicao", "Quadra e Lote",
            Data(typeof(CmdQuadraLote), "Quadra", "quadra",
                "Clique um lote: medidas padrão da quadra, sobra, miolo, testadas (apontar com cliques, em ordem de prioridade), lote de esquina em cada esquina " +
                "(por uma testada, pela outra ou com medida própria), sentido da numeração e lote 1."),
            Data(typeof(CmdLotesPorLinhas), "Lotes por Linhas", "lotelinhas",
                "Selecione o limite da quadra (linhas fechadas ou um lote de uma quadra) e depois as linhas que dividem os lotes: cada região fechada vira um lote. Mova as linhas e use Atualizar para refazer."),
            Data(typeof(CmdCategoriaLote), "Categoria do Lote", "categorialote",
                "Pincel: escolha a categoria (Lote, Institucional, Área verde...) e clique os lotes um a um – cada clique já aplica; ESC termina."),
            Data(typeof(CmdLote), "Lote", "lote",
                "Clique um lote: frente, profundidade, divisão em partes e categoria (Lote, Institucional, Área verde ou outra) – os vizinhos e a numeração se ajustam."),
            Data(typeof(CmdUnirLotes), "Unir Lotes", "unirlotes",
                "Selecione lotes vizinhos da mesma quadra: viram um lote só (sobra maior, lote maior que o padrão) e a numeração se refaz. Desfazer: Lote → Desfazer a união."));
        StackTwo(lote,
            Data(typeof(CmdNomearVias), "Nomear Vias", "vianome", "Selecione a via (eixo, piso ou elemento dela) e escreva o nome – entra nas confrontações do memorial, no detalhamento e no masterplan."),
            Data(typeof(CmdCategoriasLotes), "Categorias e Filtros", "filtrolote",
                "Categorias de lote (cor e hachura em planta e no 3D, numeração com prefixo, área pública, mínimo %), nova categoria e filtros de vista para mostrar/ocultar cada uma."));
        Split(lote, "SvLoteDoc", "Detalhar e Quadros",
            Data(typeof(CmdDetalharLotes), "Detalhar Lotes", "detalhelotes", "Na vista ativa: número, área, medidas dos lados, categoria, letra da quadra num círculo e nome das vias (cada item opcional)."),
            Data(typeof(CmdQuadroQuadra), "Quadro da Quadra", "quadrolote",
                "Clique um lote: o quadro de lotes só daquela quadra (lote, categoria, via, área, perímetro), numa Legenda própria para qualquer folha ou na vista ativa."),
            Data(typeof(CmdQuadrosLoteamento), "Quadros do Loteamento", "quadrolote",
                "Quadro de áreas (m² e % da gleba), de quadras e de lotes (lote a lote: via, área e perímetro – todas as quadras, uma ou uma por quadra) " +
                "numa Legenda própria (vai para qualquer folha) – e a tabela nativa dos lotes."),
            Data(typeof(CmdMemorialLoteamento), "Memorial do Loteamento", "memorial", "Memorial descritivo (.docx): gleba, quadro de áreas, sistema viário, quadras e descrição de cada lote com as confrontações."));
        Large(lote, typeof(CmdMasterplan), "Masterplan", "masterplan",
            "Cópia da planta ativa só com o layout: lotes nas cores das categorias, vias, calçadas, árvores e mobiliário, nomes das quadras e vias e números dos lotes – " +
            "sem sinalização, cotas nem textos técnicos. Cria o modelo de vista \"SV - Masterplan\".");

    }

    private static void Split(RibbonPanel panel, string name, string text, params PushButtonData[] items)
    {
        if (panel.AddItem(new SplitButtonData(name, text)) is not SplitButton sb) return;
        sb.IsSynchronizedWithCurrentItem = true;
        foreach (var it in items) sb.AddPushButton(it);
    }

    private static PushButtonData Data(Type cmd, string text, string icon, string tooltip)
    {
        var d = new PushButtonData(cmd.Name, text, AssemblyPath, cmd.FullName)
        {
            ToolTip = tooltip,
            LargeImage = IconFactory.Large(icon),
            Image = IconFactory.Small(icon),
        };
        return d;
    }

    private static void Large(RibbonPanel panel, Type cmd, string text, string icon, string tooltip) =>
        panel.AddItem(Data(cmd, text, icon, tooltip));

    private static void Stack(RibbonPanel panel, PushButtonData a, PushButtonData b, PushButtonData c) =>
        panel.AddStackedItems(a, b, c);

    private static void StackTwo(RibbonPanel panel, PushButtonData a, PushButtonData b) =>
        panel.AddStackedItems(a, b);
}
