#!/usr/bin/env python3
"""
Gera a seção "placas" do catálogo padrão (src/SinalizacaoViaria.Core/Catalog/catalogo-padrao.json).

Códigos e nomes conforme o Anexo II do CTB e o Manual Brasileiro de Sinalização de Trânsito
(Vol. I Regulamentação, Vol. II Advertência, Vol. III Indicação) e a sinalização temporária de obras.
Os pictogramas são esquemáticos (reconhecíveis em planta, prévia e 3D); confira sempre o desenho
oficial, as dimensões e as cores na edição vigente do MBST.

Uso:  python3 tools/gerar_placas.py
"""
import json
import math
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "src", "SinalizacaoViaria.Core", "Catalog", "catalogo-padrao.json")

REF_R = "MBST Vol. I – Sinalização vertical de regulamentação / CTB Anexo II (conferir desenho e dimensões)"
REF_A = "MBST Vol. II – Sinalização vertical de advertência / CTB Anexo II (conferir desenho e dimensões)"
REF_I = "MBST Vol. III – Sinalização vertical de indicação (conferir desenho, código e dimensões)"
REF_O = "Sinalização temporária de obras – MBST Vol. VII / manual do órgão com circunscrição sobre a via"

R_SIZES = [0.40, 0.50, 0.60, 0.75, 1.00]
A_SIZES = [0.45, 0.50, 0.60, 0.75, 0.90]


# ---------------------------------------------------------------- primitivas do pictograma

def r3(v):
    return round(v, 3)


def P(pts):
    return [[r3(x), r3(y)] for x, y in pts]


def arc(cx, cy, r, a0, a1, n=12):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def seta(pts, w=0.09, cab=2.8, dupla=False, cor=None):
    d = {"tipo": "seta", "pts": P(pts), "w": w, "cabeca": cab}
    if dupla:
        d["dupla"] = True
    if cor:
        d["cor"] = cor
    return d


def linha(pts, w=0.1, cor=None):
    d = {"tipo": "linha", "pts": P(pts), "w": w}
    if cor:
        d["cor"] = cor
    return d


def poli(pts, cor=None, vazado=False):
    d = {"tipo": "poligono", "pts": P(pts)}
    if cor:
        d["cor"] = cor
    if vazado:
        d["vazado"] = True
    return d


def ret(x0, y0, x1, y1, cor=None, vazado=False):
    d = {"tipo": "retangulo", "pts": P([(x0, y0), (x1, y1)])}
    if cor:
        d["cor"] = cor
    if vazado:
        d["vazado"] = True
    return d


def circ(x, y, r, cor=None):
    d = {"tipo": "circulo", "c": [r3(x), r3(y)], "r": r}
    if cor:
        d["cor"] = cor
    return d


def anel(x, y, r, w, cor=None):
    d = {"tipo": "anel", "c": [r3(x), r3(y)], "r": r, "w": w}
    if cor:
        d["cor"] = cor
    return d


def txt(t, x=0.0, y=0.0, h=0.3, w=0.95, edit=False, cor=None):
    d = {"tipo": "texto", "c": [r3(x), r3(y)], "texto": t, "h": h, "w": w}
    if edit:
        d["editavel"] = True
    if cor:
        d["cor"] = cor
    return d


def sil(nome, x=0.0, y=0.0, k=1.0, rot=0, esp=False, cor=None):
    d = {"tipo": "silhueta", "nome": nome, "c": [r3(x), r3(y)], "k": k}
    if rot:
        d["rot"] = rot
    if esp:
        d["espelhar"] = True
    if cor:
        d["cor"] = cor
    return d


def mirror(items):
    """Espelha um pictograma (versões "a" à esquerda / "b" à direita)."""
    out = []
    for it in items:
        it = json.loads(json.dumps(it))
        if "pts" in it:
            it["pts"] = [[-x, y] for x, y in it["pts"]]
            if it["tipo"] == "retangulo":
                (x0, y0), (x1, y1) = it["pts"]
                it["pts"] = [[min(x0, x1), y0], [max(x0, x1), y1]]
        if "c" in it and it["tipo"] != "texto":
            it["c"] = [-it["c"][0], it["c"][1]]
        if it["tipo"] == "silhueta":
            it["espelhar"] = not it.get("espelhar", False)
            if it.get("rot"):
                it["rot"] = -it["rot"]
        out.append(it)
    return out


# ---------------------------------------------------------------- formas recorrentes

def turn_left(y0=-0.42, x=0.12, yt=0.02, r=0.2, xe=-0.42):
    return [(x, y0), (x, yt)] + arc(x - r, yt, r, 0, 90)[1:] + [(xe, yt + r)]


def uturn_left():
    return [(0.18, -0.42), (0.18, 0.08)] + arc(-0.04, 0.08, 0.22, 0, 180, 16)[1:] + [(-0.26, -0.22)]


def curve_left():
    return arc(-0.78, -0.46, 0.92, 0, 58, 14)


def sharp_left():
    return [(0.14, -0.46), (0.14, 0.02)] + arc(0.02, 0.02, 0.12, 0, 90, 8)[1:] + [(-0.44, 0.14)]


def sinuous_left(n=40):
    return [(-0.16 * math.sin(math.pi * 2.2 * i / n), -0.46 + 0.9 * i / n) for i in range(n + 1)]


def s_sharp_left():
    return [(0.16, -0.46), (0.16, -0.18), (-0.16, -0.18), (-0.16, 0.16), (0.16, 0.16), (0.16, 0.46)]


def s_curve_left(n=30):
    return [(0.16 * math.cos(math.pi * i / n), -0.46 + 0.92 * i / n) for i in range(n + 1)]


def zebra(y=-0.36, n=5):
    return [ret(-0.36 + i * 0.16, y - 0.06, -0.28 + i * 0.16, y + 0.06) for i in range(n)]


def hump():
    pts = [(-0.46, -0.2)] + [(-0.26 + 0.52 * i / 16, -0.2 + 0.26 * math.sin(math.pi * i / 16)) for i in range(17)] + [(0.46, -0.2), (0.46, -0.3), (-0.46, -0.3)]
    return poli(pts)


def dip():
    pts = [(-0.46, 0.05), (-0.26, 0.05)] + [(-0.26 + 0.52 * i / 16, 0.05 - 0.22 * math.sin(math.pi * i / 16)) for i in range(17)] + [(0.46, 0.05), (0.46, -0.3), (-0.46, -0.3)]
    return poli(pts)


