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

Atualizada a cada ciclo. ✅ concluída (critério da fase atendido e comprovado), 🔄 em andamento ou parcial, ⬜ não
iniciada. **Fase atual** = onde está o trabalho do ciclo em curso.

| Fase | Situação | Feito | Falta para o ✅ |
|---|---|---|---|
| I – Auditoria técnica completa | ✅ | AUDIT.md: inventário, riscos, matriz de funcionalidades com evidências, linha de base (EV1) | — |
| II – Estabilização e linha de base | ✅ | compilação sem avisos, testes do núcleo, pacotes fixados, procedimento de versão (ARCHITECTURE §7), limitações do ambiente registradas; DLL carregada no Revit 2027 (EV2–EV3, EW1) | — |
| III – Arquitetura integrada | 🔄 | dados versionados com migrações, grafo de dependências, validação antes de aplicar, identidade persistente (EV3–EV4) | usar o grafo na regeneração (D-004) e catálogo por documento (F3, K1) – só com Autoteste no Revit |
| IV – Motor geométrico e topologia | 🔄 **atual** | casos degenerados, `PolygonChecks`, `AxisSnap` (contornos e linhas abertas; ponta de arco desliza pela reta até a circunferência), `ProfileCleanup` (espinhos cortados no lado longo), `PieceOverlaps`; simplificação e ajuste do contorno sem perder o fim exato das retas; ajuste conferido (`FitRing`) e união de pisos sem contornos que se tocam (EV5, EW2, EY, EZ, FA); arcos das esquinas no centro exato apesar dos cortes sobre as cordas (D-015, FC) | sobreposições restantes do Autoteste (K11) e ajuste topológico do contorno (K13: emenda em curva e rotatórias) |
| V – Alinhamentos e perfis | 🔄 | elementos do traçado, do greide e do eixo das vias comuns, com CSV (EV5, EW3) | restrições geométricas configuráveis (raio e K mínimos informados pelo usuário, sem norma inventada) com relatório (K8) |
| VI – Corredores, seções e pavimentação | 🔄 | seções, larguras variáveis, pisos no greide (rodadas anteriores); todos os modelos de via sem pisos sobrepostos (EY); recuos (baias, aceleração/desaceleração) sem sobreposição, com a calçada absorvendo até sumir e o alinhamento avançando com aviso (FA) | conferir no Revit a atualização após mudar eixo/perfil/seção; quantitativo por componente |
| VII – Terreno, terraplenagem e interseções | 🔄 | volumes pela malha e pelo método das seções no núcleo (EV6); sinalização das entradas das rotatórias sem pintura sobre pintura (EZ); pintura das rotatórias fora da sarjeta, T de via reconhecida em pisos e cruzamento rodoferroviário só nas faixas de rolamento (FC) | tabela por estaca no Revit (F7b); interseções e rotatórias sem sobreposições no Autoteste (K11) |
| VIII – Drenagem e redes | ⬜ | só a geometria dos dispositivos (rodadas anteriores) | rede geométrica de trechos e PVs (F8), sem dimensionamento hidráulico inventado |
| IX – Quadras, lotes e parcelamento | ✅ | parcelamento completo no núcleo, sobras e mínimos configuráveis, quadros e memorial, Marca única (EX), validação V09–V10; etapas do Autoteste sem erro no Revit | — |
| X – Compatibilização | 🔄 | `ModelValidator` V01–V11 com severidade, confiança e revisão humana; comando Validar Modelo no Revit (EV4–EV5) | verificações sobre a geometria gerada (F10: pintura fora do pavimento, lote × calçada, pisos sobrepostos) |
| XI – Mobilidade e tráfego | ✅ | simulador auditado (AUDIT §6.11): modelo, hipóteses e limites registrados; não calibrado, sem dados de campo (K6) | — (calibração depende de contagens reais) |
| XII – Cenários e otimização | 🔄 | cenários e soluções de tráfego (rodadas anteriores) | cenários de projeto comparados por quantitativos (F12) |
| XIII – Interface e experiência | ⬜ | janelas existentes (rodadas anteriores), sem trabalho desta missão | revisão das mensagens e fluxos com base no uso real |
| XIV – Regras da API do Revit | 🔄 **atual** | transações e falhas tratadas; avisos do Revit medidos e reduzidos pelos Autotestes (EW–FC): retas fora do eixo 1 385 → 85 → 8 → 1 (corrigida no FC); tipo de piso do usuário só onde ele trocou (D-012); forma direta confere cada objeto; rotatória oval sem lascas na extrusão (FC) | Autoteste completo sem pares persistentes nem retas fora do eixo evitáveis |
| XV – Testes, desempenho, resiliência e entrega | 🔄 | 2 869 testes do núcleo; seis Autotestes no Revit, quatro completos (o 4º em 36 min, era 1 h 41 min); leitura das marcas sem interpretar o que não precisa (D-013), recortes só perto do contorno, tempo por trecho do plugin e arquivo de dados completo no Autoteste, conferência final de todos os pisos (FB); 4º Autoteste reproduzido no núcleo pelo arquivo de dados (FC) | etapas que restarem lentas (K14, K15); pacote de entrega final |

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

## Próximas etapas (em ordem)

| # | Fase da missão | Etapa | Critério de aceite |
|---|---|---|---|
| K11 | XIV – Revit | Pares persistentes restantes: com a lista por tipo do próximo Autoteste, reproduzir cada tipo no núcleo (`PieceOverlaps`) e corrigir | Autoteste sem par persistente |
| K13 | IV – geometria | Ajuste topológico do contorno: cada borda compartilhada entre pisos da mesma marca ajustada uma vez (sem lascas entre vizinhos) | sondagem do núcleo sem pares a mais depois do ajuste |
| K12 | XIV – Revit | Confirmar no próximo Autoteste que as retas fora do eixo sumiram (a nota mede as que restarem) | Autoteste sem esse aviso, ou só retas de projeto |
| F3 | III – arquitetura | Catálogo por documento (resolve K1); avaliar trocar `DependencyOrder` pelo `ModelGraph` **só com Autoteste no Revit** | Autoteste do usuário sem regressão |
| F7b | VII – terraplenagem | Tabela por estaca no quantitativo de terraplenagem do Revit (seções sobre a superfície de projeto e o Toposolid), ao lado da malha | Autoteste com os dois volumes |
| F10 | X – compatibilização | Verificações que precisam da geometria gerada (lotes sobre a calçada, obra × via, pintura fora do pavimento): exigem rodar os geradores no validador ou no Autoteste | testes por código Vxx |
| F8 | VIII – drenagem | Rede (trechos e PVs ligados) só geométrica: declividade, cobrimento e sentido, com regras configuráveis **e fonte citada**; sem dimensionamento hidráulico até haver especificação | testes de geometria; nada de vazão sem método citado |
| F11 | XI – tráfego | Separar no relatório o que é simulação e o que é animação; registrar parâmetros e sementes usados | relatório reprodutível |
| F12 | XII – cenários | Cenários de projeto (variantes de seção e de greide) comparados por quantitativos | testes de comparação |

## Fora do escopo até haver especificação

- Dimensionamento hidráulico, aprovação legal de loteamento, critérios de visibilidade sem norma citada e calibração
  de tráfego sem dados de campo. Nada disso é inventado.
