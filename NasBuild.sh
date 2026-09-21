#!/bin/sh

set -euo pipefail

PROJECT_DIR="/share/Public/repos/DLNAServer/DLNAServer"

# =========================
# INPUT PARAMETERS
# =========================
PUBLISH_DIR="${1:-/share/Internal/DLNA/publish}"
ASSEMBLY_NAME="${2:-DLNAServerTest}"

if [[ -z "$PUBLISH_DIR" ]]; then
  echo "Publish directory is required as first argument"
  exit 1
fi

if [[ -z "$ASSEMBLY_NAME" ]]; then
  echo "Assembly name is required as second argument"
  exit 1
fi

mkdir -p "$PUBLISH_DIR"

cd "$PROJECT_DIR"

# =========================
# ENVIRONMENT
# =========================
export DOTNET_GCServer=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_DefaultStackSize=0x180000

export MALLOC_TRIM_THRESHOLD_=65536
export MALLOC_MMAP_THRESHOLD_=65536
export MALLOC_ARENA_MAX=32

# =========================
# CLEAN OLD BUILD OUTPUT (SAFE)
# =========================
echo "Cleaning previous build artifacts in publish folder..."

find "$PUBLISH_DIR" -maxdepth 2 -type f \( \
  -name "*.dll" -o \
  -name "*.pdb" -o \
  -name "*.json" -o \
  -name "*.deps.json" -o \
  -name "*.runtimeconfig.json" -o \
  -name "*.exe" -o \
  -name "*.so" -o \
  -name "*.a" -o \
  -name "*.dbg" \
\) -delete

rm -f "$PUBLISH_DIR/$ASSEMBLY_NAME"
rm -f "$PUBLISH_DIR/$ASSEMBLY_NAME.exe"
rm -f "$PUBLISH_DIR/$ASSEMBLY_NAME.dll"

# =========================
# RESTORE
# =========================
dotnet restore \
  -p:RestoreUseStaticGraphEvaluation=true \
  -p:RestoreParallel=true \
  -p:RestoreDisableParallel=false \
  --force \
  --no-cache \
  ./

# =========================
# BUILD
# =========================
dotnet build -c Release \
  -p:Optimize=true \
  -p:Deterministic=true \
  -p:InvariantGlobalization=true \
  -p:TieredCompilation=true \
  -p:UseSharedCompilation=false \
  -p:ConcurrentBuild=true \
  -p:DebugType=None \
  -p:DebuggerSupport=false \
  -p:TrimUnusedDependencies=true \
  -p:EnforceCodeStyleInBuild=false \
  -o ./bin/NAS/build

# =========================
# PUBLISH
# =========================
dotnet publish -c Release \
  -p:Deterministic=true \
  -p:Optimize=true \
  --self-contained false \
  -p:LinkDuringPublish=true \
  -p:PublishReadyToRunUseCrossgen2=true \
  -p:EnableCompressionInSingleFile=true \
  -p:GCServer=true \
  -p:GCConcurrent=true \
  -p:InvariantGlobalization=true \
  -p:TieredCompilation=true \
  -p:StripSymbols=true \
  -p:Prefer32Bit=false \
  -p:DebuggerSupport=false \
  -p:TrimUnusedDependencies=true \
  -p:ReadyToRun=true \
  -p:DebugType=Full \
  -p:DebugSymbols=true \
  -p:UseAppHost=false \
  --runtime linux-x64 \
  -o "$PUBLISH_DIR" \
  -p:AssemblyName="$ASSEMBLY_NAME"

# =========================
# RUN
# =========================
cd "$PUBLISH_DIR"

dotnet "$ASSEMBLY_NAME.dll" & disown
