# Third-party notices

HardwareTest uses **iText for .NET** and its Bouncy Castle adapter under the **GNU Affero General Public License v3** to create and verify embedded PAdES signatures. The linked HardwareTest and iText build is offered under the AGPL v3. HardwareTest's first-party code is licensed under [AGPL-3.0-only](LICENSE). The [previous MIT copyright and permission notice](licenses/MIT-previous.txt) remains in source and binary archives.

- iText licensing: <https://itextpdf.com/how-buy/legal/agpl-gnu-affero-general-public-license>
- GNU AGPL v3 text: <https://www.gnu.org/licenses/agpl-3.0.html>

OpenTAP retains its MPL-2.0 terms, Instrument Components its MIT or Apache-2.0 terms, and Bouncy Castle, `Pkcs11Interop.X509Store`, and OpenSC their respective upstream licenses. The release-specific source index identifies the exact HardwareTest source and the upstream source versions for iText, Bouncy Castle, OpenTAP, and Instrument Components.

`IviFoundation.Visa` includes a separate IVI Foundation license agreement. It requires recipients to agree to its terms and limits nonmembers to object code and headers necessary to use it. The current app archives contain `Ivi.Visa.dll` both as a direct dependency and inside the standalone VISA TapPackage. The draft includes the agreement from the restored package. Public distribution of a draft containing this dependency requires resolving the scope of the IVI terms and the combined build's corresponding-source obligations. See the [maintainer release instructions](docs/releases.md).
