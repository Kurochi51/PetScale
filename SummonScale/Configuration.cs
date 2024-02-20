using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Configuration;
using PetScale.SummonsData;

namespace PetScale;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public IList<SummonStruct> SummonData { get; set; } = [];


    public void Save(DalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
