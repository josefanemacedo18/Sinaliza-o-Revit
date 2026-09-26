# SinalizaBIM – Sinalização viária e urbanização para Revit 2027

**SinalizaBIM** é um plugin para projetar **sinalização viária horizontal e vertical paramétrica, urbanização e moderação de tráfego** dentro do **Autodesk Revit 2027**,
com base no *Manual Brasileiro de Sinalização de Trânsito – Vol. IV (CONTRAN/SENATRAN)*, nas
normas ABNT de acessibilidade (NBR 9050, NBR 16537) e nas práticas do DNIT. Faz no Revit o que
ferramentas como o *SinC* fazem no Civil 3D: você desenha o eixo, escolhe a marca e o plugin gera,
atualiza e quantifica tudo automaticamente.

![Exemplos gerados pelo núcleo do plugin](docs/img/exemplo-geradores.png)

*Setas PEM, símbolos (SIA, bicicleta, "Dê a preferência", cruz de Santo André, saúde), legenda em
duas linhas, via de mão dupla com LFO-4/LMS-2/LBO em curva, faixa de pedestres com retenção em meia
pista, zebrado ZPA, chevron e vagas PcD, 45° e de idoso. A imagem foi gerada pelos mesmos geradores
que o plugin usa dentro do Revit.*

---

## O que o plugin faz

