using System;
using System.Linq;
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
    public bool UpdateNeeded { get; internal set; }

    public void UpdateConfig()
    {
        for (var i = 0; i < PetData.Count; i++)
        {
            if (PetData[i].CharacterName.Equals("Other players", StringComparison.Ordinal))
            {
                PetData[i] = PetData[i] with { Generic = true, ContentId = 1 };
            }
            else if (PetData[i].PetID is Enums.PetModel.AllPets)
            {
                PetData[i] = PetData[i] with { Generic = true };
            }
        }
        UpdateNeeded = PetData.Any(data => data.UpdateRequired());
    }

    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
