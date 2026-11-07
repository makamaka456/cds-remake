#!/usr/bin/env bash
# 안드로이드용으로 "갈라 만든" 파일에 원본(PC)의 새 변경을 3-way 병합으로 가져온다.
#   base   = 갈라 만들 때 베낀 원본 파일(옛 버전)      ← 우리가 베낀 순간의 PC 파일
#   ours   = 지금 안드로이드 갈래 파일(HWND·툴바 등을 걷어 낸 것)
#   theirs = 원본의 새 버전
# 결과: <출력폴더>/<이름>.merged (충돌은 <<<<<<< ours ... ======= ... >>>>>>> theirs 로 표시)
# (f4c2d62 처럼 옛 이름 ShipMapWindow/ShipMapHost 시절이 기준이면 OLDNAME_WINDOW · OLDNAME_HOST 에 옛 경로를 준다)
# 쓰기:  bash CostaDelSol.Android/devtools/merge-forks.sh <옛 기준 커밋(f4c2d62 등)> <새 기준(v1.1.65 등)> [출력폴더]
# 시험 결과(f4c2d62 → v1.1.65): ShipMapWindow 충돌 33 · ShipMapHost 7 · CityPicView 14 · EventAnimationPopup 7 — 사람이 풀 만한 양이다.
set -uo pipefail
OLD="$1"; NEW="$2"; OUT="${3:-/tmp/cds-merge}"; mkdir -p "$OUT"; R="$(git rev-parse --show-toplevel)"; cd "$R"
A=CostaDelSol.Android
# 이름: 옛 PC 경로 | 안드로이드 경로 | 새 PC 경로(이름이 바뀌었으면 바뀐 이름)
while IFS='|' read -r name oldpc ours newpc; do
  [ -z "$name" ] && continue
  git show "$OLD:$oldpc" > "$OUT/$name.base" 2>/dev/null || { echo "$name: 옛 파일 없음($oldpc)"; continue; }
  git show "$NEW:$newpc" > "$OUT/$name.theirs" 2>/dev/null || { echo "$name: 새 파일 없음($newpc) — 이름이 바뀌었는지 git diff -M 로 확인"; continue; }
  cp "$ours" "$OUT/$name.ours"; cp "$ours" "$OUT/$name.merged"
  git merge-file -L ours -L base -L theirs "$OUT/$name.merged" "$OUT/$name.base" "$OUT/$name.theirs"; rc=$?
  echo "$name: 충돌 $rc 군데 → $OUT/$name.merged  (base $(wc -l < "$OUT/$name.base")줄 · ours $(wc -l < "$OUT/$name.ours")줄 · theirs $(wc -l < "$OUT/$name.theirs")줄)"
done <<LIST
WorldMapWindow|${OLDNAME_WINDOW:-CdsHelper.Game/UI/Views/WorldMapWindow.cs}|$A/Map/WorldMapWindow.cs|${NEWNAME_WINDOW:-CdsHelper.Game/UI/Views/WorldMapWindow.cs}
WorldMapHost|${OLDNAME_HOST:-CdsHelper.Game/UI/Rendering/WorldMapHost.cs}|$A/Map/WorldMapHost.cs|${NEWNAME_HOST:-CdsHelper.Game/UI/Rendering/WorldMapHost.cs}
CityPicView|CdsHelper.Game/UI/Views/CityPicView.cs|$A/Town/CityPicView.cs|CdsHelper.Game/UI/Views/CityPicView.cs
EventAnimationPopup|CdsHelper.Game/UI/Views/EventAnimationPopup.cs|$A/Map/EventAnimationPopup.cs|CdsHelper.Game/UI/Views/EventAnimationPopup.cs
LIST
