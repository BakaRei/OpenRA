#!/bin/sh
# Launch OpenRA for RL agent training.
#
# Starts the game client with a local game, adds an RL bot and an AI opponent,
# and auto-starts the match. The RL bot opens a gRPC server for the Python agent.
#
# Usage:
#   $ ./launch-rl.sh                              # Default: RA mod, Normal AI opponent
#   $ Mod="cnc" ./launch-rl.sh                    # Use C&C mod
#   $ Map="singles.oramap" ./launch-rl.sh         # Use a specific map
#   $ BotType="rush" ./launch-rl.sh               # Change AI opponent type
#   $ RLSlot="Multi0" AISlot="Multi1" ./launch-rl.sh  # Specify player slots

set -o errexit || exit $?

ENGINEDIR=$(dirname "$0")

Mod="${Mod:-"ra"}"
Map="${Map:-"singles.oramap"}"
RLSlot="${RLSlot:-"Multi1"}"
AISlot="${AISlot:-""}"
BotType="${BotType:-"normal"}"

# Build the bots configuration string
# The human player auto-joins one slot; we add bots to the other slot(s)
BOTS="${RLSlot}:rl-agent"
if [ -n "$AISlot" ]; then
    BOTS="${BOTS},${AISlot}:${BotType}"
fi

export DOTNET_ROLL_FORWARD=LatestMajor

dotnet "${ENGINEDIR}/bin/OpenRA.dll" Engine.EngineDir="${ENGINEDIR}" Game.Mod="$Mod" \
     Launch.Map="$Map" \
     Launch.Bots="$BOTS"
