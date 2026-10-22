# Entregas por fase

Cada pasta `FaseNN` guarda a DLL do plugin gerada ao concluir a fase NN da missão, com o `.addin` e o manifesto
`RELEASE.md`. O manifesto traz a versão, a data, o commit do código, a configuração e o resultado da compilação, o SHA-256
da DLL, os testes executados e o que **não** foi verificado.

A instalação é igual à de `Instalar/Revit2027` (ver `Instalar/LEIA-ME.txt`): copie os dois arquivos da pasta da versão
desejada. `Instalar/Revit2027` tem sempre a versão mais recente.

## Regras

- **Rastreável:** a DLL é compilada de um commit sem mudanças pendentes. A versão informativa do assembly
  (`Versão+commit`, visível nas propriedades do arquivo) é conferida contra esse commit (`tools/release.sh`).
- **Preservada:** uma entrega nunca é sobrescrita. Uma segunda entrega da mesma fase vai para `FaseNN/r2`, `r3`…
- **Versão:** cada entrega sobe o número do meio de `<Version>` (`Directory.Build.props`): 2.2.0, 2.3.0… A identidade do
  plugin (nome, `ClientId`, classe de entrada) não muda.
- **Sem DLL fictícia:** se a compilação falhar, não há entrega. Uma fase sem mudança de código não gera DLL nova.
- **Validação no Revit:** o ambiente de desenvolvimento é Linux, sem Revit. Cada entrega é compilada aqui e no CI (Windows,
  testes do núcleo + compilação), mas só é **validada no Revit 2027** depois de um Autoteste no Revit, registrado em
  `TESTS.md` e `docs/evidencias`. Até lá o manifesto diz "não executado no Revit 2027".

## Índice

| Fase | Versão | Data | Commit | Testes do núcleo | Revit 2027 |
|---|---|---|---|---|---|
| I – Auditoria | 2.0.0 | 2026-10-19 | `777a278` | 2 730/2 730 | Autoteste rápido (2026-10-19) |
| II – Estabilização | 2.1.0 | 2026-10-19 | `12b2b43` | 2 771/2 771 | Autoteste rápido (2026-10-19) |
| IX – Quadras e lotes | 2.1.0 | 2026-10-20 | `52c71af` | 2 785/2 785 | Autotestes completos (2026-10-20) |
| XI – Tráfego | 2.0.0 | 2026-10-19 | `777a278` | 2 730/2 730 | Autoteste rápido (2026-10-19) |
| III – Arquitetura integrada | 2.2.0 | 2026-10-21 | `87cf484` | 2 876/2 876 | não executado – [`Fase03`](Fase03/RELEASE.md) |
| IV – Motor geométrico e topologia | 2.3.0 | 2026-10-22 | `43bf855` | 2 887/2 887 | não executado – [`Fase04`](Fase04/RELEASE.md) |
| V – Alinhamentos e perfis | 2.4.0 | 2026-10-22 | `dd8b1fc` | 2 894/2 894 | não executado – [`Fase05`](Fase05/RELEASE.md) |

As fases I, II, IX e XI foram concluídas antes desta pasta existir. As DLLs delas estão no histórico do git, no commit
indicado: `git show <commit>:Instalar/Revit2027/SinalizaBIM.dll > SinalizaBIM.dll`. As fases seguintes ganham uma linha
aqui e uma pasta `FaseNN` ao serem concluídas.