| Guia **SinalizaBIM** | Recursos |
|---|---|
| **Sinalizar Via** | Monta a **seção transversal completa** a partir do eixo, com modelos prontos: faixas de rolamento, **exclusivas e preferenciais** de ônibus (linha MFE + legenda ÔNIBUS repetida), **ciclofaixas** (pintura vermelha, bicicletas, setas, segregadores), **faixa de estacionamento** (vagas de qualquer tipo), **acostamento**, **faixa de segurança/transição** zebrada, **canteiros central e laterais** (físicos ou pintados), **calçadas** (meio-fio, faixa de serviço, faixa livre NBR 9050, faixa de acesso) e dispositivos de segregação. As linhas entre os elementos (LFO, LMS, MFE, CIC-LD, LBO) são escolhidas automaticamente pela velocidade. |
| **Linha Longitudinal** | LFO-1…4, LMS-1/2, LBO, LCO, faixa exclusiva, faixa reversível, LCA, LPP – ao longo de linhas, arcos e splines. Deslocamento lateral, fase, alinhamento do tracejado (início/fim/centro/ajuste), recuos, inversão de lados (LFO-4). |
| **Faixa de Pedestres** | FTP-1 (zebrada) e FTP-2 (paralela) por dois cliques nos bordos, com **linhas de retenção automáticas** a 1,60 m (editável), em meia pista (mão dupla) ou pista inteira. Barras sempre inteiras e centralizadas. |
| **Linha Transversal** | LRE, LDP, LRV (sequência decrescente de espaçamentos), MCC, FTP… por dois cliques (repete até ESC). |
| **Zebrado** | ZPA branco/amarelo, zebrado rodoviário, **chevron**, área de conflito quadriculada, lombada, faixa adicional PcD – em qualquer contorno fechado (inclusive côncavo), com linha de canalização (LCA). |
| **Setas e Símbolos** | Setas PEM (frente, direita, esquerda, combinadas, retorno), mudança obrigatória de faixa, SIA, bicicleta, "Dê a preferência", cruz de Santo André, serviço de saúde. Comprimentos 5,0 / 7,5 m ou livre, com **escala (%)**. |
| **Legendas** | PARE, ÔNIBUS, ESCOLA, SÓ ÔNIBUS, TÁXI… com **qualquer fonte instalada**, letras alongadas (1,60 / 2,40 / 4,00 m) e ordem de leitura de baixo para cima; **escala (%)** da legenda inteira. |
| **Vagas** | Vagas paralelas ou a 30/45/60/90°, PcD com faixa adicional zebrada e SIA, idoso, moto, carga e descarga, ônibus, táxi, ambulância. Preenche o meio-fio automaticamente ou quantidade fixa. |
| **Calçada e Canteiro** | Calçadas, meios-fios e canteiros gramados (3D, 0,15 m de altura) ao longo de qualquer linha. |
| **Bloqueios Físicos** | Segregadores (tartarugas), tachões, balizadores flexíveis, cilindros delimitadores, pilaretes/frades, prismas de concreto, separador contínuo, barreira New Jersey (contínua ou modular "gelo baiano"), barreira plástica e defensa metálica – em 3D com **forma real** (perfil New Jersey padronizado, lâmina em "W", faixas refletivas, módulos vermelho/branco), contados por unidade ou metro. |
| **Placas** (Sinalização Vertical) | **Série completa**: regulamentação R-1 a R-40, advertência A-1a a A-48, serviços auxiliares, turísticas, educativas, indicação, informações complementares e obras (168 placas, com **pictograma desenhado**, descrição e miniaturas com pesquisa), em 3D com coluna simples ou dupla, altura livre de 2,10 m, orla, fundo e legenda editável (ex.: velocidade). Face voltada automaticamente para o tráfego. |
| **Meio-fio e Sarjeta** | Meios-fios (0,15, 0,12, alto, rebaixado, com sarjeta conjugada), sarjetas, **sarjetões com perfil côncavo real que recortam a pista** (travessia de águas), calçadas, gramados e **faixas de caminhada azuis ou verdes**. |
| **Rampas** | Rebaixamentos de calçada NBR 9050 modelados como **sólidos inclinados reais** (rampa com a inclinação exata, abas triangulares a 10 %, piso tátil de alerta acompanhando a rampa, direcional opcional), **rebaixamento total** da calçada com rampas laterais e guias rebaixadas de veículos – **recortam a calçada e o meio-fio automaticamente**. Em 2D mostram a seta de subida e a inclinação. |
| **Quebra-mola e Lombadas** | Ondulações transversais tipo A e B, faixa elevada para travessia (com zebrado no platô e triângulos nas rampas) e lombada invertida – volume 3D com a pintura acompanhando o perfil. |
| **Área de Conflito** | Quadriculado amarelo em cruzamentos. |
| **Elementos Urbanos** | Redesenhados em 3D e planta: árvores (copa em lóbulos), palmeiras, arbustos, bancos de ripas, lixeiras, postes com braço curvo e de pedestres, abrigos de ônibus, paraciclos, hidrantes, floreiras, placas de rua e semáforos – por ponto ou distribuídos ao longo de um caminho. |
| **Guard Rail** | Defensas metálicas simples, duplas e de cabos. |
| **Complementos** | Tachas e tachões (contagem automática), **rota tátil NBR 16537** (placas moduladas com relevo – barras direcionais e domos de alerta automáticos nas mudanças de direção, extremos e junções), **ciclovia / faixa de caminhada completa** (fundo, linhas, símbolos, setas e segregadores num só comando, sem sobreposições). |
| **Montagem passo a passo e pisos** | **Pista** (só a parte dos veículos, já conectada) + meio-fio, calçada, grama e sarjeta **junto ao bordo da via**, empilhados e incorporados às conexões. **Modelos de via personalizados** salvos. Pavimentos, calçadas, meios-fios, sarjetas e grama – **inclusive nas interseções** – como **Piso do Revit** (contorno editável; edições manuais são preservadas). Qualquer ferramenta por linha aceita **bordas de pisos/lajes/topografia** como referência e a **posição em relação à linha** (centro ou borda, com o lado indicado por clique). Interseções automáticas simples (PARE, linhas da principal contínuas). Orelhas com cada ponta em curva, chanfro ou reta. Faixa de opções enxuta com botões agrupados. |
| **Nova via conectada e hierarquia viária** | **Ímã de conexão (como no InfraWorks)**: puxe a ponta de qualquer eixo até o meio de outra via e ela se conecta sozinha, em qualquer ângulo; vias ligadas acompanham quando a outra é movida. **Nova Via** desenhada por pontos (com curvas concordadas) que se encaixa nas vias existentes – na ponta (continuação), no eixo (T/cruzamento) ou numa rotatória (novo ramo) – e o sistema viário se ajusta sozinho: interseção ou rotatória nos encontros e **cul-de-sac** nas pontas livres, sempre ligados às vias. **Conexão** troca o tratamento de qualquer encontro ou ponta. Toda via recebe a **hierarquia viária (CTB art. 60)**, gravada em todos os elementos (SV_Hierarquia) e usada nos quantitativos (resumo por hierarquia), na via preferencial e no raio das esquinas – o **raio de concordância das esquinas (guia)** pode ser informado na criação da via e alterado depois. |
| **Pavimento, interseções e rotatórias** | O **Sinalizar Via** gera o **pavimento** (asfalto, bloquete ou concreto) e **ajusta automaticamente os cruzamentos e entroncamentos** com as vias existentes — também ao mover ou editar eixos, e em vias de versões anteriores: qualquer ângulo (ortogonal, oblíquo, T, Y, vários ramos), esquinas com qualquer raio e meio-fio curvo, calçadas contornando a curva e canteiros refeitos, sinalização interrompida, vagas removidas a 5 m da esquina, faixas de pedestres e rampas; controle por **PARE / Dê a preferência / Semáforo** na via secundária (LRE/LDP, legendas, R-1/R-2) e os tipos **II – ilha gota**, **III – faixa de conversão livre com ilha** e **IV – bolsão de conversão à esquerda** (físicos ou pintados). **Rotatórias** com ilha central ajardinada, faixa galgável, 1–4 faixas, ilhas separadoras, dê a preferência, travessias e placas R-2/R-33. |
| **Calçadas** | **Orelha de calçada** (avanço sobre o estacionamento, em meio de quadra ou **contornando a esquina**, com curvas reversas ou chanfro, meio-fio, canteiro e árvores), **área de calçada por contorno** (esquinas, ilhas, parklets, ciclovia no nível da calçada, faixa de serviço, cantos arredondados), **canteiros/jardineiras/grelhas de árvore** e **cul-de-sac** (circular, excêntrico, gota, T, Y e L, com ilha e linha de bordo) – recortando automaticamente as marcas e calçadas sob eles. |
| **Detalhamento** | **Detalhar Placas**: na planta, cada placa aparece ampliada ao lado do suporte, com linha de chamada vermelha e código/nome (ex.: *R-1 Parada obrigatória*) – tamanhos em mm de papel, acompanhando a escala da vista e a edição da placa. **Anotar**: chamada com texto automático para qualquer sinalização (código, nome, largura, padrão, espaçamento…). **Quadro de Legenda**: amostra desenhada + descrição de cada tipo usado no projeto, atualizado automaticamente. **Mostrar/Ocultar Eixos** na vista ativa. **Cotar Seção** (cadeia de cotas automática das faixas, eixo a eixo), **Detalhe Típico** cotado (linhas, zebrados, vagas e placa em elevação), **Quadro de Quantitativos** e **Quadro de Placas** (com numeração P01, P02...) na prancha, **Notas Gerais** e **Norte**. |
| **Editar / Atualizar / 2D-3D / Selecionar conjunto** | Toda marca guarda sua definição: editar reabre a janela com os valores e regenera; converter entre 3D e 2D; selecionar a marca ou o grupo (ex.: a via inteira). |
| **Quantitativos** | **Separados por categoria** (horizontal, vertical, dispositivos, acessibilidade, calçadas/urbanização, moderação, mobiliário) com filtro, pesquisa e subtotais; área pintada por código, cor e material, extensão, unidades, consumo estimado de tinta/termoplástico e microesferas, exportação **CSV** (Excel) e **tabela nativa** do Revit. |
| **Configurações / Catálogo / Normas** | Preferências, catálogo normativo em JSON editável e referências normativas. |

