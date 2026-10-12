#!/usr/bin/env bash
# Сборка 4 сборок мода в 1.6/Assemblies. Нужен только .NET 6 runtime.
set -e
D="$(cd "$(dirname "$0")" && pwd)"
CSC="${CSC:?Укажите путь к Roslyn csc.dll: CSC=.../tasks/net6.0/bincore/csc.dll (NuGet Microsoft.Net.Compilers.Toolset)}"
G="C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed"
HW="C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077"
# Harmony раскладывает сборки по версиям игры (1.6/Assemblies); старая раскладка — Current/Assemblies
H="${HARMONY:-$HW/1.6/Assemblies/0Harmony.dll}"
[ -f "$H" ] || H="$HW/Current/Assemblies/0Harmony.dll"
OUT="$D/1.6/Assemblies"
# 0MultiplayerAPI берём у самого Multiplayer (обязательная зависимость мода, в мод не кладём)
MPAPI="${MPAPI:-C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2606448745/1.6/Assemblies/0MultiplayerAPI.dll}"
mkdir -p "$OUT"
BASE=(-nologo -nostdlib -noconfig -target:library -langversion:latest -optimize+ -deterministic -nowarn:CS0162,CS0168,CS0219,CS0414,CS0649,CS8019,CS1701,CS1702
  "-r:$G/mscorlib.dll" "-r:$G/System.dll" "-r:$G/System.Core.dll" "-r:$G/netstandard.dll"
  "-r:$G/Assembly-CSharp.dll" "-r:$G/UnityEngine.dll" "-r:$G/UnityEngine.CoreModule.dll"
  "-r:$G/UnityEngine.IMGUIModule.dll" "-r:$G/UnityEngine.TextRenderingModule.dll" "-r:$H")
build() { # name, extra args
  local n=$1; shift
  echo "== $n"
  dotnet "$CSC" "${BASE[@]}" "$@" -out:"$OUT/$n.dll" "$D/Source/$n"/*.cs
}
build RaidAnimalFix "-r:$G/System.IO.Compression.dll" "-r:$G/System.IO.Compression.FileSystem.dll"
build RaidAnimalFix_RNG "-r:$OUT/RaidAnimalFix.dll"
build RaidAnimalFix_Filters "-r:$OUT/RaidAnimalFix.dll" "-r:$MPAPI"
build RaidAnimalFix_Hotfix
echo "OK -> $OUT"
