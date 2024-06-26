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
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Internal.Notifications;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using PetScale.Structs;
using PetScale.Enums;

namespace PetScale.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly DalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly PetScale plugin;
    private readonly IPluginLog log;
    private readonly INotificationManager notificationManager;
    private readonly Dictionary<PetSize, string> sizeMap = new()
    {
        { PetSize.SmallModelScale,   "Small" },
        { PetSize.MediumModelScale,  "Medium"},
        { PetSize.LargeModelScale,   "Large" },
    };
    private readonly string deleteButtonIcon, addButtonIcon;
    private readonly CancellationTokenSource cts;
    private readonly CancellationToken cToken;
    private readonly Notification notification = new();
    private const string DefaultPetSelection = "Pet";
    private const string DefaultSizeSelection = "Size";
    private const string DefaultCharacterSelection = "Characters";
    private const string LongestCharaName = "WWWWWWWWWWWWWWW WWWWW";
    private const string LongestSize = "Medium";

    public Dictionary<string, PetModel> petMap { get; } = new(StringComparer.Ordinal);
    private Queue<string> players => plugin.players;
    private IList<PetStruct> petData => config.PetData;
    private IFontHandle iconFont => pluginInterface.UiBuilder.IconFontFixedWidthHandle;

    private string petSelection = DefaultPetSelection, longestPetName = string.Empty, sizeSelection = DefaultSizeSelection, charaName = DefaultCharacterSelection;
    private string filterTemp = string.Empty;
    private float tableButtonAlignmentOffset, charaWidth, petWidth, sizesWidth;
    private bool fontChange;

    public unsafe ConfigWindow(PetScale _plugin,
        Configuration _config,
        DalamudPluginInterface _pluginInterface,
        IPluginLog _pluginLog,
        INotificationManager _notificationManager) : base($"{nameof(PetScale)} Config")
    {
        plugin = _plugin;
        config = _config;
        pluginInterface = _pluginInterface;
        log = _pluginLog;
        notificationManager = _notificationManager;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(470, 365),
            MaximumSize = new Vector2(Device.Instance()->SwapChain->Width, Device.Instance()->SwapChain->Height),
        };

        cts = new CancellationTokenSource();
        cToken = cts.Token;

        deleteButtonIcon = FontAwesomeIcon.Trash.ToIconString();
        addButtonIcon = FontAwesomeIcon.Plus.ToIconString();
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
        ProcessPetData(save: true);
        charaName = DefaultCharacterSelection;
        petSelection = DefaultPetSelection;
        sizeSelection = DefaultSizeSelection;
    }

    public override void Draw()
    {
        ResizeIfNeeded();
        using var tabBar = ImRaii.TabBar("TabBar");
        if (!tabBar)
        {
            return;
        }
        GeneralTab();
        MiscTab();
    }

    private void MiscTab()
    {
        using var miscTab = ImRaii.TabItem("Misc");
        if (!miscTab)
        {
            return;
        }
        DrawRadioButtons(
            "Scale SCH fairy to the size of other in-game fairies",
            () =>
            {
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Seraph is excluded, as she's bigger by default");
            },
            config,
            c => c.FairySize,
            (c, value) => c.FairySize = value,
            "Off", "Self", "Others", "All");
        DrawBottomButtons(onlyClose: true);
    }

    private void GeneralTab()
    {
        using var generalTab = ImRaii.TabItem("General");
        if (!generalTab)
        {
            return;
        }
#if DEBUG
        DevWindow.Print("Summon entries: " + petData.Count.ToString());
#endif
        ImGui.TextUnformatted("Amount of players: " + GetPlayerCount(players.Count, plugin.clientState.IsLoggedIn).ToString(CultureInfo.InvariantCulture));
        var buttonPressed = false;
        DrawComboBox("Characters", charaName, charaWidth, out charaName, players, filter: true);
        ImGui.SameLine();
        DrawComboBox("Pets", petSelection, petWidth, out petSelection, petMap.Keys, filter: false);
        ImGui.SameLine();
        DrawComboBox("Sizes", sizeSelection, sizesWidth, out sizeSelection, sizeMap.Values, filter: false);
        ImGui.SameLine();
        if (IconButton(iconFont, addButtonIcon, "AddButton", 1))
        {
            buttonPressed = true;
        }
        if (buttonPressed)
        {
            var error = false;
            if (charaName.IsNullOrWhitespace() || charaName.Equals("Characters", StringComparison.Ordinal))
            {
                CreateNotification("Invalid Character selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (petSelection.Equals(DefaultPetSelection, StringComparison.Ordinal))
            {
                CreateNotification("Invalid Pet selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (sizeSelection.Equals("Size", StringComparison.Ordinal))
            {
                CreateNotification("Invalid Pet Size selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (!error)
            {
                CheckPossibleEntry();
            }
        }
        DisplayEntries();
        DrawBottomButtons();
    }

    private unsafe void DisplayEntries()
    {
        var currentSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (36f * ImGuiHelpers.GlobalScale));
        using var tableBorderColor = ImRaii.PushColor(ImGuiCol.TableBorderStrong, ColorHelpers.RgbaVector4ToUint(*ImGui.GetStyleColorVec4(ImGuiCol.Border)));
        using var table = ImRaii.Table("UserEntries", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.PreciseWidths | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter, currentSize);
        if (!table || petData.Count <= 0)
        {
            return;
        }

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, charaWidth);
        ImGui.TableSetupColumn("Pet", ImGuiTableColumnFlags.WidthFixed, petWidth);
        ImGui.TableSetupColumn("PetSize", ImGuiTableColumnFlags.WidthFixed, sizesWidth);
        ImGui.TableSetupColumn("DeleteButton", ImGuiTableColumnFlags.WidthFixed, IconButtonSize(iconFont, deleteButtonIcon).X);
        var itemRemoved = false;
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        var clipperHeight = IconButtonSize(iconFont, deleteButtonIcon).Y + (ImGui.GetStyle().FramePadding.Y * 2);
        clipper.Begin(petData.Count, clipperHeight);

        var clipperBreak = false;
        while (clipper.Step())
        {
            if (clipperBreak)
            {
                break;
            }
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= petData.Count)
                {
                    clipperBreak = true;
                    break;
                }
                ImGui.TableNextRow();
                var buttonId = "##" + i.ToString(CultureInfo.InvariantCulture);
                var item = petData[i];

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(" " + item.CharacterName);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(item.PetID.ToString());
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(sizeMap[item.PetSize]);
                ImGui.TableSetColumnIndex(3);
                ImGui.SetCursorPosX(tableButtonAlignmentOffset);
                if (IconButton(iconFont, deleteButtonIcon, buttonId + deleteButtonIcon, 1))
                {
                    petData.RemoveAt(i);
                    CreateNotification("Entry " + item.CharacterName + ", " + petSelection + ", " + sizeMap[item.PetSize] + " was removed.", "Entry removed");
                    itemRemoved = true;
                }
            }
        }
        clipper.End();
        clipper.Destroy();
        if (itemRemoved)
        {
            ProcessPetData(save: true);
        }
    }

    private void DrawComboBox<T>(string label, string current, float width, out string result, IReadOnlyCollection<T> list, bool filter) where T : notnull
    {
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo("##Combo" + label, current);
        result = current;
        if (!combo)
        {
            return;
        }
        var tempList = list.Select(item => item.ToString()!).ToList();
        if (tempList.Count > 0)
        {
            tempList.Sort(2, tempList.Count - 2, StringComparer.InvariantCulture);
        }
        if (filter)
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputTextWithHint("##Filter" + label, "Filter..", ref filterTemp, 30);
            tempList = tempList.Where(item => item.Contains(filterTemp, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var itemCount = tempList.Count;
        var height = ImGui.GetTextLineHeightWithSpacing() * Math.Min(itemCount + 1.5f, 8);
        height += itemCount > 0 ? -ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().FramePadding.Y : 0;
        using var listChild = ImRaii.Child("###child" + label, new Vector2(width, height));
        DrawClippedList(itemCount, current, tempList, out result);
    }

    private static unsafe void DrawClippedList(int itemCount, string preview, IReadOnlyList<string> list, out string result)
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
                if (!ImGui.Selectable(item + "##" + i.ToString(CultureInfo.CurrentCulture)))
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

    private static Vector2 IconButtonSize(IFontHandle fontHandle, string icon)
    {
        using (fontHandle.Push())
        {
            return new Vector2(ImGuiHelpers.GetButtonSize(icon).X, ImGui.GetFrameHeight());
        }
    }

    // widthOffset is a pain in the ass, at 100% you want 0, <100% you want 1 or more, >100% it entirely depends on whether you get a non-repeating divison or not... maybe?
    // also this entirely varies for each icon, so good luck aligning everything
    private static bool IconButton(IFontHandle fontHandle, string icon, string buttonIDLabel, float widthOffset = 0f)
    {
        using (fontHandle.Push())
        {
            var cursorScreenPos = ImGui.GetCursorScreenPos();
            var frameHeight = ImGui.GetFrameHeight();
            var result = ImGui.Button("##" + buttonIDLabel, new Vector2(ImGuiHelpers.GetButtonSize(icon).X, frameHeight));
            var pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X + widthOffset,
                cursorScreenPos.Y + (frameHeight / 2f) - (ImGui.CalcTextSize(icon).Y / 2f));
            ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon);

            return result;
        }
    }

    private void CheckPossibleEntry()
    {
        var currentPetSize = sizeMap.SingleOrDefault(x => x.Value.Equals(sizeSelection, StringComparison.OrdinalIgnoreCase));
        if (currentPetSize.Value is null)
        {
            throw new NotSupportedException();
        }
        var currentPetData = new PetStruct()
        {
            CharacterName = charaName,
            PetID = petMap[petSelection],
            PetSize = currentPetSize.Key,
        };
        var checkPet = petData
            .SingleOrDefault(data => data.CharacterName.Equals(currentPetData.CharacterName, StringComparison.Ordinal)
            && data.PetID == currentPetData.PetID);
        if (currentPetData.Equals(checkPet))
        {
            return;
        }
        if (checkPet.IsDefault())
        {
            petData.Add(currentPetData);
            CreateNotification(
                "Entry " + currentPetData.CharacterName + ", " + petSelection + ", " + sizeMap[currentPetSize.Key] + " was added.",
                "New entry");
        }
        else if (!checkPet.PetSize.Equals(currentPetData.PetSize))
        {
            var index = petData.IndexOf(checkPet);
            var entry = "Entry " + petData[index].CharacterName + " with " + petData[index].PetID.ToString() + " changed size from " + sizeMap[petData[index].PetSize] + " to ";
            checkPet.PetSize = currentPetSize.Key;
            petData[index] = checkPet;
            log.Debug("Entry {name} with {pet} at {size} got changed.", checkPet.CharacterName, petSelection, checkPet.PetSize);
            CreateNotification(entry + sizeMap[petData[index].PetSize], "Entry changed");
        }
        ProcessPetData(save: true);
    }

    private void DrawBottomButtons(bool onlyClose = false)
    {
        var originPos = ImGui.GetCursorPos();
        if (!onlyClose)
        {
            ImGui.SetCursorPosX(10f);
            ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - (3f * ImGuiHelpers.GlobalScale) + (ImGui.GetScrollY() * 2));
            if (ImGui.Button("Update Character List"))
            {
                RequestCache(newCache: true);
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear All Entries"))
            {
                petData.Clear();
                ProcessPetData(save: true);
            }
            ImGui.SetCursorPos(originPos);
        }
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
        if (fontChange || charaWidth is 0 || petWidth is 0 || sizesWidth is 0)
        {
            var currentSize = ImGui.CalcTextSize(longestPetName).X;
            foreach (var petName in petMap.Select(pet => pet.Key))
            {
                var size = ImGui.CalcTextSize(petName).X;
                if (size > currentSize)
                {
                    longestPetName = petName;
                    currentSize = size;
                }
            }
            charaWidth = ImGui.CalcTextSize(LongestCharaName).X + (ImGui.GetStyle().FramePadding.X * 2);
            petWidth = ImGui.CalcTextSize(longestPetName).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            sizesWidth = ImGui.CalcTextSize(LongestSize).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            tableButtonAlignmentOffset = charaWidth + petWidth + sizesWidth + (ImGui.GetStyle().ItemSpacing.X * 3);
            if (SizeConstraints.HasValue)
            {
                var newWidth = tableButtonAlignmentOffset + IconButtonSize(iconFont, deleteButtonIcon).X + (ImGui.GetStyle().WindowPadding.X * 2) + ImGui.GetStyle().ScrollbarSize;
                SizeConstraints = new WindowSizeConstraints()
                {
                    MinimumSize = new Vector2(newWidth / ImGuiHelpers.GlobalScale, SizeConstraints.Value.MinimumSize.Y),
                    MaximumSize = SizeConstraints.Value.MaximumSize,
                };
            }
            fontChange = false;
        }
    }

    public void ProcessPetData(bool save)
    {
        if (petData.Count is 0)
        {
            plugin.lastIndexOfOthers = -1;
            if (save)
            {
                config.Save(pluginInterface);
            }
            return;
        }
        var tempEnumerable = petData.Where(item => item.CharacterName.Equals(PetScale.Others, StringComparison.Ordinal));
        if (tempEnumerable.Count() is not 0)
        {
            var tempList = tempEnumerable.ToList();
            tempList.AddRange([.. petData.Except(tempList).OrderBy(item => item.CharacterName, StringComparer.Ordinal).ThenBy(item => item.PetID.ToString(), StringComparer.Ordinal)]);
            if (tempList.Count == petData.Count && petData.ToHashSet().SetEquals(tempList))
            {
                config.PetData = tempList;
            }
            var otherEntry = tempList.Last(item => item.CharacterName.Equals(PetScale.Others, StringComparison.Ordinal));
            plugin.lastIndexOfOthers = tempList.LastIndexOf(otherEntry);
        }
        else
        {
            var orderedList = petData.OrderBy(item => item.CharacterName, StringComparer.Ordinal).ThenBy(item => item.PetID.ToString(), StringComparer.Ordinal).ToList();
            config.PetData = orderedList;
            plugin.lastIndexOfOthers = -1;
        }
        if (save)
        {
            config.Save(pluginInterface);
        }
    }

    private static int GetPlayerCount(int queueCount, bool loggedIn)
    {
        if (!loggedIn)
        {
            return 0;
        }
        return queueCount switch
        {
            < 2 => 0,
            >= 2 => queueCount - 1,
        };
    }

    private async void QueueColumnWidthChange(IFontHandle handle, ILockedImFont lockedFont)
    {
        while (!handle.Available && !cToken.IsCancellationRequested)
        {
            await Task.Delay((int)TimeSpan.FromSeconds(1).TotalMilliseconds, cToken).ConfigureAwait(false);
        }
        fontChange = true;
    }

    private void CreateNotification(string content, string title, NotificationType type = NotificationType.Success)
    {
        notification.Content = content;
        notification.Title = notification.MinimizedText = title;
        notification.Type = type;
        notificationManager.AddNotification(notification);
    }

    /// <summary>
    ///     This function draws as many <see cref="ImGui.RadioButton(string, ref int, int)"/> as the number of <paramref name="buttons"/> passed,
    ///     with the option to add an extra element, like a <see cref="ImGuiComponents.HelpMarker(string)"/> after the intial <paramref name="label"/>.
    /// </summary>
    /// <param name="label">The text used for <see cref="ImGui.TextUnformatted(string)"/>.</param>
    /// <param name="extra">Nullable action to be called immediately after <see cref="ImGui.TextUnformatted(string)"/>, and before the <see cref="ImGui.RadioButton(string, ref int, int)"/> are drawn.</param>
    /// <param name="config">Instance of <see cref="Configuration"/> used for <paramref name="getOption"/> and <paramref name="setOption"/></param>
    /// <param name="getOption">Function responsible for retrieving the desired <see cref="Configuration"/> property.</param>
    /// <param name="setOption">Action responsible for setting the option from <paramref name="getOption"/> back to an instance of <see cref="Configuration"/>.</param>
    /// <param name="buttons">The labels used for <see cref="ImGui.RadioButton(string, ref int, int)"/>.</param>
    private void DrawRadioButtons(string label, Action? extra, in Configuration config, Func<Configuration, int> getOption, Action<Configuration, int> setOption, params string[] buttons)
    {
        ImGui.TextUnformatted(label);
        extra?.Invoke();
        var radioOption = getOption(config);
        if (radioOption > buttons.Length)
        {
            radioOption = 0;
            setOption(config, radioOption);
        }
        var space = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < buttons.Length; i++)
        {
            if (ImGui.RadioButton(buttons[i], ref radioOption, i))
            {
                setOption(config, radioOption);
                config.Save(pluginInterface);
            }
            space -= ImGui.CalcTextSize(buttons[i]).X + GetStyleWidth();
            if (i + 1 < buttons.Length && space > ImGui.CalcTextSize(buttons[i + 1]).X + GetStyleWidth())
            {
                ImGui.SameLine();
            }
            else
            {
                space = ImGui.GetContentRegionAvail().X;
            }
        }
    }

    private static float GetStyleWidth()
        => (ImGui.GetStyle().FramePadding.X * 2) + (ImGui.GetStyle().ItemSpacing.X * 2) + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().ItemInnerSpacing.X;

    public void Dispose()
    {
        pluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged -= QueueColumnWidthChange;
        cts.Cancel();
        cts.Dispose();
    }
}
