@echo off
REM ============================================================
REM  push.bat - tu dong add + commit + push len GitHub
REM  Cach dung:   push.bat "noi dung commit"
REM  Khong ghi message thi mac dinh la "update"
REM ============================================================
setlocal
set "MSG=%~1"
if "%MSG%"=="" set "MSG=update"

git add -A
git commit -m "%MSG%"
if errorlevel 1 (
  echo [i] Khong co thay doi de commit, van thu push...
)
git push
echo.
echo [OK] Da day len GitHub.
endlocal
