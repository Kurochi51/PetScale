using System;
using System.Collections.Generic;

using Dalamud;
using Lumina.Excel;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Utility;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
        if (actor is null || actor->ObjectID is 0xE0000000)
        {
            return;
        }
        *ActorDrawState(actor) ^= DrawState.Invisibility;
    }

    public unsafe void CachePlayerList(uint playerObjectId, Queue<string> queue, Span<Pointer<BattleChara>> CharacterSpan)
    {
        foreach (var chara in CharacterSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is not (byte)ObjectKind.Player || chara.Value->Character.GameObject.ObjectID is 0xE0000000)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectID != playerObjectId)
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

    public static IFontHandle CreateIconFont(DalamudPluginInterface pi)
        => pi.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new()
            {
                SizePx = pi.UiBuilder.DefaultFontSpec.SizePx,
                GlyphMinAdvanceX = pi.UiBuilder.DefaultFontSpec.SizePx,
                GlyphMaxAdvanceX = pi.UiBuilder.DefaultFontSpec.SizePx,
            }));
            e.OnPostBuild(tk =>
            {
                var font = tk.Font;
                var fontSize = font.FontSize;
                var glyphs = font.GlyphsWrapped();
                foreach (ref var glyph in glyphs.DataSpan)
                {
                    var ratio = 1f;
                    if (glyph.X1 - glyph.X0 > fontSize)
                    {
                        ratio = Math.Max(ratio, (glyph.X1 - glyph.X0) / fontSize);
                    }
                    if (glyph.Y1 - glyph.Y0 > fontSize)
                    {
                        ratio = Math.Max(ratio, (glyph.Y1 - glyph.Y0) / fontSize);
                    }
                    var width = MathF.Round((glyph.X1 - glyph.X0) / ratio, MidpointRounding.ToZero);
                    var height = MathF.Round((glyph.Y1 - glyph.Y0) / ratio, MidpointRounding.AwayFromZero);
                    glyph.X0 = MathF.Round((fontSize - width) / 2f, MidpointRounding.ToZero);
                    glyph.Y0 = MathF.Round((fontSize - height) / 2f, MidpointRounding.AwayFromZero);
                    glyph.X1 = glyph.X0 + width;
                    glyph.Y1 = glyph.Y0 + height;
                    glyph.AdvanceX = fontSize;
                }

                tk.BuildLookupTable(font);
            });
        });
}
