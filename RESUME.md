# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-20 (fim do ciclo EY)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`).
- **Ciclos concluídos:** EV1–EV6, EW1–EW3, EX e EY (Autoteste completo):
  - perfis de placas aceitos pelo Revit (`ProfileCleanup`);
  - retas alinhadas pelo critério do Revit, até 0,2° e incluindo 45° (D-010);
  - sobreposições reproduzidas no núcleo (`PieceOverlaps`) e corrigidas: vagas, ciclofaixas, canteiros e faixa × vagas;
  - relatório com todos os pares persistentes e a medida das retas.
- **Revit:** dois Autotestes rápidos e um **completo** (178 etapas, 1 erro, corrigido em EY), mais um Validar Modelo.
  - Relatórios em `docs/evidencias/`.
  - As mudanças de EY do lado do Revit ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 800/2 800 (EY: +15 `EYRevitFullRunTests`).

## Próxima tarefa

1. **Novo Autoteste completo do usuário com a DLL do EY**, numa cópia, terminando com **Desfazer tudo**. Conferir:
   - placas sem erro (EY1);
   - a nota "Retas fora do eixo lidas no Revit" de cada etapa: o que sobrar mostra desalinhamento e deslocamento (K12);
   - a nota "Pares que continuam sobrepostos, por tipo": reproduzir cada tipo no núcleo com `PieceOverlaps` e corrigir (K11).
2. **K13**: ajuste topológico do contorno. Cada borda compartilhada entre pisos da mesma marca é ajustada uma vez, sem lascas
   entre vizinhos. Medir com a sondagem do EY (pares a mais depois do ajuste).
3. **F7b:** tabela de volumes por estaca no quantitativo de terraplenagem do Revit.
4. **F3:** catálogo de placas personalizadas por documento (resolve K1).

## Como retomar

```bash
git checkout claude/dreamy-gates-a3nrta && git pull origin claude/dreamy-gates-a3nrta
dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true   # gera Instalar/Revit2027/SinalizaBIM.dll
dotnet test tests/SinalizacaoViaria.Core.Tests -c Release                            # ~15–22 min
```

Leia nesta ordem: AUDIT.md (estado e evidências) → KNOWN_ISSUES.md → ROADMAP.md → DECISIONS.md.

## Regras de trabalho em vigor

- Commits em nome de Josefane Macedo, com push para o ramo acima. Não abrir PR sem pedido.
- Antes de mudar o formato de uma definição: `CurrentVersion` + passo em `DefinitionVersioning.Passos` + teste.
- Nada de norma inventada: valor normativo só com a fonte citada no código ou no catálogo.
- Nunca alterar modelos reais do usuário. O Autoteste roda numa área de teste e desfaz tudo.
