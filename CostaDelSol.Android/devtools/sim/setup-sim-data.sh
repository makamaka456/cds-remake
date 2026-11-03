#!/usr/bin/env bash
# 시뮬레이터가 읽는 "게임 폴더"를 만든다(앱이 첫 실행 때 꺼내 쓰는 것과 같은 구성).
# 쓰기:  bash CostaDelSol.Android/devtools/sim/setup-sim-data.sh [폴더]     (기본 $HOME/cds-tools/game)
# 시뮬레이터 실행:  $TOOLS/dn/dotnet $TOOLS/sim/sim.dll <이 폴더> 0 0     (뒤의 0 0 = NEW GAME 에서 EASY · 라몬)
set -euo pipefail
G="${1:-$HOME/cds-tools/game}"; R="$(git rev-parse --show-toplevel)"; mkdir -p "$G/save"
# 그림 자료(CDSX)는 저장소에 없다 — 릴리즈 map-assets 에서 받는다(원본 저장소 cds-helper 에 있다. 새 저장소에 옮겨졌는지 확인)
base="${MAP_ASSETS:-https://github.com/Kyeongrok/cds-helper/releases/download/map-assets}"
curl -fsSL --retry 4 -o "$G/world.cdsx" "$base/world.cdsx"
for n in OCEAN CITYCG DISCOVER DSTILL MPCG WAVES ITEM MALE FEMALE LANDDATA EVSTILL FIGHTER OPENBOOK ENC TRAMP MISC BOOKSHEL MPEFFECT CLOUD; do
  curl -fsSL --retry 4 -o "$G/$n.CDSX" "$base/$n.CDSX"
done
curl -fsSL --retry 4 -o "$G/TRAMP.PX" "$base/TRAMP.PX"
cp "$R"/CdsHelper/*.json "$G/"                     # 표(발견물·이야기·인물…)
ln -sfn "$R/asset" "$G/asset"
# 표는 복사본 — 시험 중 표를 고쳐 적어도(도시 성장 따위) 저장소가 바뀌지 않게
rm -rf "$G/exe-tables"; cp -r "$R/exe-tables" "$G/exe-tables"
du -sh "$G"; echo "게임 폴더 준비 끝: $G"
