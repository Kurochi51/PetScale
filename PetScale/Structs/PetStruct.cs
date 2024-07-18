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
    public ulong ContentId { get; set; }
    public bool Generic { get; set; }

    public PetStruct()
    {
        CharacterName = "Default";
    }

    public readonly bool IsDefault()
        => CharacterName.IsNullOrWhitespace() || CharacterName.Equals("Default", System.StringComparison.Ordinal);

    public readonly bool UpdateRequired()
        => ContentId is 0;
}
