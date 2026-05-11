#!/usr/bin/env bash
# Pre-commit hook: runs backend build and frontend typecheck
# based on which file types are staged.
# When invoked as a Claude hook, stdin carries a JSON payload;
# skip if the tool call is not a git-commit Bash invocation.

STDIN_DATA=""
if [ ! -t 0 ]; then
  STDIN_DATA=$(cat)
fi

if [ -n "$STDIN_DATA" ]; then
  TOOL_NAME=$(echo "$STDIN_DATA" | python3 -c "import sys,json; print(json.load(sys.stdin).get('tool_name',''))" 2>/dev/null || true)
  COMMAND=$(echo "$STDIN_DATA" | python3 -c "import sys,json; print(json.load(sys.stdin).get('tool_input',{}).get('command',''))" 2>/dev/null || true)
  if [ "$TOOL_NAME" != "Bash" ] || ! echo "$COMMAND" | grep -q '\bgit[[:space:]]\+commit\b'; then
    exit 0
  fi
fi

cd "$(git rev-parse --show-toplevel)" || exit 1

STAGED=$(git diff --cached --name-only 2>/dev/null)
HAS_CS=false
HAS_VUE=false

if echo "$STAGED" | grep -qE '\.cs$'; then
  HAS_CS=true
fi
if echo "$STAGED" | grep -qE '\.(vue|ts)$'; then
  HAS_VUE=true
fi

ERRORS=""

if [ "$HAS_CS" = true ]; then
  RESULT=$(dotnet build backend/Taskdeck.sln -c Release --nologo -v q 2>&1)
  if [ $? -ne 0 ]; then
    ERRORS="Backend build failed:\n$RESULT"
  fi
fi

if [ "$HAS_VUE" = true ]; then
  RESULT=$(cd frontend/taskdeck-web && npx vue-tsc --noEmit 2>&1)
  if [ $? -ne 0 ]; then
    ERRORS="$ERRORS\nFrontend typecheck failed:\n$RESULT"
  fi
fi

if [ -n "$ERRORS" ]; then
  echo "PRE-COMMIT CHECK FAILED:" >&2
  echo -e "$ERRORS" >&2
  exit 2
fi

exit 0
