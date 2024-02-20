using System.Runtime.InteropServices;

using Dalamud.Utility;
using PetScale.Enums;

namespace PetScale.Structs;

[StructLayout(LayoutKind.Auto)]
public record struct PetStruct
{
    public string CharacterName { get; set; }
    public required PetModel PetID { get; set; }
    public required PetSize PetSize { get; set; }

    public PetStruct()
    {
        CharacterName = "Default";
    }

    public readonly bool IsDefault()
    {
        return CharacterName.IsNullOrWhitespace() || CharacterName.Equals("Default", System.StringComparison.Ordinal);
    }
}
