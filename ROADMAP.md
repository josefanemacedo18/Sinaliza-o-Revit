# ROADMAP – Plano de evolução

O estado atual de cada item está em [AUDIT.md](AUDIT.md) §5. Este roteiro ordena o que falta pela prioridade da missão:
1. segurança de dados;
2. defeitos críticos;
3. compilação;
4. integridade geométrica;
5. testes;
6. arquitetura;
7. infraestrutura;
8. interoperabilidade com o Revit;
9. desempenho;
10. usabilidade;
11. simulação e otimização;
12. extras.

Cada etapa só é marcada como concluída com compilação limpa, suíte verde e registro no CHANGELOG.

## Situação das 15 fases da missão

Atualizada a cada ciclo. ✅ concluída (critério da fase atendido e comprovado no ambiente disponível: núcleo, testes e
compilação aqui e no CI Windows), 🔄 em andamento ou parcial, ⬜ não iniciada. **Fase atual** = onde está o trabalho do
ciclo em curso. O Revit não roda neste ambiente (TESTS.md): a coluna "Revit 2027" diz se a fase já passou por um Autoteste
no Revit; cada fase concluída gera uma DLL em `Releases/FaseNN` (ver `Releases/README.md`).

| Fase | Situação | Feito | Falta para o ✅ | Revit 2027 |
|---|---|---|---|---|
| I – Auditoria técnica completa | ✅ | AUDIT.md: inventário, riscos, matriz de funcionalidades com evidências, linha de base (EV1) | — | validada (Autoteste rápido) |
| II – Estabilização e linha de base | ✅ | compilação sem avisos, testes do núcleo, pacotes fixados, procedimento de versão (ARCHITECTURE §7), limitações do ambiente registradas; DLL carregada no Revit 2027 (EV2–EV3, EW1) | — | validada (Autoteste rápido) |
| III – Arquitetura integrada | ✅ | dados versionados com migrações, grafo de dependências, validação antes de aplicar, identidade persistente (EV3–EV4); ordem de regeneração pelo grafo (`ModelGraph.RegenerationSort`, D-004) e placas personalizadas só nas listas do projeto que as tem (K1) – Fase III | — | DLL 2.2.0 (`Releases/Fase03`) não executada no Revit |
| IV – Motor geométrico e topologia | ✅ | casos degenerados, `PolygonChecks`, `AxisSnap` (contornos, linhas abertas e trechos com pontas presas), `ProfileCleanup`, `PieceOverlaps`; simplificação e ajuste do contorno sem perder o fim exato das retas; ajuste conferido (`FitRing`) e união de pisos sem contornos que se tocam (EV5, EW2, EY, EZ, FA); arcos das esquinas no centro exato (D-015, FC); **ajuste topológico** dos pisos de cada marca (`BoundaryFit.FitRings`, D-016): bordas comuns ajustadas uma vez, emenda em curva e rotatórias sem lascas na fixture do 4º Autoteste; MER-0 × ORELHA e FTP-1 × ZPA corrigidos – Fase IV | — | DLL 2.3.0 (`Releases/Fase04`) não executada no Revit |
| V – Alinhamentos e perfis | ✅ | elementos do traçado, do greide e do eixo das vias comuns, com CSV (EV5, EW3); limites geométricos informados pelo usuário com a fonte (raio, tangente, deflexão sem curva, rampa, K, curva vertical): `GeometricCheck`, CSV de verificação no Perfil da Via e V12 no Validar Modelo, sem valor normativo no plugin (K8) – Fase V | — (distância de visibilidade: K8) | DLL 2.4.0 (`Releases/Fase05`) não executada no Revit |
| VI – Corredores, seções e pavimentação | ✅ | seções, larguras variáveis, pisos no greide (rodadas anteriores); todos os modelos de via sem pisos sobrepostos (EY); recuos (baias, aceleração/desaceleração) sem sobreposição, com a calçada absorvendo até sumir e o alinhamento avançando com aviso (FA); quantitativo por componente da seção (pavimento, meio-fio com extensão, sarjeta, calçada, gramado, ilhas) medido nas peças de vias, interseções, rotatórias, extensões e balões; regras da regeneração no núcleo (`ModelGraph.ConnectionsToRefresh`, `IntersectionGenerator.FollowNode`) com eixo movido e seção alterada conferidos – Fase VI | — (atualização pelo updater no Revit: K17) | DLL 2.5.0 (`Releases/Fase06`) não executada no Revit |
| VII – Terreno, terraplenagem e interseções | ✅ | volumes pela malha e pelo método das seções no núcleo (EV6); sinalização das entradas das rotatórias sem pintura sobre pintura (EZ); pintura das rotatórias fora da sarjeta, T de via reconhecida em pisos e cruzamento rodoferroviário só nas faixas de rolamento (FC); rotatória com by-pass sem pisos sobrepostos no núcleo (K16) e volumes por estaca de cada via no quantitativo, ao lado da malha (F7b) – Fase VII | — (conferência no próximo Autoteste: K11, K16, K7) | DLL 2.6.0 (`Releases/Fase07`) não executada no Revit |
| VIII – Drenagem e redes | ✅ | dispositivos (rodadas anteriores); rede de trechos entre caixas (geometria, sentido, ligações, ciclos, recobrimento, diâmetros, PVs); vazão pelo método racional e galerias por Manning a seção plena, sarjetas e bocas de lobo, com as fontes enviadas pelo usuário (Manual SUDERHSA 2002 e memorial modelo de Miguelópolis/SP 2019; D-019); Projeto de Drenagem por documento (D-020), Relatório da Rede com a planilha de galerias e os diâmetros calculados, V13 e Memorial de Drenagem (.docx) – Fase VIII | — (George Ribeiro, divisão de vazões e excesso entre bocas de lobo: K5) | DLL 2.7.0 (`Releases/Fase08`) não executada no Revit |
| IX – Quadras, lotes e parcelamento | ✅ | parcelamento completo no núcleo, sobras e mínimos configuráveis, quadros e memorial, Marca única (EX), validação V09–V10; etapas do Autoteste sem erro no Revit | — | validada (Autotestes completos) |
| X – Compatibilização | 🔄 **atual** | `ModelValidator` V01–V13 com severidade, confiança e revisão humana; comando Validar Modelo no Revit (EV4–EV5) | verificações sobre a geometria gerada (F10: pintura fora do pavimento, lote × calçada, pisos sobrepostos classificados, pisos suspensos ou enterrados – RT-1) | Validar Modelo (2026-10-19) |
| XI – Mobilidade e tráfego | ✅ | simulador auditado (AUDIT §6.11): modelo, hipóteses e limites registrados; não calibrado, sem dados de campo (K6) | — (calibração depende de contagens reais) | validada (Autoteste) |
| XII – Cenários e otimização | 🔄 | cenários e soluções de tráfego (rodadas anteriores) | cenários de projeto comparados por quantitativos (F12) | — |
| XIII – Interface e experiência | ⬜ | janelas existentes (rodadas anteriores), sem trabalho desta missão | revisão das mensagens e fluxos com base no uso real | — |
| XIV – Regras da API do Revit | 🔄 | transações e falhas tratadas; avisos do Revit medidos e reduzidos pelos Autotestes (EW–FC): retas fora do eixo 1 385 → 85 → 8 → 1 (corrigida no FC); tipo de piso do usuário só onde ele trocou (D-012); forma direta confere cada objeto; rotatória oval sem lascas na extrusão (FC) | Autoteste completo sem pares persistentes nem retas fora do eixo evitáveis | 4 Autotestes completos; FC pendente |
| XV – Testes, desempenho, resiliência e entrega | 🔄 | 2 869 testes do núcleo; seis Autotestes no Revit, quatro completos (o 4º em 36 min, era 1 h 41 min); leitura das marcas sem interpretar o que não precisa (D-013), recortes só perto do contorno, tempo por trecho do plugin e arquivo de dados completo no Autoteste, conferência final de todos os pisos (FB); 4º Autoteste reproduzido no núcleo pelo arquivo de dados (FC) | etapas que restarem lentas (K14, K15); pacote de entrega final | 4 Autotestes completos |