def rough():
    top = [(-0.46 + 0.92 * i / 24, -0.1 + 0.08 * math.sin(math.pi * i / 3)) for i in range(25)]
    return poli(top + [(0.46, -0.3), (-0.46, -0.3)])


def tri_limits(vertical=True):
    if vertical:
        return [poli([(-0.12, 0.46), (0.12, 0.46), (0, 0.3)]), poli([(-0.12, -0.46), (0.12, -0.46), (0, -0.3)])]
    return [poli([(-0.48, 0.12), (-0.48, -0.12), (-0.32, 0)]), poli([(0.48, 0.12), (0.48, -0.12), (0.32, 0)])]


def axle(y=-0.3):
    return [linha([(-0.3, y), (0.3, y)], 0.05), circ(-0.3, y, 0.09), circ(0.3, y, 0.09)]


def roundabout():
    """Três setas curvas formando o círculo (sentido anti-horário)."""
    items = []
    for a in (90, 210, 330):
        items.append(seta(arc(0, 0, 0.3, a - 40, a + 50, 10), 0.08, 2.6))
    return items


# ---------------------------------------------------------------- definição das placas

placas = []


def add(codigo, nome, categoria, forma, largura, altura, fundo, orla_cor, orla, legenda_cor, descricao,
        referencia, picto=None, proib=False, legenda=None, tamanhos=None):
    d = {
        "codigo": codigo, "nome": nome, "categoria": categoria, "forma": forma,
        "largura": largura, "altura": altura, "corFundo": fundo, "corOrla": orla_cor, "orla": orla,
        "corLegenda": legenda_cor, "descricao": descricao, "referencia": referencia,
    }
    if legenda is not None:
        d["legenda"] = legenda
    if tamanhos:
        d["tamanhos"] = tamanhos
    if picto:
        d["pictograma"] = picto
    if proib:
        d["proibicao"] = True
    placas.append(d)


def R(codigo, nome, descricao, picto=None, proib=False, legenda=None):
    add(codigo, nome, "Regulamentacao", "Circulo", 0.50, 0.50, "Branca", "Vermelha", 0.10, "Preta",
        descricao, REF_R, picto, proib, legenda, R_SIZES)


def A(codigo, nome, descricao, picto=None, legenda=None, fundo="Amarela", categoria="Advertencia"):
    add(codigo, nome, categoria, "Losango", 0.45, 0.45, fundo, "Preta", 0.04, "Preta",
        descricao, REF_A if categoria == "Advertencia" else REF_O, picto, False, legenda, A_SIZES)


# ---- Regulamentação (MBST Vol. I)
add("R-1", "Parada obrigatória", "Regulamentacao", "Octogono", 0.60, 0.60, "Vermelha", "Branca", 0.05, "Branca",
    "Assinala ao condutor que deve parar o veículo antes de entrar ou cruzar a via/pista, dando preferência aos que nela circulam.",
    REF_R, legenda="PARE", tamanhos=[0.25, 0.35, 0.50, 0.60, 0.75, 1.00])
add("R-2", "Dê a preferência", "Regulamentacao", "TrianguloInvertido", 0.90, 0.78, "Branca", "Vermelha", 0.14, "Preta",
    "Assinala ao condutor a obrigatoriedade de dar preferência de passagem ao veículo que circula na via em que vai entrar ou cruzar.",
    REF_R, tamanhos=[0.75, 0.90, 1.00])
add("R-3", "Sentido proibido", "Regulamentacao", "Circulo", 0.50, 0.50, "Vermelha", "Vermelha", 0.0, "Branca",
    "Assinala ao condutor que é proibido seguir em frente ou entrar na via/pista no sentido indicado.",
    REF_R, [ret(-0.4, -0.1, 0.4, 0.1)], False, None, R_SIZES)
R("R-4a", "Proibido virar à esquerda", "Assinala ao condutor que é proibido executar o movimento de conversão à esquerda.",
  [seta(turn_left())], True)
R("R-4b", "Proibido virar à direita", "Assinala ao condutor que é proibido executar o movimento de conversão à direita.",
  mirror([seta(turn_left())]), True)
R("R-5a", "Proibido retornar à esquerda", "Assinala ao condutor que é proibido executar o movimento de retorno à esquerda.",
  [seta(uturn_left())], True)
R("R-5b", "Proibido retornar à direita", "Assinala ao condutor que é proibido executar o movimento de retorno à direita.",
  mirror([seta(uturn_left())]), True)
R("R-6a", "Proibido estacionar", "Assinala ao condutor que é proibido o estacionamento no lado da via em que a placa está colocada (complemento: horário, extensão).",
  [txt("E", 0, 0, 0.62)], True)
R("R-6b", "Estacionamento regulamentado", "Assinala ao condutor que o estacionamento é permitido, com as condições estabelecidas na informação complementar.",
  [txt("E", 0, 0, 0.62)])
R("R-6c", "Proibido parar e estacionar", "Assinala ao condutor que é proibido parar ou estacionar o veículo no lado da via em que a placa está colocada.",
  [txt("E", 0, 0, 0.62), linha([(-0.42, -0.42), (0.42, 0.42)], 0.12, "Vermelha")], True)
R("R-7", "Proibido ultrapassar", "Assinala ao condutor que é proibido realizar ultrapassagem a partir do local da placa.",
  [sil("carroTopo", -0.17, 0, 0.62), sil("carroTopo", 0.17, 0.02, 0.62, cor="Vermelha")])
R("R-8a", "Proibido mudar de faixa ou pista da esquerda para a direita", "Assinala ao condutor que é proibida a transposição da faixa/pista da esquerda para a direita.",
  [seta([(-0.2, -0.42), (-0.2, 0.42)], 0.08), seta([(-0.2, -0.3), (-0.2, -0.05), (0.2, 0.2), (0.2, 0.42)], 0.08)], True)
R("R-8b", "Proibido mudar de faixa ou pista da direita para a esquerda", "Assinala ao condutor que é proibida a transposição da faixa/pista da direita para a esquerda.",
  mirror([seta([(-0.2, -0.42), (-0.2, 0.42)], 0.08), seta([(-0.2, -0.3), (-0.2, -0.05), (0.2, 0.2), (0.2, 0.42)], 0.08)]), True)
R("R-9", "Proibido trânsito de caminhões", "Assinala a proibição de circulação de caminhões a partir do local da placa.",
  [sil("caminhao", 0, 0.04, 0.85)], True)
R("R-10", "Proibido trânsito de veículos automotores", "Assinala a proibição de circulação de veículos automotores a partir do local da placa.",
  [sil("carro", 0, 0.04, 0.85)], True)
