using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Utility;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Lumina.Excel.GeneratedSheets2;
using Dalamud.Game.ClientState.Objects.SubKinds;
using BattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.Interop;
using PetScale.Structs;
using PetScale.Helpers;
using PetScale.Windows;
using PetScale.Enums;

namespace PetScale;

public sealed class PetScale : IDalamudPlugin
{
    private const string CommandName = "/pscale";
    public const string Others = "Other players";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Utilities utilities;
    public readonly IClientState clientState;

    private readonly StringComparison ordinalComparison = StringComparison.Ordinal;
    private readonly Dictionary<Pointer<BattleChara>, (Pointer<Character> character, bool petSet)> activePetDictionary = [];
    private readonly Dictionary<string, (float smallScale, float mediumScale, float largeScale)> petSizeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch stopwatch = new();
    private readonly double dictionaryExpirationTime = TimeSpan.FromMilliseconds(500).TotalMilliseconds;

    private readonly Dictionary<PetRow, PetModel> petModelMap = new()
    {
        { PetRow.Bahamut,       PetModel.Bahamut        },
        { PetRow.Phoenix,       PetModel.Phoenix        },
        { PetRow.Ifrit,         PetModel.Ifrit          },
        { PetRow.Titan,         PetModel.Titan          },
        { PetRow.Garuda,        PetModel.Garuda         },
        { PetRow.SolarBahamut,  PetModel.SolarBahamut   },
    };

    public WindowSystem WindowSystem { get; } = new("PetScale");
    public Queue<(string Name, ulong ContentId)> players { get; } = new();
    public bool requestedCache { get; set; } = true;
    public int lastIndexOfOthers { get; set; } = -1;
    private ConfigWindow ConfigWindow { get; init; }
#if DEBUG
    private DevWindow DevWindow { get; init; }
    private readonly Dictionary<string, (PetModel?, int)> petModelDic = [];
#endif

    private unsafe Span<Pointer<BattleChara>> BattleCharaSpan => CharacterManager.Instance()->BattleCharas;
    private string? playerName;

