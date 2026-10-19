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

## Próximas etapas (em ordem)

| # | Fase da missão | Etapa | Critério de aceite |
|---|---|---|---|
| F5b | V – alinhamentos | Elementos do eixo das **vias** (desenhado no Revit: retas e arcos de raio de canto), não só do traçado por PIs dos nós viários | testes com eixos de retas + arcos conhecidos |
| F3 | III – arquitetura | Catálogo por documento (resolve K1); avaliar trocar `DependencyOrder` pelo `ModelGraph` **só com Autoteste no Revit** | Autoteste do usuário sem regressão |
| F7 | VII – terraplenagem | Volumes por estaca (seções) ao lado da malha, com a diferença entre métodos | teste com sólido de volume conhecido |
| F10 | X – compatibilização | Verificações que precisam da geometria gerada (lotes sobre a calçada, obra × via, pintura fora do pavimento): exigem rodar os geradores no validador ou no Autoteste | testes por código Vxx |
| F8 | VIII – drenagem | Rede (trechos e PVs ligados) só geométrica: declividade, cobrimento e sentido, com regras configuráveis **e fonte citada**; sem dimensionamento hidráulico até haver especificação | testes de geometria; nada de vazão sem método citado |
| F11 | XI – tráfego | Separar no relatório o que é simulação e o que é animação; registrar parâmetros e sementes usados | relatório reprodutível |
| F12 | XII – cenários | Cenários de projeto (variantes de seção e de greide) comparados por quantitativos | testes de comparação |

## Fora do escopo até haver especificação

- Dimensionamento hidráulico, aprovação legal de loteamento, critérios de visibilidade sem norma citada e calibração
  de tráfego sem dados de campo. Nada disso é inventado.
