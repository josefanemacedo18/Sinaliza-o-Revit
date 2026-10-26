#!/usr/bin/env bash
# Entrega de uma fase ou de uma etapa do plano unificado (docs/PLANO_DE_ACAO.md, D-022): compila o plugin a partir de um
# commit limpo e guarda a DLL em Releases/FaseNN ou Releases/EtapaNN, com o manifesto (versão, fase ou etapa, data,
# commit, configuração, resultado da compilação e dos testes). Nunca sobrescreve uma entrega.
#
# Uso: tools/release.sh <fase 1-15 | etapa E1-E13> "<resultado da suíte>" ["<o que não foi verificado>"]
#   ex.: tools/release.sh 3 "2 880/2 880 em 15 min (Linux)" "não executado no Revit 2027"
#        tools/release.sh E1 "2 958/2 958 em 26 min (Linux)"
# Antes: suba <Version> em Directory.Build.props, rode a suíte completa e faça o commit do código da fase.
# Depois: commit de Releases/ e de Instalar/Revit2027 (a mesma DLL) e push.
set -euo pipefail

unit=${1:?fase (1-15) ou etapa (E1-E13)}
tests=${2:?resultado da suíte completa}
pending=${3:-"não executado no Revit 2027 (o ambiente de desenvolvimento não tem Revit)"}
root=$(git rev-parse --show-toplevel)
cd "$root"

if [ -n "$(git status --porcelain)" ]; then
  echo "A árvore tem mudanças não commitadas: a DLL não seria rastreável a um commit." >&2
  exit 1
fi

commit=$(git rev-parse HEAD)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
case "$unit" in
  [Ee][0-9]*) n=$((10#${unit:1})); dir=$(printf 'Releases/Etapa%02d' "$n"); title="Etapa $n"; field="Etapa do plano unificado"; what="nesta etapa" ;;
  *) n=$((10#$unit)); dir=$(printf 'Releases/Fase%02d' "$n"); title="Fase $n"; field="Fase da missão"; what="nesta fase" ;;
esac
if [ -e "$dir/SinalizaBIM.dll" ]; then
  n=2
  while [ -e "$dir/r$n/SinalizaBIM.dll" ]; do n=$((n + 1)); done
  dir="$dir/r$n"
fi
mkdir -p "$dir"

log=$(mktemp)
if ! dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true >"$log" 2>&1; then
  tail -30 "$log" >&2
  rmdir "$dir" 2>/dev/null || true
  echo "Compilação falhou: nenhuma DLL entregue." >&2
  exit 1
fi
warnings=$(grep -Eo '[0-9]+ Warning\(s\)' "$log" | tail -1)
errors=$(grep -Eo '[0-9]+ Error\(s\)' "$log" | tail -1)

cp Instalar/Revit2027/SinalizaBIM.dll Instalar/Revit2027/SinalizaBIM.addin "$dir/"
info=$(strings -el "$dir/SinalizaBIM.dll" | grep -m1 "^$version+" || true)
case "$info" in
  "$version+$commit") ;;
  *) echo "A DLL não traz o commit esperado ($version+$commit): '$info'." >&2; exit 1 ;;
esac
sha=$(sha256sum "$dir/SinalizaBIM.dll" | cut -d' ' -f1)
date=$(date -u +%Y-%m-%d)

cat >"$dir/RELEASE.md" <<EOF
# SinalizaBIM $version – $title

| Campo | Valor |
|---|---|
| Versão | $version (InformationalVersion \`$info\`) |
| $field | $n |
| Data (UTC) | $date |
| Commit do código | \`$commit\` |
| Configuração | \`dotnet build src/SinalizacaoViaria.Revit -c Release -p:EnableWindowsTargeting=true\` (.NET 10, Revit 2027 API 2027.0.0) |
| Compilação | $warnings, $errors |
| SHA-256 da DLL | \`$sha\` |
| Testes do núcleo | $tests |
| Não verificado | $pending |

Arquivos: \`SinalizaBIM.dll\` e \`SinalizaBIM.addin\` (instalação em \`Instalar/LEIA-ME.txt\`). O que mudou $what está
no CHANGELOG.md; as limitações, no KNOWN_ISSUES.md.
EOF
rm -f "$log"
echo "$dir"