    public PetScale(IDalamudPluginInterface _pluginInterface,
        ICommandManager _commandManager,
        IFramework _framework,
        IPluginLog _log,
        IClientState _clientState,
        IDataManager _dataManger,
        INotificationManager _notificationManager)
    {
        pluginInterface = _pluginInterface;
        commandManager = _commandManager;
        framework = _framework;
        log = _log;
        clientState = _clientState;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        utilities = new Utilities(_dataManger, log);

        ConfigWindow = new ConfigWindow(this, config, pluginInterface, log, _notificationManager);
#if DEBUG
        DevWindow = new DevWindow(log, pluginInterface);
        WindowSystem.AddWindow(DevWindow);
        dictionaryExpirationTime = TimeSpan.FromMilliseconds(20).TotalMilliseconds;
#endif

        WindowSystem.AddWindow(ConfigWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close Pet Scale's config window.",
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
        clientState.TerritoryChanged += TerritoryChanged;
        clientState.Login += SetStopwatch;
        clientState.Logout += SetStopwatch;
        framework.Update += OnFrameworkUpdate;
        stopwatch.Start();

        _ = Task.Run(InitSheet);
        ConfigWindow.ProcessPetData(save: true);
        QueueOnlyExistingData();
    }

    private void SetStopwatch()
    {
        playerName = clientState.LocalPlayer?.Name.TextValue;
        if (clientState.IsLoggedIn)
        {
            stopwatch.Start();
        }
        else
        {
            QueueOnlyExistingData();
            stopwatch.Stop();
        }
    }

    private void TerritoryChanged(ushort obj)
    {
        stopwatch.Restart();
    }

    private void QueueOnlyExistingData()
    {
        players.Clear();
        foreach (var entry in config.PetData.DistinctBy(item => item.CharacterName))
        {
            players.Enqueue((entry.CharacterName, entry.ContentId));
        }
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
            if (!Enum.IsDefined((PetRow)pet.RowId))
            {
                continue;
            }
            var scales = (pet.SmallScalePercentage / 100f, pet.MediumScalePercentage / 100f, pet.LargeScalePercentage / 100f);
            if ((scales.Item1 >= 1 || scales.Item2 >= 1) && !pet.NonCombatSummon)
            {
                continue;
            }
            petSizeMap.Add(pet.Name, scales);
            if (petModelMap.ContainsKey((PetRow)pet.RowId))
            {
                ConfigWindow.petMap.Add(pet.Name, petModelMap[(PetRow)pet.RowId]);
            }
        }
        foreach (var entry in petSizeMap)
        {
            log.Debug("{pet} with scales {small} - {medium} - {large}", entry.Key, entry.Value.smallScale, entry.Value.mediumScale, entry.Value.largeScale);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
#if DEBUG
        DevWindowThings();
#endif
        if (clientState is not { LocalPlayer: { } player })
        {
            return;
        }
        unsafe
        {
            if (requestedCache)
            {
                var csPlayer = (Character*)player.Address;
                playerName ??= csPlayer->NameString;
                RefreshCache(playerName, csPlayer);
                requestedCache = false;
            }
        }
        CheckDictionary(player);
    }

    private void CheckDictionary(IPlayerCharacter player)
    {
        if (stopwatch.Elapsed.TotalMilliseconds >= dictionaryExpirationTime)
        {
            stopwatch.Restart();
            activePetDictionary.Clear();
            PopulateDictionary();
            return;
        }
        ParseDictionary(player);
    }

    private unsafe void PopulateDictionary()
    {
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is not ObjectKind.BattleNpc || chara.Value->Character.GameObject.OwnerId is 0xE0000000)
            {
                continue;
            }
            var petName = chara.Value->Character.NameString;
            if (petName.IsNullOrWhitespace() || !petSizeMap.ContainsKey(petName))
            {
                continue;
            }
#if DEBUG
            if (!petModelDic.ContainsKey(petName))
            {
                PetModel? petModel = Enum.IsDefined(typeof(PetModel), chara.Value->Character.CharacterData.ModelCharaId) ? (PetModel)chara.Value->Character.CharacterData.ModelCharaId : null;
                petModelDic.Add(petName, (petModel, chara.Value->Character.CharacterData.ModelCharaId));
            }
#endif
            if (!Enum.IsDefined(typeof(PetModel), chara.Value->Character.CharacterData.ModelCharaId))
            {
                continue;
            }
#if DEBUG
            DevWindow.AddObjects(&chara.Value->Character.GameObject);
#endif
            activePetDictionary.Add(chara.Value, (null, false));
        }
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is ObjectKind.Pc && chara.Value->Character.GameObject.EntityId is not 0xE0000000)
            {
                foreach (var possiblePair in activePetDictionary.Keys.Where(pet => pet.Value->Character.GameObject.OwnerId == chara.Value->Character.GameObject.EntityId))
                {
                    activePetDictionary[possiblePair] = ((Pointer<Character>)(&chara.Value->Character), false);
                }
            }
        }
    }

    private unsafe void ParseDictionary(IPlayerCharacter player)
    {
        foreach (var pair in activePetDictionary)
        {
            if (pair.Value.petSet)
            {
                continue;
            }

            var pet = pair.Key.Value;
            var character = pair.Value.character.Value;
            if (pet is null || character is null)
            {
                continue;
            }
            if (character->NameString.IsNullOrWhitespace() || pet->NameString.IsNullOrWhitespace())
            {
                continue;
            }
#if DEBUG
            DevWindow.Print(pet->NameString + ": " + pet->Character.CharacterData.ModelCharaId + " owned by " + character->NameString + " size " + pet->Character.GameObject.Scale);
#endif
            if (config.FairySize is not 0 && Utilities.IsFairy(pet->Character.CharacterData.ModelCharaId) && utilities.PetVisible(pet))
            {
                switch (config.FairySize)
                {
                    case 1 when character->GameObject.EntityId == player.EntityId:
                    case 2 when character->GameObject.EntityId != player.EntityId:
                    case 3:
                        {
                            utilities.SetScale(pet, 1.5f);
                            activePetDictionary[pair.Key] = (pair.Value.character, true);
                            continue;
                        }
                    default:
                        break;
                }
            }
            if (ParseStruct(pet, character, pet->Character.CharacterData.ModelCharaId, character->GameObject.EntityId == player.EntityId))
            {
                activePetDictionary[pair.Key] = (pair.Value.character, true);
            }
        }
    }

    private unsafe bool ParseStruct(BattleChara* pet, Character* character, int modelId, bool isLocalPlayer)
    {
        var modelType = (PetModel)modelId;
        if (!Enum.IsDefined(modelType))
        {
            return false;
        }
        var savePending = false;
        var allPets = config.PetData.Where(userData => userData.PetID is PetModel.AllPets).ToList();
        var petSet = config.UpdateNeeded
            ? OldParse(pet, character, isLocalPlayer, allPets, modelType, out savePending)
            : NewParse(pet, character, isLocalPlayer, allPets, modelType);
        if (savePending)
        {
            config.UpdateNeeded = config.PetData.Any(data => data.UpdateRequired());
            config.Save(pluginInterface);
        }
        return petSet;
    }

    private unsafe bool OldParse(BattleChara* pet, Character* character, bool isLocalPlayer, List<PetStruct> allPets, PetModel modelType, out bool savePending)
    {
        var petSet = false;
        savePending = false;
        var petName = pet->NameString;
        var index = lastIndexOfOthers >= 0 ? lastIndexOfOthers : -1;
        for (var i = 0; i <= index; i++)
        {
            if (isLocalPlayer)
            {
                continue;
            }
            var userData = config.PetData[i];
            // General Pet for General Character
            if (allPets.Exists(item => item.CharacterName.Equals(userData.CharacterName, ordinalComparison))
                && userData.PetID is PetModel.AllPets)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
            // Specific Pet for General Character
            if (userData.Generic && userData.PetID == modelType)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
        }
        for (var i = index + 1; i < config.PetData.Count; i++)
        {
            var userData = config.PetData[i];
            if (!userData.CharacterName.Equals(character->NameString, ordinalComparison))
            {
                continue;
            }
            userData.ContentId = character->ContentId;
            config.PetData[i] = userData;
            savePending = true;
            // General Pet for Specific Character
            if (allPets.Exists(item => item.CharacterName.Equals(userData.CharacterName, ordinalComparison))
                && userData.PetID is PetModel.AllPets)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
            // Specific Pet for Specific Character
            if (userData.CharacterName.Equals(character->NameString, ordinalComparison) && userData.PetID == modelType)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
        }
        return petSet;
    }

    private unsafe bool NewParse(BattleChara* pet, Character* character, bool isLocalPlayer, List<PetStruct> allPets, PetModel modelType)
    {
        var petSet = false;
        var petName = pet->NameString;
        var index = lastIndexOfOthers >= 0 ? lastIndexOfOthers : -1;
        for (var i = 0; i <= index; i++)
        {
            var userData = config.PetData[i];
            if (isLocalPlayer || !userData.Generic)
            {
                continue;
            }
            // General Pet for General Character
            if (allPets.Exists(item => item.ContentId == userData.ContentId) && userData.PetID is PetModel.AllPets)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
            // Specific Pet for General Character
            if (userData.PetID == modelType)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
        }
        for (var i = index + 1; i < config.PetData.Count; i++)
        {
            var userData = config.PetData[i];
            if (userData.ContentId != character->ContentId)
            {
                continue;
            }
            // General Pet for Specific Character
            if (allPets.Exists(item => item.ContentId == userData.ContentId) && userData.PetID is PetModel.AllPets && userData.Generic)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
            // Specific Pet for Specific Character
            if (userData.PetID == modelType)
            {
                SetScale(pet, userData, petName);
                petSet = true;
            }
        }
        return petSet;
    }

    private unsafe void RefreshCache(string playerName, Character* player)
    {
        players.Clear();
        players.Enqueue((playerName, player->ContentId));
        players.Enqueue((Others, 1));
        utilities.CachePlayerList(player->EntityId, players, BattleCharaSpan);
    }

    private unsafe void SetScale(BattleChara* pet, in PetStruct userData, string petName)
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
        DevWindow.Print("Actor pair count: " + activePetDictionary.Count.ToString());
        foreach (var entry in petModelDic)
        {
            var petModel = entry.Value.Item1 is not null ? "True - " + entry.Value.Item1.ToString() : "False";
            DevWindow.Print($"Pet: {entry.Key} - ModelCharaId: {entry.Value.Item2} - IsPetModel: {petModel}");
        }
        var charInSpan = BattleCharaSpan.ToArray()
            .Count(x => x.Value is not null
                && x.Value->Character.ObjectKind is ObjectKind.Pc);
        var nonCharInSpan = BattleCharaSpan.ToArray()
            .Count(x => x.Value is not null
            && x.Value->Character.ObjectKind is not ObjectKind.Pc);
        DevWindow.Print("Player Characters in BattleCharaSpan: " + charInSpan);
        DevWindow.Print("Not Player Characters in BattleCharaSpan: " + nonCharInSpan);
    }
#endif

    private void OnCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    public void Dispose()
    {
        stopwatch.Stop();
        players.Clear();

        framework.Update -= OnFrameworkUpdate;
        clientState.Login -= SetStopwatch;
        clientState.Logout -= SetStopwatch;
        clientState.TerritoryChanged -= TerritoryChanged;
        pluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;

        ConfigWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
    }
}