R("R-11", "Proibido trânsito de veículos de tração animal", "Assinala a proibição de circulação de veículos de tração animal.",
  [sil("carroca", 0, 0.02, 0.9)], True)
R("R-12", "Proibido trânsito de bicicletas", "Assinala a proibição de circulação de bicicletas a partir do local da placa.",
  [sil("bicicleta", 0, 0.04, 0.85)], True)
R("R-13", "Proibido trânsito de tratores e máquinas de obras", "Assinala a proibição de circulação de tratores e máquinas de obras.",
  [sil("trator", 0, 0.04, 0.85)], True)
R("R-14", "Peso bruto total máximo permitido", "Limita o peso bruto total (veículo + carga) dos veículos que transitam na via. Edite a legenda (ex.: 10 t).",
  [txt("10 t", 0, 0, 0.36, 0.9, True)], legenda="10 t")
R("R-15", "Altura máxima permitida", "Limita a altura dos veículos (com carga) que transitam no local. Edite a legenda (ex.: 3,0 m).",
  [txt("3,0 m", 0, 0, 0.3, 0.84, True)] + tri_limits(True), legenda="3,0 m")
R("R-16", "Largura máxima permitida", "Limita a largura dos veículos (com carga) que transitam no local. Edite a legenda (ex.: 2,0 m).",
  [txt("2,0 m", 0, 0, 0.26, 0.6, True)] + tri_limits(False), legenda="2,0 m")
R("R-17", "Peso máximo permitido por eixo", "Limita o peso transmitido ao pavimento por eixo do veículo. Edite a legenda (ex.: 10 t).",
  [txt("10 t", 0, 0.12, 0.3, 0.8, True)] + axle(-0.26), legenda="10 t")
R("R-18", "Comprimento máximo permitido", "Limita o comprimento dos veículos (com carga) que transitam no local. Edite a legenda (ex.: 10 m).",
  [txt("10 m", 0, 0.12, 0.3, 0.8, True), seta([(-0.4, -0.24), (0.4, -0.24)], 0.05, 3, True)], legenda="10 m")
R("R-19", "Velocidade máxima permitida", "Estabelece o limite máximo de velocidade a partir do local da placa. Edite a legenda com a velocidade (ex.: 30, 40, 60).",
  [txt("40", 0, 0.05, 0.5, 0.85, True), txt("km/h", 0, -0.33, 0.12, 0.4)], legenda="40")
R("R-20", "Proibido acionar buzina ou sinal sonoro", "Assinala a proibição de uso da buzina (áreas hospitalares, escolares...).",
  [sil("buzina", 0, 0, 0.8)], True)
R("R-21", "Alfândega", "Assinala a obrigatoriedade de parada do veículo no posto alfandegário.",
  [txt("ALFÂNDEGA", 0, 0, 0.16, 0.95)])
R("R-22", "Uso obrigatório de corrente", "Assinala a obrigatoriedade do uso de correntes nos pneus.",
  [sil("pneu", 0, 0.12, 0.62), sil("corrente", 0, -0.3, 0.7)])
R("R-23", "Conserve-se à direita", "Assinala ao condutor que deve manter-se na faixa da direita.",
  [seta([(-0.12, -0.42), (-0.12, 0.0), (0.22, 0.26), (0.22, 0.42)], 0.09)])
R("R-24a", "Sentido de circulação da via/pista", "Indica o sentido único de circulação dos veículos na via/pista.",
  [seta([(-0.42, 0), (0.42, 0)], 0.1)])
R("R-24b", "Passagem obrigatória", "Assinala que a passagem é obrigatória pelo lado indicado (contornar obstáculo, ilha ou canteiro).",
  [seta([(-0.3, 0.3), (0.3, -0.3)], 0.1)])
R("R-25a", "Vire à esquerda", "Assinala a obrigatoriedade de virar à esquerda.", [seta(turn_left())])
R("R-25b", "Vire à direita", "Assinala a obrigatoriedade de virar à direita.", mirror([seta(turn_left())]))
R("R-25c", "Siga em frente ou à esquerda", "Assinala que só são permitidos os movimentos em frente ou à esquerda.",
  [seta([(0.12, -0.42), (0.12, 0.44)], 0.08), seta(turn_left(yt=-0.06), 0.08)])
R("R-25d", "Siga em frente ou à direita", "Assinala que só são permitidos os movimentos em frente ou à direita.",
  mirror([seta([(0.12, -0.42), (0.12, 0.44)], 0.08), seta(turn_left(yt=-0.06), 0.08)]))
R("R-26", "Siga em frente", "Assinala a obrigatoriedade de seguir em frente.", [seta([(0, -0.44), (0, 0.44)], 0.1)])
R("R-27", "Ônibus, caminhões e veículos de grande porte mantenham-se à direita",
  "Assinala que ônibus, caminhões e veículos de grande porte devem circular pela faixa da direita.",
  [sil("caminhao", -0.02, 0.2, 0.55), seta([(-0.1, -0.1), (-0.1, -0.2), (0.3, -0.4)], 0.06, 2.6)])
R("R-28", "Duplo sentido de circulação", "Assinala que a via/pista passa a ter circulação nos dois sentidos.",
  [seta([(-0.14, -0.42), (-0.14, 0.42)], 0.08), seta([(0.14, 0.42), (0.14, -0.42)], 0.08)])
R("R-29", "Proibido trânsito de pedestres", "Assinala a proibição de circulação de pedestres no local.",
  [sil("pedestre", 0, 0, 0.9)], True)
R("R-30", "Pedestre, ande pela esquerda", "Assinala ao pedestre que deve caminhar pela esquerda da via.",
  [sil("pedestre", 0.14, 0.06, 0.72), seta([(0.1, -0.36), (-0.4, -0.36)], 0.06, 2.6)])
R("R-31", "Pedestre, ande pela direita", "Assinala ao pedestre que deve caminhar pela direita da via.",
  mirror([sil("pedestre", 0.14, 0.06, 0.72), seta([(0.1, -0.36), (-0.4, -0.36)], 0.06, 2.6)]))
R("R-32", "Circulação exclusiva de ônibus", "Assinala a via/pista/faixa de uso exclusivo de ônibus.",
  [sil("onibus", 0, 0, 0.88)])
R("R-33", "Sentido circular obrigatório", "Assinala a obrigatoriedade de circular no sentido indicado em rotatórias.",
  roundabout())
R("R-34", "Circulação exclusiva de bicicletas", "Assinala a via/pista/faixa de uso exclusivo de bicicletas (ciclovia/ciclofaixa).",
  [sil("bicicleta", 0, 0, 0.88)])
