# Upstream release notices

The release packager copies notices from every published dependency and runtime manifest. The catalog supplies additional files for packages whose NuGet archive omits the relevant text and for the IVI license stored in a subdirectory. It names exact package versions and links to the applicable upstream source. Build jobs read the checked-in files.

- PDFium notices come from the Linux x64 and Windows x64 assets of the indexed binary release. These binaries match the corresponding NuGet native files. The packaging project also declares Apache-2.0.
- The Inter font license comes from the commit recorded in the bundled font's version metadata. The Avalonia notices also apply to the Avalonia.Fonts.Inter assembly. [Inter source](https://github.com/rsms/inter/blob/0a5106e0bde18df09374066bf3a7998e3546307d/LICENSE.txt)
- OpenTAP 9.35.0 links to the source commit of its published tag and carries the engine's license. The OpenTAP runtime tree also includes its dependencies' notices.
- The Serilog Windows Event Log sink links to the Event Log repository at the commit from its NuGet metadata, which points to the Async repository by mistake.
