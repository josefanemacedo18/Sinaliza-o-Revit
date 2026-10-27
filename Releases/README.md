# Entregas por fase e por etapa

Cada pasta `FaseNN` guarda a DLL do plugin gerada ao concluir a fase NN da missão. Desde 2026-10-26 (D-022), cada pasta
`EtapaNN` guarda a DLL da etapa NN do plano unificado (`docs/PLANO_DE_ACAO.md`). As duas vêm com o `.addin` e o manifesto
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
| VI – Corredores, seções e pavimentação | 2.5.0 | 2026-10-22 | `ec398bc` | 2 914/2 914 | não executado – [`Fase06`](Fase06/RELEASE.md) |
| VII – Terreno, terraplenagem e interseções | 2.6.0 | 2026-10-22 | `b87e668` | 2 921/2 921 | não executado – [`Fase07`](Fase07/RELEASE.md) |
| VIII – Drenagem e redes | 2.7.0 | 2026-10-22 | `9ed7e12` | 2 949/2 949 | não executado – [`Fase08`](Fase08/RELEASE.md) |
| Etapa 1 – integridade geométrica (Fase X) | 2.8.0 | 2026-10-26 | `43ab058` | 2 966/2 966 | não executado – [`Etapa01`](Etapa01/RELEASE.md) |
| Etapas 2 e 3 – diagnóstico padronizado e topografia (Fase X) | 2.9.0 | 2026-10-26 | `23c92c8` | 2 979/2 979 | não executado – [`Etapa03`](Etapa03/RELEASE.md) (entrega conjunta; não há `Etapa02`) |
| Etapa 4 – estaqueamento e limites dos avisos (Fases V e VII) | 2.10.0 | 2026-10-26 | `ee75f3f` | 2 986/2 986 | não executado – [`Etapa04`](Etapa04/RELEASE.md) |
| Etapa 5 – estrutura do pavimento e terraplenagem desatualizada (Fases VI, VII e X) | 2.11.0 | 2026-10-26 | `5dc91f7` | 3 000/3 000 | não executado – [`Etapa05`](Etapa05/RELEASE.md) |
| Etapa 6 – drenagem integrada às vias e ao terreno (Fase VIII, RT-1.5) | 2.12.0 | 2026-10-26 | `ab92d46` | 3 011/3 011 | não executado – [`Etapa06`](Etapa06/RELEASE.md) |
| Etapa 7 – urbanismo e elementos complementares (Fase IX) | 2.13.0 | 2026-10-26 | `e3a27c4` | 3 016/3 016 | não executado – [`Etapa07`](Etapa07/RELEASE.md) |
| Etapa 8 – integração associativa: estado dos resultados (Fase X) | 2.14.0 | 2026-10-26 | `072b683` | 3 022/3 022 | não executado – [`Etapa08`](Etapa08/RELEASE.md) |
| Etapa 9 – design system e faixa de opções por tema (Fase XIII) | 2.15.0 | 2026-10-27 | `d074465` | 3 036/3 036 | não executado – [`Etapa09`](Etapa09/RELEASE.md) |
| Etapa 10 – Central do Projeto e painel contextual (Fase XIII) | 2.16.0 | 2026-10-27 | `1a7f2b5` | 3 048/3 048 | não executado – [`Etapa10`](Etapa10/RELEASE.md) |
| Etapa 11 – Telas dos módulos (Fase XIII) | 2.17.0 | 2026-10-27 | `70d43af` | 3 075/3 075 | não executado – [`Etapa11`](Etapa11/RELEASE.md) |
| Etapa 12 – Qualidade, quantitativos e configurações (Fase XIII) | 2.18.0 | 2026-10-27 | `9102a7b` | 3 123/3 123 | não executado – [`Etapa12`](Etapa12/RELEASE.md) |
| Etapa 13 – Desempenho, validação e entrega (Fases XIII e XV) | 2.19.0 | 2026-10-27 | `fdb39c2` | 3 136/3 136 | não executado – [`Etapa13`](Etapa13/RELEASE.md) |

As fases I, II, IX e XI foram concluídas antes desta pasta existir. As DLLs delas estão no histórico do git, no commit
indicado: `git show <commit>:Instalar/Revit2027/SinalizaBIM.dll > SinalizaBIM.dll`. As fases seguintes ganham uma linha
aqui e uma pasta `FaseNN` ao serem concluídas; desde 2026-10-26, as etapas do plano unificado, uma pasta `EtapaNN`.
