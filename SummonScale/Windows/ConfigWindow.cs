using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using ImGuiNET;
using Dalamud.Plugin;
using Dalamud.Utility;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using PetScale.SummonsData;
using PetScale.Enums;

namespace PetScale.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly DalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly SummonScale plugin;
    private readonly IPluginLog log;
    private readonly Dictionary<SummonSize, string> sizeMap = new()
    {
        { SummonSize.SmallModelScale,   "Small"     },
        { SummonSize.MediumModelScale,  "Medium"    },
        { SummonSize.LargeModelScale,   "Large"     },
    };
    private readonly string buttonIcon;
    private readonly CancellationTokenSource cts;
    private readonly CancellationToken cToken;
    private const string DefaultSummonSelection = "Summon";
    private const string LongestCharaName = "WWWWWWWWWWWWWWW WWWWW";
    private const string LongestSummonName = "Emerald Garuda";
    private const string LongestSize = "Medium";

    public Dictionary<string, SummonModel> summonMap { get; } = new(StringComparer.Ordinal);
    private Queue<string> players => plugin.players;
    private IList<SummonStruct> summonData => config.SummonData;

    private string summonSelection = DefaultSummonSelection, sizeSelection = "Size", charaName = "Characters";
    private float tableButtonAlingmentOffset, charaWidth, summonsWidth, sizesWidth;
    private bool fontChange;

    public unsafe ConfigWindow(SummonScale _plugin,
        Configuration _config,
        DalamudPluginInterface _pluginInterface,
        IPluginLog _pluginLog) : base($"{nameof(SummonScale)} Config")
    {
        plugin = _plugin;
        config = _config;
        pluginInterface = _pluginInterface;
        log = _pluginLog;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(470, 365),
            MaximumSize = new Vector2(Device.Instance()->SwapChain->Width, Device.Instance()->SwapChain->Height),
        };

        cts = new CancellationTokenSource();
        cToken = cts.Token;

        buttonIcon = FontAwesomeIcon.Trash.ToIconString();
        pluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged += QueueColumnWidthChange;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RequestCache(bool newCache)
        => plugin.requestedCache = newCache;

    public override void OnOpen()
    {
        RequestCache(newCache: true);
    }

    public override void OnClose()
    {
        Save();
    }

    public override void Draw()
    {
        ResizeIfNeeded();
        ImGui.TextUnformatted("Amount of players: " + (players.Count > 2 ? players.Count - 1 : players.Count).ToString(CultureInfo.InvariantCulture));
        var buttonPressed = false;
        DrawComboBox("Characters", charaName, charaWidth, out charaName, players, filter: true);
        ImGui.SameLine();
        DrawComboBox("Summons", summonSelection, summonsWidth, out summonSelection, summonMap.Keys, filter: false);
        ImGui.SameLine();
        DrawComboBox("Sizes", sizeSelection, sizesWidth, out sizeSelection, sizeMap.Values, filter: false);
        ImGui.SameLine();
        if (DrawIconButton(fontHandle: null, FontAwesomeIcon.Plus.ToIconString(), "AddButton", 1))
        {
            buttonPressed = true;
        }
        if (buttonPressed
        && !charaName.IsNullOrWhitespace() && !charaName.Equals("Characters", StringComparison.Ordinal)
        && !summonSelection.Equals(DefaultSummonSelection, StringComparison.Ordinal)
        && !sizeSelection.Equals(nameof(SummonSize), StringComparison.Ordinal))
        {
            CheckPossibleEntry();
        }
        DisplayEntries();
        DrawBottomButtons();
#if DEBUG
        DevWindow.Print("Summon entries: " + summonData.Count.ToString());
#endif
    }

    private unsafe void DisplayEntries()
    {
        var currentSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (36f * ImGuiHelpers.GlobalScale));
        using var tableBorderColor = ImRaii.PushColor(ImGuiCol.TableBorderStrong, ColorHelpers.RgbaVector4ToUint(*ImGui.GetStyleColorVec4(ImGuiCol.Border)));
        using var table = ImRaii.Table("UserEntries", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.PreciseWidths | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter, currentSize);
        if (!table || summonData.Count <= 0)
        {
            return;
        }

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, charaWidth);
        ImGui.TableSetupColumn("Summon", ImGuiTableColumnFlags.WidthFixed, summonsWidth);
        ImGui.TableSetupColumn("SummonSize", ImGuiTableColumnFlags.WidthFixed, sizesWidth);
        ImGui.TableSetupColumn("DeleteButton", ImGuiTableColumnFlags.WidthFixed, GetIconButtonSize(fontHandle: null, buttonIcon).X);
        var itemRemoved = false;
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        var clipperHeight = GetIconButtonSize(fontHandle: null, buttonIcon).Y + (ImGui.GetStyle().FramePadding.Y * 2);
        clipper.Begin(summonData.Count, clipperHeight);

        var clipperBreak = false;
        while (clipper.Step())
        {
            if (clipperBreak)
            {
                break;
            }
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= summonData.Count)
                {
                    clipperBreak = true;
                    break;
                }
                ImGui.TableNextRow();
                var buttonId = "##" + i.ToString(CultureInfo.InvariantCulture);
                var item = summonData[i];

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(" " + item.CharacterName);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(item.SummonID.ToString());
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(sizeMap[item.SummonSize]);
                ImGui.TableSetColumnIndex(3);
                ImGui.SetCursorPosX(tableButtonAlingmentOffset);
                if (DrawIconButton(fontHandle: null, buttonIcon, buttonId + buttonIcon))
                {
                    summonData.RemoveAt(i);
                    pluginInterface.UiBuilder.AddNotification("Entry " + item.CharacterName + ", " + summonSelection + ", " + sizeMap[item.SummonSize] + " was removed.");
                    itemRemoved = true;
                }
            }
        }
        clipper.End();
        clipper.Destroy();
        if (itemRemoved)
        {
            Save();
        }
    }

    private static unsafe void DrawComboBox<T>(string label, string current, float width, out string result, IReadOnlyCollection<T> list, bool filter) where T : notnull
    {
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo("##Combo" + label, current);
        result = current;
        if (!combo)
        {
            return;
        }

        var tempList = list.Select(item => item.ToString()!).ToList();
        var comboWidth = ImGui.GetWindowWidth() - (2 * ImGui.GetStyle().FramePadding.X);
        string? temp = null;
        if (filter)
        {
            temp = string.Empty;
            ImGui.SetNextItemWidth(comboWidth);
            ImGui.InputTextWithHint("##Filter" + label, "Filter..", ref temp, 30);
            if (tempList.Count > 0)
            {
                tempList.Sort(2, tempList.Count - 2, StringComparer.InvariantCulture);
            }
        }
        var itemCount = tempList.Count;
        var height = (ImGui.GetTextLineHeightWithSpacing() * Math.Min(itemCount + 1.5f, 8)) - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().FramePadding.Y;
        using var listChild = ImRaii.Child("###child" + label, new Vector2(comboWidth, height));
        DrawClippedList(itemCount, temp, current, tempList, out result);
    }

    private static unsafe void DrawClippedList(int itemCount, string? filter, string preview, IReadOnlyList<string> list, out string result)
    {
        result = preview;
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        clipper.Begin(itemCount, ImGui.GetTextLineHeightWithSpacing());

        var clipperBreak = false;
        while (clipper.Step())
        {
            if (clipperBreak)
            {
                break;
            }
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= itemCount)
                {
                    clipperBreak = true;
                    break;
                }
                var item = list[i];
                if (item.IsNullOrWhitespace() || (!filter.IsNullOrEmpty() && !item.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if (!ImGui.Selectable(item, preview.Equals(item, StringComparison.Ordinal)))
                {
                    continue;
                }
                result = item;
                ImGui.CloseCurrentPopup();
            }
        }
        clipper.End();
        clipper.Destroy();
    }

    private static Vector2 GetIconButtonSize(IFontHandle? fontHandle, string icon)
    {
        using var currentFont = fontHandle == null ? ImRaii.PushFont(UiBuilder.IconFont) : fontHandle.Push();
        var iconSize = ImGui.CalcTextSize(icon);
        var iconscaling = (iconSize.X < iconSize.Y ? (iconSize.Y - iconSize.X) / 2f : 0f, iconSize.X > iconSize.Y ? 1f / (iconSize.X / iconSize.Y) : 1f);
        var normalized = iconscaling.Item2 == 1f ?
            new Vector2(iconSize.Y, iconSize.Y)
            : new((iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2), (iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2));
        var padding = ImGui.GetStyle().FramePadding;
        return normalized with { X = normalized.X + (padding.X * 2), Y = normalized.Y + (padding.Y * 2) };
    }

    // Shamelessly ripped off mare
    private static bool DrawIconButton(IFontHandle? fontHandle, string icon, string buttonIDLabel, float widthOffset = 0f)
    {
        var clicked = false;
        using var currentFont = fontHandle == null ? ImRaii.PushFont(UiBuilder.IconFont) : fontHandle.Push();
        var iconSize = ImGui.CalcTextSize(icon);
        var iconscaling = (iconSize.X < iconSize.Y ? (iconSize.Y - iconSize.X) / 2f : 0f, iconSize.X > iconSize.Y ? 1f / (iconSize.X / iconSize.Y) : 1f);
        var normalized = iconscaling.Item2 == 1f ?
            new Vector2(iconSize.Y, iconSize.Y)
            : new((iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2), (iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2));
        var padding = ImGui.GetStyle().FramePadding;
        var cursor = ImGui.GetCursorPos();
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var scrollPosY = ImGui.GetScrollY();
        var scrollPosX = ImGui.GetScrollX();
        var buttonSize = normalized with { X = normalized.X + (padding.X * 2), Y = normalized.Y + (padding.Y * 2) };

        if (ImGui.Button("##" + buttonIDLabel, buttonSize))
        {
            clicked = true;
        }

        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * iconscaling.Item2,
            new(pos.X - scrollPosX + cursor.X + iconscaling.Item1 + padding.X + widthOffset,
                pos.Y - scrollPosY + cursor.Y + ((buttonSize.Y - (iconSize.Y * iconscaling.Item2)) / 2f)),
            ImGui.GetColorU32(ImGuiCol.Text), icon);

        return clicked;
    }

    private void CheckPossibleEntry()
    {
        var currentSummonSize = sizeMap.SingleOrDefault(x => x.Value.Equals(sizeSelection, StringComparison.OrdinalIgnoreCase));
        if (currentSummonSize.Value is null)
        {
            throw new NotSupportedException();
        }
        var currentSummonData = new SummonStruct()
        {
            CharacterName = charaName,
            SummonID = summonMap[summonSelection],
            SummonSize = currentSummonSize.Key,
        };
        var checkSummon = summonData
            .SingleOrDefault(data => data.CharacterName.Equals(currentSummonData.CharacterName, StringComparison.Ordinal)
            && data.SummonID == currentSummonData.SummonID);
        if (currentSummonData.Equals(checkSummon))
        {
            return;
        }
        if (checkSummon.IsDefault())
        {
            summonData.Add(currentSummonData);
            pluginInterface.UiBuilder.AddNotification("Entry " + currentSummonData.CharacterName + ", " + summonSelection + ", " + sizeMap[currentSummonSize.Key] + " was added.");
        }
        else if (!checkSummon.SummonSize.Equals(currentSummonData.SummonSize))
        {
            var index = summonData.IndexOf(checkSummon);
            var entry = "Entry " + summonData[index].CharacterName + " with " + summonData[index].SummonID.ToString() + " changed size from " + sizeMap[summonData[index].SummonSize] + " to ";
            checkSummon.SummonSize = currentSummonSize.Key;
            summonData[index] = checkSummon;
            log.Debug("Entry {name} with {summon} at {size} got changed.", checkSummon.CharacterName, summonSelection, checkSummon.SummonSize);
            pluginInterface.UiBuilder.AddNotification(entry + sizeMap[summonData[index].SummonSize]);
        }
        Save();
    }

    private void DrawBottomButtons()
    {
        var originPos = ImGui.GetCursorPos();
        ImGui.SetCursorPosX(10f);
        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - (3f * ImGuiHelpers.GlobalScale) + (ImGui.GetScrollY() * 2));
        if (ImGui.Button("Update Character List"))
        {
            RequestCache(newCache: true);
        }
        ImGui.SetCursorPos(originPos);
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize("Close").X - 10f);
        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - (3f * ImGuiHelpers.GlobalScale) + (ImGui.GetScrollY() * 2));
        if (ImGui.Button("Close"))
        {
            IsOpen = false;
        }
        ImGui.SetCursorPos(originPos);
    }

    private void ResizeIfNeeded()
    {
        if (fontChange || charaWidth is 0 || summonsWidth is 0 || sizesWidth is 0)
        {
            charaWidth = ImGui.CalcTextSize(LongestCharaName).X + (ImGui.GetStyle().FramePadding.X * 2);
            summonsWidth = ImGui.CalcTextSize(LongestSummonName).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            sizesWidth = ImGui.CalcTextSize(LongestSize).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            tableButtonAlingmentOffset = charaWidth + summonsWidth + sizesWidth + (ImGui.GetStyle().ItemSpacing.X * 3);
            if (SizeConstraints.HasValue)
            {
                var newWidth = tableButtonAlingmentOffset + GetIconButtonSize(fontHandle: null, buttonIcon).X + (ImGui.GetStyle().WindowPadding.X * 3) + ImGui.GetStyle().ScrollbarSize;
                SizeConstraints = new WindowSizeConstraints()
                {
                    MinimumSize = new Vector2(newWidth / ImGuiHelpers.GlobalScale, SizeConstraints.Value.MinimumSize.Y),
                    MaximumSize = SizeConstraints.Value.MaximumSize,
                };
            }
            fontChange = false;
        }
    }

    private void Save()
    {
        var tempList = summonData.Where(item => item.CharacterName.Equals("Other players", StringComparison.Ordinal)).ToList();
        tempList.AddRange(summonData.Except(tempList).OrderBy(item => item.CharacterName, StringComparer.Ordinal).ToList());
        if (tempList.Count == summonData.Count && summonData.ToHashSet().SetEquals(tempList))
        {
            config.SummonData = tempList;
        }
        var x = tempList.Last(item => item.CharacterName.Equals("Other players", StringComparison.Ordinal));
        var lastX = tempList.LastIndexOf(x);
        log.Warning("Last entry of Other Players is at index: {i}", lastX);
        config.Save(pluginInterface);
    }

    private async void QueueColumnWidthChange(IFontHandle handle, ILockedImFont lockedFont)
    {
        while (!handle.Available && !cToken.IsCancellationRequested)
        {
            await Task.Delay((int)TimeSpan.FromSeconds(1).TotalMilliseconds, cToken).ConfigureAwait(false);
        }
        fontChange = true;
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged -= QueueColumnWidthChange;
        cts.Cancel();
        cts.Dispose();
    }
}