R("R-35a", "Ciclista, transite à esquerda", "Assinala ao ciclista que deve transitar pelo lado esquerdo.",
  [sil("bicicleta", 0.08, 0.1, 0.62), seta([(0.2, -0.32), (-0.38, -0.32)], 0.06, 2.6)])
R("R-35b", "Ciclista, transite à direita", "Assinala ao ciclista que deve transitar pelo lado direito.",
  mirror([sil("bicicleta", 0.08, 0.1, 0.62), seta([(0.2, -0.32), (-0.38, -0.32)], 0.06, 2.6)]))
R("R-36a", "Ciclistas à esquerda, pedestres à direita", "Assinala a divisão do espaço compartilhado: ciclistas à esquerda e pedestres à direita.",
  [linha([(0, -0.46), (0, 0.46)], 0.04), sil("bicicleta", -0.23, 0, 0.42), sil("pedestre", 0.23, 0, 0.62)])
R("R-36b", "Pedestres à esquerda, ciclistas à direita", "Assinala a divisão do espaço compartilhado: pedestres à esquerda e ciclistas à direita.",
  [linha([(0, -0.46), (0, 0.46)], 0.04), sil("pedestre", -0.23, 0, 0.62), sil("bicicleta", 0.23, 0, 0.42)])
R("R-37", "Proibido trânsito de motocicletas, motonetas e ciclomotores", "Assinala a proibição de circulação de motocicletas, motonetas e ciclomotores.",
  [sil("moto", 0, 0, 0.85)], True)
R("R-38", "Proibido trânsito de ônibus", "Assinala a proibição de circulação de ônibus a partir do local da placa.",
  [sil("onibus", 0, 0, 0.85)], True)
R("R-39", "Circulação exclusiva de caminhão", "Assinala a via/pista/faixa de uso exclusivo de caminhões.",
  [sil("caminhao", 0, 0, 0.88)])
R("R-40", "Trânsito proibido a carros de mão", "Assinala a proibição de circulação de carros de mão.",
  [sil("carrodemao", 0, 0, 0.85)], True)

# ---- Advertência (MBST Vol. II)
A("A-1a", "Curva acentuada à esquerda", "Adverte a existência de curva acentuada (fechada) à esquerda adiante.", [seta(sharp_left(), 0.11, 2.4)])
A("A-1b", "Curva acentuada à direita", "Adverte a existência de curva acentuada (fechada) à direita adiante.", mirror([seta(sharp_left(), 0.11, 2.4)]))
A("A-2a", "Curva à esquerda", "Adverte a existência de curva à esquerda adiante.", [seta(curve_left(), 0.11, 2.4)])
A("A-2b", "Curva à direita", "Adverte a existência de curva à direita adiante.", mirror([seta(curve_left(), 0.11, 2.4)]))
A("A-3a", "Pista sinuosa à esquerda", "Adverte a existência de trecho com curvas sucessivas, a primeira à esquerda.", [seta(sinuous_left(), 0.1, 2.4)])
A("A-3b", "Pista sinuosa à direita", "Adverte a existência de trecho com curvas sucessivas, a primeira à direita.", mirror([seta(sinuous_left(), 0.1, 2.4)]))
A("A-4a", "Curva acentuada em \"S\" à esquerda", "Adverte a existência de curvas acentuadas em sentidos opostos, a primeira à esquerda.",
  [seta(s_sharp_left(), 0.1, 2.4)])
A("A-4b", "Curva acentuada em \"S\" à direita", "Adverte a existência de curvas acentuadas em sentidos opostos, a primeira à direita.",
  mirror([seta(s_sharp_left(), 0.1, 2.4)]))
A("A-5a", "Curva em \"S\" à esquerda", "Adverte a existência de curvas em sentidos opostos, a primeira à esquerda.",
  [seta(s_curve_left(), 0.1, 2.4)])
A("A-5b", "Curva em \"S\" à direita", "Adverte a existência de curvas em sentidos opostos, a primeira à direita.",
  mirror([seta(s_curve_left(), 0.1, 2.4)]))
A("A-6", "Cruzamento de vias", "Adverte a existência de cruzamento de vias adiante.",
  [linha([(0, -0.46), (0, 0.46)], 0.12), linha([(-0.46, 0.02), (0.46, 0.02)], 0.12)])
A("A-7a", "Via lateral à esquerda", "Adverte a existência de via lateral à esquerda adiante.",
  [linha([(0.06, -0.46), (0.06, 0.46)], 0.12), linha([(0.06, 0.06), (-0.44, 0.06)], 0.1)])
A("A-7b", "Via lateral à direita", "Adverte a existência de via lateral à direita adiante.",
  mirror([linha([(0.06, -0.46), (0.06, 0.46)], 0.12), linha([(0.06, 0.06), (-0.44, 0.06)], 0.1)]))
A("A-8", "Interseção em \"T\"", "Adverte a existência de interseção em \"T\" adiante.",
  [linha([(0, -0.46), (0, 0.24)], 0.12), linha([(-0.46, 0.24), (0.46, 0.24)], 0.12)])
A("A-9", "Bifurcação em \"Y\"", "Adverte a existência de bifurcação em \"Y\" adiante.",
  [linha([(0, -0.46), (0, 0.0), (-0.3, 0.4)], 0.12), linha([(0, 0.0), (0.3, 0.4)], 0.12)])
A("A-10a", "Entroncamento oblíquo à esquerda", "Adverte a existência de entroncamento oblíquo à esquerda adiante.",
  [linha([(0.08, -0.46), (0.08, 0.46)], 0.12), linha([(0.08, -0.04), (-0.34, 0.36)], 0.1)])
A("A-10b", "Entroncamento oblíquo à direita", "Adverte a existência de entroncamento oblíquo à direita adiante.",
  mirror([linha([(0.08, -0.46), (0.08, 0.46)], 0.12), linha([(0.08, -0.04), (-0.34, 0.36)], 0.1)]))
A("A-11a", "Junções sucessivas contrárias, primeira à esquerda", "Adverte a existência de junções sucessivas em lados opostos, a primeira à esquerda.",
  [linha([(0, -0.46), (0, 0.46)], 0.12), linha([(0, -0.14), (-0.4, -0.14)], 0.1), linha([(0, 0.2), (0.4, 0.2)], 0.1)])
A("A-11b", "Junções sucessivas contrárias, primeira à direita", "Adverte a existência de junções sucessivas em lados opostos, a primeira à direita.",
  mirror([linha([(0, -0.46), (0, 0.46)], 0.12), linha([(0, -0.14), (-0.4, -0.14)], 0.1), linha([(0, 0.2), (0.4, 0.2)], 0.1)]))
