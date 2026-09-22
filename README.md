# KeyMate

자주 쓰는 말을, 더 빠르게. C# / .NET 8 / WPF로 만든 Windows 트레이 텍스트 대치 앱입니다.

`ㅈㅅ`을 입력하고 **Space**를 누르면 `안녕하세요. 좋은 하루 보내세요. `로 바뀝니다.

## 실행

Windows 10/11 x64용 자체 포함 배포본의 `KeyMate.exe`를 실행하세요. 별도의 .NET 설치는 필요하지 않습니다. 폴더를 원하는 위치에 둔 후 실행하고, 자동 시작은 설정에서 켜세요. 창의 X 버튼은 트레이로 숨기며, 완전히 종료하려면 트레이 메뉴의 **종료**를 선택합니다. 다시 실행하면 기존 창이 열립니다.

1. **새 항목 추가**에서 공백 없는 단축어와 대치 문구를 저장합니다.
2. 메인 화면 아래에서 연습한 다음 사용할 앱의 입력창에서 확인합니다.
3. 항목 오른쪽 스위치로 사용 여부를 바꾸고, `⋯` 메뉴에서 수정하거나 삭제합니다.
4. `@@`, `주소1` 예시는 실제 정보를 넣기 전까지 기본으로 꺼져 있습니다.

### 기능

- 항목 추가·수정·삭제·검색, 항목별 활성화, 대소문자·단어 경계 옵션
- 전역 Space 치환, 한글 IME가 조합을 완료한 뒤 실제 입력 문자열 확인
- 선택 가능한 Enter·Tab 치환: **영문 키보드의 ASCII 단축어만** 지원
- SQLite 저장, 트레이 일시 정지, Windows 로그인 시 자동 시작
- 앱별 제외 목록, 라이트·다크·시스템 테마
- 여러 줄 문구, `{date}` → `yyyy-MM-dd`, `{time}` → `HH:mm`
- JSON 내보내기·가져오기: 같은 단축어 갱신, 새 항목 추가, 기존 다른 항목 유지

### 동작 범위와 제한

- iPhone의 입력 시스템 자체를 확장하는 기능과 달리, Windows **UI Automation TextPattern**을 제공하는 편집창에서 작동합니다. 모든 프로그램에서 동작한다는 보장은 없습니다.
- **한글 단축어에는 Space를 사용하세요.** Enter·Tab은 기본으로 꺼져 있고, 한국어·일본어·중국어 입력 레이아웃에서는 치환하지 않습니다. 영문 키보드에서 치환에 성공한 Enter·Tab은 소비되므로 문구만 확정되며, 전송/탭 이동은 한 번 더 누르면 됩니다. 일치하지 않으면 원래 키를 전달합니다.
- Space 뒤에서 빠르게 다음 문자를 입력하면 정확성을 위해 이번 치환을 생략할 수 있습니다. 입력을 계속하기 전에 치환이 끝나는지 확인하세요.
- 비밀번호, 읽기 전용, 선택된 텍스트, 수정 키(Ctrl/Alt/Shift/Win) 조합, 제외된 앱은 건너뜁니다. 일반 실행 앱에서 관리자 권한 앱에 입력을 주입할 수 없습니다.
- 터미널/원격 데스크톱은 기본 제외 목록에 있습니다. 게임·원격 입력·일부 웹 편집기·앱의 접근성 미지원 영역은 지원되지 않을 수 있습니다. Chrome, 카카오톡, VS Code, Word의 모든 버전에 대한 호환성 검증은 아직 하지 않았습니다.
- 여러 줄 문구의 줄바꿈은 **Shift+Enter**로 입력합니다. 대상 앱에서 Shift+Enter가 줄바꿈을 만드는지 먼저 확인하세요. 임의 앱의 전송 키 설정까지 판단하지는 않습니다. 클립보드는 사용하거나 변경하지 않습니다.
- 단축어는 NFC 형태의 1~64자이며 공백·이모지·결합 문자는 지원하지 않습니다. 문구는 4,000자까지이며 줄바꿈을 제외한 제어 문자와 탭은 받지 않습니다.
- 클라우드 동기화, 클립보드 변수, 커서 위치 마커, 앱별 허용 목록, TSF 입력 서비스 설치, 코드 서명/자동 업데이트는 포함하지 않습니다.

