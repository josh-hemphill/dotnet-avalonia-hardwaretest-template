using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

[Display("Hardware DUT", Groups: ["HardwareTest"], Description: "Unit under test identity for the bench session.")]
public sealed class HardwareDut : Dut
{
    [Display("Serial Number", Order: 1)]
    public string SerialNumber { get; set; } = string.Empty;

    [Display("Part Number", Order: 2)]
    public string PartNumber { get; set; } = string.Empty;

    [Display("Revision", Order: 3)]
    public string Revision { get; set; } = string.Empty;

    [Display("Family", Order: 4, Description: "Used for program-family session re-confirm.")]
    public string Family { get; set; } = "generic";
}
