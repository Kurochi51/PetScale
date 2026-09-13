using System;
using System.Linq;
using System.Collections.Generic;

using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Dalamud.Game;
using Dalamud.Utility;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.Interop;
using PetScale.Structs;
using PetScale.Enums;

namespace PetScale.Helpers;

public class Utilities(IDataManager _dataManager, IPluginLog _pluginLog, ClientLanguage _language)
{
    private readonly IDataManager dataManager = _dataManager;
    private readonly IPluginLog log = _pluginLog;
    private readonly ClientLanguage language = _language;
    private ExcelSheet<World>? worldSheet = null;
    private ExcelSheet<World>? WorldSheet
    {
        get
        {
            worldSheet ??= GetSheet<World>(language);
            return worldSheet;
        }
    }
    internal static List<PetModel> SortedModels { get; } =
        [
            PetModel.Eos,
            PetModel.Selene,
            PetModel.Seraph,
            PetModel.Rook,
            PetModel.AutomatonQueen,
            PetModel.Esteem,
            PetModel.Carbuncle,
            PetModel.RubyCarbuncle,
            PetModel.TopazCarbuncle,
            PetModel.EmeraldCarbuncle,
            PetModel.IfritEgi,
            PetModel.TitanEgi,
            PetModel.GarudaEgi,
        ];

    internal static Dictionary<PetRow, PetModel> summonerPetModelMap { get; } = new()
    {
        { PetRow.Bahamut,       PetModel.Bahamut        },
        { PetRow.Phoenix,       PetModel.Phoenix        },
        { PetRow.Ifrit,         PetModel.Ifrit          },
        { PetRow.Titan,         PetModel.Titan          },
        { PetRow.Garuda,        PetModel.Garuda         },
        { PetRow.SolarBahamut,  PetModel.SolarBahamut   },
    };

    // This one can actually be built at runtime since SE is gracious enough to link back to a PetMirage entry with a ModelChara reference
    // TODO: Revisit once excel sheets are updated in Evercold.
    internal static Dictionary<PetRow, PetModel> beastmasterPetModelMap { get; } = new()
    {
        { PetRow.CuSith,          PetModel.CuSith               },
        { PetRow.Squirrel,        PetModel.Squirrel             },
        { PetRow.Lamb,            PetModel.Lamb                 },
        { PetRow.Pugil,           PetModel.Pugil                },
        { PetRow.Opo_Opo,         PetModel.Opo_Opo              },
        { PetRow.Dodo,            PetModel.Dodo                 },
        { PetRow.Coblyn,          PetModel.Coblyn               },
        { PetRow.Diremite,        PetModel.Diremite             },
        { PetRow.Megalocrab,      PetModel.Megalocrab           },
        { PetRow.Wespe,           PetModel.Wespe                },
        { PetRow.Vulture,         PetModel.Vulture              },
        { PetRow.Mandragora,      PetModel.Mandragora           },
        { PetRow.Geshunpest,      PetModel.Geshunpest           },
        { PetRow.Puk,             PetModel.Puk                  },
        { PetRow.Crab,            PetModel.Crab                 },
        { PetRow.Mantis,          PetModel.Mantis               },
        { PetRow.Slime,           PetModel.Slime                },
        { PetRow.Dullahan,        PetModel.Dullahan             },
        { PetRow.Bat,             PetModel.Bat                  },
        { PetRow.Flying_Trap,     PetModel.Flying_Trap          },
        { PetRow.Ziz,             PetModel.Ziz                  },
        { PetRow.Sabotender,      PetModel.Sabotender           },
        { PetRow.Golem,           PetModel.Golem                },
        { PetRow.Apkallu,         PetModel.Apkallu              },
        { PetRow.Adamantoise,     PetModel.Adamantoise          },
        { PetRow.Buffalo,         PetModel.Buffalo              },
        { PetRow.Uragnite,        PetModel.Uragnite             },
        { PetRow.Worm,            PetModel.Worm                 },
        { PetRow.Spriggan,        PetModel.Spriggan             },
        { PetRow.Goobbue,         PetModel.Goobbue              },
        { PetRow.Gigantoad,       PetModel.Gigantoad            },
        { PetRow.Colibri,         PetModel.Colibri              },
        { PetRow.Coeurl,          PetModel.Coeurl               },
        { PetRow.Raptor,          PetModel.Raptor               },
        { PetRow.Drake,           PetModel.Drake                },
        { PetRow.Treant,          PetModel.Treant               },
        { PetRow.Antling,         PetModel.Antling              },
        { PetRow.Chimera,         PetModel.Chimera              },
        { PetRow.Morbol,          PetModel.Morbol               },
        { PetRow.Ghost,           PetModel.Ghost                },
        { PetRow.Salamander,      PetModel.Salamander           },
        { PetRow.Cobra,           PetModel.Cobra                },
        { PetRow.Hydra,           PetModel.Hydra                },
        { PetRow.Damselfly,       PetModel.Damselfly            },
        { PetRow.Rotting_Goobbue, PetModel.Rotting_Goobbue      },
        { PetRow.Zu,              PetModel.Zu                   },
        { PetRow.Ice_Golem,       PetModel.Ice_Golem            },
        { PetRow.Karlabos,        PetModel.Karlabos             },
        { PetRow.Rafflesia,       PetModel.Rafflesia            },
        { PetRow.Behemoth,        PetModel.Behemoth             },
    };

