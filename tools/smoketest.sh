#!/bin/bash
# Boots a throwaway Vintage Story server with dist/froststory loaded, creates a world, and reports
# errors. Exit 0 = the server came up and nothing logged an error mentioning the mod.
# Usage: tools/smoketest.sh [repo-dir] [extra server args...]     (build first)
REPO=${1:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}; shift
VS=${VINTAGE_STORY:-/opt/vsenv/vs}
export PATH=/opt/vsenv/dotnet:$PATH DOTNET_ROOT=/opt/vsenv/dotnet
DATA=$(mktemp -d /tmp/vsdata.XXXX)
PORT=$((43000 + RANDOM % 2000))
mkdir -p $DATA/Mods && cp -r $REPO/dist/froststory $DATA/Mods/ || { echo "no dist/froststory: build first"; exit 2; }
cd $VS
timeout 300 dotnet VintagestoryServer.dll --dataPath $DATA --port $PORT --tracelog "$@" > $DATA/out.txt 2>&1 &
PID=$!
for i in $(seq 1 290); do
  sleep 1
  grep -q "Dedicated Server now running" $DATA/out.txt $DATA/Logs/server-main.log 2>/dev/null && { UP=1; sleep 5; break; }
  kill -0 $PID 2>/dev/null || break
done
kill $PID 2>/dev/null; sleep 2; kill -9 $PID 2>/dev/null
LOGS="$DATA/Logs/server-main.log $DATA/Logs/server-debug.log $DATA/out.txt"
echo "== server up: ${UP:-0}   logs: $DATA/Logs"
echo "== errors/warnings mentioning the mod:"
MOD=$(grep -h -i -E "error|exception|warning|failed|could not|not found" $LOGS 2>/dev/null | grep -i -E "froststory|frost" | sort -u | head -60)
echo "${MOD:-  (none)}"
echo "== all errors (first 40):"
grep -h -E "\[(Error|Fatal)\]|Exception" $LOGS 2>/dev/null | sort -u | head -40
[ "${UP:-0}" = 1 ] && [ -z "$(echo "$MOD" | grep -i -E 'error|exception|failed')" ]
