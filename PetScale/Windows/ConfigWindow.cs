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
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using PetScale.Helpers;
using PetScale.Structs;
using PetScale.Enums;
using System.Text;

namespace PetScale.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly PetScale plugin;
    private readonly IPluginLog log;
    private readonly INotificationManager notificationManager;
    private readonly Utilities utilities;
    private readonly Dictionary<string, string?> comboFilter = [];
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
    private const string DefaultPetSelection = "Pet", DefaultSizeSelection = "Size", DefaultCharacterSelection = "Characters", DefaultWorldSelection = "World";
    private const string LongestSize = "Medium", LongestWorldName = "Pandaemonium", LongestCharaName = "WWWWWWWWWWWWWWW WWWWW" + "@" + LongestWorldName;
    private const string WorldSelectModal = "World Select";

    public Dictionary<string, PetModel> presetPetMap { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, PetModel> customPetMap { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> worldMap { get; } = new(StringComparer.Ordinal);
    private Queue<(string Name, ulong ContentId, ushort HomeWorld)> players => plugin.players;
    private IList<PetStruct> petData => config.PetData;
    private IFontHandle iconFont => pluginInterface.UiBuilder.IconFontFixedWidthHandle;

    private string petSelection = DefaultPetSelection, longestPetName = string.Empty, sizeSelection = DefaultSizeSelection, charaName = DefaultCharacterSelection;
    private string filterTemp = string.Empty, otherPetSelection = DefaultPetSelection, world = DefaultWorldSelection;
    private float tableButtonAlignmentOffset, charaWidth, petWidth, sizesWidth, worldWidth, tempPetSize = 1f;
    private bool fontChange, showModal = false;
    private Tab currentTab;

    public unsafe ConfigWindow(PetScale _plugin,
        Configuration _config,
        IDalamudPluginInterface _pluginInterface,
        IPluginLog _pluginLog,
        INotificationManager _notificationManager,
        Utilities _utils) : base($"{nameof(PetScale)} Config")
    {
        plugin = _plugin;
        config = _config;
        pluginInterface = _pluginInterface;
        log = _pluginLog;
        notificationManager = _notificationManager;
        utilities = _utils;
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

        _ = Task.Run(() => utilities.InitWorldMap(worldMap), cToken);
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
        petSelection = otherPetSelection = DefaultPetSelection;
        sizeSelection = DefaultSizeSelection;
        world = DefaultWorldSelection;
        comboFilter.Clear();
    }

    public override void Draw()
    {
        ResizeIfNeeded();
        using var tabBar = ImRaii.TabBar("TabBar");
        if (!tabBar)
        {
            return;
        }
        PresetTab();
        CustomTab();
        MiscTab();
        foreach (var item in comboFilter)
        {
            DevWindow.Print("Filter stuff: " + item.Key + " - " + (item.Value ?? "null"));
        }
    }

    private void MiscTab()
    {
        using var miscTab = ImRaii.TabItem("Misc");
        if (!miscTab)
        {
            return;
        }
        currentTab = Tab.None;
        DrawRadioButtons(
            "Scale SCH fairy to the size of other in-game fairies",
            () =>
            {
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Seraph is excluded, as she's bigger by default.\nThis will be overwritten by any size set for Eos or Selene.");
            },
            config,
            c => (int)c.FairyState,
            (c, value) => c.FairyState = (PetState)value,
            "Off", "Self", "Others", "All");
        DrawBottomButtons(onlyClose: true);
    }

    private void CustomTab()
    {
        using var otherPetsTab = ImRaii.TabItem("Other Pets");
        if (!otherPetsTab)
        {
            return;
        }
        currentTab = Tab.Others;
        ImGui.TextUnformatted("Amount of players: " + GetPlayerCount(players.Count, plugin.clientState.IsLoggedIn).ToString(CultureInfo.InvariantCulture));
        var buttonPressed = false;
        DrawComboBox("Characters", charaName, charaWidth, out charaName, players.Select(player => player.Name).ToList(), filter: true, newEntryPossible: true);
        ImGui.SameLine();
        DrawComboBox("Pets", otherPetSelection, petWidth, out otherPetSelection, customPetMap.Keys, filter: false);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sizesWidth);
        if (!otherPetSelection.Equals(DefaultPetSelection, StringComparison.Ordinal)
            && tempPetSize < Utilities.GetMinSize(customPetMap[otherPetSelection]))
        {
            tempPetSize = Utilities.GetMinSize(customPetMap[otherPetSelection]);
        }
        if (!otherPetSelection.Equals(DefaultPetSelection, StringComparison.Ordinal))
        {
            ImGui.DragFloat("##TempPetSize", ref tempPetSize, 0.01f, Utilities.GetMinSize(customPetMap[otherPetSelection]), 4f, "%.3g", ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Pet Size");
            }
        }
        else
        {
            ImGui.DragFloat("##TempPetSize", ref tempPetSize, 0.01f, 1f, 4f, "%.3g", ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Pet Size");
            }
        }
        ImGui.SameLine();
        if (ImGuiUtils.IconButton(iconFont, addButtonIcon, "AddButton", 1))
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
            if (otherPetSelection.Equals(DefaultPetSelection, StringComparison.Ordinal))
            {
                CreateNotification("Invalid Pet selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (!error)
            {
                CheckOtherPossibleEntry();
            }
        }
        DisplayEntries(customSize: true);
        DrawBottomButtons(onlyClose: false, otherData: true);
    }

    private void PresetTab()
    {
        using var generalTab = ImRaii.TabItem("Preset Pets");
        if (!generalTab)
        {
            return;
        }
        currentTab = Tab.Summoner;
#if DEBUG
        DevWindow.Print("Summon entries: " + petData.Count.ToString());
#endif
        ImGui.TextUnformatted("Amount of players: " + GetPlayerCount(players.Count, plugin.clientState.IsLoggedIn).ToString(CultureInfo.InvariantCulture));
        var buttonPressed = false;
        DrawComboBox("Characters", charaName, charaWidth, out charaName, players.Select(player => player.Name).ToList(), filter: true, newEntryPossible: true);
        ImGui.SameLine();
        DrawComboBox("Pets", petSelection, petWidth, out petSelection, presetPetMap.Keys, filter: false);
        ImGui.SameLine();
        DrawComboBox("Sizes", sizeSelection, sizesWidth, out sizeSelection, sizeMap.Values, filter: false);
        ImGui.SameLine();
        if (ImGuiUtils.IconButton(iconFont, addButtonIcon, "AddButton", 1))
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

    private unsafe void DisplayEntries(bool customSize = false)
    {
        using var tableBorderColor = ImRaii.PushColor(ImGuiCol.TableBorderStrong, ColorHelpers.RgbaVector4ToUint(*ImGui.GetStyleColorVec4(ImGuiCol.Border)));
        using var table = ImRaii.Table("UserEntries", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.PreciseWidths | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter,
            new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (36f * ImGuiHelpers.GlobalScale)));
        if (!table || petData.Count <= 0)
        {
            return;
        }

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, charaWidth);
        ImGui.TableSetupColumn("Pet", ImGuiTableColumnFlags.WidthFixed, petWidth);
        ImGui.TableSetupColumn("PetSize", ImGuiTableColumnFlags.WidthFixed, sizesWidth);
        ImGui.TableSetupColumn("DeleteButton", ImGuiTableColumnFlags.WidthFixed, ImGuiUtils.IconButtonSize(iconFont, deleteButtonIcon).X);
        var itemRemoved = false;
        var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        clipper.Begin(petData.Count, ImGuiUtils.IconButtonSize(iconFont, deleteButtonIcon).Y + (ImGui.GetStyle().FramePadding.Y * 2));

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
                if ((customSize && petData[i].PetSize is not PetSize.Custom)
                    || (!customSize && petData[i].PetSize is PetSize.Custom))
                {
                    continue;
                }
                DisplayTableRow(petData[i], i, customSize, "##" + i.ToString(CultureInfo.CurrentCulture), ref itemRemoved);
            }
        }
        clipper.End();
        clipper.Destroy();
        if (itemRemoved)
        {
            ProcessPetData(save: true);
        }
    }

    private void DisplayTableRow(PetStruct item, int index, bool customSize, string buttonId, ref bool itemRemoved)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(" " + item.CharacterName);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(item.PetID.ToString());
        ImGui.TableSetColumnIndex(2);
        if (customSize)
        {
            ImGuiUtils.CenterText(item.AltPetSize.ToString(CultureInfo.CurrentCulture), sizesWidth);
        }
        else
        {
            ImGui.TextUnformatted(sizeMap[item.PetSize]);
        }
        ImGui.TableSetColumnIndex(3);
        ImGui.SetCursorPosX(tableButtonAlignmentOffset);
        if (ImGuiUtils.IconButton(iconFont, deleteButtonIcon, buttonId + deleteButtonIcon, 1))
        {
            petData.RemoveAt(index);
            CreateNotification("Entry " + item.CharacterName + ", " + petSelection + ", " + (customSize ? item.AltPetSize.ToString(CultureInfo.CurrentCulture) : sizeMap[item.PetSize]) + " was removed.", "Entry removed");
            itemRemoved = true;
        }
    }

    // I don't like the way filters are managed, but I can't think of a better way
    private void DrawComboBox<T>(string label, string current, float width, out string result, 
        IReadOnlyCollection<T> list, bool filter, bool newEntryPossible = false) where T : notnull
    {
        ImGui.SetNextItemWidth(width);
        var comboLabel = "##Combo" + label;
        using var combo = ImRaii.Combo(comboLabel, current);
        result = current;
        if (!combo)
        {
            if (filter && comboFilter.ContainsValue(comboLabel))
            {
                comboFilter.Remove(comboLabel);
            }
            return;
        }
        var tempList = list.Select(item => item.ToString()!).ToList();
        if (tempList.Count > 1 && filter)
        {
            tempList.Sort(2, tempList.Count - 2, StringComparer.InvariantCulture);
        }
        if (filter)
        {
            comboLabel += "_" + currentTab.ToString();
            comboFilter.TryAdd(comboLabel, null);
            if (comboFilter[comboLabel] is null)
            {
                comboFilter[comboLabel] = string.Empty;
            }
            ImGui.SetNextItemWidth(width);
            var filterStr = comboFilter[comboLabel];
            if (ImGui.InputTextWithHint("##Filter" + label, newEntryPossible ? "New Entry or Filter.." : "Filter..", ref filterStr, 21, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                comboFilter[comboLabel] = filterStr;
                log.Debug("filter matches {a}", tempList.Count(item => item.Contains(comboFilter[comboLabel]!, StringComparison.OrdinalIgnoreCase)));
                if (newEntryPossible && tempList.Count(item => item.Contains(comboFilter[comboLabel]!, StringComparison.OrdinalIgnoreCase)) is 0)
                {
                    showModal = true;
                    filterStr = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(comboFilter[comboLabel]!.ToLower(CultureInfo.CurrentCulture));
                    ImGui.OpenPopup(WorldSelectModal);
                }
            }
            comboFilter[comboLabel] = filterStr;
            if (newEntryPossible && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("New entry can be made if your search doesn't return any returns in the list");
            }
            if (PopupModal(WorldSelectModal, comboFilter[comboLabel]!))
            {
                comboFilter[comboLabel] = string.Empty;
            }
            tempList = tempList.Where(item => item.Contains(comboFilter[comboLabel]!, StringComparison.OrdinalIgnoreCase)).ToList();
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

    private void CheckOtherPossibleEntry(string? altName = null)
    {
        var tempDic = players.ToDictionary(player => player.Name, cid => (cid.ContentId, cid.HomeWorld), StringComparer.Ordinal);
        var currentPetData = new PetStruct()
        {
            CharacterName = altName ?? charaName,
            PetID = customPetMap[otherPetSelection],
            PetSize = PetSize.Custom,
            AltPetSize = tempPetSize,
        };
        if (!altName.IsNullOrWhitespace())
        {
            currentPetData.HomeWorld = utilities.GetHomeWorldId(world);
        }
        if (tempDic.TryGetValue(charaName, out var cid))
        {
            currentPetData.ContentId = cid.ContentId;
            currentPetData.HomeWorld = cid.HomeWorld;
        }
        if (currentPetData.PetID is PetModel.AllPets)
        {
            currentPetData.Generic = true;
        }
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
                "Entry " + currentPetData.CharacterName + ", " + otherPetSelection + ", " + tempPetSize.ToString(CultureInfo.CurrentCulture) + " was added.",
                "New entry");
        }
        else if (checkPet.PetSize.Equals(currentPetData.PetSize) && !checkPet.AltPetSize.Equals(currentPetData.AltPetSize))
        {
            var index = petData.IndexOf(checkPet);
            var entry = "Entry " + petData[index].CharacterName + " with " + petData[index].PetID.ToString() + " changed size from " + petData[index].PetSize + " to ";
            checkPet.AltPetSize = tempPetSize;
            if (checkPet.UpdateRequired())
            {
                checkPet.ContentId = cid.ContentId;
                checkPet.HomeWorld = cid.HomeWorld;
                if (checkPet.PetID is PetModel.AllPets)
                {
                    checkPet.Generic = true;
                }
            }
            petData[index] = checkPet;
            log.Debug("Entry {name} with {pet} at {size} got changed.", checkPet.CharacterName, otherPetSelection, checkPet.AltPetSize);
            CreateNotification(entry + petData[index].AltPetSize.ToString(CultureInfo.CurrentCulture), "Entry changed");
        }
        ProcessPetData(save: true);
    }

    private void CheckPossibleEntry(string? altName = null)
    {
        var currentPetSize = sizeMap.SingleOrDefault(x => x.Value.Equals(sizeSelection, StringComparison.OrdinalIgnoreCase));
        if (currentPetSize.Value is null)
        {
            throw new NotSupportedException();
        }
        var tempDic = players.ToDictionary(player => player.Name, cid => cid.ContentId, StringComparer.Ordinal);
        var currentPetData = new PetStruct()
        {
            CharacterName = altName ?? charaName,
            PetID = presetPetMap[petSelection],
            PetSize = currentPetSize.Key,
        };
        if (!altName.IsNullOrWhitespace())
        {
            currentPetData.HomeWorld = utilities.GetHomeWorldId(world);
        }
        if (tempDic.TryGetValue(charaName, out var cid))
        {
            currentPetData.ContentId = cid;
        }
        if (currentPetData.PetID is PetModel.AllPets)
        {
            currentPetData.Generic = true;
        }
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
            if (checkPet.UpdateRequired())
            {
                checkPet.ContentId = cid;
                if (checkPet.PetID is PetModel.AllPets)
                {
                    checkPet.Generic = true;
                }
            }
            petData[index] = checkPet;
            log.Debug("Entry {name} with {pet} at {size} got changed.", checkPet.CharacterName, petSelection, checkPet.PetSize);
            CreateNotification(entry + sizeMap[petData[index].PetSize], "Entry changed");
        }
        ProcessPetData(save: true);
    }

    private void DrawBottomButtons(bool onlyClose = false, bool otherData = false)
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
                if (otherData)
                {
                    var tempList = petData.Where(item => item.PetSize is PetSize.Custom).ToList();
                    foreach (var item in tempList)
                    {
                        petData.Remove(item);
                    }
                }
                else
                {
                    var tempList = petData.Where(item => item.PetSize is not PetSize.Custom).ToList();
                    foreach (var item in tempList)
                    {
                        petData.Remove(item);
                    }
                }
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
            foreach (var petName in presetPetMap.Select(pet => pet.Key))
            {
                var size = ImGui.CalcTextSize(petName).X;
                if (size > currentSize)
                {
                    longestPetName = petName;
                    currentSize = size;
                }
            }
            foreach (var petName in customPetMap.Select(pet => pet.Key))
            {
                var size = ImGui.CalcTextSize(petName).X;
                if (size > currentSize)
                {
                    longestPetName = petName;
                    currentSize = size;
                }
            }
            worldWidth = ImGui.CalcTextSize(LongestWorldName).X + (ImGui.GetStyle().FramePadding.X * 2);
            charaWidth = ImGui.CalcTextSize(LongestCharaName).X + (ImGui.GetStyle().FramePadding.X * 2);
            petWidth = ImGui.CalcTextSize(longestPetName).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            sizesWidth = ImGui.CalcTextSize(LongestSize).X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
            tableButtonAlignmentOffset = charaWidth + petWidth + sizesWidth + (ImGui.GetStyle().ItemSpacing.X * 3);
            if (SizeConstraints.HasValue)
            {
                var newWidth = tableButtonAlignmentOffset + ImGuiUtils.IconButtonSize(iconFont, deleteButtonIcon).X + (ImGui.GetStyle().WindowPadding.X * 2) + ImGui.GetStyle().ScrollbarSize;
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
            config.UpdateConfig();
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
            if (ImGui.RadioButton(buttons[i] + "##" + label, ref radioOption, i))
            {
                setOption(config, radioOption);
                config.Save(pluginInterface);
            }
            space -= ImGui.CalcTextSize(buttons[i]).X + ImGuiUtils.GetStyleWidth();
            if (i + 1 < buttons.Length && space > ImGui.CalcTextSize(buttons[i + 1]).X + ImGuiUtils.GetStyleWidth())
            {
                ImGui.SameLine();
            }
            else
            {
                space = ImGui.GetContentRegionAvail().X;
            }
        }
    }

    private bool PopupModal(string label, string newCharacter)
    {
        var size = new Vector2(ImGui.CalcTextSize(newCharacter + " - ").X + worldWidth + (ImGui.GetStyle().FramePadding.X * 2) + 25, 150f);
        var buttonSize = ImGui.CalcTextSize("Add character").X + (ImGui.GetStyle().FramePadding.X * 2) + 25;
        if (size.X < buttonSize)
        {
            size.X = buttonSize;
        }
        ImGui.SetNextWindowSize(size);
        using var modal = ImRaii.PopupModal(label, ref showModal, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar);
        if (!modal || currentTab is Tab.None)
        {
            return false;
        }
        ImGuiUtils.CenterCursor(size, new Vector2(worldWidth + ImGui.CalcTextSize(newCharacter).X, ImGui.GetTextLineHeightWithSpacing()));
        ImGui.TextUnformatted(newCharacter);
        ImGui.SameLine();
        DrawComboBox("World Selection", world, worldWidth, out world, worldMap.Keys, filter: true);
        ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - (ImGui.GetFrameHeight() / 2f) - (ImGui.CalcTextSize("Add character").Y / 2f)  - (3f * ImGuiHelpers.GlobalScale) + (ImGui.GetScrollY() * 2));
        //ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - (3f * ImGuiHelpers.GlobalScale) + (ImGui.GetScrollY() * 2));
        ImGui.SetCursorPosX((ImGui.GetContentRegionMax().X / 2 ) - (ImGuiHelpers.GetButtonSize("Add character").X / 2));
        if (ImGui.Button("Add character"))
        {
            showModal = false;
            var error = false;
            if (newCharacter.IsNullOrWhitespace() || !newCharacter.IsValidCharacterName() || newCharacter.Equals("Other Players", StringComparison.Ordinal))
            {
                CreateNotification("Invalid Character name", "Invalid entry", NotificationType.Error);
                log.Warning("Filter character name: {a}", newCharacter);
                error = true;
            }
            if (currentTab is Tab.Summoner && petSelection.Equals(DefaultPetSelection, StringComparison.Ordinal))
            {
                CreateNotification("Invalid Pet selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (currentTab is Tab.Others && otherPetSelection.Equals(DefaultPetSelection, StringComparison.Ordinal))
            {
                CreateNotification("Invalid Pet selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (world.Equals(DefaultWorldSelection, StringComparison.Ordinal))
            {
                CreateNotification("Invalid World selected", "Invalid entry", NotificationType.Error);
                error = true;
            }
            if (!error)
            {
                if (currentTab is Tab.Summoner)
                {
                    CheckPossibleEntry(newCharacter);
                }
                if (currentTab is Tab.Others)
                {
                    CheckOtherPossibleEntry(newCharacter);
                }
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged -= QueueColumnWidthChange;
        cts.Cancel();
        cts.Dispose();
    }
}

#pragma warning disable MA0048 // File name must match type name
public enum Tab
{
    None,
    Summoner,
    Others,
}
