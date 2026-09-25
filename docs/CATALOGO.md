# Catálogo normativo (JSON)

O plugin traz um catálogo padrão embutido (`src/SinalizacaoViaria.Core/Catalog/catalogo-padrao.json`).
Pelo comando **Catálogo → Abrir o catálogo para edição**, uma cópia é criada em
`%AppData%\SinalizacaoViaria\catalogo.json`. Ao recarregar:

* itens do usuário com o **mesmo código** (ou nome, para materiais/tamanhos) **substituem** os padrões;
* itens com código novo são **acrescentados**;
* o arquivo pode conter apenas os itens alterados.

Depois de editar: **Catálogo → Recarregar** e **Atualizar Todas** para aplicar às marcas existentes.
Unidades em metros; cores: `Branca`, `Amarela`, `Vermelha`, `Azul`, `Preta`.

## `lineares` – marcas formadas por faixas paralelas ao caminho

```json
{
  "codigo": "LFO-4",
  "nome": "Linha dupla contínua/seccionada",
  "grupo": "Longitudinal",
  "cor": "Amarela",
  "alinhamento": "Inicio",
  "descartarParciais": false,
  "unidades": false,
  "espessura": 0,
  "larguraMin": 0.10, "larguraMax": 0.15,
  "transversal": false,
  "variantes": [
    { "nome": "V ≤ 60 km/h", "velocidadeMax": 60,
      "faixas": [
        { "deslocamento": 0.10, "largura": 0.10 },
        { "deslocamento": -0.10, "largura": 0.10, "padrao": [ 2.0, 4.0 ] }
      ] }
  ]
}
```

| Campo | Significado |
|---|---|
| `grupo` | `Longitudinal`, `Transversal`, `Canalizacao`, `Estacionamento`, `Inscricao`, `Dispositivo`, `Acessibilidade`, `Ciclovia` – define em qual comando o tipo aparece. |
| `alinhamento` | `Inicio`, `Fim`, `Centro`, `Ajustar`. |
| `descartarParciais` | Descarta traços cortados nas extremidades (barras de faixa de pedestres). |
| `unidades` | Cada peça conta como unidade (tachas). |
| `espessura` | Altura específica no 3D (m); 0 = material. |
| `larguraMin/Max` | Intervalo de referência para avisos. |
| `transversal` | Tipo desenhado por dois cliques atravessando a via. |
| `variantes[].velocidadeMax` | Maior velocidade (km/h) para a qual a variante vale; 0 = sem critério. |
| `faixas[].deslocamento` | Posição lateral do eixo da faixa (positivo = esquerda). |
| `faixas[].padrao` | `[traço, espaço, traço, espaço, ...]`; vazio = contínua. |
| `faixas[].repetir` | `false` para sequências únicas (LRV). |
| `faixas[].cor` | Cor própria da faixa (opcional). |

**Truque útil**: uma faixa de pedestres zebrada é uma faixa larga (largura = 4,00 m) com padrão
`[0.40, 0.60]` ao longo do alinhamento da travessia; quadrados de MCC são faixas de 0,40 m com padrão
`[0.40, 0.40]`. Assim novas marcas podem ser criadas só com o catálogo.

## `hachuras` – marcações de área

```json
{ "codigo": "ZPA", "nome": "Zebrado", "grupo": "Canalizacao",
  "corBarras": "Branca", "corBorda": "Branca",
  "larguraBarra": 0.30, "espacamento": 1.10, "angulo": 45,
  "larguraBorda": 0.15, "chevron": false, "cruzado": false }
```

`espacamento` é o espaço livre entre barras; `larguraBorda` = 0 dispensa a linha de canalização.

## `simbolos`

```json
{ "codigo": "PEM-F", "nome": "Seta – siga em frente", "forma": "SetaFrente",
  "cor": "Branca", "corFundo": null, "comprimentos": [ 5.0, 7.5 ] }
```

Formas disponíveis: `SetaFrente`, `SetaDireita`, `SetaEsquerda`, `SetaFrenteDireita`,
`SetaFrenteEsquerda`, `SetaDireitaEsquerda`, `SetaRetornoEsquerda`, `SetaRetornoDireita`,
`SetaMudancaFaixaEsquerda`, `SetaMudancaFaixaDireita`, `AcessoDeficiente`, `Bicicleta`,
`DePreferencia`, `CruzSantoAndre`, `ServicoSaude`. Um mesmo desenho pode ser cadastrado com outro
código, cor ou comprimento (ex.: `CIC-SETA`).

## `legendas` e `tamanhosLetra`

```json
{ "texto": "SÓ\nÔNIBUS" }
{ "nome": "Via urbana – letras de 1,60 m", "altura": 1.60, "fatorLargura": 0.40, "entrelinha": 1.00, "espacamento": 0.08 }
```

## `vagas`

```json
{ "codigo": "PCD-90", "nome": "Vaga PcD 90°", "tipo": "PessoaComDeficiencia",
  "angulo": 90, "largura": 2.50, "comprimento": 5.00, "larguraLinha": 0.10,
  "cor": "Branca", "faixaAdicional": 1.20, "legenda": null }
```

`tipo`: `Comum`, `PessoaComDeficiencia`, `Idoso`, `Motocicleta`, `CargaDescarga`, `Onibus`, `Taxi`,
`Ambulancia`, `Viatura`. Vagas PcD recebem o símbolo SIA; as demais usam `legenda`.

## `materiais`

```json
{ "nome": "Termoplástico extrudado", "espessuraMm": 3.0, "consumo": 6.0,
  "unidadeConsumo": "kg/m²", "microesferasKgM2": 0.40 }
```

`consumo` × área = consumo estimado nos quantitativos. Ajuste aos valores do seu fornecedor ou
especificação.

## `normas`

Lista exibida em **Normas / Sobre** (`sigla`, `titulo`, `aplicacao`).