    internal static Dictionary<PetRow, PetModel> customPetModelMap { get; } = new()
    {
        { PetRow.Eos,               PetModel.Eos                },
        { PetRow.Selene,            PetModel.Selene             },
        { PetRow.Seraph,            PetModel.Seraph             },
        { PetRow.Rook,              PetModel.Rook               },
        { PetRow.AutomatonQueen,    PetModel.AutomatonQueen     },
        { PetRow.Esteem,            PetModel.Esteem             },

        { PetRow.Carbuncle,         PetModel.Carbuncle          },
        { PetRow.RubyCarbuncle,     PetModel.RubyCarbuncle      },
        { PetRow.TopazCarbuncle,    PetModel.TopazCarbuncle     },
        { PetRow.EmeraldCarbuncle,  PetModel.EmeraldCarbuncle   },
        { PetRow.IfritEgi,          PetModel.IfritEgi           },
        { PetRow.TitanEgi,          PetModel.TitanEgi           },
        { PetRow.GarudaEgi,         PetModel.GarudaEgi          },
    };

    /// <summary>
    ///     Attempt to retrieve an <see cref="ExcelSheet{T}"/>, optionally in a specific <paramref name="language"/>.
    /// </summary>
    /// <returns><see cref="ExcelSheet{T}"/> or <see langword="null"/> if <see cref="IDataManager.GetExcelSheet{T}(ClientLanguage)"/> returns an invalid sheet.</returns>
    public ExcelSheet<T>? GetSheet<T>(ClientLanguage language = ClientLanguage.English) where T : struct, IExcelRow<T>
    {
        try
        {
            var sheet = dataManager.GetExcelSheet<T>(language);
            if (sheet is null)
            {
                log.Fatal("Invalid lumina sheet!");
            }
            return sheet;
        }
        catch (Exception e)
        {
            log.Fatal("Retrieving lumina sheet failed!");
            log.Fatal(e.Message);
            return null;
        }
    }

    public static unsafe void SetScale(BattleChara* pet, float scale, float? vfxScale = null)
    {
        if (pet is null)
        {
            return;
        }
        if (vfxScale.HasValue)
        {
            pet->VfxScale = vfxScale.Value;
        }
        pet->Scale = scale;
        pet->ModelScale = scale;
        var drawObject = pet->GetDrawObject();
        if (drawObject is not null)
        {
            drawObject->Scale.X = scale;
            drawObject->Scale.Y = scale;
            drawObject->Scale.Z = scale;
        }
    }

    private static unsafe DrawState* ActorDrawState(IGameObject actor)
        => (DrawState*)&((GameObject*)actor.Address)->RenderFlags;

    public static unsafe void ToggleVisibility(IGameObject actor)
    {
        if (actor is null || actor.EntityId is 0xE0000000)
        {
            return;
        }
        *ActorDrawState(actor) ^= DrawState.Invisibility;
    }

    public unsafe void CachePlayerList(uint playerEntityId, ushort homeWorld, Queue<(string Name, ulong ContentId, ushort HomeWorld)> queue, Span<Pointer<BattleChara>> CharacterSpan)
    {
        foreach (var chara in CharacterSpan)
        {
            if (chara.Value is null)
            {
                continue;
            }
            if (chara.Value->ObjectKind is not ObjectKind.Pc || chara.Value->EntityId is 0xE0000000)
            {
                continue;
            }
            if (chara.Value->EntityId != playerEntityId)
            {
                var world = string.Empty;
                if (homeWorld is not 0 && homeWorld != chara.Value->HomeWorld && !GetHomeWorldName(chara.Value->HomeWorld).IsNullOrWhitespace())
                {
                    world = "@" + GetHomeWorldName(chara.Value->HomeWorld);
                }
                queue.Enqueue((chara.Value->NameString + world, chara.Value->ContentId, chara.Value->HomeWorld));
            }
        }
    }