A("A-12", "Interseção em círculo", "Adverte a existência de rotatória (interseção em círculo) adiante.", roundabout())
A("A-13a", "Confluência à esquerda", "Adverte a existência de confluência (entrada de fluxo) pela esquerda adiante.",
  [linha([(0.1, -0.46), (0.1, 0.46)], 0.12), linha([(-0.34, -0.4), (0.1, 0.1)], 0.1)])
A("A-13b", "Confluência à direita", "Adverte a existência de confluência (entrada de fluxo) pela direita adiante.",
  mirror([linha([(0.1, -0.46), (0.1, 0.46)], 0.12), linha([(-0.34, -0.4), (0.1, 0.1)], 0.1)]))
A("A-14", "Semáforo à frente", "Adverte a existência de interseção ou travessia controlada por semáforo adiante.",
  [ret(-0.15, -0.46, 0.15, 0.46), circ(0, 0.28, 0.1, "Vermelha"), circ(0, 0, 0.1, "Amarela"), circ(0, -0.28, 0.1, "Verde")])
A("A-15", "Parada obrigatória à frente", "Adverte a existência de sinalização de parada obrigatória (R-1) adiante.",
  [poli([(0.3 * math.cos(math.radians(22.5 + 45 * i)), 0.12 + 0.3 * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)], "Vermelha"),
   txt("PARE", 0, 0.12, 0.13, 0.42, cor="Branca"), seta([(0, -0.46), (0, -0.22)], 0.07, 2.4)])
A("A-16", "Bonde", "Adverte a existência de trânsito de bondes (veículos sobre trilhos) na via.", [sil("bonde", 0, 0, 0.9)])
A("A-17", "Pista irregular", "Adverte a existência de trecho com pavimento irregular adiante.", [rough(), sil("carro", 0, 0.14, 0.6)])
A("A-18", "Saliência ou lombada", "Adverte a existência de lombada (ondulação transversal) ou saliência adiante.", [hump()])
A("A-19", "Depressão", "Adverte a existência de depressão na pista adiante.", [dip()])
A("A-20a", "Declive acentuado", "Adverte a existência de declive acentuado (descida). Edite a legenda com a inclinação.",
  [poli([(-0.46, 0.3), (0.46, -0.34), (-0.46, -0.34)]), txt("10%", 0.18, 0.2, 0.18, 0.5, True)], legenda="10%")
A("A-20b", "Aclive acentuado", "Adverte a existência de aclive acentuado (subida). Edite a legenda com a inclinação.",
  [poli([(0.46, 0.3), (-0.46, -0.34), (0.46, -0.34)]), txt("10%", -0.18, 0.2, 0.18, 0.5, True)], legenda="10%")
A("A-21a", "Estreitamento de pista ao centro", "Adverte a redução da largura da pista pelos dois lados.",
  [linha([(-0.26, -0.46), (-0.26, -0.1), (-0.1, 0.12), (-0.1, 0.46)], 0.08), linha([(0.26, -0.46), (0.26, -0.1), (0.1, 0.12), (0.1, 0.46)], 0.08)])
A("A-21b", "Estreitamento de pista à esquerda", "Adverte a redução da largura da pista pelo lado esquerdo.",
  [linha([(-0.26, -0.46), (-0.26, -0.1), (-0.02, 0.14), (-0.02, 0.46)], 0.08), linha([(0.2, -0.46), (0.2, 0.46)], 0.08)])
A("A-21c", "Estreitamento de pista à direita", "Adverte a redução da largura da pista pelo lado direito.",
  mirror([linha([(-0.26, -0.46), (-0.26, -0.1), (-0.02, 0.14), (-0.02, 0.46)], 0.08), linha([(0.2, -0.46), (0.2, 0.46)], 0.08)]))
A("A-21d", "Alargamento de pista à esquerda", "Adverte o aumento da largura da pista pelo lado esquerdo.",
  [linha([(-0.02, -0.46), (-0.02, -0.1), (-0.26, 0.14), (-0.26, 0.46)], 0.08), linha([(0.2, -0.46), (0.2, 0.46)], 0.08)])
A("A-21e", "Alargamento de pista à direita", "Adverte o aumento da largura da pista pelo lado direito.",
  mirror([linha([(-0.02, -0.46), (-0.02, -0.1), (-0.26, 0.14), (-0.26, 0.46)], 0.08), linha([(0.2, -0.46), (0.2, 0.46)], 0.08)]))
A("A-22", "Ponte estreita", "Adverte a existência de ponte (ou viaduto) com largura inferior à da pista.",
  [linha([(-0.3, -0.46), (-0.3, -0.26), (-0.14, -0.16), (-0.14, 0.16), (-0.3, 0.26), (-0.3, 0.46)], 0.08),
   linha([(0.3, -0.46), (0.3, -0.26), (0.14, -0.16), (0.14, 0.16), (0.3, 0.26), (0.3, 0.46)], 0.08)])
A("A-23", "Ponte móvel", "Adverte a existência de ponte móvel (levadiça/giratória) adiante.",
  [poli([(-0.46, -0.3), (-0.06, -0.3), (-0.1, 0.1), (-0.46, -0.1)]), poli([(0.46, -0.3), (0.06, -0.3), (0.1, 0.1), (0.46, -0.1)]),
   linha([(-0.46, -0.36), (0.46, -0.36)], 0.04)])
A("A-25", "Mão dupla adiante", "Adverte que a via de sentido único passa a ter circulação nos dois sentidos adiante.",
  [seta([(-0.14, -0.44), (-0.14, 0.44)], 0.09), seta([(0.14, 0.44), (0.14, -0.44)], 0.09)])
A("A-26a", "Sentido único", "Adverte que a via ou pista tem sentido único de circulação.", [seta([(0, -0.46), (0, 0.46)], 0.12)])
A("A-26b", "Sentido duplo", "Adverte que a via ou pista tem circulação nos dois sentidos.",
  [seta([(0, -0.46), (0, 0.46)], 0.12, 2.4, True)])
A("A-27", "Área com desmoronamento", "Adverte a existência de área sujeita a queda de barreiras/desmoronamento.", [sil("pedras", 0, 0, 0.95)])
A("A-28", "Pista escorregadia", "Adverte a existência de trecho com pista escorregadia (chuva, óleo, areia...).",
  [sil("carroTopo", 0, 0.14, 0.52),
   linha([(-0.12 + 0.07 * math.sin(math.pi * i / 5), -0.12 - 0.34 * i / 10) for i in range(11)], 0.05),
   linha([(0.12 + 0.07 * math.sin(math.pi * i / 5), -0.12 - 0.34 * i / 10) for i in range(11)], 0.05)])
