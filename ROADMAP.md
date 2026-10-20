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