## Concluído

| Etapa | Entrega | Evidência |
|---|---|---|
| EV1 | Auditoria completa com matriz de funcionalidades | AUDIT.md |
| EV2 | Documentos de controle (este conjunto) | raiz do repositório |
| EV3 | Proteção de dados de versão mais nova, base de migrações, log com rotação, capturas que afetam resultado registradas, avisos de compilação zerados, pacotes de teste fixados | DECISIONS D-003, D-006; `EVModelIntegrityTests` |
| EV4 | Grafo de dependências (`ModelGraph`), validação do modelo (`ModelValidator`, V01–V08), contornos (`PolygonChecks`), comando **Validar Modelo** (somente leitura) | DECISIONS D-004, D-005; `EVModelIntegrityTests` |
| EV5 / F4 | Primitivas geométricas em casos degenerados: sem NaN/∞, pontas preservadas, contrato da projeção documentado | `EVGeometryEdgeTests` |
| EV5 / F5 | Elementos do traçado (TS/SC/CS/ST) e do greide (PIV, L, K, PCV/PTV, alto/baixo), com CSV; Perfil da Via grava o CSV do greide | `EVAlignmentElementsTests`, MANUAL §24.5 |
| EV5 / F15 | Procedimento de versão documentado (ARCHITECTURE §7) | — |
| EV5 / F10 (parte) | Validador: quadras sobrepostas (V09), quadra fora da gleba (V10), interseção sem duas vias (V11) | `EVModelIntegrityTests` |
| EV6 / F7 (Core) | Volumes pelo método das seções transversais, conferidos com a malha | `EVSectionVolumesTests` |
| EW1 | Primeira evidência no Revit 2027 (Autoteste rápido, Validar Modelo); versão 2.1.0; V06 da gleba recalibrado | `docs/evidencias`, `EVModelIntegrityTests` |
| EW2 | Retas quase alinhadas aos eixos exatas no contorno dos pisos (aviso do Revit reproduzido e corrigido); Autoteste identifica os pisos dos avisos | `EWRevitWarningsTests`, DECISIONS D-008 |
| EW3 / F5b | Elementos do eixo das vias comuns (tangentes, curvas, cantos), CSV no Perfil da Via | `EWAxisElementsTests` |
| EY | Autoteste completo (178 etapas): perfis de placas aceitos pelo Revit; retas alinhadas pelo critério do Revit (0,2°, com 45°); sobreposições reproduzidas no núcleo (`PieceOverlaps`) e corrigidas (vagas × canteiro lateral, ciclofaixa, canteiros com árvore, faixa × vagas); relatório com todos os pares persistentes e a medida das retas | `EYRevitFullRunTests`, DECISIONS D-010 |
| FB | Autoteste completo mais rápido e com tudo numa rodada: leitura das marcas pelo id, pelo tipo no começo do JSON e pelo id citado (D-013); "é filha de um nó?" e consultas dos geradores sem ler o projeto inteiro; recortes só perto do contorno; etapas pesadas antes dos catálogos; tempo por trecho; arquivo `-dados.jsonl` (todos os avisos, pares, retas, contornos dos pisos e definições) e etapa "Modelo final" | `FBAutotestSpeedTests`, CHANGELOG FB |
| FC | 4º Autoteste completo (36 min, 1 erro, 323 pares no modelo final) reproduzido no núcleo pelo arquivo de dados: rotatória oval sem lascas na extrusão; LBO e faixas de pedestres das rotatórias fora da sarjeta; via reconhecida em pisos com pavimento "Nenhum" forma a interseção (D-014); cruzamento rodoferroviário só nas faixas de rolamento; arcos das esquinas no centro exato (D-015: 2,7–11,5 L → 0–31 cm³ por interseção); reta da sarjeta fora do eixo pela lombada invertida | `FCRevitFullRun4Tests`, `docs/evidencias/2026-10-21-autoteste-completo-4.txt` |
| FA | 3º Autoteste completo (1 erro, 8 retas, 590 pares com cotas): tipo de piso do usuário só onde ele trocou e por espessura (gola da árvore e sarjeta com o tipo de 15 cm do meio-fio); gola em peça única, união dos pisos e ajuste conferidos; forma direta confere cada objeto; espinhos cortados no lado longo; recuos sem sobreposição | `FARevitFullRun3Tests`, DECISIONS D-012 |
| EZ | 2º Autoteste completo (0 erros, 85 retas fora do eixo, 622 pares persistentes por tipo): as 85 retas reproduzidas e corrigidas no núcleo (zebrados a 45°, pisos no terreno, retas das rotatórias, via reconhecida); triângulo "dê a preferência" e faixa das rotatórias sem pintura sobre pintura; tipos de piso do plugin com a espessura do nome | `EZRevitFullRun2Tests`, DECISIONS D-011 |
| EX | Segundo Autoteste (2.1.0): Marca dos lotes única com vários loteamentos; alinhamento aos eixos também nos lotes, áreas remanescentes, acabamentos do terreno e contornos só de retas; resumo de avisos por elemento e conferência das sobreposições no fim da etapa | `EXRevitWarningsTests`, DECISIONS D-009 |
| VIII / F8 | Rede de drenagem: trechos entre as caixas, conferência da geometria e critérios; vazão pelo método racional, galerias, sarjetas e bocas de lobo; Projeto de Drenagem, Relatório da Rede, V13 e Memorial de Drenagem – com os exemplos do Manual SUDERHSA e a planilha do memorial modelo reproduzidos | `Fase08DrenagemTests`, `Fase08HidraulicaTests`, DECISIONS D-019 e D-020 |