A("A-29", "Projeção de cascalho", "Adverte a existência de trecho com cascalho solto que pode ser projetado pelos pneus.", [sil("cascalho", 0, 0, 0.95)])
A("A-30a", "Trânsito de ciclistas", "Adverte a presença de ciclistas na via.", [sil("bicicleta", 0, 0, 0.95)])
A("A-30b", "Passagem sinalizada de ciclistas", "Adverte a existência de travessia sinalizada de ciclistas adiante.",
  [sil("bicicleta", 0, 0.1, 0.72)] + zebra(-0.36))
A("A-30c", "Trânsito compartilhado por ciclistas e pedestres", "Adverte que ciclistas e pedestres compartilham o espaço de circulação.",
  [sil("bicicleta", -0.18, -0.08, 0.55), sil("pedestre", 0.22, 0.06, 0.62)])
A("A-31", "Trânsito de tratores ou maquinaria agrícola", "Adverte a presença de tratores ou máquinas agrícolas na via.", [sil("trator", 0, 0, 0.95)])
A("A-32a", "Trânsito de pedestres", "Adverte a presença de pedestres na via ou em suas margens.", [sil("pedestre", 0, 0, 0.95)])
A("A-32b", "Passagem sinalizada de pedestres", "Adverte a existência de faixa de travessia de pedestres adiante.",
  [sil("pedestre", 0, 0.12, 0.72)] + zebra(-0.38))
A("A-33a", "Área escolar", "Adverte a proximidade de escola (área com circulação de escolares).", [sil("criancas", 0, 0, 0.95)])
A("A-33b", "Passagem sinalizada de escolares", "Adverte a existência de travessia de escolares adiante.",
  [sil("criancas", 0, 0.12, 0.72)] + zebra(-0.38))
A("A-34", "Crianças", "Adverte a presença de crianças (parques, áreas de lazer).",
  [sil("crianca", -0.12, 0, 0.9), circ(0.26, -0.36, 0.08)])
A("A-35", "Animais", "Adverte a possibilidade de animais domésticos na pista.", [sil("animal", 0, 0, 0.95)])
A("A-36", "Animais selvagens", "Adverte a possibilidade de animais silvestres atravessando a pista.", [sil("cervo", 0, 0, 0.9)])
A("A-37", "Altura limitada", "Adverte a existência de obstáculo que limita a altura dos veículos (viaduto, passarela). Edite a legenda.",
  [txt("3,0 m", 0, 0, 0.24, 0.78, True)] + tri_limits(True), legenda="3,0 m")
A("A-38", "Largura limitada", "Adverte a existência de obstáculo que limita a largura dos veículos. Edite a legenda.",
  [txt("2,0 m", 0, 0, 0.2, 0.56, True)] + tri_limits(False), legenda="2,0 m")
A("A-39", "Passagem de nível sem barreira", "Adverte a existência de cruzamento com ferrovia sem barreira (cancela).", [sil("trem", 0, 0, 0.95)])
A("A-40", "Passagem de nível com barreira", "Adverte a existência de cruzamento com ferrovia com barreira (cancela).",
  [ret(-0.44, -0.08, 0.44, 0.08)] + [ret(-0.36 + i * 0.2, -0.08, -0.26 + i * 0.2, 0.08, vazado=True) for i in range(4)] +
  [ret(-0.44, -0.4, -0.36, 0.08), ret(0.36, -0.4, 0.44, 0.08)])
add("A-41", "Cruz de Santo André", "Advertencia", "CruzSantoAndre", 1.20, 0.90, "Branca", "Vermelha", 0.04, "Preta",
    "Indica ao condutor o local exato de cruzamento em nível com a ferrovia; deve ser respeitada como parada obrigatória.",
    REF_A, tamanhos=[1.00, 1.20])
A("A-42a", "Início de pista dupla", "Adverte o início de pista dupla (com separação física) adiante.",
  [linha([(-0.1, -0.46), (-0.1, 0.0), (-0.3, 0.2), (-0.3, 0.46)], 0.08), linha([(0.1, -0.46), (0.1, 0.0), (0.3, 0.2), (0.3, 0.46)], 0.08),
   poli([(0, 0.08), (0.14, 0.24), (0.14, 0.46), (-0.14, 0.46), (-0.14, 0.24)])])
A("A-42b", "Fim de pista dupla", "Adverte o fim da pista dupla adiante.",
  [linha([(-0.3, -0.46), (-0.3, -0.2), (-0.1, 0.0), (-0.1, 0.46)], 0.08), linha([(0.3, -0.46), (0.3, -0.2), (0.1, 0.0), (0.1, 0.46)], 0.08),
   poli([(0, -0.08), (0.14, -0.24), (0.14, -0.46), (-0.14, -0.46), (-0.14, -0.24)])])
A("A-42c", "Pista dividida", "Adverte a existência de obstáculo/ilha que divide a pista adiante.",
  [seta([(0, -0.46), (0, -0.1), (-0.24, 0.16), (-0.24, 0.46)], 0.08), seta([(0, -0.1), (0.24, 0.16), (0.24, 0.46)], 0.08)])
A("A-43", "Aeroporto", "Adverte a proximidade de aeroporto (aeronaves em baixa altitude).", [sil("aviao", 0, 0, 0.95, rot=45)])
A("A-44", "Vento lateral", "Adverte a existência de trecho sujeito a ventos laterais fortes.", [sil("vento", 0, 0, 0.95)])
A("A-45", "Rua sem saída", "Adverte que a via adiante não tem saída.",
  [linha([(0, -0.46), (0, 0.2)], 0.14), linha([(-0.26, 0.26), (0.26, 0.26)], 0.12, "Vermelha")])
A("A-46", "Peso bruto total limitado", "Adverte a existência de limite de peso bruto total adiante. Edite a legenda.",
  [sil("caminhao", 0, -0.2, 0.55), txt("10 t", 0, 0.22, 0.2, 0.6, True)], legenda="10 t")
A("A-47", "Peso limitado por eixo", "Adverte a existência de limite de peso por eixo adiante. Edite a legenda.",
  [txt("10 t", 0, 0.16, 0.22, 0.6, True)] + axle(-0.22), legenda="10 t")
