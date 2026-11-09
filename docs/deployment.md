# Windows x64 통합 설치

배포 파일: `HarborDrive-0.1.3-x64-Setup.exe`

이 파일 하나만 회사 PC에 전달하면 됩니다. x64는 x86-64 / x86_64 / AMD64와 같은 64비트 명령어 아키텍처를 가리키는 Windows/.NET 표기입니다. Intel과 AMD 64비트 Windows PC에 사용합니다. 32비트 Windows(x86)는 설치 대상이 아닙니다. Windows 11 ARM64에서도 x64 에뮬레이션으로 실행할 수 있습니다.

## 설치 내용

- HarborDrive 0.1.3, x64 .NET 10 런타임, x64 rclone 1.75.2를 설치합니다. .NET 별도 설치나 설치 중 인터넷 접속이 필요하지 않습니다.
- 공식 WinFsp 2.1.25156 MSI를 내장합니다. x64 DLL 버전이 2.1 이상이면 재사용하고, 없거나 오래되었으면 공식 MSI를 실행합니다. WinFsp의 서명과 SHA256을 빌드에서 확인하고, 실행 시 내장 MSI 해시를 다시 확인합니다.
- 기본 앱 경로는 `%ProgramFiles%\HarborDrive`입니다. 모든 사용자용 시작 메뉴와 선택적인 바탕화면 바로가기를 만듭니다.
- S3 인증 정보는 설치 파일에 포함하지 않습니다. 각 Windows 사용자가 앱에서 자신의 연결을 설정합니다.
- 설치에는 관리자 권한이 필요합니다. 설치 후 앱은 일반 사용자로 실행합니다. 관리자가 다른 계정으로 승인했다면 설치 완료 화면의 실행 기능은 원래 사용자로 실행하도록 설정했습니다.
- 드라이버 설치 후 재시작이 필요한 경우 완료 화면에 안내하며, 무인 배포는 `/NORESTART`를 사용해 재시작 시점을 관리할 수 있습니다.

HarborDrive 앱과 통합 설치 파일에는 아직 코드 서명이 없습니다. 회사의 배포 정책에 코드 서명이 필요하면 조직의 인증서로 서명해야 합니다. 내장 WinFsp 원본 MSI는 공급자의 유효한 디지털 서명을 유지합니다.

## 업데이트와 제거

먼저 앱에서 **모든 연결 해제 후 종료**를 선택하세요. X 버튼은 트레이에 숨기는 기능입니다.

설치·제거 프로그램은 실행 중인 HarborDrive나 설치 폴더의 rclone을 확인하면 진행하지 않습니다. 미전송 파일이 있는 프로세스를 자동으로 강제 종료하지 않습니다. 프로세스 상태를 조회하지 못한 경우도 완료로 간주하지 않습니다. 상태 조회에는 Windows WMI 서비스를 사용합니다.

Windows 설정의 설치된 앱에서 HarborDrive를 제거할 수 있습니다. 제거 시 다음을 보존합니다.

- S3 파일 전체
- `%LOCALAPPDATA%\HarborDrive`의 설정·인증 정보·캐시·로그
- 사용자가 지정한 별도 캐시 경로
- 다른 프로그램과 공유하는 WinFsp

로드된 Windows 사용자 프로필 중 이 설치 경로를 정확히 가리키는 HarborDrive 로그인 시작 항목만 제거합니다. 다른 경로의 항목과 다른 프로그램의 항목은 변경하지 않습니다.

## 무인 배포

관리자 권한으로 실행합니다.

```powershell
.\HarborDrive-0.1.3-x64-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /LOG="C:\Temp\HarborDrive-install.log"
```

성공은 종료 코드 0, 설치가 성공했지만 재시작이 필요하면 3010입니다. 그 외 코드는 실패이며 설치 로그를 확인해야 합니다. 앱 또는 엔진이 실행 중이라 설치 준비 단계에서 중단되면 Inno Setup의 준비 단계 실패 코드가 반환됩니다. 무인 설치는 앱을 자동 실행하지 않습니다.

```powershell
& "$env:ProgramFiles\HarborDrive\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="C:\Temp\HarborDrive-uninstall.log"
```

명령을 자동화 시스템에서 호출할 때는 프로세스 종료를 기다린 뒤 종료 코드를 확인하세요. 설치·제거 테스트 스크립트는 `scripts/test-installer.ps1`입니다. 실제 관리자 설치와 레지스트리 등록/제거를 수행하므로 전용 시험 환경에서 사용합니다.

SHA256 확인용 `.exe.sha256` 파일은 무결성 확인용 보조 파일입니다. 설치에 필요한 것은 Setup.exe 하나입니다.
