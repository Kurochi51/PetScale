using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Configuration;
using PetScale.Structs;

namespace PetScale;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public IList<PetStruct> PetData { get; set; } = [];
    public int FairySize { get; set; } = 0;

    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
