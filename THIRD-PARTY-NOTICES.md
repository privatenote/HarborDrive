# Third-party notices

- **rclone 1.75.2** — Copyright (C) 2012 by Nick Craig-Wood and contributors. MIT license. The portable package includes its native executable. Full license: `tools/RCLONE-LICENSE.txt`. Source: https://github.com/rclone/rclone/tree/v1.75.2
- **WinFsp 2.1.25156** — WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos and contributors. GPLv3 with the WinFsp exception; see https://github.com/winfsp/winfsp/blob/v2.1/License.txt . The integrated installer includes the official unmodified signed MSI. Its full license is installed in `licenses/WINFSP-LICENSE.txt`. Original source and build instructions: https://github.com/winfsp/winfsp/tree/v2.1 . WinFsp is a separate shared system component and is retained when HarborDrive is uninstalled.
- **.NET 10** — Copyright .NET Foundation and contributors, MIT and other component licenses. Self-contained packages include Microsoft's runtime and desktop licenses plus runtime notices in `licenses/`. https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- **Inno Setup 7.1.1** — Copyright (C) 1997-2026 Jordan Russell; portions Copyright (C) 2000-2026 Martijn Laan. Installer compiler and bootstrapper. Original notices are retained; license is included in `licenses/INNO-SETUP-LICENSE.txt`. https://jrsoftware.org/

HarborDrive communicates with the separately running rclone executable over its authenticated local RC API. No rclone or WinFsp source code is incorporated into HarborDrive's assemblies.