A("A-48", "Comprimento limitado", "Adverte a existência de limite de comprimento dos veículos adiante. Edite a legenda.",
  [txt("10 m", 0, 0.14, 0.22, 0.6, True), seta([(-0.36, -0.22), (0.36, -0.22)], 0.05, 3, True)], legenda="10 m")
add("MA-ALIN", "Marcador de alinhamento (chevron)", "Advertencia", "Retangulo", 0.50, 0.60, "Amarela", "Preta", 0.0, "Preta",
    "Indica a mudança de alinhamento da via em curvas e a posição de obstáculos. Instalar em série no lado externo da curva.",
    REF_A, [poli([(-0.25, 0.42), (0.05, 0.42), (0.3, 0.0), (0.05, -0.42), (-0.25, -0.42), (0.0, 0.0)])])
add("MP-PERIGO", "Marcador de perigo", "Advertencia", "Retangulo", 0.30, 1.00, "Amarela", "Preta", 0.0, "Preta",
    "Indica obstáculo junto à pista (pilares, cabeceiras, fim de pista). Faixas pretas inclinadas descendo para o lado da pista.",
    REF_A, [poli([(-0.5, -0.5 + 0.5 * i - 0.14), (0.5, -0.5 + 0.5 * i + 0.36 - 0.14), (0.5, -0.5 + 0.5 * i + 0.36 + 0.06), (-0.5, -0.5 + 0.5 * i + 0.06)]) for i in range(-1, 4)])

# ---- Sinalização temporária de obras (fundo laranja)
A("A-24", "Obras", "Adverte a existência de obras ou trabalhadores na pista ou no acostamento (sinalização temporária).",
  [sil("trabalhador", 0, 0, 0.95)], fundo="Laranja", categoria="Obras")
A("OBR-21a", "Estreitamento de pista (obras)", "Versão temporária (fundo laranja) do A-21a para desvios e estreitamentos em obras.",
  [linha([(-0.26, -0.46), (-0.26, -0.1), (-0.1, 0.12), (-0.1, 0.46)], 0.08), linha([(0.26, -0.46), (0.26, -0.1), (0.1, 0.12), (0.1, 0.46)], 0.08)],
  fundo="Laranja", categoria="Obras")
for codigo, nome, texto in [
    ("OBR-AVISO", "Obras a ... m", "OBRAS\nA 500 m"),
    ("OBR-DESVIO", "Desvio", "DESVIO"),
    ("OBR-INTERD", "Pista interditada", "PISTA\nINTERDITADA"),
    ("OBR-VEL", "Reduza a velocidade", "REDUZA A\nVELOCIDADE"),
    ("OBR-FIM", "Fim de obras", "FIM DE\nOBRAS"),
]:
    add(codigo, nome, "Obras", "Retangulo", 1.00, 0.50, "Laranja", "Preta", 0.03, "Preta",
        "Placa de sinalização temporária de obras com legenda editável.", REF_O, legenda=texto, tamanhos=[0.80, 1.00, 1.50])
add("OBR-SETA", "Seta de desvio (obras)", "Obras", "Retangulo", 1.00, 0.40, "Laranja", "Preta", 0.03, "Preta",
    "Indica o sentido do desvio em obras. Gire a placa ou use a versão espelhada conforme o lado.", REF_O,
    [seta([(-0.9, 0), (0.9, 0)], 0.16, 2.4)])

# ---- Indicação (MBST Vol. III)
for codigo, nome, fundo, texto, w, h, desc in [
    ("IND-DEST", "Placa de orientação de destino", "Verde", "CENTRO", 1.20, 0.60, "Orienta o condutor sobre destinos e direções a seguir. Edite a legenda."),
    ("IND-DIST", "Placa de distância", "Verde", "SÃO PAULO 12 km", 1.50, 0.50, "Informa a distância até localidades/destinos. Edite a legenda."),
    ("IND-LOC", "Identificação de localidade/município", "Verde", "LIMITE DE\nMUNICÍPIO", 1.50, 0.70, "Identifica municípios, divisas, rios e pontos notáveis."),
    ("IND-KM", "Marco quilométrico", "Azul", "KM\n12", 0.40, 0.60, "Indica a quilometragem da rodovia no local."),
    ("IND-EDUC", "Placa de indicação educativa", "Branca", "TRAFEGUE PELA\nDIREITA", 1.20, 0.60, "Mensagem educativa (fundo branco, legenda preta)."),
]:
    add(codigo, nome, "Educativa" if fundo == "Branca" else "Indicacao", "Retangulo", w, h, fundo,
        "Preta" if fundo == "Branca" else "Branca", 0.03, "Preta" if fundo == "Branca" else "Branca", desc, REF_I, legenda=texto)
add("LOG", "Placa de identificação de logradouro", "Indicacao", "Retangulo", 0.45, 0.25, "Azul", "Branca", 0.04, "Branca",
    "Placa de nome de rua (padrão municipal). Edite a legenda.", "Padrão municipal", legenda="RUA")

# Serviços auxiliares: fundo azul com quadro branco e pictograma preto
for codigo, nome, picto, desc in [
    ("SAU-HOSP", "Hospital / pronto-socorro", [sil("cruz", 0, 0.08, 0.7, cor="Vermelha")], "Indica hospital ou pronto-socorro."),
    ("SAU-POSTO", "Posto de abastecimento", [sil("bomba", 0, 0.08, 0.66)], "Indica posto de combustível."),
    ("SAU-REST", "Restaurante", [sil("talheres", 0, 0.08, 0.66)], "Indica restaurante."),
    ("SAU-HOTEL", "Hotel / pousada", [sil("cama", 0, 0.08, 0.72)], "Indica hotel, pousada ou área de hospedagem."),
    ("SAU-TEL", "Telefone", [sil("telefone", 0, 0.08, 0.66)], "Indica telefone público / de emergência."),
    ("SAU-MEC", "Serviço mecânico", [sil("chave", 0, 0.08, 0.66)], "Indica oficina mecânica."),
    ("SAU-BORR", "Borracharia", [sil("pneu", 0, 0.08, 0.66)], "Indica borracharia."),
    ("SAU-ONIBUS", "Ponto de parada de ônibus", [sil("onibus", 0, 0.08, 0.66)], "Indica ponto de embarque/desembarque de ônibus."),
    ("SAU-TAXI", "Ponto de táxi", [sil("taxi", 0, 0.08, 0.66)], "Indica ponto de táxi."),
    ("SAU-AERO", "Aeroporto", [sil("aviao", 0, 0.08, 0.62, rot=45)], "Indica aeroporto."),
    ("SAU-INFO", "Informação turística", [sil("informacao", 0, 0.08, 0.66)], "Indica posto de informações turísticas."),
    ("SAU-SANIT", "Sanitários", [sil("banheiro", 0, 0.08, 0.66)], "Indica sanitários públicos."),
    ("SAU-POLICIA", "Polícia", [sil("policia", 0, 0.08, 0.6)], "Indica posto policial."),
    ("SAU-PCD", "Atendimento/estacionamento para pessoas com deficiência", [sil("cadeirante", 0, 0.08, 0.66)], "Indica local acessível ou vaga reservada."),
    ("SAU-BARCO", "Travessia por embarcação", [sil("barco", 0, 0.08, 0.66)], "Indica travessia por balsa/embarcação."),
    ("SAU-DESC", "Área de descanso", [sil("arvore", 0, 0.08, 0.66)], "Indica área de descanso/parada."),
    ("SAU-BIKE", "Estacionamento de bicicletas / ciclovia", [sil("bicicleta", 0, 0.08, 0.7)], "Indica bicicletário, paraciclo ou ciclovia."),
]:
    add(codigo, nome, "Servicos", "Retangulo", 0.50, 0.60, "Azul", "Branca", 0.03, "Preta",
        desc + " Fundo azul, quadro branco e pictograma preto.", REF_I,
        [ret(-0.42, -0.34, 0.42, 0.5, cor="Branca")] + picto + [txt(nome.split(" /")[0].upper(), 0, -0.48, 0.1, 0.9, cor="Branca")])
