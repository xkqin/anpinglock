# Third-party components

AnpingLock application source uses the MIT license in the repository root.

## .NET and Windows projections

The self-contained binaries include components from .NET 8 and the Windows SDK .NET projections. Their upstream licenses and notices apply to those components. [dotnet/runtime](https://github.com/dotnet/runtime) · [dotnet/wpf](https://github.com/dotnet/wpf) · [CsWinRT](https://github.com/microsoft/CsWinRT) · [Windows SDK license](https://aka.ms/WinSDKLicenseURL). The installer includes runtime license/notice files and the Windows SDK package manifest containing its upstream license URL in `Notices/`.

## Installer

The native background agent is built with Microsoft's C++ toolchain and statically linked C/C++ runtime. It uses the Windows SDK C++/WinRT headers and Windows-provided Bluetooth, JSON and session APIs; it does not package or load the .NET runtime. The on-demand WPF interface still includes the .NET components described above.

The installer is built with Inno Setup 6.7.3 through the checksum-pinned `Tools.InnoSetup` compiler package. The installer engine is distributed under Inno Setup's upstream terms. [Inno Setup](https://jrsoftware.org/isinfo.php)

`installer/ChineseSimplified.isl` is the Simplified Chinese translation from the Inno Setup source repository. Its translator credits and source comments are retained. [Translation source](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl)

## Visuals

The Windows Bluetooth and Dynamic Lock screenshots were supplied for this project with device names redacted. Interface previews are rendered from the actual v1.1.0 WPF layout with an unselected device. The office photograph is by Mikhail Nilov, sourced from [Pexels photo 8297861](https://www.pexels.com/photo/a-computer-on-an-office-desk-8297861/) under the [Pexels License](https://www.pexels.com/license/). [Detailed image credits](docs/assets/PHOTO_CREDITS.md). Current documentation uses workplace photography and actual interface exports.