### Paramétrico de verdade

* **Associativo**: as marcas ficam ligadas às linhas de modelo/detalhe usadas como caminho. Mova,
  estique ou edite o eixo e **toda a sinalização se regenera sozinha** (Dynamic Model Updater).
* **Editável**: cada elemento guarda a definição completa (Extensible Storage). *Editar* reabre a
  janela com os parâmetros originais.
* **Normativo e configurável**: dimensões, padrões de tracejado, cores, símbolos, vagas e materiais
  vêm de um **catálogo JSON** que você pode ajustar ao manual do seu órgão de trânsito.
* **Cópias inteligentes**: copiar/colar uma marca gera uma marca independente na nova posição.

### Representação no Revit

| Modo | Elemento | Uso |
|---|---|---|
| **Modelo 3D** (padrão) | *DirectShape* na categoria **Modelos genéricos**, sólido fino com material por cor (preenchimento sólido visível em planta) | Plantas, cortes, 3D, renderização, tabelas, filtros de vista. Pode **acompanhar Toposolid/pisos/topografia** (projeção sobre o greide). |
| **Detalhe 2D** | **Região preenchida** na vista ativa (tipos "SV - Branca", "SV - Amarela"...) | Pranchas de sinalização em vistas de planta ou de desenho. |
| **Detalhamento** | Regiões preenchidas + **textos** (tipos "SV - Texto 2.0 mm"...) + **linhas de detalhe** (estilo "SV - Chamada") na vista | Símbolos de placas, anotações e quadro de legenda – sempre ligados à marca de origem. |

Parâmetros compartilhados de instância (**SV_Codigo, SV_Descricao, SV_Grupo, SV_Cor, SV_Material,
SV_Area, SV_Extensao, SV_Quantidade, SV_Referencia, SV_Id**) são criados automaticamente, permitindo
tabelas, etiquetas e filtros de vista nativos.