# Código da versão 2.0 mantido para projetos existentes.
add("IND-SERV", "Placa de serviço auxiliar (legenda livre)", "Servicos", "Retangulo", 0.40, 0.60, "Azul", "Branca", 0.04, "Branca",
    "Retângulo azul com legenda branca editável.", REF_I, legenda="ÔNIBUS")
add("SAU-EST", "Área de estacionamento", "Servicos", "Retangulo", 0.50, 0.60, "Azul", "Branca", 0.03, "Branca",
    "Indica área de estacionamento. Fundo azul com letra \"E\" branca.", REF_I, [txt("E", 0, 0.06, 0.62, 0.8)])

# Atrativos turísticos: fundo marrom, pictograma e legenda brancos
for codigo, nome, picto, texto in [
    ("IND-TUR", "Placa de atrativo turístico", None, "MUSEU"),
    ("TUR-PARQUE", "Parque / área natural", [sil("arvore", -0.62, 0, 0.8, cor="Branca"), txt("PARQUE", 0.3, 0, 0.3, 1.1, True, "Branca")], "PARQUE"),
    ("TUR-PRAIA", "Praia / lago", [sil("barco", -0.62, 0, 0.8, cor="Branca"), txt("PRAIA", 0.3, 0, 0.3, 1.1, True, "Branca")], "PRAIA"),
    ("TUR-IGREJA", "Igreja / patrimônio histórico", [linha([(-0.62, -0.4), (-0.62, 0.4)], 0.1, "Branca"), linha([(-0.84, 0.16), (-0.4, 0.16)], 0.1, "Branca"),
                                                    txt("IGREJA", 0.3, 0, 0.3, 1.1, True, "Branca")], "IGREJA"),
]:
    add(codigo, nome, "Turistica", "Retangulo", 1.00, 0.50, "Marrom", "Branca", 0.03, "Branca",
        "Indica atrativo turístico. Edite a legenda.", REF_I, picto, legenda=texto)

# Educativas
for codigo, texto in [
    ("EDU", "RESPEITE O PEDESTRE"),
    ("EDU-CINTO", "USE O CINTO\nDE SEGURANÇA"),
    ("EDU-CEL", "NÃO USE O CELULAR\nAO DIRIGIR"),
    ("EDU-VEL", "RESPEITE OS LIMITES\nDE VELOCIDADE"),
    ("EDU-ALCOOL", "SE BEBER\nNÃO DIRIJA"),
    ("EDU-CICL", "RESPEITE O CICLISTA\nDISTÂNCIA DE 1,5 m"),
]:
    add(codigo, "Placa educativa – " + texto.replace("\n", " ").capitalize(), "Educativa", "Retangulo", 1.00, 0.50,
        "Branca", "Preta", 0.03, "Preta", "Mensagem educativa (fundo branco, orla e legenda pretas). Edite a legenda.", REF_I, legenda=texto)

# Informações complementares (instaladas sob a placa principal)
for codigo, nome, texto in [
    ("INF-COMP", "Informação complementar", "EXCETO\nÔNIBUS"),
    ("INF-HOR", "Informação complementar – horário", "SEG A SEX\n7h ÀS 19h"),
    ("INF-DIST", "Informação complementar – distância", "A 100 m"),
    ("INF-FISC", "Informação complementar – fiscalização eletrônica", "FISCALIZAÇÃO\nELETRÔNICA"),
]:
    add(codigo, nome, "Regulamentacao", "Retangulo", 0.50, 0.25, "Branca", "Preta", 0.03, "Preta",
        "Placa retangular instalada sob a placa principal com informação complementar (horário, exceções, distância). Edite a legenda.",
        REF_R, legenda=texto, tamanhos=[0.40, 0.50, 0.75])


def main():
    codes = [p["codigo"] for p in placas]
    dup = {c for c in codes if codes.count(c) > 1}
    if dup:
        raise SystemExit(f"Códigos duplicados: {dup}")
    with open(CATALOG, encoding="utf-8-sig") as f:
        cat = json.load(f)
    cat["placas"] = placas
    text = json.dumps(cat, ensure_ascii=False, indent=2)
    # Listas numéricas em uma linha (pontos dos pictogramas) para manter o arquivo legível.
    text = re.sub(r"\[\s*([-\d.eE+]+(?:,\s*[-\d.eE+]+)*)\s*\]", lambda m: "[" + ", ".join(x.strip() for x in m.group(1).split(",")) + "]", text)
    text = re.sub(r"\[\s*(\[[^\[\]]*\](?:,\s*\[[^\[\]]*\])*)\s*\]", lambda m: "[" + re.sub(r"\],\s*\[", "], [", m.group(1)) + "]", text)
    text = re.sub(r"\{\s*(\"tipo\"[^{}]*?)\s*\}", lambda m: "{ " + re.sub(r",\s*\n\s*", ", ", m.group(1)) + " }", text)
    with open(CATALOG, "w", encoding="utf-8") as f:
        f.write(text + "\n")
    print(f"{len(placas)} placas gravadas em {os.path.relpath(CATALOG, ROOT)}")


if __name__ == "__main__":
    main()
