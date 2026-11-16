#!/bin/bash
# Installs the .NET 10 SDK and a Vintage Story 1.22 dedicated server under /opt/vsenv, which is
# enough to build the mod and boot it headless (tools/smoketest.sh). Idempotent.
set -e
ROOT=/opt/vsenv
VS_VERSION=${VS_VERSION:-1.22.7}
mkdir -p $ROOT
if [ ! -x $ROOT/dotnet/dotnet ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o $ROOT/dotnet-install.sh
  bash $ROOT/dotnet-install.sh --channel 10.0 --install-dir $ROOT/dotnet
fi
if [ ! -f $ROOT/vs/VintagestoryAPI.dll ]; then
  curl -sSL https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_${VS_VERSION}.tar.gz -o $ROOT/vs_server.tgz
  mkdir -p $ROOT/vs && tar xzf $ROOT/vs_server.tgz -C $ROOT/vs && rm $ROOT/vs_server.tgz
fi
echo "export PATH=$ROOT/dotnet:\$PATH DOTNET_ROOT=$ROOT/dotnet VINTAGE_STORY=$ROOT/vs DOTNET_CLI_TELEMETRY_OPTOUT=1"
