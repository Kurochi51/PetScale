using System;
using System.Linq;
using System.Numerics;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Frozen;
using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Utility;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
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
    private readonly IGameConfig gameConfig;
    public readonly IClientState clientState;

    private readonly StringComparison ordinalComparison = StringComparison.Ordinal;
    private readonly Dictionary<Pointer<BattleChara>, (Pointer<Character> character, bool petSet)> activePetDictionary = [];
    private readonly Dictionary<int, (uint characterEiD, uint petEiD, bool petSet)> secondaryActivePetDictionary = [];
    private readonly Dictionary<string, (float smallScale, float mediumScale, float largeScale)> petSizeMap = new(StringComparer.OrdinalIgnoreCase);
    public static IDictionary<PetModel, PetSize> vanillaPetSizeMap { get; } = new Dictionary<PetModel, PetSize>();
    private readonly Stopwatch stopwatch = new();
    private readonly double dictionaryExpirationTime = TimeSpan.FromMilliseconds(500).TotalMilliseconds;
    public static FrozenSet<PetModel> petModelSet { get; } = new HashSet<PetModel>((PetModel[])Enum.GetValues(typeof(PetModel))).ToFrozenSet();
    public static bool DrawAvailable { get; private set; }

    internal static Dictionary<PetRow, PetModel> presetPetModelMap { get; } = new()
    {
        { PetRow.Bahamut,       PetModel.Bahamut        },
        { PetRow.Phoenix,       PetModel.Phoenix        },
        { PetRow.Ifrit,         PetModel.Ifrit          },
        { PetRow.Titan,         PetModel.Titan          },
        { PetRow.Garuda,        PetModel.Garuda         },
        { PetRow.SolarBahamut,  PetModel.SolarBahamut   },
    };

    internal static Dictionary<PetRow, PetModel> customPetModelMap { get; } = new()
    {
        { PetRow.Eos,               PetModel.Eos                },
        { PetRow.Selene,            PetModel.Selene             },
        { PetRow.Seraph,            PetModel.Seraph             },
        { PetRow.Rook,              PetModel.Rook               },
        { PetRow.AutomatonQueen,    PetModel.AutomatonQueen     },
        { PetRow.Esteem,            PetModel.Esteem             },

        { PetRow.Carbuncle,         PetModel.Carbuncle          },
        { PetRow.RubyCarbuncle,     PetModel.RubyCarbuncle      },
        { PetRow.TopazCarbuncle,    PetModel.TopazCarbuncle     },
        { PetRow.EmeraldCarbuncle,  PetModel.EmeraldCarbuncle   },
        { PetRow.IfritEgi,          PetModel.IfritEgi           },
        { PetRow.TitanEgi,          PetModel.TitanEgi           },
        { PetRow.GarudaEgi,         PetModel.GarudaEgi          },
    };

    public static WindowSystem WindowSystem { get; } = new("PetScale");
    public Queue<(string Name, ulong ContentId, ushort HomeWorld)> players { get; } = new();
    internal Dictionary<ulong, PetStruct> removedPlayers { get; } = [];
    public bool requestedCache { get; set; } = true;
    public bool queueFairyForRemoval { get; set; }
    public int lastIndexOfOthers { get; set; } = -1;
    internal List<Pointer<BattleChara>> fairies { get; } = [];
    private ConfigWindow ConfigWindow { get; init; }
