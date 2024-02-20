using System;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;

using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Utility;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Lumina.Excel.GeneratedSheets;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using BattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.Interop;
using PetScale.Structs;
using PetScale.Helpers;
using PetScale.Windows;
using PetScale.Enums;

namespace PetScale;

public sealed class PetScale : IDalamudPlugin
{
    private const string CommandName = "/pscale";

    private readonly DalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly Utilities utilities;

    private readonly CultureInfo cultureInfo = CultureInfo.InvariantCulture;
    private readonly StringComparison ordinalComparison = StringComparison.Ordinal;
    private readonly Dictionary<Pointer<BattleChara>, Pointer<Character>> activePetDictionary = [];
    private readonly Dictionary<string, (float smallScale, float mediumScale, float largeScale)> petSizeMap = [];
    private const string Others = "Other players";

    public WindowSystem WindowSystem { get; } = new("PetScale");
    public Queue<string> players { get; } = new(101);
    public bool requestedCache { get; set; } = true;

    private ConfigWindow ConfigWindow { get; init; }
#if DEBUG
    private DevWindow DevWindow { get; init; }
#endif

    private unsafe Span<Pointer<BattleChara>> BattleCharaSpan => CharacterManager.Instance()->BattleCharaListSpan;
    private string? playerName;

