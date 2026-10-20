# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-20 (fim do ciclo EX)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`), aprovada pelo usuário.
- **Ciclos concluídos:** EV1–EV6, EW1–EW3 e EX:
  - Marca dos lotes única com mais de um loteamento (D-009);
  - alinhamento aos eixos também nos lotes, áreas remanescentes, acabamentos do terreno e contorno só de retas;
  - Autoteste com resumo dos avisos por elemento e conferência das sobreposições no fim da etapa.
- **Revit:** dois Autotestes **rápidos** registrados (DLL 2.0.0 e 2.1.0), ambos com 0 erros, e um Validar Modelo.
  - Relatórios em `docs/evidencias/`.
  - O modo **completo** ainda não foi rodado.
  - As mudanças de EX do lado do Revit ainda não rodaram no Revit.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:** suíte completa 2 785/2 785 (EX: +10 `EXRevitWarningsTests`).

## Próxima tarefa

1. **Aguardar o próximo Autoteste do usuário** com a DLL do EX, de preferência o **completo**, numa cópia do projeto.
   Conferir no relatório:
   - "Marca" duplicada: deve sumir (D-009);
   - "ligeiramente fora da linha central": o resumo diz em que pisos ficaram (K12). Se ainda houver, reproduzir no núcleo
     com o código dos pisos;
   - "Pisos realçadas se sobrepõem": a linha "Pisos sobrepostos conferidos no fim da etapa" decide o K11.
     - Se os pares não se sobrepõem mais (de passagem): criar a via e o recorte da interseção sem a gravação intermediária
       sobreposta (`CmdSinalizarVia.Create` → `IntersectionRunner.Run`).
     - Se continuam sobrepostos: reproduzir no núcleo com os códigos e as marcas do relatório e corrigir.
2. **F7b:** tabela de volumes por estaca no quantitativo de terraplenagem do Revit (`SectionVolumes` já está no Core).
3. **F3:** catálogo de placas personalizadas por documento (resolve K1).

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
