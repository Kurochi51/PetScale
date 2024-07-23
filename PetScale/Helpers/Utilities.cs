using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Lumina.Excel;
using Lumina.Excel.GeneratedSheets2;
using Dalamud.Game;
using Dalamud.Utility;
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

    public unsafe void SetScale(BattleChara* pet, float scale, float? vfxScale = null)
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
        var drawObject = pet->Character.GameObject.GetDrawObject();
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
            if (chara.Value->Character.GameObject.ObjectKind is not ObjectKind.Pc || chara.Value->Character.GameObject.EntityId is 0xE0000000)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.EntityId != playerEntityId)
            {
                var world = string.Empty;
                if (homeWorld is not 0 && homeWorld != chara.Value->Character.HomeWorld && !GetHomeWorldName(chara.Value->Character.HomeWorld).IsNullOrWhitespace())
                {
                    world = "@" + GetHomeWorldName(chara.Value->Character.HomeWorld);
                }
                queue.Enqueue((chara.Value->Character.NameString + world, chara.Value->ContentId, chara.Value->HomeWorld));
            }
        }
    }

    public unsafe bool PetVisible(BattleChara* pet)
    {
        if (pet is null || pet->Character.GameObject.GetDrawObject() is null)
        {
            return false;
        }
        return pet->Character.GameObject.GetDrawObject()->IsVisible;
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
    /// is one of the SMN pets that use "/petsize", the returned value will correspond to <see cref="PetSize.SmallModelScale"/>.
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

    public unsafe void CheckPetRemoval(BattleChara* pet, Character* character, ConcurrentDictionary<ulong, PetStruct> removalQueue)
    {
        foreach (var player in removalQueue)
        {
            if (pet is null || character is null)
            {
                continue;
            }
            if (!Enum.IsDefined(typeof(PetModel), pet->ModelCharaId))
            {
                continue;
            }
            if (player.Key != character->ContentId)
            {
                continue;
            }
            if ((PetModel)pet->ModelCharaId != player.Value.PetID)
            {
                continue;
            }
            SetScale(pet, GetDefaultScale(player.Value.PetID, player.Value.PetSize));
            removalQueue.TryRemove(player);
        }
    }
}
