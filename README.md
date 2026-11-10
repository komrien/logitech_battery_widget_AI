# Logitech Battery Widget

로지텍 **G HUB**와 연동해 무선 마우스의 배터리 잔량과 충전 상태를 실시간으로 보여주는 작은 Windows 데스크톱 위젯입니다.

## 기능

- 배터리 잔량(%)과 막대 그래프를 보여줍니다. 색은 잔량에 따라 초록(36% 이상) / 노랑(16~35%) / 빨강(15% 이하)으로 바뀝니다.
- 충전 상태를 표시합니다: `⚡ 충전 중`, `✔ 충전 완료`, `배터리 사용 중 · 약 N시간 남음`
- 실시간으로 갱신합니다. G HUB의 배터리 변경 알림을 받고, 혹시 놓칠 때를 대비해 3초마다 직접 확인도 합니다.
- G HUB에 연결된 무선 기기가 여러 개면 모두 표시합니다.
- 테두리 없는 작은 창이라 드래그해서 옮길 수 있고, 옮긴 위치를 기억합니다.
- 우클릭 메뉴: 새로고침 / 항상 위 / 종료

## 동작 방식

1. **G HUB 로컬 API** (`ws://localhost:9010`)에 연결해 `/battery/<deviceId>/state` 값을 받습니다. 잔량, 충전 여부, 완충 여부, 예상 사용 시간을 얻을 수 있습니다.
2. API에 연결할 수 없으면 `%LOCALAPPDATA%\LGHUB\settings.db`를 **읽기 전용**으로 열어 마지막으로 저장된 잔량을 보여줍니다. 이 경우 충전 여부는 표시되지 않습니다.

G HUB 설정은 바꾸지 않습니다.

## 두 가지 버전

| | Python 버전 | C# 버전 |
|---|---|---|
| 위치 | `mouse_battery_widget.pyw` | `csharp/` |
| 실행 | Python + `websockets` 설치 필요 | exe 하나로 실행, 따로 설치할 것 없음 |
| 화면 | tkinter | WPF (둥근 모서리, 반투명) |

기능은 두 버전이 같습니다. 공통으로 Windows 10/11과 [Logitech G HUB](https://www.logitechg.com/innovation/g-hub.html)(설치 및 실행 중)가 필요합니다.

## C# 버전

### 실행 파일로 쓰기
[Releases](../../releases)에서 `LogitechBatteryWidget.exe`를 받아 실행합니다. 런타임이 포함되어 있어서 .NET을 따로 설치하지 않아도 됩니다.

### 직접 빌드
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

```bash
cd csharp
dotnet run                                  # 바로 실행
dotnet publish -c Release -o publish        # publish/LogitechBatteryWidget.exe 단일 파일 생성
```

설정(위치, 항상 위)은 `%APPDATA%\LogitechBatteryWidget\config.json`에 저장됩니다.

## Python 버전

Python 3.8 이상(tkinter 포함)이 필요합니다.

```bash
pip install -r requirements.txt
pythonw mouse_battery_widget.pyw
```

### 윈도우 시작 시 자동 실행

C# 버전은 bat 파일을 더블클릭하면 됩니다.

- `자동실행등록.bat`: 시작 프로그램에 위젯을 등록하고, 위젯이 꺼져 있으면 바로 실행합니다. exe는 bat과 같은 폴더에서 먼저 찾고, 없으면 `csharp\publish\`에서 찾습니다. Releases에서 받은 exe 옆에 bat을 두고 써도 됩니다.
- `자동실행등록해제.bat`: 시작 프로그램에서 위젯을 뺍니다. 지금 떠 있는 위젯은 끄지 않습니다.

직접 등록하려면 `Win + R` → `shell:startup` 입력 → 열린 폴더에 아래 내용의 바로가기를 만듭니다.

- C# 버전 대상: `<경로>\LogitechBatteryWidget.exe`
- Python 버전 대상: `pythonw.exe "<경로>\mouse_battery_widget.pyw"`

## 참고

- G HUB 로컬 API는 공식 공개 API가 아니어서 G HUB가 업데이트되면 동작이 바뀔 수 있습니다.
- PRO X SUPERLIGHT로 테스트했습니다.