    public static unsafe bool PetVisible(BattleChara* pet)
    {
        if (pet is null || pet->GetDrawObject() is null)
        {
            return false;
        }
        return pet->GetDrawObject()->IsVisible;
    }

    public ushort GetHomeWorldId(string name)
    {
        ushort id = 0;
        if (WorldSheet is null)
        {
            return id;
        }
        foreach (var world in WorldSheet.Where(item => item.IsPublic))
        {
            if (!world.Name.GetText().Equals(name, StringComparison.Ordinal))
            {
                continue;
            }
            id = (ushort)world.RowId;
            break;
        }
        return id;
    }

    public string GetHomeWorldName(ushort id)
    {
        if (WorldSheet is null)
        {
            return string.Empty;
        }
        return WorldSheet.GetRow(id).Name.GetText() ?? string.Empty;
    }

    public void InitWorldMap(IDictionary<string, string> worldDictionary)
    {
        if (WorldSheet is null)
        {
            return;
        }
        foreach (var currentWorld in WorldSheet)
        {
            if (!currentWorld.IsPublic)
            {
                continue;
            }
            if (currentWorld.Name.GetText().Contains("test", StringComparison.Ordinal))
            {
                continue;
            }
            worldDictionary.Add(currentWorld.Name.GetText(), currentWorld.DataCenter.Value!.Name.ToDalamudString().TextValue);
        }
    }

    /// <summary>
    /// Returns a default scale for vfx, preset size, or custom size.
    /// </summary>
    /// <remarks>
    /// When a <paramref name="size"/> is provided, it will currently dictate which size the BST pets default to,
    /// otherwise SMN pets will try to use the in-game value then fallback to the small size. Custom pets have their
    /// own default sizes hardcoded.
    /// <br>BST pets will default to the small size for now.</br>
    /// </remarks>
    /// <exception cref="ArgumentException"> <see cref="PetModel.AllPets"/> is not an accepted value. </exception>
    public static float GetDefaultScale(PetModel pet, PetSize size = PetSize.SmallModelScale, bool vfx = false)
    {
        if (vfx)
        {
            return GetVfxDefault(pet);
        }
        // This would only apply to fairies, mch, drk, and early smn pets
        if (size is PetSize.Custom || !PetScale.petSizeMap.TryGetValue(pet, out var petSize))
        {
            return pet switch
            {
                PetModel.Eos
                or PetModel.Selene
                or PetModel.Carbuncle
                or PetModel.Esteem
                or PetModel.RubyCarbuncle
                or PetModel.TopazCarbuncle
                or PetModel.EmeraldCarbuncle
                or PetModel.Rook
                => 1f,
                PetModel.GarudaEgi
                or PetModel.IfritEgi
                => 0.4f,
                PetModel.TitanEgi => 0.35f,
                PetModel.Seraph => 1.25f,
                PetModel.AutomatonQueen => 1.3f,
                _ => throw new ArgumentException("Unsupported custom PetModel.", pet.ToString()),
            };
        }
        if (beastmasterPetModelMap.ContainsValue(pet))
        {
            return size switch
            {
                PetSize.SmallModelScale => petSize.smallScale,
                PetSize.MediumModelScale => petSize.mediumScale,
                PetSize.LargeModelScale => petSize.largeScale,
                _ => throw new ArgumentException("Unsupported PetModel size for BST.", pet.ToString()),
            };
        }
        if (PetScale.vanillaPetSizeMap.TryGetValue(pet, out var vanillaPetSize))
        {
            return pet switch
            {
                PetModel.SolarBahamut => GetPresetSize(pet, vanillaPetSize),
                PetModel.Bahamut => GetPresetSize(pet, vanillaPetSize),
                PetModel.Phoenix => GetPresetSize(pet, vanillaPetSize),
                PetModel.Ifrit => GetPresetSize(pet, vanillaPetSize),
                PetModel.Titan => GetPresetSize(pet, vanillaPetSize),
                PetModel.Garuda => GetPresetSize(pet, vanillaPetSize),
                _ => throw new ArgumentException("Unsupported PetModel.", pet.ToString()),
            };
        }
        return pet switch
        {
            PetModel.SolarBahamut => petSize.smallScale,
            PetModel.Bahamut => petSize.smallScale,
            PetModel.Phoenix => petSize.smallScale,
            PetModel.Ifrit => petSize.smallScale,
            PetModel.Titan => petSize.smallScale,
            PetModel.Garuda => petSize.smallScale,
            _ => throw new ArgumentException("Unsupported PetModel.", pet.ToString()),
        };
    }