    public PetScale(DalamudPluginInterface _pluginInterface,
        ICommandManager _commandManager,
        IFramework _framework,
        IPluginLog _log,
        IClientState _clientState,
        IDataManager _dataManger)
    {
        pluginInterface = _pluginInterface;
        commandManager = _commandManager;
        framework = _framework;
        log = _log;
        clientState = _clientState;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        utilities = new Utilities(_dataManger, log);

        ConfigWindow = new ConfigWindow(this, config, pluginInterface, log);
#if DEBUG
        DevWindow = new DevWindow(log, pluginInterface);
        WindowSystem.AddWindow(DevWindow);
#endif

        WindowSystem.AddWindow(ConfigWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close Pet Scale's config window.",
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
        framework.Update += OnFrameworkUpdate;

        _ = Task.Run(InitSheet);
    }

    private void InitSheet()
    {
        var petSheet = utilities.GetSheet<Pet>(clientState.ClientLanguage);
        if (petSheet is null)
        {
            return;
        }
        ConfigWindow.petMap.Add(nameof(PetModel.AllPets), PetModel.AllPets);
        foreach (var pet in petSheet)
        {
            if (!Enum.TryParse(typeof(PetRow), pet.RowId.ToString(cultureInfo), out var row) || row is not PetRow)
            {
                continue;
            }
            var scales = (pet.Unknown9 / 100f, pet.Unknown10 / 100f, pet.Unknown11 / 100f);
            if (scales.Item1 >= 1 || scales.Item2 >= 1)
            {
                continue;
            }
            petSizeMap.Add(pet.Name, scales);
            var name = row.ToString();
            if (name.IsNullOrWhitespace())
            {
                log.Debug("Invalid PetRow {name}", row);
                continue;
            }
            if (!Enum.TryParse(typeof(PetModel), name, out var model) || model is not PetModel modelValue)
            {
                log.Debug("PetRow {name} couldn't map onto PetModel", name);
                continue;
            }
            ConfigWindow.petMap.Add(pet.Name, modelValue);
        }
        foreach (var entry in petSizeMap)
        {
            log.Debug("{pet} with scales {small} - {meduim} - {large}", entry.Key, entry.Value.smallScale, entry.Value.mediumScale, entry.Value.largeScale);
        }
        foreach (var entry in ConfigWindow.petMap)
        {
            log.Debug("{pet} with {model}", entry.Key, entry.Value);
        }
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
#if DEBUG
        DevWindowThings();
#endif
        if (clientState is not { LocalPlayer: { } player })
        {
            if (playerName is not null)
            {
                playerName = null;
            }
            players.Clear();
            foreach (var entry in config.PetData.Select(item => item.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                players.Enqueue(entry);
            }
            return;
        }
        if (requestedCache)
        {
            playerName ??= player.Name.TextValue;
            RefreshCache(playerName, player.ObjectId);
            requestedCache = false;
        }
        PopulateDictionary();
        ParseDictionary(player);
        activePetDictionary.Clear();
    }

    private unsafe void PopulateDictionary()
    {
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is not (byte)ObjectKind.BattleNpc || chara.Value->Character.GameObject.OwnerID is 0xE0000000)
            {
                continue;
            }
            var petName = MemoryHelper.ReadStringNullTerminated((nint)chara.Value->Character.GameObject.GetName());
            if (petName.IsNullOrWhitespace() || !petSizeMap.ContainsKey(petName))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(PetModel), chara.Value->Character.CharacterData.ModelCharaId.ToString(cultureInfo), out _))
            {
                continue;
            }
#if DEBUG
            DevWindow.AddObjects(&chara.Value->Character.GameObject);
#endif
            activePetDictionary.TryAdd(chara.Value, value: null);
        }
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is (byte)ObjectKind.Player && chara.Value->Character.GameObject.ObjectID is not 0xE0000000)
            {
                foreach (var possiblePair in activePetDictionary.Keys.Where(pet => pet.Value->Character.GameObject.OwnerID == chara.Value->Character.GameObject.ObjectID))
                {
                    activePetDictionary[possiblePair] = &chara.Value->Character;
                }
            }
        }
    }

    private unsafe void ParseDictionary(PlayerCharacter player)
    {
        foreach (var pair in activePetDictionary)
        {
            var pet = pair.Key.Value;
            var character = pair.Value.Value;
            if (character is null || pet is null)
            {
                continue;
            }
            var petName = MemoryHelper.ReadStringNullTerminated((nint)pet->Character.GameObject.GetName());
            var characterName = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());
            if (characterName.IsNullOrWhitespace() || petName.IsNullOrWhitespace())
            {
                continue;
            }
#if DEBUG
            DevWindow.Print(petName + ": " + pet->Character.CharacterData.ModelCharaId + " owned by " + characterName);
#endif
            if (config.PetData.Any(data => data.CharacterName.Equals(characterName, ordinalComparison) && (int)data.PetID == pet->Character.CharacterData.ModelCharaId))
            {
                ParseStruct(pet, characterName, petName, pet->Character.CharacterData.ModelCharaId);
                continue;
            }
            ParseGeneric(pet, characterName, petName, pet->Character.CharacterData.ModelCharaId, character->GameObject.ObjectID == player.ObjectId);
        }
    }

    // ParseStruct will only match userData.Name to characterName and (int)data.SummonID to summon->ModelCharaId
    // ParseGeneric will only match userData.SummonID that's AllSummons

    private unsafe void ParseGeneric(BattleChara* pet, string characterName, string petName, int modelId, bool isLocalPlayer)
    {
        foreach (var userData in config.PetData)
        {
            // redundant?
            //if (userData.SummonID is not SummonModel.AllSummons)
            //{
            //    continue;
            //}
            if ((!userData.CharacterName.Equals(characterName, ordinalComparison) || !isLocalPlayer)
                && (!userData.CharacterName.Equals(Others, ordinalComparison) || isLocalPlayer))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(PetModel), modelId.ToString(cultureInfo), out _))
            {
                continue;
            }
            SetScale(pet, userData, petName);
            log.Debug("Scale set by ParseGeneric for: {chara} - {pet} - {size}", characterName, petName, userData.PetSize.ToString());
        }
    }

    private unsafe void ParseStruct(BattleChara* pet, string characterName, string petName, int modelId)
    {
        foreach (var userData in config.PetData)
        {
            if (!characterName.Equals(userData.CharacterName, ordinalComparison))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(PetModel), modelId.ToString(cultureInfo), out var model)
                || model is not PetModel modelType
                || modelType != userData.PetID)
            {
                continue;
            }
            SetScale(pet, userData, petName);
            log.Debug("Scale set by ParseStruct for: {chara} - {pet} - {size}", characterName, petName, userData.PetSize.ToString());
        }
    }

    private void RefreshCache(string playerName, uint playerObjectId)
    {
        players.Clear();
        players.Enqueue(playerName);
        players.Enqueue(Others);
        Utilities.CachePlayerList(playerObjectId, players, BattleCharaSpan);
    }

    private unsafe void SetScale(BattleChara* pet, PetStruct userData, string petName)
    {
        var scale = userData.PetSize switch
        {
            PetSize.SmallModelScale => petSizeMap[petName].smallScale,
            PetSize.MediumModelScale => petSizeMap[petName].mediumScale,
            PetSize.LargeModelScale => petSizeMap[petName].largeScale,
            _ => throw new ArgumentException("Invalid PetSize", paramName: userData.PetSize.ToString()),
        };
        utilities.SetScale(pet, scale);
    }

#if DEBUG
    private unsafe void DevWindowThings()
    {
        DevWindow.IsOpen = true;
        DevWindow.Print("Actor pair count: " + activePetDictionary.Count.ToString(cultureInfo));
    }
#endif

    private void OnCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    public void Dispose()
    {
        players.Clear();
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;

        ConfigWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
    }
}