## 데이터

`%LOCALAPPDATA%\KeyMate\keymate.db`에 항목과 설정을 저장합니다. 입력 기록은 저장하지 않으며, 현재 커서 앞의 짧은 텍스트만 일시적으로 검사합니다. 서버 통신, 원격 수집, 클립보드 읽기/쓰기는 없습니다. DB와 JSON 백업은 별도 암호화되지 않은 로컬 파일입니다.

가져오기는 형식·길이·중복 검사를 모두 통과한 뒤 하나의 트랜잭션으로 저장합니다. 다른 PC의 자동 시작 경로·테마·제외 앱 설정은 JSON 백업에 포함되지 않습니다. 앱 제거 시 자동 시작을 먼저 끄고 실행 파일을 삭제하면 됩니다. 사용자 데이터 삭제는 별도로 선택하세요.

## 개발

Windows와 .NET SDK 8.0.425 이상(8.0 패치 계열)이 필요합니다. 의존성은 NuGet 잠금 파일에 고정합니다.

```powershell
dotnet restore KeyMate.sln --locked-mode
dotnet build KeyMate.sln -c Release --no-restore
dotnet run --project tests/KeyMate.Tests -c Release --no-build
dotnet run --project tests/KeyMate.Integration -c Release --no-build
dotnet run --project src/KeyMate.App -c Release --no-build
```

통합 테스트는 Windows의 대화형 데스크톱과 Microsoft 한국어 IME가 필요합니다. 임시 테스트 창에만 키를 보내며 포커스가 달라지면 주입을 거절합니다. 테스트 동안 임시 창의 포커스를 유지하세요. 마지막에 입력 언어와 IME 상태를 복구합니다. 임시 테스트 창과 별도 프로세스의 편집창을 모두 검사합니다.

```powershell
# 별도 .NET 런타임 설치가 필요 없는 단일 실행 파일
./publish.ps1
```

결과는 `artifacts/win-x64/`에 생성합니다. 다른 위치는 `./publish.ps1 -OutputDirectory 'C:\path\KeyMate'`로 지정합니다. 스크립트는 기존 폴더를 재귀 삭제하지 않습니다.

```powershell
# 예제 데이터로 WPF 화면 렌더링. 사용자 DB와 자동 시작 설정을 사용하지 않습니다.
dotnet run --project src/KeyMate.App -c Release -- --render-preview artifacts/preview
```

## 구조

- `KeyMate.Core`: 항목 검증·단어 경계 매칭·변수 확장·SQLite·JSON
- `KeyMate.App`: WPF 관리 화면, 트레이, 시작 프로그램 등록, 키보드/마우스 훅
- `TextProbe`: 별도 MTA 작업에서 커서와 읽기 전용/비밀번호 상태, 실제 텍스트 확인
- `ExpansionEngine`: 훅 전용 메시지 스레드, 포커스/입력 세대 검사, 제한 시간, 자기 주입 이벤트 배제
- `tests`: 핵심 규칙/저장소와 실제 Windows 입력 통합 검증

Space는 대상 앱에 먼저 전달해 IME를 확정시킨 뒤 검사합니다. 텍스트 읽기는 훅 콜백 밖에서 하며, 검증된 단축어와 구분 문자만 Backspace + Unicode `SendInput`으로 바꿉니다. 선택 범위나 전체 문서 값을 덮어쓰지 않습니다. Enter·Tab의 제한 시간은 180ms, Space 검사 유효 시간은 240ms입니다. 응답하지 않는 접근성 공급자에서 진행 중인 읽기가 끝나지 않으면 추가 검사를 쌓지 않고 일반 입력을 통과시킵니다.

설계 참고: [Microsoft LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [UI Automation 스레딩](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues), [TextPattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview), [SendInput과 권한 제약](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
