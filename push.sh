#!/usr/bin/env bash
# push.sh - tu dong add + commit + push len GitHub
# Cach dung:  ./push.sh "noi dung commit"
set -e
MSG="${1:-update}"
git add -A
git commit -m "$MSG" || echo "[i] Khong co thay doi de commit, van thu push..."
git push
echo "[OK] Da day len GitHub."