    // I don't remember where I got these, or why.
    private static float GetVfxDefault(PetModel pet)
    {
        return pet switch
        {
            PetModel.Eos
            or PetModel.Selene
            or PetModel.Seraph
            or PetModel.AutomatonQueen
            or PetModel.Esteem
            or PetModel.IfritEgi
            or PetModel.TitanEgi
            or PetModel.GarudaEgi
            or PetModel.SolarBahamut
            or PetModel.Phoenix
            or PetModel.Garuda
            => 1f,
            PetModel.Carbuncle
            or PetModel.RubyCarbuncle
            or PetModel.TopazCarbuncle
            or PetModel.EmeraldCarbuncle
            => 0.4f,
            PetModel.Rook => 0.6f,
            PetModel.Ifrit => 4f,
            PetModel.Titan => 4f,
            PetModel.Bahamut => 8f,
            _ => throw new ArgumentException("Unsupported VFX for PetModel provided.", pet.ToString()),
        };
    }

    private static float GetPresetSize(PetModel pet, PetSize size)
    {
        foreach (var petSize in PetScale.petSizeMap)
        {
            if (petSize.Key != pet)
            {
                continue;
            }
            return size switch
            {
                PetSize.SmallModelScale => petSize.Value.smallScale,
                PetSize.MediumModelScale => petSize.Value.mediumScale,
                PetSize.LargeModelScale => petSize.Value.largeScale,
                _ => throw new ArgumentException("Unsupported PetModel size for BST.", pet.ToString()),
            };
        }
        throw new ArgumentException("Invalid PetModel provided.", pet.ToString());
    }

    public unsafe void CheckPetRemoval(IDictionary<ulong, PetStruct> removalQueue, IDictionary<int, (uint characterEiD, uint petEiD)> activePlayers)
    {
        foreach (var removedPlayer in removalQueue)
        {
            foreach (var activePlayer in activePlayers.Values)
            {
                var pet = CharacterManager.Instance()->LookupBattleCharaByEntityId(activePlayer.petEiD);
                var character = CharacterManager.Instance()->LookupBattleCharaByEntityId(activePlayer.characterEiD);
                if (pet is null || character is null)
                {
                    continue;
                }
                if (!PetScale.petModelSet.Contains((PetModel)pet->ModelContainer.ModelCharaId))
                {
                    continue;
                }
                if (removedPlayer.Key != character->ContentId && !removedPlayer.Value.Generic)
                {
                    continue;
                }
                if (removedPlayer.Value.PetID is (PetModel.AllPets or PetModel.AllBeasts))
                {
                    SetScale(pet, GetDefaultScale((PetModel)pet->ModelContainer.ModelCharaId));
                    removalQueue.Remove(removedPlayer);
                    continue;
                }
                if ((PetModel)pet->ModelContainer.ModelCharaId != removedPlayer.Value.PetID)
                {
                    continue;
                }
                if (removedPlayer.Value.PetSize is PetSize.Custom)
                {
                    SetScale(pet, GetDefaultScale(removedPlayer.Value.PetID, removedPlayer.Value.PetSize));
                    removalQueue.Remove(removedPlayer);
                    continue;
                }
                SetScale(pet, GetDefaultScale(removedPlayer.Value.PetID));
                removalQueue.Remove(removedPlayer);
            }
        }
    }

    public static unsafe bool ResetFairy(BattleChara* fairy, float size)
    {
        var fairyModel = (PetModel)fairy->ModelContainer.ModelCharaId;
        if (!PetScale.petModelSet.Contains(fairyModel) || fairyModel is not PetModel.Eos and not PetModel.Selene)
        {
            return false;
        }
        if (fairy->Scale != size)
        {
            return false;
        }
        var scale = GetDefaultScale(fairyModel, PetSize.Custom);
        fairy->Scale = scale;
        fairy->ModelScale = scale;
        var drawObject = fairy->GetDrawObject();
        if (drawObject is not null)
        {
            drawObject->Scale.X = scale;
            drawObject->Scale.Y = scale;
            drawObject->Scale.Z = scale;
        }
        return true;
    }

