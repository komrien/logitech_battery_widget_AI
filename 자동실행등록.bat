@echo off
chcp 65001 >nul
setlocal

rem 위젯 exe 찾기: 이 bat과 같은 폴더 → csharp\publish 순서
set "EXE=%~dp0LogitechBatteryWidget.exe"
if not exist "%EXE%" set "EXE=%~dp0csharp\publish\LogitechBatteryWidget.exe"
if not exist "%EXE%" (
    echo [오류] LogitechBatteryWidget.exe 를 찾지 못했어요.
    echo        csharp 폴더에서 "dotnet publish -c Release -o publish" 로 먼저 빌드해 주세요.
    echo.
    pause
    exit /b 1
)

set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Mouse Battery Widget.lnk"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:LNK);" ^
  "$s.TargetPath = $env:EXE;" ^
  "$s.WorkingDirectory = Split-Path $env:EXE;" ^
  "$s.Description = 'Logitech mouse battery widget';" ^
  "$s.Save()"

if exist "%LNK%" (
    echo 자동 실행을 등록했어요. 다음 로그인부터 위젯이 자동으로 켜져요.
    echo   실행 파일: %EXE%
    tasklist /fi "imagename eq LogitechBatteryWidget.exe" | find /i "LogitechBatteryWidget.exe" >nul || start "" "%EXE%"
) else (
    echo [오류] 자동 실행 등록에 실패했어요.
)
echo.
pause
