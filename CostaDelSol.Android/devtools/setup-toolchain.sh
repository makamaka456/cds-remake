#!/usr/bin/env bash
# 클라우드 세션에는 안드로이드 SDK 도 dotnet 도 없다. NuGet 패키지만 받아(프록시 통과) 컴파일러와 런타임을 꾸린다.
# 쓰기:  bash CostaDelSol.Android/devtools/setup-toolchain.sh [설치폴더]     (기본 $HOME/cds-tools)
# 끝나면 TOOLS=<설치폴더> 를 환경변수로 두고 check.py · grow.py · sim/build.py 를 쓴다. 약 550MB, 2~3분.
set -euo pipefail
T="${1:-$HOME/cds-tools}"; mkdir -p "$T/pkgs"; cd "$T/pkgs"
get(){ [ -d "$1" ] && return 0; curl -sSL --retry 3 -o "$1.zip" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg" \
  && mkdir -p "$1" && (cd "$1" && python3 -I -m zipfile -e "../$1.zip" . ) && echo "ok $1"; }
get microsoft.netcore.app.runtime.linux-x64 10.0.14      # .NET 런타임(실행용)
get microsoft.netcore.app.ref 10.0.14                    # 표준 라이브러리 참조(컴파일용)
get microsoft.net.compilers.toolset 5.9.0                # csc 컴파일러
get microsoft.android.ref.36 36.1.118                    # Mono.Android 참조(안드로이드 API 타입)
get runtime.linux-x64.microsoft.netcore.dotnethost 8.0.33
get runtime.linux-x64.microsoft.netcore.dotnethostresolver 8.0.33
# CommunityToolkit.Mvvm — 도구 앱 화면(소스 생성기 필요)
if [ ! -d mvvm ]; then mkdir -p mvvm && (cd mvvm && curl -sSL -o mvvm.nupkg https://api.nuget.org/v3-flatcontainer/communitytoolkit.mvvm/8.4.0/communitytoolkit.mvvm.8.4.0.nupkg \
  && python3 -I -c "import zipfile;zipfile.ZipFile('mvvm.nupkg').extractall('.')") && echo "ok mvvm"; fi
# dotnet 실행기를 조립한다(SDK 없이 csc.dll 과 콘솔 프로그램을 돌릴 수 있다)
cd "$T"; mkdir -p dn/host/fxr/8.0.33 dn/shared/Microsoft.NETCore.App/10.0.14
cp pkgs/runtime.linux-x64.microsoft.netcore.dotnethost/runtimes/linux-x64/native/dotnet dn/
cp pkgs/runtime.linux-x64.microsoft.netcore.dotnethostresolver/runtimes/linux-x64/native/libhostfxr.so dn/host/fxr/8.0.33/
R=pkgs/microsoft.netcore.app.runtime.linux-x64/runtimes/linux-x64
cp $R/lib/net10.0/* $R/native/* dn/shared/Microsoft.NETCore.App/10.0.14/ && chmod +x dn/dotnet
dn/dotnet --list-runtimes
dn/dotnet pkgs/microsoft.net.compilers.toolset/tasks/netcore/bincore/csc.dll -version
echo "준비 끝 — export TOOLS=$T"
