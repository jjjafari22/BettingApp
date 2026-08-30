#!/bin/bash
PAYLOAD=$(cat)

# Extract CommandLine using jq
CMD_LINE=$(echo "$PAYLOAD" | jq -r '.toolCall.args.CommandLine // ""')

# Check if curl is in the command line
if echo "$CMD_LINE" | grep -qE "(^|[[:space:]])curl([[:space:]]|$)"; then
  echo '{"decision": "deny", "reason": "curl is strictly banned in this project via .agents/hooks.json. Use read_url_content tool instead."}'
  exit 0
fi

# Check if dotnet run is in the command line
if echo "$CMD_LINE" | grep -qE "(^|[[:space:]])dotnet[[:space:]]+run([[:space:]]|$)"; then
  echo '{"decision": "deny", "reason": "dotnet run is strictly banned in this project because it crashes the machine. You MUST use dotnet build followed by executing the .dll directly."}'
  exit 0
fi

echo '{"decision": "allow"}'
