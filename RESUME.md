# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-20 (fim do ciclo EZ)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`).
- **Fases da missão:** quadro no ROADMAP. ✅ I, II, IX, XI; 🔄 III, IV (atual), V, VI, VII, X, XII, XIV (atual), XV;
  ⬜ VIII, XIII.
- **Ciclos concluídos:** EV1–EV6, EW1–EW3, EX, EY e EZ (2º Autoteste completo):
  - as 85 retas fora do eixo que sobraram reproduzidas e corrigidas no núcleo (zebrados a 45°, pisos no terreno, retas das
    rotatórias, via reconhecida em pisos);
  - triângulo "dê a preferência" e faixa de pedestres das rotatórias sem pintura sobre pintura;
  - tipos de piso do plugin com estrutura diferente do nome corrigidos ao serem usados (D-011, hipótese para os 149 pares
    ROTATORIA × ROTATORIA).
- **Revit:** dois Autotestes rápidos e dois **completos** (o 2º com 0 erros), mais um Validar Modelo.
  - Relatórios em `docs/evidencias/`.
  - As mudanças de EZ ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 825/2 825 (EZ: +25 `EZRevitFullRun2Tests`).

## Próxima tarefa

1. **Novo Autoteste completo do usuário com a DLL do EZ**, numa cópia, terminando com **Desfazer tudo**. Conferir:
   - a Preparação: lista de tipos de piso com estrutura diferente do nome (D-011);
   - "Retas fora do eixo lidas no Revit": esperado zero ou só retas de projeto (K12);
   - "Pares que continuam sobrepostos, por tipo": ROTATORIA × ROTATORIA (hipótese do tipo de piso), FTP-1 × ROTATORIA,
     LBO × ROTATORIA e MER-0 × SARJETA, com as cotas de base e topo dos exemplos (K11).
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
