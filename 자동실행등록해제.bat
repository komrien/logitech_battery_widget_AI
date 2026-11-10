@echo off
chcp 65001 >nul
setlocal

set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Mouse Battery Widget.lnk"

if exist "%LNK%" (
    del "%LNK%"
    echo 자동 실행을 해제했어요. 다음 로그인부터 위젯이 자동으로 켜지지 않아요.
) else (
    echo 자동 실행이 등록되어 있지 않아요.
)
echo   지금 떠 있는 위젯은 그대로예요. 끄려면 위젯을 우클릭 - 종료 하세요.
echo.
pause
