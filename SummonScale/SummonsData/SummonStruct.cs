using System.Runtime.InteropServices;

using Dalamud.Utility;
using PetScale.Enums;

namespace PetScale.SummonsData;

[StructLayout(LayoutKind.Auto)]
public record struct SummonStruct
{
    public string CharacterName { get; set; }
    public required SummonModel SummonID { get; set; }
    public required SummonSize SummonSize { get; set; }

    public SummonStruct()
    {
        CharacterName = "Default";
    }

    public readonly bool IsDefault()
    {
        return CharacterName.IsNullOrWhitespace() || CharacterName.Equals("Default", System.StringComparison.Ordinal);
    }
}
