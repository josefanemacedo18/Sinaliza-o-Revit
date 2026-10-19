# RESUME – Ponto exato de retomada

Atualize este arquivo ao fim de cada ciclo de trabalho.

## Estado em 2026-10-19 (fim do ciclo EW)

- **Ramo:** `claude/dreamy-gates-a3nrta`. Sem PR aberto (o fluxo do projeto é commit e push direto no ramo).
- **Versão do plugin:** 2.1.0 (`Directory.Build.props`), aprovada pelo usuário.
- **Ciclos concluídos:** EV1–EV6 (auditoria, segurança de dados, validação do modelo, geometria, alinhamentos, volumes) e
  EW1–EW3:
  - evidência do Revit;
  - V06 da gleba recalibrado;
  - retas quase alinhadas aos eixos exatas nos pisos;
  - Autoteste identifica os pisos dos avisos;
  - elementos do eixo das vias comuns.
- **Revit:** primeira execução registrada.
  - Revit 2027.0, build 27.0.4.412.
  - Autoteste **rápido**: 0 erros.
  - Validar Modelo: 1 informação depois da recalibração.
  - Relatórios em `docs/evidencias/`.
  - O modo **completo** ainda não foi rodado.
- **Compilação:** Core, plugin e testes com 0 avisos e 0 erros.
- **Testes:**
  - suíte completa 2 771/2 771 (EW1/EW2);
  - +4 do eixo, verdes na classe;
  - total 2 775.

## Próxima tarefa

1. **Aguardar o próximo Autoteste do usuário**, com a DLL 2.1.0. Ele identifica cada piso dos avisos "Pisos realçados se
   sobrepõem" (id do elemento e da marca) e marca "[mesma marca]".
   - Se forem pares da mesma marca: são duplicados da regeneração. Corrigir em `MarkingService.Render`, apagando os pisos
     antigos que não serão reaproveitados **antes** de criar os novos, ou conferindo o `pool`.
   - Se forem marcas diferentes: é sobreposição real. Reproduzir no núcleo com a cena do Autoteste.
   - Conferir também se o aviso "ligeiramente fora da linha central" sumiu (EW2).
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
