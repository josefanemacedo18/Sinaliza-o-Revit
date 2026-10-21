# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-21 (fim do ciclo FC)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`).
- **Fases da missão:** quadro no ROADMAP. ✅ I, II, IX, XI; 🔄 III, IV (atual), V, VI, VII, X, XII, XIV (atual), XV;
  ⬜ VIII, XIII.
- **Ciclo FC (4º Autoteste completo, DLL do FB: 180 etapas em 36 min, 1 erro, 323 pares no modelo final):** tudo
  reproduzido no núcleo pelo arquivo de dados (`FCRevitFullRun4Tests` e as fixtures em `tests/…/Fixtures`):
  - rotatória oval sem as lascas que viravam arestas abaixo da tolerância na extrusão;
  - LBO e faixas de pedestres das rotatórias fora da sarjeta do perfil das vias; pisos da via recortados 1 mm além da zona;
  - via reconhecida em pisos com pavimento "Nenhum": a definição do pavimento fica como dado e o T é formado (D-014);
  - cruzamento rodoferroviário só nas faixas de rolamento (`RailCrossingSetup.FitTo`);
  - arcos das esquinas no centro exato apesar dos cortes sobre as cordas do Clipper (D-015); `AxisSnap` desliza a ponta
    do arco pela reta alinhada até a circunferência;
  - lombada invertida não recorta mais a sarjeta (a reta de 218 m fora do eixo); conferência final sem o ruído de 0°.
- **Ciclos anteriores:** EV1–EV6, EW1–EW3, EX, EY, EZ, FA e FB (leitura das marcas sem interpretar o que não precisa,
  D-013; tempo por trecho e arquivo de dados completo no Autoteste).
- **Revit:** dois Autotestes rápidos e quatro **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças do FC ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 869/2 869 (FC: +13 `FCRevitFullRun4Tests`).

## Próxima tarefa

1. **5º Autoteste completo com a DLL do FC**, numa cópia, terminando com **Desfazer tudo**. Enviar o relatório
   `autoteste-….txt` **e** o arquivo `autoteste-…-dados.jsonl` (`%AppData%\SinalizaBIM`). Conferir:
   - rotatória oval sem erro; etapa "Modelo final" com retas fora do eixo (esperado zero) e os pares restantes por tipo;
   - queda de FTP-1/LBO × ROTATORIA, INTERSECAO × INTERSECAO, os pares do T da via reconhecida e do cruzamento
     rodoferroviário;
   - reproduzir no núcleo cada par restante pelo arquivo de dados.
2. **K11 – abertos:** MER-0 × ORELHA (1 par, 55 L) e FTP-1 × ZPA (4 pares) – reproduzir pelo arquivo de dados.
3. **K13**: emenda em curva (2,8 L nas transições das pontas) e rotatórias; depois, ajuste topológico do contorno.
4. **F7b:** tabela de volumes por estaca no quantitativo de terraplenagem do Revit.

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
