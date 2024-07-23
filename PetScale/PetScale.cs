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
using FFXIVClientStructs.FFXIV.Common.Math;

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
    public static bool DrawAvailable { get; private set; }

    public static Dictionary<PetRow, PetModel> presetPetModelMap { get; } = new()
    {
        { PetRow.Bahamut,       PetModel.Bahamut        },
        { PetRow.Phoenix,       PetModel.Phoenix        },
        { PetRow.Ifrit,         PetModel.Ifrit          },
        { PetRow.Titan,         PetModel.Titan          },
        { PetRow.Garuda,        PetModel.Garuda         },
        { PetRow.SolarBahamut,  PetModel.SolarBahamut   },
    };

    public static Dictionary<PetRow, PetModel> customPetModelMap { get; } = new()
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
    public bool requestedCache { get; set; } = true;
    public int lastIndexOfOthers { get; set; } = -1;
    private ConfigWindow ConfigWindow { get; init; }
#if DEBUG
    private DevWindow DevWindow { get; init; }
    private readonly Dictionary<string, (PetModel?, int, Vector3 scale)> petModelDic = [];
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
                petSizeMap.Add(pet.Name, scales);
                ConfigWindow.presetPetMap.Add(pet.Name, presetPetModelMap[(PetRow)pet.RowId]);
            }
        }
        // List of pet rows sorted by SCH pets, MCH pets, DRK pet then in ascending order
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
            var currentRow = petSheet.GetRow(row);
            if (currentRow is null)
            {
                continue;
            }
            if (!Enum.IsDefined((PetRow)currentRow.RowId) || !customPetModelMap.ContainsKey((PetRow)currentRow.RowId))
            {
                continue;
            }
            if (currentRow.NonCombatSummon || currentRow.Unknown15 || sortedRows.Contains(currentRow.RowId))
            {
                ConfigWindow.customPetMap.Add(currentRow.Name, customPetModelMap[(PetRow)currentRow.RowId]);
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
                petModelDic.Add(petName, (petModel, chara.Value->Character.CharacterData.ModelCharaId, Vector3.Zero));
            }
#endif
            if (!Enum.IsDefined(typeof(PetModel), chara.Value->Character.CharacterData.ModelCharaId))
            {
                continue;
            }
#if DEBUG
            //DevWindow.AddObjects(objectTable.CreateObjectReference((nint)(&chara.Value->Character)));
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
            var drawModel = pet->Character.GetDrawObject();
            if (drawModel is not null && petModelDic.TryGetValue(pet->NameString, out var current))
            {
                current.scale = drawModel->Scale;
                petModelDic[pet->NameString] = current;
            }
#endif
            if (config.FairyState is not PetState.Off && (PetModel)pet->Character.CharacterData.ModelCharaId is PetModel.Eos or PetModel.Selene)
            {
                switch (config.FairyState)
                {
                    case PetState.Self when character->GameObject.EntityId == playerEntityId:
                    case PetState.Others when character->GameObject.EntityId != playerEntityId:
                    case PetState.All:
                        {
                            utilities.SetScale(pet, 1.5f);
                            activePetDictionary[pair.Key] = (pair.Value.character, true);
                            if (config.PetData.Any(
                                item => (item.ContentId == character->ContentId || item.CharacterName.Equals(character->NameString, ordinalComparison))
                                && item.PetID == (PetModel)pet->Character.CharacterData.ModelCharaId))
                            {
                                break;
                            }
                            continue;
                        }
                    default:
                        break;
                }
            }
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
            if (userData.PetID is PetModel.AllPets && allPets.Exists(item => item.ContentId == userData.ContentId))
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

    private unsafe void RefreshCache(string playerName, ulong contentId, uint entityId, ushort homeWorld)
    {
        players.Clear();
        players.Enqueue((playerName, contentId, homeWorld));
        players.Enqueue((Others, OthersContendId, OthersHomeWorld));
        utilities.CachePlayerList(entityId, config.HomeWorld, players, BattleCharaSpan);
    }

    private unsafe void SetScale(BattleChara* pet, in PetStruct userData, string petName)
    {
        /*var scale = userData.PetSize switch
        {
            PetSize.SmallModelScale => petSizeMap[petName].smallScale,
            PetSize.MediumModelScale => petSizeMap[petName].mediumScale,
            PetSize.LargeModelScale => petSizeMap[petName].largeScale,
            PetSize.Custom => Math.Max(userData.AltPetSize, Utilities.GetMinSize(userData.PetID)),
            _ => throw new ArgumentException("Invalid PetSize", paramName: userData.PetSize.ToString()),
        };
        utilities.SetScale(pet, scale);*/
        if (presetPetModelMap.ContainsValue(userData.PetID))
        {
            var scale = userData.PetSize switch
            {
                PetSize.SmallModelScale => petSizeMap[petName].smallScale,
                PetSize.MediumModelScale => petSizeMap[petName].mediumScale,
                PetSize.LargeModelScale => petSizeMap[petName].largeScale,
                _ => throw new ArgumentException("Invalid PetSize", paramName: userData.PetSize.ToString()),
            };
            utilities.SetScale(pet, scale);
            return;
        }
        if (customPetModelMap.ContainsValue(userData.PetID) && userData.PetSize is PetSize.Custom)
        {
            var scale = Math.Max(userData.AltPetSize, Utilities.GetMinSize(userData.PetID));
            utilities.SetScale(pet, scale);
        }
    }

#if DEBUG
    private unsafe void DevWindowThings()
    {
        DevWindow.IsOpen = true;
        DevWindow.Print("Actor pair count: " + activePetDictionary.Count.ToString());
        foreach (var entry in petModelDic)
        {
            var petModel = entry.Value.Item1 is not null ? "True - " + entry.Value.Item1.ToString() : "False";
            DevWindow.Print($"Pet: {entry.Key} - ModelCharaId: {entry.Value.Item2} - Scale: {entry.Value.scale} - IsPetModel: {petModel}");
        }
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

        ConfigWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
    }
}
