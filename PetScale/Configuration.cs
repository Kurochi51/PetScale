using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Configuration;
using PetScale.Structs;

namespace PetScale;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public IList<PetStruct> PetData { get; set; } = [];
    public bool FairyResize { get; set; } = false;


    public void Save(DalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
