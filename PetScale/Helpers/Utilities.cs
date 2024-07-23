using System;
using System.Linq;
using System.Collections.Generic;

using Lumina.Excel;
using Lumina.Excel.GeneratedSheets2;
using Dalamud.Game;
using Dalamud.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.Interop;
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
            worldSheet??= GetSheet<World>(language);
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

    public unsafe void SetScale(BattleChara* pet, float scale)
    {
        if (pet is null)
        {
            return;
        }
        pet->Character.GameObject.Scale = scale;
        pet->Character.CharacterData.ModelScale = scale;
        var drawObject = pet->Character.GameObject.GetDrawObject();
        if (drawObject is not null)
        {
            drawObject->Object.Scale.X = scale;
            drawObject->Object.Scale.Y = scale;
            drawObject->Object.Scale.Z = scale;
        }
        //log.Debug("Set {a} to size {b}", pet->NameString, scale);
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

    public static float GetMinSize(PetModel pet)
    {
        return pet switch
        {
            PetModel.Seraph => 1.25f,
            PetModel.AutomatonQueen => 1.3f,
            _ => 1f,
        };
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
}
