# Catálogo normativo (JSON)

O plugin traz um catálogo padrão embutido (`src/SinalizacaoViaria.Core/Catalog/catalogo-padrao.json`).
Pelo comando **Catálogo → Abrir o catálogo para edição**, uma cópia é criada em
`%AppData%\SinalizaBIM\catalogo.json`. Ao recarregar:

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

## `dispositivos` – bloqueios e segregação física

```json
{ "codigo": "BAL-FLEX", "nome": "Balizador flexível", "forma": "Balizador", "cor": "Amarela",
  "comprimento": 0.08, "largura": 0.08, "altura": 0.75, "espacamento": 2.00 }
```

`forma`: `Caixa`, `Cilindro`, `Balizador`, `Tartaruga`, `Prisma`, `NewJersey`, `Defensa`.
`espacamento` é a distância entre centros; `0` = contínuo (barreira, separador). Na defensa, é a
distância entre postes. Cores físicas: `Concreto`, `Grama`, `Metal` (sem consumo de tinta).

Os tipos lineares do grupo `Urbanizacao` (`CALCADA`, `MEIO-FIO`, `GRAMADO`) são usados pela seção
transversal; a `espessura` é a altura do elemento.

## `placas` – sinalização vertical

```json
{ "codigo": "R-19", "nome": "Velocidade máxima permitida", "categoria": "Regulamentacao",
  "forma": "Circulo", "largura": 0.50, "altura": 0.50, "corFundo": "Branca", "corOrla": "Vermelha",
  "orla": 0.10, "legenda": "40", "corLegenda": "Preta", "tamanhos": [0.40, 0.50, 0.75] }
```

`forma`: `Circulo`, `Octogono`, `TrianguloInvertido`, `Losango`, `Retangulo`, `Quadrado`, `CruzSantoAndre`.
`categoria`: `Regulamentacao`, `Advertencia`, `Indicacao`, `Educativa`, `Servicos`, `Turistica`, `Obras`.
`orla` é a largura da borda em fração da largura.

### Pictogramas

`pictograma` é uma lista de elementos desenhados, em ordem (cada um fica por cima do anterior), em
coordenadas da **área útil** da placa: −0,5 a 0,5, Y para cima. `proibicao: true` acrescenta a tarja
diagonal vermelha por cima de tudo.

```json
"pictograma": [
  { "tipo": "seta", "pts": [[0.12, -0.42], [0.12, 0.02], [-0.42, 0.22]], "w": 0.09, "cabeca": 2.8 },
  { "tipo": "silhueta", "nome": "caminhao", "c": [0, 0.04], "k": 0.85 },
  { "tipo": "texto", "c": [0, 0], "texto": "3,0 m", "h": 0.3, "w": 0.84, "editavel": true },
  { "tipo": "retangulo", "pts": [[-0.4, -0.1], [0.4, 0.1]], "cor": "Branca" }
],
"proibicao": true
```

| Tipo | Campos |
|---|---|
| `linha` | `pts`, `w` (espessura) |
| `seta` | `pts` (eixo), `w`, `cabeca` (ponta = w × cabeca), `dupla` |
| `poligono` | `pts` |
| `retangulo` | `pts` = [canto, canto oposto] |
| `circulo` / `anel` | `c`, `r` (e `w` no anel) |
| `texto` | `c` (centro), `texto`, `h` (altura), `w` (largura máxima), `editavel` (usa a legenda da placa) |
| `silhueta` | `nome`, `c`, `k` (escala), `rot` (graus), `espelhar` |

Todos aceitam `cor` (padrão: `corLegenda`) e `vazado` (pinta com a cor de fundo). Silhuetas
disponíveis: `carro`, `carroTopo`, `caminhao`, `onibus`, `moto`, `bicicleta`, `pedestre`, `crianca`,
`criancas`, `trator`, `animal`, `cervo`, `carroca`, `trem`, `bonde`, `aviao`, `cadeirante`,
`trabalhador`, `carrodemao`, `cruz`, `bomba`, `talheres`, `cama`, `telefone`, `chave`, `pneu`, `taxi`,
`informacao`, `buzina`, `corrente`, `vento`, `pedras`, `cascalho`, `arvore`, `barco`, `policia`,
`banheiro`.

A série padrão é gerada pelo script `tools/gerar_placas.py` (Python 3), que pode ser adaptado.

## `mobiliario` – elementos urbanísticos

```json
{ "codigo": "POSTE", "nome": "Poste de iluminação", "forma": "PosteIluminacao",
  "comprimento": 1.80, "largura": 0.20, "altura": 8.00, "cor": "Metal", "espacamento": 30 }
```

`forma`: `Banco`, `Lixeira`, `PosteIluminacao`, `Arvore`, `AbrigoOnibus`, `Paraciclo`, `Hidrante`,
`Floreira`, `PlacaLogradouro`, `Semaforo`.

Cores adicionais: `Verde`, `Laranja`, `Marrom` (tinta/placas) e `Asfalto` (volumes).

## `materiais`

```json
{ "nome": "Termoplástico extrudado", "espessuraMm": 3.0, "consumo": 6.0,
  "unidadeConsumo": "kg/m²", "microesferasKgM2": 0.40 }
```

`consumo` × área = consumo estimado nos quantitativos. Ajuste aos valores do seu fornecedor ou
especificação.

## `normas`

Lista exibida em **Normas / Sobre** (`sigla`, `titulo`, `aplicacao`).
