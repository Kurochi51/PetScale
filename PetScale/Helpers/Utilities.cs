using System;
using System.Linq;
using System.Collections.Generic;

using Lumina.Excel;
using Lumina.Excel.GeneratedSheets2;
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

    /// <summary>
    ///     Attempt to retrieve an <see cref="ExcelSheet{T}"/>, optionally in a specific <paramref name="language"/>.
    /// </summary>
    /// <returns><see cref="ExcelSheet{T}"/> or <see langword="null"/> if <see cref="IDataManager.GetExcelSheet{T}(ClientLanguage)"/> returns an invalid sheet.</returns>
    public ExcelSheet<T>? GetSheet<T>(ClientLanguage language = ClientLanguage.English) where T : ExcelRow
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
            if (!world.Name.ToDalamudString().TextValue.Equals(name, StringComparison.Ordinal))
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
        return WorldSheet.GetRow(id)?.Name ?? string.Empty;
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
            if (currentWorld.Name.ToDalamudString().TextValue.Contains("test", StringComparison.Ordinal))
            {
                continue;
            }
            worldDictionary.Add(currentWorld.Name, currentWorld.DataCenter.Value!.Name.ToDalamudString().TextValue);
        }
    }

    /// <summary>
    /// Returns a default scale for vfx, preset size, or custom size.
    /// </summary>
    /// <remarks>
    /// When the provided <paramref name="size"/> is <see cref="PetSize.Custom"/> and the <paramref name="pet"/>
    /// is one of the SMN pets that use "/petsize", the returned value will correspond to <see cref="PetSize.SmallModelScale"/>,
    /// unless the player is logged in, which will instead return the in-game value.
    /// </remarks>
    /// <exception cref="ArgumentException"> <see cref="PetModel.AllPets"/> is not an accepted value. </exception>
    public static float GetDefaultScale(PetModel pet, PetSize size, bool vfx = false)
    {
        if (vfx)
        {
            return GetVfxDefault(pet);
        }
        if (size is not PetSize.Custom)
        {
            return GetPresetSize(pet, size);
        }
        if (PetScale.vanillaPetSizeMap.Count is 0)
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
                PetModel.SolarBahamut => GetDefaultScale(PetModel.SolarBahamut, PetSize.SmallModelScale),
                PetModel.Bahamut => GetDefaultScale(PetModel.Bahamut, PetSize.SmallModelScale),
                PetModel.Phoenix => GetDefaultScale(PetModel.Phoenix, PetSize.SmallModelScale),
                PetModel.Ifrit => GetDefaultScale(PetModel.Ifrit, PetSize.SmallModelScale),
                PetModel.Titan => GetDefaultScale(PetModel.Titan, PetSize.SmallModelScale),
                PetModel.Garuda => GetDefaultScale(PetModel.Garuda, PetSize.SmallModelScale),
                _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
            };
        }
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
            PetModel.SolarBahamut => GetDefaultScale(PetModel.SolarBahamut, PetScale.vanillaPetSizeMap[PetModel.SolarBahamut]),
            PetModel.Bahamut => GetDefaultScale(PetModel.Bahamut, PetScale.vanillaPetSizeMap[PetModel.Bahamut]),
            PetModel.Phoenix => GetDefaultScale(PetModel.Phoenix, PetScale.vanillaPetSizeMap[PetModel.Phoenix]),
            PetModel.Ifrit => GetDefaultScale(PetModel.Ifrit, PetScale.vanillaPetSizeMap[PetModel.Ifrit]),
            PetModel.Titan => GetDefaultScale(PetModel.Titan, PetScale.vanillaPetSizeMap[PetModel.Titan]),
            PetModel.Garuda => GetDefaultScale(PetModel.Garuda, PetScale.vanillaPetSizeMap[PetModel.Garuda]),
            _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
        };
    }

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
            _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
        };
    }

    private static float GetPresetSize(PetModel pet, PetSize size)
    {
        switch (size)
        {
            case PetSize.SmallModelScale:
            {
                return pet switch
                {
                    PetModel.SolarBahamut => 0.13f,
                    PetModel.Bahamut => 0.1f,
                    PetModel.Ifrit => 0.25f,
                    PetModel.Phoenix
                    or PetModel.Titan
                    or PetModel.Garuda
                    => 0.33f,
                    _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
                };
            }
            case PetSize.MediumModelScale:
            {
                return pet switch
                {
                    PetModel.SolarBahamut => 0.26f,
                    PetModel.Bahamut => 0.2f,
                    PetModel.Ifrit => 0.5f,
                    PetModel.Phoenix
                    or PetModel.Titan
                    or PetModel.Garuda
                    => 0.66f,
                    _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
                };
            }
            case PetSize.LargeModelScale:
            {
                return pet switch
                {
                    PetModel.SolarBahamut => 0.4f,
                    PetModel.Bahamut => 0.3f,
                    PetModel.Ifrit => 0.75f,
                    PetModel.Phoenix
                    or PetModel.Titan
                    or PetModel.Garuda
                    => 1f,
                    _ => throw new ArgumentException("Invalid PetModel provided.", pet.ToString())
                };
            }
            default:
                throw new ArgumentException("Invalid PetSize provided.", size.ToString());
        }
    }

    public static unsafe void CheckPetRemoval(IDictionary<ulong, PetStruct> removalQueue, IDictionary<Pointer<BattleChara>, (Pointer<Character> character, bool petSet)> activePlayers)
    {
        foreach (var removedPlayer in removalQueue)
        {
            foreach (var activePlayer in activePlayers)
            {
                var pet = activePlayer.Key.Value;
                var character = activePlayer.Value.character.Value;
                if (pet is null || character is null)
                {
                    continue;
                }
                if (!PetScale.petModelSet.Contains((PetModel)pet->ModelCharaId))
                {
                    continue;
                }
                if (removedPlayer.Key != character->ContentId && !removedPlayer.Value.Generic)
                {
                    continue;
                }
                if (removedPlayer.Value.PetID is PetModel.AllPets && PetScale.vanillaPetSizeMap.TryGetValue((PetModel)pet->ModelCharaId, out var size))
                {
                    SetScale(pet, GetDefaultScale((PetModel)pet->ModelCharaId, size));
                    removalQueue.Remove(removedPlayer);
                    continue;
                }
                if ((PetModel)pet->ModelCharaId != removedPlayer.Value.PetID)
                {
                    continue;
                }
                if (removedPlayer.Value.PetSize is PetSize.Custom)
                {
                    SetScale(pet, GetDefaultScale(removedPlayer.Value.PetID, removedPlayer.Value.PetSize));
                    removalQueue.Remove(removedPlayer);
                    continue;
                }
                SetScale(pet, GetDefaultScale(removedPlayer.Value.PetID, PetScale.vanillaPetSizeMap[(PetModel)pet->ModelCharaId]));
                removalQueue.Remove(removedPlayer);
            }
        }
    }

    public static unsafe bool ResetFairy(BattleChara* fairy, float size)
    {
        var fairyModel = (PetModel)fairy->ModelCharaId;
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

    public static unsafe void ResetPets(IDictionary<Pointer<BattleChara>, (Pointer<Character> character, bool petSet)> activePets, IList<PetStruct> userData)
    {
        foreach (var pair in activePets)
        {
            var pet = pair.Key.Value;
            var character = pair.Value.character.Value;
            if (pet is null || character is null)
            {
                continue;
            }
            var petModel = (PetModel)pet->ModelCharaId;
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
                if (data.PetID is PetModel.AllPets && PetScale.vanillaPetSizeMap.TryGetValue(petModel, out var size))
                {
                    SetScale(pet, GetDefaultScale(petModel, size));
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
                SetScale(pet, GetDefaultScale(data.PetID, PetScale.vanillaPetSizeMap[petModel]));
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
            if (tempEnumerable.Count() is not 0)
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
        if (tempEnumerable.Count() is not 0)
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
