#!/bin/bash
# Вызов Claude Code для WowBot — читает промпт из stdin
PROMPT=$(cat)
if [ -z "$PROMPT" ]; then echo 'Error: no prompt'; exit 1; fi

cd /home/claude/wow-bot

RESUME_FLAG=""
if [ -n "$CLAUDE_RESUME" ]; then
    RESUME_FLAG="--resume $CLAUDE_RESUME"
fi

EDIT_FLAG=""
if [ "$CLAUDE_ALLOW_EDIT" = "1" ]; then
    EDIT_FLAG="--dangerously-skip-permissions"
fi

claude -p "$PROMPT" $EDIT_FLAG --output-format json $RESUME_FLAG 2>&1