---

## Instalação (copiar e colar – sem instalar nada)

A pasta **[`Instalar/Revit2027`](Instalar/Revit2027)** já traz o plugin compilado, com apenas **2 arquivos**:

| Arquivo | O que é |
|---|---|
| `SinalizaBIM.addin` | Manifesto que o Revit lê ao iniciar |
| `SinalizaBIM.dll` | O plugin completo (núcleo e bibliotecas incluídos numa única DLL) |

1. Feche o Revit.
2. Copie os **dois arquivos** para `%AppData%\Autodesk\Revit\Addins\2027`
   (ou `C:\ProgramData\Autodesk\Revit\Addins\2027` para todos os usuários). Eles devem ficar juntos
   na mesma pasta.
3. Se vieram da internet: botão direito na DLL → *Propriedades* → **Desbloquear**.
4. Abra o Revit 2027 e escolha **"Sempre carregar"** no aviso de add-in não assinado.

Não é necessário .NET SDK nem outro programa: o Revit 2027 já inclui o runtime .NET 10. A DLL foi
compilada contra a API da primeira versão do Revit 2027, portanto funciona em qualquer atualização
2027.x. Instruções detalhadas em [`Instalar/LEIA-ME.txt`](Instalar/LEIA-ME.txt).

Para atualizar, substitua os dois arquivos; para desinstalar, apague-os. (Opcionalmente,
`install.ps1` faz essa cópia automaticamente.)

> Para desenvolvedores: `dotnet build src/SinalizacaoViaria.Revit -c Release` (requer .NET 10 SDK)
> recompila e atualiza a pasta `Instalar/Revit2027`; no Windows também copia para a pasta de Add-ins.

---

## Início rápido

1. Numa **vista em planta**, desenhe o eixo da via com *Linha de modelo* (linhas, arcos, splines) ou
   use **Desenhar Eixo**.
2. **Sinalizar Via** → informe faixas, velocidade e tipo de eixo → *Criar* → selecione o eixo.
3. **Faixa de Pedestres** → *Criar* → clique os dois bordos da pista.
4. **Setas e Símbolos** / **Legendas** → clique a base e o sentido do tráfego.
5. Mova o eixo: tudo acompanha. Use **Editar** para mudar qualquer parâmetro.
6. **Quantitativos** → exporte o CSV ou crie a tabela no Revit.

Manual completo: [docs/MANUAL.md](docs/MANUAL.md) · Catálogo: [docs/CATALOGO.md](docs/CATALOGO.md) ·
Arquitetura: [docs/ARQUITETURA.md](docs/ARQUITETURA.md)

---

## Normas e responsabilidade técnica

Os valores padrão do catálogo foram compilados a partir do **MBST Vol. IV – Sinalização Horizontal**
(aprovado pela Resolução CONTRAN nº 236/2007) e de práticas usuais de projeto (DNIT, órgãos
municipais). Referências consideradas: MBST, CTB e resoluções do CONTRAN, Manual de Sinalização
Rodoviária do DNIT, ABNT NBR 9050, NBR 16537, NBR 14723, NBR 15405, NBR 15870 e NBR 14282.

**O plugin é uma ferramenta de produtividade; a responsabilidade pelo projeto é do profissional.**
Confira sempre a edição vigente do manual, as resoluções do CONTRAN/SENATRAN e as exigências do órgão
com circunscrição sobre a via. Todos os valores são editáveis no catálogo. Os valores marcados como
"exemplo" (ex.: sequências de LRV) devem ser ajustados ao estudo de cada local.

---

## Desenvolvimento

```
src/SinalizacaoViaria.Core     Núcleo independente do Revit (net10.0): geometria, catálogo, geradores,
                               definições paramétricas, automações, quantitativos. Roda em qualquer SO.
src/SinalizacaoViaria.Revit    Plugin (net10.0-windows, WPF): faixa de opções, comandos, janelas,
                               renderização DirectShape/FilledRegion, Extensible Storage, DMU.
                               Compila o núcleo junto, gerando uma DLL única.
Instalar/Revit2027             Pacote pronto: SinalizaBIM.dll + SinalizaBIM.addin.
tests/SinalizacaoViaria.Core.Tests   105 testes xUnit do núcleo.
```

```bash
dotnet test tests/SinalizacaoViaria.Core.Tests     # funciona em Windows, Linux ou macOS
dotnet build src/SinalizacaoViaria.Revit           # referências da API do Revit 2027 via NuGet
```
