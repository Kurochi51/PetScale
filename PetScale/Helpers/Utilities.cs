using System;
using System.Collections.Generic;

using Dalamud.Game;
using Lumina.Excel;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.Interop;
using PetScale.Enums;

namespace PetScale.Helpers;

public class Utilities(IDataManager _dataManager, IPluginLog _pluginLog)
{
    private readonly IDataManager dataManager = _dataManager;
    private readonly IPluginLog log = _pluginLog;

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
    }

    private static unsafe DrawState* ActorDrawState(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* actor)
        => (DrawState*)&actor->RenderFlags;

    public static unsafe void ToggleVisibility(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* actor)
    {
        if (actor is null || actor->EntityId is 0xE0000000)
        {
            return;
        }
        *ActorDrawState(actor) ^= DrawState.Invisibility;
    }

    public unsafe void CachePlayerList(uint playerEntityId, Queue<string> queue, Span<Pointer<BattleChara>> CharacterSpan)
    {
        foreach (var chara in CharacterSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is not ObjectKind.Pc || chara.Value->Character.GameObject.EntityId is 0xE0000000)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.EntityId != playerEntityId)
            {
                queue.Enqueue(MemoryHelper.ReadStringNullTerminated((nint)chara.Value->Character.GameObject.GetName()));
            }
        }
    }

    public static bool IsFairy(int modelId)
        => (PetModel)modelId is PetModel.Eos or PetModel.Selene;

    public unsafe bool PetVisible(BattleChara* pet)
    {
        if (pet is null || pet->Character.GameObject.GetDrawObject() is null)
        {
            return false;
        }
        return pet->Character.GameObject.GetDrawObject()->IsVisible;
    }
}