    public unsafe void ResetPets(IDictionary<int, (uint characterEiD, uint petEiD, bool petSet)> activePets, IList<PetStruct> userData)
    {
        foreach (var pair in activePets.Values)
        {
            var pet = CharacterManager.Instance()->LookupBattleCharaByEntityId(pair.petEiD);
            var character = CharacterManager.Instance()->LookupBattleCharaByEntityId(pair.characterEiD);
            if (pet is null || character is null)
            {
                continue;
            }
            var petModel = (PetModel)pet->ModelContainer.ModelCharaId;
            if (!PetScale.petModelSet.Contains(petModel))
            {
                continue;
            }
            foreach (var data in userData)
            {
                if (data.ContentId != character->ContentId && !data.Generic)
                {
                    continue;
                }
                if (data.PetID is (PetModel.AllPets or PetModel.AllBeasts))
                {
                    SetScale(pet, GetDefaultScale(petModel));
                    continue;
                }
                if (petModel != data.PetID)
                {
                    continue;
                }
                if (data.PetSize is PetSize.Custom)
                {
                    SetScale(pet, GetDefaultScale(data.PetID, data.PetSize));
                    continue;
                }
                SetScale(pet, GetDefaultScale(data.PetID));
            }
        }
    }

    public static PetSize GetVanillaPetSize(uint pet)
    {
        return pet switch
        {
            1 => PetSize.MediumModelScale,
            3 => PetSize.LargeModelScale,
            _ => PetSize.SmallModelScale,
        };
    }

    public static void GetPetSizes(IGameConfig gConfig, IDictionary<PetModel, PetSize> sizeMap)
    {
        sizeMap.Clear();
        gConfig.TryGet(UiConfigOption.SolBahamutSize, out uint solBahamutSize);
        gConfig.TryGet(UiConfigOption.PhoenixSize, out uint phoenixSize);
        gConfig.TryGet(UiConfigOption.BahamutSize, out uint bahamutSize);
        gConfig.TryGet(UiConfigOption.IfritSize, out uint ifritSize);
        gConfig.TryGet(UiConfigOption.TitanSize, out uint titanSize);
        gConfig.TryGet(UiConfigOption.GarudaSize, out uint garudaSize);
        //XBMNoteModule.Instance()->GetPetSize(petId)
        //That returns the 0 - small, 1 - medium, 2 - large size of a BST pet, petId seems to correspond to the rowId of said pet in XBMPet sheet

        sizeMap.TryAdd(PetModel.SolarBahamut, GetVanillaPetSize(solBahamutSize));
        sizeMap.TryAdd(PetModel.Phoenix, GetVanillaPetSize(phoenixSize));
        sizeMap.TryAdd(PetModel.Bahamut, GetVanillaPetSize(bahamutSize));
        sizeMap.TryAdd(PetModel.Ifrit, GetVanillaPetSize(ifritSize));
        sizeMap.TryAdd(PetModel.Titan, GetVanillaPetSize(titanSize));
        sizeMap.TryAdd(PetModel.Garuda, GetVanillaPetSize(garudaSize));
    }

    internal static void SortList(ref List<PetStruct> petList, bool customSize)
    {
        if (petList.Count is 0)
        {
            return;
        }
        var tempEnumerable = petList.Where(item => item.CharacterName.Equals(PetScale.Others, StringComparison.Ordinal));
        if (customSize)
        {
            if (tempEnumerable.Any())
            {
                var tempList = tempEnumerable.ToList();
                tempList.AddRange([.. petList
                    .Except(tempList)
                    .OrderBy(item => SortedModels.FindIndex(sItem => sItem == item.PetID))
                    .ThenBy(item => item.CharacterName, StringComparer.Ordinal),
                ]);
                if (tempList.Count == petList.Count && petList.ToHashSet().SetEquals(tempList))
                {
                    petList = tempList;
                }
            }
            else
            {
                var orderedList = petList
                    .OrderBy(item => SortedModels.FindIndex(sItem => sItem == item.PetID))
                    .ThenBy(item => item.CharacterName, StringComparer.Ordinal).ToList();
                petList = orderedList;
            }
            return;
        }
        if (tempEnumerable.Any())
        {
            var tempList = tempEnumerable.ToList();
            tempList.AddRange([.. petList.Except(tempList).OrderBy(item => item.CharacterName, StringComparer.Ordinal).ThenBy(item => item.PetID.ToString(), StringComparer.Ordinal)]);
            if (tempList.Count == petList.Count && petList.ToHashSet().SetEquals(tempList))
            {
                petList = tempList;
            }
        }
        else
        {
            var orderedList = petList.OrderBy(item => item.CharacterName, StringComparer.Ordinal).ThenBy(item => item.PetID.ToString(), StringComparer.Ordinal).ToList();
            petList = orderedList;
        }
    }
}

public static class ReadOnlySeStringExtension
{
    public static string GetText(this ReadOnlySeString sestring)
        => sestring.ExtractText();
}
