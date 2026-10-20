# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-20 (fim do ciclo FA)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`).
- **Fases da missão:** quadro no ROADMAP. ✅ I, II, IX, XI; 🔄 III, IV (atual), V, VI, VII, X, XII, XIV (atual), XV;
  ⬜ VIII, XIII.
- **Ciclos concluídos:** EV1–EV6, EW1–EW3, EX, EY, EZ e FA (3º Autoteste completo):
  - tipo de piso do usuário só onde ele trocou e por espessura (D-012) – a causa dos 150 pares das rotatórias e dos pares
    sobre a sarjeta;
  - gola das árvores em peça única, união dos pisos sem contornos que se tocam, ajuste do contorno conferido (`FitRing`),
    forma direta que confere cada objeto (o erro da rotatória oval);
  - espinhos cortados no lado longo (as 8 retas da rotatória com by-pass);
  - recuos sem sobreposição (calçada absorve até sumir; faixas físicas com bordas na normal do eixo).
- **Revit:** dois Autotestes rápidos e três **completos**, mais um Validar Modelo. Relatórios em `docs/evidencias/`. As
  mudanças de FA ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 843/2 843 (FA: +18 `FARevitFullRun3Tests`).

## Próxima tarefa

1. **Novo Autoteste completo do usuário com a DLL do FA**, numa cópia, terminando com **Desfazer tudo**. Conferir:
   - rotatória oval sem erro; se algum sólido for recusado, o aviso traz a posição;
   - "Retas fora do eixo lidas no Revit": esperado zero;
   - "Pares que continuam sobrepostos, por tipo": esperada a queda de ROTATORIA × ROTATORIA, FTP-1/LBO × ROTATORIA,
     INTERSECAO × INTERSECAO e dos pisos das etapas de recuos.
2. **K11 – recortes de pintura:** MAC desenhada sobre a extensão de calçada, LRE × vagas, FTP-1 × ZPA-A e × LCO nas
   interseções, vaga × sarjeta.
3. **K13**: ajuste topológico do contorno (bordas compartilhadas ajustadas uma vez).
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