## Próximas etapas (em ordem)

| # | Fase da missão | Etapa | Critério de aceite |
|---|---|---|---|
| K11 | XIV – Revit | Pares persistentes restantes: com a lista por tipo do próximo Autoteste, reproduzir cada tipo no núcleo (`PieceOverlaps`) e corrigir | Autoteste sem par persistente |
| K12 | XIV – Revit | Confirmar no próximo Autoteste que as retas fora do eixo sumiram (a nota mede as que restarem) | Autoteste sem esse aviso, ou só retas de projeto |
| F10 | X – compatibilização | Verificações que precisam da geometria gerada (lotes sobre a calçada, obra × via, pintura fora do pavimento): exigem rodar os geradores no validador ou no Autoteste | testes por código Vxx |
| F11 | XI – tráfego | Separar no relatório o que é simulação e o que é animação; registrar parâmetros e sementes usados | relatório reprodutível |
| F12 | XII – cenários | Cenários de projeto (variantes de seção e de greide) comparados por quantitativos | testes de comparação |

## Fora do escopo até haver especificação

- Aprovação legal de loteamento, critérios de visibilidade sem norma citada e calibração de tráfego sem dados de campo.
  Nada disso é inventado.
- Drenagem além das fontes enviadas pelo usuário: macrodrenagem com hidrograma (HUT do SCS, que o manual descreve para
  áreas acima de 2 km²), reservatórios de detenção e o tempo de concentração de George Ribeiro (citado no memorial modelo
  sem a expressão).