#if DEBUG
    private DevWindow DevWindow { get; init; }
    private readonly Dictionary<string, (PetModel?, int, Vector3 scale, Vector3 scale2)> petModelDic = [];
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
        IObjectTable _objectTable,
        IGameConfig _gameConfig)
    {
        pluginInterface = _pluginInterface;
        commandManager = _commandManager;
        framework = _framework;
        log = _log;
        clientState = _clientState;
        gameConfig = _gameConfig;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        utilities = new Utilities(_dataManger, log, clientState.ClientLanguage);

        ConfigWindow = new ConfigWindow(this, config, pluginInterface, log, _notificationManager, utilities);
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

        pluginInterface.UiBuilder.Draw += UiDraw;
        pluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
        clientState.TerritoryChanged += TerritoryChanged;
        clientState.Login += SetStopwatch;
        clientState.Logout += SetStopwatch;
        framework.Update += OnFrameworkUpdate;
        stopwatch.Start();

        _ = Task.Run(InitSheet);
        ConfigWindow.ProcessPetData(save: true);
        QueueOnlyExistingData();
        if (clientState.IsLoggedIn)
        {
            Utilities.GetPetSizes(gameConfig, vanillaPetSizeMap);
        }
    }

    private void SetStopwatch(int _type, int _code) => SetStopwatch();

    private void SetStopwatch()
    {
        if (clientState.IsLoggedIn)
        {
            playerName = clientState.LocalPlayer?.Name.TextValue;
            Utilities.GetPetSizes(gameConfig, vanillaPetSizeMap);
            stopwatch.Start();
            return;
        }
        secondaryActivePetDictionary.Clear();
        config.HomeWorld = 0;
        QueueOnlyExistingData();
        stopwatch.Stop();
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
            if (config.HomeWorld is not 0 && entry.HomeWorld is not 1 && config.HomeWorld != entry.HomeWorld && !utilities.GetHomeWorldName(entry.HomeWorld).IsNullOrWhitespace())
            {
                world = "@" + utilities.GetHomeWorldName(entry.HomeWorld);
            }
            players.Enqueue((entry.CharacterName + world, entry.ContentId, entry.HomeWorld));
        }
    }

    private void InitSheet()
    {
        var petSheet = utilities.GetSheet<Pet>(clientState.ClientLanguage);
        if (petSheet is null)
        {
            return;
        }
        ConfigWindow.presetPetMap.Add(nameof(PetModel.AllPets), PetModel.AllPets);
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
            if (presetPetModelMap.ContainsKey((PetRow)pet.RowId))
            {
                petSizeMap.Add(pet.Name.GetText(), scales);
                ConfigWindow.presetPetMap.Add(pet.Name.GetText(), presetPetModelMap[(PetRow)pet.RowId]);
            }
        }
        // List of pet rows sorted by SCH pets, MCH pets, DRK pet, sub-90 SMN pets then in ascending order
        List<uint> sortedRows =
        [
            (uint)PetRow.Eos,
            (uint)PetRow.Selene,
            (uint)PetRow.Seraph,
            (uint)PetRow.Rook,
            (uint)PetRow.AutomatonQueen,
            (uint)PetRow.Esteem,
            (uint)PetRow.Carbuncle,
            (uint)PetRow.RubyCarbuncle,
            (uint)PetRow.TopazCarbuncle,
            (uint)PetRow.EmeraldCarbuncle,
            (uint)PetRow.IfritEgi,
            (uint)PetRow.TitanEgi,
            (uint)PetRow.GarudaEgi,
        ];
        foreach (var row in sortedRows)
        {
            var currentRow = petSheet.GetRowOrDefault(row);
            if (currentRow is null)
            {
                continue;
            }
            if (!Enum.IsDefined((PetRow)currentRow.Value.RowId) || !sortedRows.Contains(currentRow.Value.RowId))
            {
                continue;
            }
            ConfigWindow.customPetMap.Add(currentRow.Value.Name.GetText(), customPetModelMap[(PetRow)currentRow.Value.RowId]);
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
        if (clientState is not { LocalPlayer: { } player } || !clientState.IsLoggedIn)
        {
            return;
        }
        if (config.HomeWorld is 0)
        {
            config.HomeWorld = (ushort)player.HomeWorld.Value.RowId;
        }
        unsafe
        {
            if (requestedCache)
            {
                playerName ??= player.Name.TextValue;
                RefreshCache(playerName, clientState.LocalContentId, player.EntityId, config.HomeWorld);
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
            secondaryActivePetDictionary.Clear();
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
            if (chara.Value->ObjectKind is not ObjectKind.BattleNpc || chara.Value->OwnerId is 0xE0000000)
            {
                continue;
            }
#if DEBUG
            var petName = chara.Value->NameString;
            if (!petModelDic.ContainsKey(petName))
            {
                PetModel? petModel = petModelSet.Contains((PetModel)chara.Value->ModelContainer.ModelCharaId) ? (PetModel)chara.Value->ModelContainer.ModelCharaId : null;
                petModelDic.Add(petName, (petModel, chara.Value->ModelContainer.ModelCharaId, Vector3.Zero, Vector3.Zero));
            }
#endif
            if (!petModelSet.Contains((PetModel)chara.Value->ModelContainer.ModelCharaId))
            {
                continue;
            }
#if DEBUG
            //DevWindow.AddObjects(objectTable.CreateObjectReference((nint)(&chara.Value->Character)));
#endif
            activePetDictionary.TryAdd(chara.Value, (null, false));
        }
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->ObjectKind is ObjectKind.Pc && chara.Value->EntityId is not 0xE0000000)
            {
                foreach (var possiblePair in activePetDictionary.Keys.Where(pet => pet.Value->OwnerId == chara.Value->EntityId))
                {
                    activePetDictionary[possiblePair] = ((Pointer<Character>)(&chara.Value->Character), false);
                }
            }
        }
        // Is there any point in doing this vs just eating the cost of going through the whole battlechara on every parse?
        // Does this also really solve the core issue of storing pointers? I guess they never actually get accessed outside of the initial frame,
        // and seems like a more desireable in-place replacement solution unless proven ineffective.
        foreach (var entry in activePetDictionary)
        {
            if (entry.Key.Value is null || entry.Value.character.Value is null)
            {
                continue;
            }
            var petEID = entry.Key.Value->EntityId;
            var charEID = entry.Value.character.Value->EntityId;
            var hash = petEID.GetHashCode() ^ ((charEID.GetHashCode() << 16) | (charEID.GetHashCode() >> (32 - 16)));
            secondaryActivePetDictionary.TryAdd(hash, (entry.Value.character.Value->EntityId, entry.Key.Value->EntityId, false));
        }
        activePetDictionary.Clear();
    }

    private unsafe void ParseDictionary(uint playerEntityId)
    {
        var allPets = config.PetData.Where(userData => userData.PetID is PetModel.AllPets).ToList();
        foreach (var entry in secondaryActivePetDictionary)
        {
            if (entry.Value.petSet)
            {
                continue;
            }
            var pet = CharacterManager.Instance()->LookupBattleCharaByEntityId(entry.Value.petEiD);
            var character = CharacterManager.Instance()->LookupBattleCharaByEntityId(entry.Value.characterEiD);
            if (pet is null || character is null)
            {
                continue;
            }
            if (character->NameString.IsNullOrWhitespace() || pet->NameString.IsNullOrWhitespace())
            {
                continue;
            }
            if (!Utilities.PetVisible(pet))
            {
                continue;
            }
            if (queueFairyForRemoval)
            {
                CheckFairies(pet);
            }

            if (config.FairyState is not PetState.Off && (PetModel)pet->ModelContainer.ModelCharaId is PetModel.Eos or PetModel.Selene)
            {
                switch (config.FairyState)
                {
                    case PetState.Self when character->EntityId == playerEntityId:
                    case PetState.Others when character->EntityId != playerEntityId:
                    case PetState.All:
                    {
                        Utilities.SetScale(pet, 1.5f);
                        secondaryActivePetDictionary[entry.Key] = (entry.Value.characterEiD, entry.Value.petEiD, true);
                        if (!fairies.Contains(pet))
                        {
                            fairies.Add(pet);
                        }
                        if (config.PetData
                            .Any(item => item.PetID == (PetModel)pet->ModelContainer.ModelCharaId &&
                            (item.ContentId == character->ContentId
                            || (item.HomeWorld is not 0 && item.HomeWorld == character->HomeWorld && item.CharacterName.Equals(character->NameString, ordinalComparison))
                            || (item.HomeWorld is 0 && item.CharacterName.Equals(character->NameString, ordinalComparison)))))
                        {
                            break;
                        }
                        continue;
                    }
                    default:
                        break;
                }
            }
            if (ParseStruct(pet, &character->Character, pet->ModelContainer.ModelCharaId, character->EntityId == playerEntityId, allPets))
            {
                secondaryActivePetDictionary[entry.Key] = (entry.Value.characterEiD, entry.Value.petEiD, true);
            }
        }
        if (removedPlayers.Count > 0)
        {
            var tempPetDictionary = secondaryActivePetDictionary
                .ToDictionary(entry => entry.Key, entry => (entry.Value.characterEiD, entry.Value.petEiD));
            Utilities.CheckPetRemoval(removedPlayers, tempPetDictionary);
            removedPlayers.Clear();
        }
    }

    private unsafe bool ParseStruct(BattleChara* pet, Character* character, int modelId, bool isLocalPlayer, List<PetStruct> allPets)
    {
        if (!petModelSet.Contains((PetModel)modelId))
        {
            return false;
        }
        var savePending = false;
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
            if (userData.PetID == modelType)
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
            userData.UpdateData(character->HomeWorld, character->ContentId);
            config.PetData[i] = userData;
            savePending = true;
            // General Pet for Specific Character
            if (allPets.Exists(item => item.CharacterName.Equals(userData.CharacterName, ordinalComparison))
                && userData.PetID is PetModel.AllPets)
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

    private unsafe bool NewParse(BattleChara* pet, Character* character, bool isLocalPlayer, List<PetStruct> allPets, PetModel modelType)
    {
        var petSet = false;
        var petName = pet->NameString;
        var index = lastIndexOfOthers >= 0 ? lastIndexOfOthers : -1;
        for (var i = 0; i <= index; i++)
        {
            var userData = config.PetData[i];
            if (isLocalPlayer)
            {
                continue;
            }
            // General Pet for General Character
            if (userData.PetID is PetModel.AllPets && allPets.Exists(item => item.ContentId == userData.ContentId))
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
            if (userData.PetID is PetModel.AllPets && allPets.Exists(item => item.ContentId == userData.ContentId))
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

    private unsafe void RefreshCache(string playerName, ulong contentId, uint entityId, ushort homeWorld)
    {
        players.Clear();
        players.Enqueue((playerName, contentId, homeWorld));
        players.Enqueue((Others, OthersContendId, OthersHomeWorld));
        utilities.CachePlayerList(entityId, config.HomeWorld, players, BattleCharaSpan);
    }

    private unsafe bool SetScale(BattleChara* pet, in PetStruct userData, string petName)
    {
        if (presetPetModelMap.ContainsValue((PetModel)pet->ModelContainer.ModelCharaId))
        {
            var scale = userData.PetSize switch
            {
                PetSize.SmallModelScale => petSizeMap[petName].smallScale,
                PetSize.MediumModelScale => petSizeMap[petName].mediumScale,
                PetSize.LargeModelScale => petSizeMap[petName].largeScale,
                _ => throw new ArgumentException("Invalid PetSize", paramName: userData.PetSize.ToString()),
            };
            Utilities.SetScale(pet, scale);
            return true;
        }
        if (clientState.IsPvPExcludingDen)
        {
            return false;
        }
        if (customPetModelMap.ContainsValue(userData.PetID) && userData.PetSize is PetSize.Custom)
        {
            var scale = Math.Max(userData.AltPetSize, Utilities.GetDefaultScale(userData.PetID, userData.PetSize));
            Utilities.SetScale(pet, scale);
            return true;
        }
        return false;
    }

    private unsafe void CheckFairies(BattleChara* pet)
    {
        if ((PetModel)pet->ModelContainer.ModelCharaId is PetModel.Eos or PetModel.Selene)
        {
            List<Pointer<BattleChara>> removedFairies = [];
            foreach (var fairy in fairies)
            {
                if (fairy.Value is null)
                {
                    continue;
                }
                if (fairy.Value->ModelContainer.ModelCharaId != pet->ModelContainer.ModelCharaId)
                {
                    continue;
                }
                if (fairy.Value->OwnerId != pet->OwnerId)
                {
                    continue;
                }
                if (Utilities.ResetFairy(fairy.Value, 1.5f))
                {
                    removedFairies.Add(fairy.Value);
                }
            }
            foreach (var removedFairy in removedFairies)
            {
                fairies.Remove(removedFairy);
            }
            removedFairies.Clear();
            queueFairyForRemoval = fairies.Count > 0;
        }
    }

#if DEBUG
    private unsafe void DevWindowThings()
    {
        DevWindow.IsOpen = true;
        DevWindow.Print("Actor pair count: " + secondaryActivePetDictionary.Count.ToString());
        var tempDisplay = secondaryActivePetDictionary;
        foreach (var kvp in tempDisplay)
        {
            var charaId = kvp.Value.characterEiD;
            var petId = kvp.Value.petEiD;
            var charaName = CharacterManager.Instance()->LookupBattleCharaByEntityId(charaId);
            var petName = CharacterManager.Instance()->LookupBattleCharaByEntityId(petId);
            if (charaName is null || petName is null)
            {
                continue;
            }
            DevWindow.Print($"Pet: {petName->NameString} - Character: {charaName->NameString} - Scale: {petName->Scale} -  Set: {kvp.Value.petSet}");
        }
        /*DevWindow.Print("Actor pair count: " + activePetDictionary.Count.ToString());
        foreach (var entry in petModelDic)
        {
            var petModel = entry.Value.Item1 is not null ? "True - " + entry.Value.Item1.ToString() : "False";
            DevWindow.Print($"Pet: {entry.Key} - ModelCharaId: {entry.Value.Item2} - Scale: {entry.Value.scale} / {entry.Value.scale2} - IsPetModel: {petModel}");
        }*/
    }
#endif

    private static void UiDraw()
    {
        DrawAvailable = true;
        WindowSystem.Draw();
        DrawAvailable = false;
    }

    private void OnCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    private void UnsetPets()
    {
        if (!clientState.IsLoggedIn)
        {
            return;
        }
        secondaryActivePetDictionary.Clear();
        PopulateDictionary();
        Utilities.ResetPets(secondaryActivePetDictionary, config.PetData);
        secondaryActivePetDictionary.Clear();
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
        pluginInterface.UiBuilder.Draw -= UiDraw;

        UnsetPets();
        ConfigWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
    }
}
