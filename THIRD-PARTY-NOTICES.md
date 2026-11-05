# Third-party notices

- **rclone 1.75.2** — Copyright (C) 2012 by Nick Craig-Wood and contributors. MIT license. The portable package includes its native executable. Full license: `tools/RCLONE-LICENSE.txt`. Source: https://github.com/rclone/rclone/tree/v1.75.2
- **WinFsp 2.1.25156** — Copyright (C) Bill Zissimopoulos and contributors. GPLv3 with the WinFsp exception; see https://github.com/winfsp/winfsp/blob/v2.1/License.txt . Installed separately from its official signed MSI; its driver is not bundled into HarborDrive.
- **.NET 10** — Copyright .NET Foundation and contributors, MIT and other component licenses. Self-contained packages include Microsoft's runtime and desktop licenses plus runtime notices in `licenses/`. https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

HarborDrive communicates with the separately running rclone executable over its authenticated local RC API. No rclone or WinFsp source code is incorporated into HarborDrive's assemblies.
