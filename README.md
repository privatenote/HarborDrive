# HarborDrive

Windows에서 Amazon S3 / S3 호환 스토리지를 탐색기 네트워크 드라이브로 연결하는 독립 데스크톱 앱입니다. .NET 10 WPF + rclone + WinFsp를 사용합니다. 기존 프로그램을 수정하지 않고 지정한 드라이브 문자로 파일을 읽고 저장합니다.

[최신 설치 파일](https://github.com/privatenote/HarborDrive/releases/latest) · [업데이트 방식](docs/updates.md) · [MIT / 타사 라이선스](docs/licensing.md)

## 바로 실행

Intel / AMD 64비트 Windows용 통합 설치 파일: **`artifacts/HarborDrive-0.1.2-x64-Setup.exe`**

설치 파일 하나를 배포해 실행하면 됩니다. .NET 런타임·x64 rclone·공식 WinFsp MSI가 모두 들어 있어 설치 중 추가 다운로드가 없습니다. Windows 관리자 승인이 필요하며, 호환 WinFsp가 이미 설치되어 있으면 재사용합니다. 시작 메뉴에서 HarborDrive를 일반 사용자 권한으로 실행하세요.

설치·제거·기업 배포 옵션은 [설치 안내](docs/deployment.md)에 있습니다. 이 환경의 ARM64 portable 실행 파일도 `artifacts/HarborDrive-win-arm64/HarborDrive.exe`에 남아 있습니다. portable 폴더로 실행할 때는 다음 순서를 따릅니다.

1. `HarborDrive.exe`를 일반 사용자 권한으로 실행합니다. 배포 폴더 전체를 함께 보관하세요. .NET 런타임은 포함되어 있습니다.
2. **첫 번째 드라이브 연결하기**에서 Access Key / Secret Key, 리전, 버킷, 경로, 드라이브 문자를 입력합니다. AWS S3는 엔드포인트를 비워둡니다.
3. **연결 확인**으로 현재 경로를 읽을 수 있는지 검사하고 **연결 저장 → 드라이브 연결**을 누릅니다. 버킷 목록 권한이 없어도 이름을 직접 입력할 수 있습니다.
4. 탐색기에서 지정한 드라이브를 사용합니다. 전송 대기열과 로그는 앱에서 확인합니다.

portable 버전은 WinFsp 2.1 이상이 필요합니다. 통합 설치 파일은 WinFsp가 필요하면 함께 설치합니다. 앱 자체는 일반 사용자 권한으로 실행하세요. 관리자 세션과 일반 탐색기는 서로 다른 드라이브를 볼 수 있습니다.

평소에는 기본 연결 하나만 사용하면 됩니다. 추가 연결은 **설정 → 추가 S3 연결…**에 있습니다. 연결마다 고유한 드라이브 문자를 사용합니다.

## 제공 기능

- AWS S3, 사용자 지정 S3 호환 엔드포인트, 버킷·폴더 탐색, 읽기 전용 모드
- Access Key / Secret Key / Session Token 또는 AWS 기본 인증 체인·이름 있는 프로필
- 전체 VFS 디스크 캐시, 캐시 위치·정리 목표 용량·최소 여유 공간·쓰기 지연 설정
- 업로드 대기·진행·재시도 횟수·전송 속도·캐시 사용량·공간 부족 표시
- 안전한 연결 해제, 미전송 캐시를 보존하는 명시적 중지, 대기열 즉시 재시도
- 트레이 실행, 로그인 시 실행, 연결별 자동 마운트, 엔진 종료 시 지수 지연 재연결
- UI 종료/비정상 종료 후 기존 엔진 재연결, 재실행 시 같은 캐시를 사용한 업로드 복구
- Windows DPAPI로 인증 정보와 복구 기록 암호화, 원자적 설정 저장
- 인증된 loopback RC API, 무작위 비밀번호, PID·시작 시각·실행 파일 경로 확인
- 연결별 JSON 로그와 크기 기반 로그 순환

## 저장·해제·복구

**파일 저장 완료와 S3 업로드 완료는 서로 다릅니다.** 기본적으로 파일을 닫은 뒤 5초가 지나면 업로드합니다. rclone의 파일 핸들 캐시 때문에 추가 지연이 있을 수 있습니다. 상태의 ‘업로드 대기 없음’은 현재 엔진의 대기열이 비었다는 뜻이며, 파일을 열고 편집하는 프로그램의 미저장 내용이나 원격 전체 무결성까지 보증하지 않습니다.

연결을 해제하기 전에 파일을 사용하는 프로그램을 닫으세요. **안전하게 해제**는 두 번의 최신 상태 검사와 디스크의 dirty 메타데이터 검사를 수행합니다. 대기·전송·오류가 있거나 상태를 확인하지 못하면 해제하지 않습니다. 해제 도중 다른 프로그램이 다시 쓰는 경쟁 조건까지 잠그지는 못합니다.

인터넷이 끊겼을 때 중지가 필요하면 **캐시 보존 후 중지**를 사용하세요. 캐시는 삭제하지 않습니다. 같은 연결로 다시 마운트하면 전송을 재개합니다. **미전송 캐시가 있는 동안 캐시 폴더를 삭제·이동하거나 다른 연결 설정으로 교체하지 마세요.** 대상 버킷·엔드포인트·캐시 위치를 바꿀 때에는 dirty 캐시를 검사하고 새 연결 ID를 부여하여 기존 캐시와 섞이지 않게 합니다.

창의 X는 트레이로 숨깁니다. 앱을 끝내려면 **모든 연결 해제 후 종료**를 사용합니다. OS 종료/전원 손실은 통제하지 않으며, 이미 디스크에 기록된 캐시를 다음 실행에서 복구하는 방식입니다. Windows 강제 재부팅과 전원 차단 시험은 수행하지 않았습니다. DPAPI 파일은 이 Windows 사용자 계정에 묶이므로 다른 사용자·PC로 복사하면 인증 정보와 복구 기록을 복호화할 수 없습니다.

## 데이터와 인증

기본 위치: `%LOCALAPPDATA%\HarborDrive`

| 경로 | 내용 |
| --- | --- |
| `settings.json` | 인증 키를 제외한 연결 설정 |
| `<connection-id>.credential` | DPAPI 암호화 인증 정보 |
| `sessions/*.session` | DPAPI 암호화 엔진 복구 정보 |
| `cache/<connection-id>/` | rclone VFS 캐시 및 메타데이터 |
| `logs/<connection-id>.log` | 최근 로그; 최대 10MiB, 백업 3개 |

캐시 기준 폴더를 지정해도 그 아래 연결 ID별 하위 폴더를 사용합니다. 캐시를 직접 정리하는 기능은 두지 않았습니다. 연결 설정 삭제도 S3 파일과 캐시를 삭제하지 않습니다.

인증 정보는 rclone 자식 프로세스의 환경 변수로 전달하고 명령줄이나 평문 설정에 넣지 않습니다. RC는 `127.0.0.1`에만 바인딩합니다. 같은 사용자 권한의 프로그램이나 관리자에 대한 보안 격리를 제공하는 것은 아닙니다.

AWS 기본 인증 체인은 rclone/AWS SDK가 지원하는 환경 변수, 공유 자격 증명 파일, 프로필 등을 사용합니다. SSO·역할별 인증 동작은 사용하는 AWS 프로필로 확인하세요. 직접 입력한 임시 Session Token은 자동 발급하지 않으므로 만료 시 연결을 중지하고 갱신해야 합니다.

필요한 IAM 권한의 기본 예시는 [docs/iam-policy.example.json](docs/iam-policy.example.json)에 있습니다. 버킷·prefix·KMS 설정에 맞게 조정하세요. 버킷 탐색에 쓰는 `s3:ListAllMyBuckets`는 선택 권한입니다.

## 파일시스템 특성과 현재 범위

- 탐색기에서 삭제하면 실제 S3 객체를 삭제합니다. 앱의 연결 설정 삭제와 별개입니다.
- 이름 변경은 S3에서 복사와 삭제로 구현되므로 원자적인 로컬 rename과 다릅니다.
- 여러 PC 사이의 파일 잠금·충돌 해결은 제공하지 않습니다. 동시 덮어쓰기에서는 마지막 업로드가 반영될 수 있습니다.
- 오프라인에서는 **이미 캐시된 파일과 유효한 디렉터리 메타데이터가 있는 작업만** 가능할 수 있습니다. 처음 여는 파일·경로와 만료된 디렉터리는 네트워크가 필요합니다.
- 디렉터리 캐시는 5분입니다. 외부에서 바뀐 파일 목록을 곧바로 보고 싶으면 **원격 목록 갱신**을 누르세요. 열려 있는 파일의 외부 변경을 병합하지 않습니다.
- 캐시 용량과 최소 여유 공간은 정리 목표입니다. 사용 중·미전송 파일은 정리하지 않아 목표를 초과할 수 있습니다. 로컬 디스크 여유 공간을 확보하세요.
- DB, VM 이미지, 다중 작성자 공유 작업처럼 로컬 디스크의 모든 의미론이 필요한 용도에 대한 호환성은 검증하지 않았습니다.
- ARM64와 x64 실행 검사를 수행했습니다. x64 검사는 ARM64 Windows의 x64 에뮬레이션 환경이며, Intel / AMD 실물 PC와 실제 AWS 계정 시험은 남아 있습니다.
- x64 통합 설치·제거와 GitHub 정식 릴리스 업데이트 감지를 제공합니다. 설치 파일 자동 실행과 코드 서명은 아직 포함하지 않습니다.

## 개발·테스트·배포

PowerShell에서 실행합니다. 개발 도구는 프로젝트의 `.tools`에 설치하며 rclone ZIP은 공식 SHA256, .NET SDK는 공식 SHA512, WinFsp MSI는 Authenticode 서명을 검증합니다.

```powershell
.\scripts\setup.ps1 -InstallWinFsp
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test.ps1 -Integration
.\scripts\build.ps1 -Publish -Architecture arm64
.\scripts\build-installer.ps1
.\scripts\test.ps1 -Integration -Architecture x64
```

통합 테스트는 사용자 S3에 접근하지 않습니다. 임시 로컬 `rclone serve s3` 서버와 비어 있는 드라이브 문자를 사용합니다. 테스트 파일과 로그는 `.devdata/test-*`에 남습니다. 이 테스트용 S3 서버는 실제 AWS S3의 모든 동작을 대체하지 않습니다.

x64 portable 패키지는 `build.ps1 -Publish -Architecture x64`, 통합 설치 파일은 `build-installer.ps1`로 만듭니다. ARM64 개발 환경에서도 교차 빌드할 수 있습니다. 빌드 스크립트가 대상 아키텍처의 rclone을 자동 준비하고 앱·런타임·rclone의 PE 아키텍처를 검사합니다.

프로젝트별 데이터 격리가 필요하면 `HARBORDRIVE_HOME`을 절대 경로로 설정하세요. 로그인 시작 경로는 실행 중인 EXE의 위치를 등록하므로 배포 폴더 위치를 고정한 후 로그인 실행을 켜세요. 이 기능은 사용자별 레지스트리 Run 항목 `HarborDrive`만 사용합니다.

## 설계와 검증 기록

`src/HarborDrive.Core`는 UI와 분리된 연결 관리·상태 조회·복구·보호 저장소입니다. `src/HarborDrive.App`은 WPF/트레이 UI이고, `tests/HarborDrive.Tests`는 외부 테스트 프레임워크 없이 실행 가능한 실제 동작 검증 프로그램입니다.

[검증 기록](docs/validation.md) · [타사 라이선스](THIRD-PARTY-NOTICES.md)

앱·설치 프로그램·트레이에 직접 제작한 아이콘을 사용합니다. 트레이는 연결 해제·연결·전송·오류 상태를 구분합니다. 벡터 원본은 `src/HarborDrive.App/Assets/*.svg`이며, Node.js와 sharp가 설치된 개발 환경에서 `node scripts/create-icons.cjs`로 여러 크기의 ICO/PNG를 재생성할 수 있습니다. 생성 파일을 소스에 포함하므로 일반 .NET 빌드에는 Node.js가 필요하지 않습니다.

HarborDrive 코드와 직접 제작한 아이콘은 [MIT](LICENSE)입니다. 타사 구성요소는 원래 라이선스를 유지합니다. WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos. https://github.com/winfsp/winfsp

기반 동작은 [rclone Windows mount/VFS 문서](https://rclone.org/commands/rclone_mount/), [rclone RC API](https://rclone.org/rc/), [WinFsp ARM64 지원](https://github.com/winfsp/winfsp/wiki/WinFsp-on-ARM64)을 참고했습니다.
