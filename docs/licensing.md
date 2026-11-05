# 공개 배포와 라이선스

HarborDrive 소스와 직접 제작한 SVG/ICO/PNG 아이콘은 루트 `LICENSE`의 MIT 라이선스로 공개합니다. 이는 포함된 타사 소프트웨어를 MIT로 재라이선스한다는 뜻이 아닙니다.

| 구성요소 | 배포에 적용되는 조건과 처리 |
| --- | --- |
| HarborDrive 소스·아이콘 | MIT. 저작권 문구와 라이선스 전문을 유지합니다. |
| rclone | MIT. 공급자 저작권과 `tools/RCLONE-LICENSE.txt`를 동봉합니다. |
| .NET 런타임 | 해당 런타임 패키지의 라이선스와 타사 고지를 `licenses/`에 동봉합니다. |
| WinFsp | GPLv3 + FLOSS 예외. 공식 원본 MSI를 수정하지 않고 동봉하고, 앱 정보·문서에 저작권과 저장소 링크를 표시합니다. 원문은 `licenses/WINFSP-LICENSE.txt`에 포함합니다. |
| Inno Setup | 공급자의 라이선스와 저작권을 유지합니다. 설치 파일에 사용하는 원본 스텁을 변경하지 않습니다. |

WinFsp의 FLOSS 예외는 별도 조건을 명시합니다. 프로그램이 FSF/OSI 정의를 만족하는 오픈소스 라이선스로 배포되어야 하고, 사용자 UI와 문서에 WinFsp 저작권·저장소 링크를 포함하며, 이 예외로 독점 소프트웨어와 혼합해서는 안 됩니다. 소스를 GitHub에 올리는 것만으로 라이선스가 자동 적용되지는 않으므로 HarborDrive에는 사용자가 선택한 MIT 라이선스를 명시했습니다.

WinFsp를 수정하거나 향후 비공개 독점 제품으로 바꾸면, 지금의 FLOSS 예외를 그대로 적용할 수 있다고 가정하지 말고 해당 조건 또는 공급자의 상용 라이선스를 다시 확인해야 합니다. 포함 바이너리는 원래 라이선스·고지 파일과 함께 배포합니다.

원문: [WinFsp 2.1 License](https://github.com/winfsp/winfsp/blob/v2.1/License.txt), [rclone MIT](https://rclone.org/licence/), [.NET runtime MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [Inno Setup License](https://github.com/jrsoftware/issrc/blob/is-7_1_1/license.txt).

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos
https://github.com/winfsp/winfsp
