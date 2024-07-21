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
    public const ulong OthersContendId = 1;
    public const ushort OthersHomeWorld = 1;

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

    private readonly Dictionary<PetRow, PetModel> otherPetModelMap = new()
    {
        { PetRow.Eos,               PetModel.Eos            },
        { PetRow.Selene,            PetModel.Selene         },
        { PetRow.Seraph,            PetModel.Seraph         },
        { PetRow.Rook,              PetModel.Rook           },
        { PetRow.AutomatonQueen,    PetModel.AutomatonQueen },
        { PetRow.Esteem,            PetModel.Esteem         },
    };

    public WindowSystem WindowSystem { get; } = new("PetScale");
    public Queue<(string Name, ulong ContentId)> players { get; } = new();
    public bool requestedCache { get; set; } = true;
    public int lastIndexOfOthers { get; set; } = -1;
    private ConfigWindow ConfigWindow { get; init; }
#if DEBUG
    private DevWindow DevWindow { get; init; }
    private readonly Dictionary<string, (PetModel?, int)> petModelDic = [];
    private readonly IObjectTable objectTable;
#endif

    private unsafe Span<Pointer<BattleChara>> BattleCharaSpan => CharacterManager.Instance()->BattleCharas;
    private string? playerName;

    public PetScale(IDalamudPluginInterface _pluginInterface,
        ICommandManager _commandManager,
        IFramework _framework,
        IPluginLog _log,
        IClientState _clientState,
        IDataManager _dataManger,
        INotificationManager _notificationManager,
        IObjectTable _objectTable)
    {
        pluginInterface = _pluginInterface;
        commandManager = _commandManager;
        framework = _framework;
        log = _log;
        clientState = _clientState;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        utilities = new Utilities(_dataManger, log, clientState.ClientLanguage);

        ConfigWindow = new ConfigWindow(this, config, pluginInterface, log, _notificationManager);
#if DEBUG
        objectTable = _objectTable;
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
            config.HomeWorld = 0;
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
            var world = string.Empty;
            if (config.HomeWorld is not 0 && config.HomeWorld != entry.HomeWorld && !utilities.GetHomeWorldName(entry.HomeWorld).IsNullOrWhitespace())
            {
                world = "@" + utilities.GetHomeWorldName(entry.HomeWorld);
            }
            players.Enqueue((entry.CharacterName + world, entry.ContentId));
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
            if (scales.Item1 >= 1 || scales.Item2 >= 1)
            {
                continue;
            }
            if (petModelMap.ContainsKey((PetRow)pet.RowId))
            {
                petSizeMap.Add(pet.Name, scales);
                ConfigWindow.petMap.Add(pet.Name, petModelMap[(PetRow)pet.RowId]);
            }
        }
        // List of pet rows sorted by SCH pets, MCH pets, DRK pet then in ascending order
        List<uint> sortedRows = [6, 7, 15, 8, 18, 17];
        foreach (var row in sortedRows)
        {
            var currentRow = petSheet.GetRow(row);
            if (currentRow is null)
            {
                continue;
            }
            if (!Enum.IsDefined((PetRow)currentRow.RowId) || !otherPetModelMap.ContainsKey((PetRow)currentRow.RowId))
            {
                continue;
            }
            if (currentRow.NonCombatSummon || currentRow.Unknown15)
            {
                ConfigWindow.otherPetMap.Add(currentRow.Name, otherPetModelMap[(PetRow)currentRow.RowId]);
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
        if (config.HomeWorld is 0)
        {
            config.HomeWorld = (ushort)player.HomeWorld.Id;
        }
        unsafe
        {
            if (requestedCache)
            {
                playerName ??= player.Name.TextValue;
                RefreshCache(playerName, clientState.LocalContentId, player.EntityId);
                requestedCache = false;
            }
        }
        CheckDictionary(player.EntityId);
    }

    private void CheckDictionary(uint playerEntityId)
    {
        if (stopwatch.Elapsed.TotalMilliseconds >= dictionaryExpirationTime)
        {
            stopwatch.Restart();
            activePetDictionary.Clear();
            PopulateDictionary();
            return;
        }
        ParseDictionary(playerEntityId);
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
#if DEBUG
            var petName = chara.Value->Character.NameString;
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
            DevWindow.AddObjects(objectTable.CreateObjectReference((nint)(&chara.Value->Character)));
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

    private unsafe void ParseDictionary(uint playerEntityId)
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
            if (!utilities.PetVisible(pet))
            {
                continue;
            }
#if DEBUG
            DevWindow.Print(pet->NameString + ": " + pet->Character.CharacterData.ModelCharaId + " owned by " + character->NameString + " size " + pet->Character.GameObject.Scale);
#endif
            if (ParseStruct(pet, character, pet->Character.CharacterData.ModelCharaId, character->GameObject.EntityId == playerEntityId))
            {
                activePetDictionary[pair.Key] = (pair.Value.character, true);
            }
        }
    }

    private unsafe bool ParseStruct(BattleChara* pet, Character* character, int modelId, bool isLocalPlayer)
    {
        if (!Enum.IsDefined((PetModel)modelId))// || !petModelMap.ContainsValue((PetModel)modelId))
        {
            return false;
        }
        var savePending = false;
        var allPets = config.PetData.Where(userData => userData.PetID is PetModel.AllPets).ToList();
        var petSet = config.UpdateNeeded
            ? OldParse(pet, character, isLocalPlayer, allPets, (PetModel)modelId, out savePending)
            : NewParse(pet, character, isLocalPlayer, allPets, (PetModel)modelId);
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
                petSet = SetScale(pet, userData, petName);
            }
            // Specific Pet for General Character
            if (userData.Generic && userData.PetID == modelType)
            {
                petSet = SetScale(pet, userData, petName);
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
            userData.HomeWorld = character->HomeWorld;
            config.PetData[i] = userData;
            savePending = true;
            // General Pet for Specific Character
            if (allPets.Exists(item => item.CharacterName.Equals(userData.CharacterName, ordinalComparison))
                && userData.PetID is PetModel.AllPets)
            {
                petSet = SetScale(pet, userData, petName);
            }
            // Specific Pet for Specific Character
            if (userData.CharacterName.Equals(character->NameString, ordinalComparison) && userData.PetID == modelType)
            {
                petSet = SetScale(pet, userData, petName);
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
                petSet = SetScale(pet, userData, petName);
            }
            // Specific Pet for General Character
            if (userData.PetID == modelType)
            {
                petSet = SetScale(pet, userData, petName);
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
                petSet = SetScale(pet, userData, petName);
            }
            // Specific Pet for Specific Character
            if (userData.PetID == modelType)
            {
                petSet = SetScale(pet, userData, petName);
            }
        }
        return petSet;
    }

    private unsafe void RefreshCache(string playerName, ulong contentId, uint entityId)
    {
        players.Clear();
        players.Enqueue((playerName, contentId));
        players.Enqueue((Others, OthersContendId));
        utilities.CachePlayerList(entityId, config.HomeWorld, players, BattleCharaSpan);
    }

    private unsafe bool SetScale(BattleChara* pet, in PetStruct userData, string petName)
    {
        if (petModelMap.ContainsValue(userData.PetID))
        {
            var scale = userData.PetSize switch
            {
                PetSize.SmallModelScale => petSizeMap[petName].smallScale,
                PetSize.MediumModelScale => petSizeMap[petName].mediumScale,
                PetSize.LargeModelScale => petSizeMap[petName].largeScale,
                _ => throw new ArgumentException("Invalid PetSize", paramName: userData.PetSize.ToString()),
            };
            utilities.SetScale(pet, scale);
            return true;
        }
        if (otherPetModelMap.ContainsValue(userData.PetID))
        {
            if (userData.PetSize is not PetSize.Custom)
            {
                return false;
            }
            var scale = Math.Max(userData.AltPetSize, Utilities.GetMinSize(userData.PetID));
            utilities.SetScale(pet, scale);
        }
        return true;
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