## Requisito transversal RT-1 – Integração entre Toposolid, vias, terraplenagem e drenagem

Registrado em 2026-10-22, a pedido do usuário, durante a Fase VIII (que seguiu sem ser interrompida nem refeita). Objetivo:
o terreno projetado (Toposolid), os pisos das vias (Floors), a terraplenagem e a drenagem funcionarem como um corredor
viário coordenado com a API do Revit 2027 – sem vias suspensas ou enterradas, sem lacunas ou degraus nas bordas, com
taludes ligados ao terreno, sem descontinuidades nos cruzamentos, rotatórias e acessos e sem pontos baixos sem captação.
As vias continuam pisos; o terreno original é preservado para comparação e volumes. Não reinicia fases: são dependências
técnicas entre IV, V, VI, VII, VIII, XIV e XV, tratadas na ordem do roteiro.

| Parte | Onde | O que existe hoje (a confirmar na auditoria) | A fazer | Verificação |
|---|---|---|---|---|
| RT-1.1 Auditoria | depois da entrega da Fase VIII | terraplenagem no Toposolid (`TerrainService`, `TerrainModel`, `Grading`), pisos no greide, terreno original guardado | levantar no código (arquivo e linha) como os Toposolids são criados, lidos e alterados, quais métodos da API 2027 são usados, como os pisos seguem o greide, como cortes, aterros e taludes são feitos e o que a drenagem lê do terreno e da via; registrar no AUDIT | AUDIT com evidências; nenhum método da API presumido |
| RT-1.2 Pisos suspensos ou enterrados, bordas e degraus | Fase X (F10) | conferência dos pisos sobrepostos no Autoteste | conferir a face superior dos pisos da via contra a superfície de projeto e o terreno nas bordas (folga, degrau, piso abaixo do terreno) | testes no núcleo; nota no Autoteste |
| RT-1.3 Classificação dos pisos sobrepostos | Fases X e XIV | `PieceOverlaps` e pares por tipo no Autoteste: 622 persistentes no 2º Autoteste completo (soma das etapas), 590 no 3º, 323 no modelo final do 4º | separar camadas construtivas intencionais, interseções legítimas, duplicações acidentais, conflitos reais e casos inconclusivos; corrigir só os conflitos reais, pela causa | testes com as fixtures dos Autotestes; nada apagado em massa |
| RT-1.4 Superfície projetada e transições | Fase VII (complemento) e XIV | plataformas, taludes de corte e aterro e reaterros no Toposolid | conferir os encontros pavimento, calçada e talude × terreno, as emendas entre segmentos e os taludes por parâmetro (sem inclinação universal) | testes no núcleo |
| RT-1.5 Drenagem × greide e terreno | Fase VIII (complemento) e X | rede, captações e cálculo da Fase VIII (área e declividade da sarjeta informadas por dispositivo) | ler da via e do terreno projetado a declividade da sarjeta e a área de contribuição; apontar pontos baixos sem captação e depressões criadas pela via | testes no núcleo |
| RT-1.6 API e validação integrada | Fases XIV e XV | transações e falhas tratadas; quatro Autotestes completos | operações do Toposolid e dos pisos conferidas na documentação da API 2027; etapa integrada no Autoteste | Autoteste no Revit (não executável neste ambiente) |

