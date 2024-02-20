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

public sealed class SummonScale : IDalamudPlugin
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
    private readonly Dictionary<Pointer<BattleChara>, Pointer<Character>> summonDictionary = [];
    private readonly Dictionary<string, (float smallScale, float mediumScale, float largeScale)> petDictionary = [];
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

    public SummonScale(DalamudPluginInterface _pluginInterface,
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
            HelpMessage = "Open or close Summon Scale's config window.",
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
        ConfigWindow.summonMap.Add(nameof(SummonModel.AllSummons), SummonModel.AllSummons);
        foreach (var pet in petSheet)
        {
            if (!Enum.TryParse(typeof(SummonRow), pet.RowId.ToString(cultureInfo), out var row) || row is not SummonRow)
            {
                continue;
            }
            var scales = (pet.Unknown9 / 100f, pet.Unknown10 / 100f, pet.Unknown11 / 100f);
            if (scales.Item1 >= 1 || scales.Item2 >= 1)
            {
                continue;
            }
            petDictionary.Add(pet.Name, scales);
            var name = row.ToString();
            if (name.IsNullOrWhitespace())
            {
                log.Debug("Invalid SummonRows {name}", row);
                continue;
            }
            if (!Enum.TryParse(typeof(SummonModel), name, out var model) || model is not SummonModel modelValue)
            {
                log.Debug("SummonRows {name} couldn't map onto SummonModel", name);
                continue;
            }
            ConfigWindow.summonMap.Add(pet.Name, modelValue);
        }
        foreach (var entry in petDictionary)
        {
            log.Debug("{pet} with scales {small} - {meduim} - {large}", entry.Key, entry.Value.smallScale, entry.Value.mediumScale, entry.Value.largeScale);
        }
        foreach (var entry in ConfigWindow.summonMap)
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
            foreach (var entry in config.SummonData.Select(item => item.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase))
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
        summonDictionary.Clear();
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
            var summonName = MemoryHelper.ReadStringNullTerminated((nint)chara.Value->Character.GameObject.GetName());
            if (summonName.IsNullOrWhitespace() || !petDictionary.ContainsKey(summonName))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(SummonModel), chara.Value->Character.CharacterData.ModelCharaId.ToString(cultureInfo), out _))
            {
                continue;
            }
#if DEBUG
            DevWindow.AddObjects(&chara.Value->Character.GameObject);
#endif
            summonDictionary.TryAdd(chara.Value, value: null);
        }
        foreach (var chara in BattleCharaSpan)
        {
            if (chara.Value is null || &chara.Value->Character is null)
            {
                continue;
            }
            if (chara.Value->Character.GameObject.ObjectKind is (byte)ObjectKind.Player && chara.Value->Character.GameObject.ObjectID is not 0xE0000000)
            {
                foreach (var possiblePair in summonDictionary.Keys.Where(summon => summon.Value->Character.GameObject.OwnerID == chara.Value->Character.GameObject.ObjectID))
                {
                    summonDictionary[possiblePair] = &chara.Value->Character;
                }
            }
        }
    }

    private unsafe void ParseDictionary(PlayerCharacter player)
    {
        foreach (var pair in summonDictionary)
        {
            var summon = pair.Key.Value;
            var character = pair.Value.Value;
            if (character is null || summon is null)
            {
                continue;
            }
            var summonName = MemoryHelper.ReadStringNullTerminated((nint)summon->Character.GameObject.GetName());
            var characterName = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());
            if (characterName.IsNullOrWhitespace() || summonName.IsNullOrWhitespace())
            {
                continue;
            }
#if DEBUG
            DevWindow.Print(summonName + ": " + summon->Character.CharacterData.ModelCharaId + " owned by " + characterName);
#endif
            if (config.SummonData.Any(data => data.CharacterName.Equals(characterName, ordinalComparison) && (int)data.SummonID == summon->Character.CharacterData.ModelCharaId))
            {
                ParseStruct(summon, characterName, summonName, summon->Character.CharacterData.ModelCharaId);
                continue;
            }
            ParseGeneric(summon, characterName, summonName, summon->Character.CharacterData.ModelCharaId, character->GameObject.ObjectID == player.ObjectId);
        }
    }

    private unsafe void ParseGeneric(BattleChara* summon, string characterName, string summonName, int modelId, bool isLocalPlayer)
    {
        foreach (var userData in config.SummonData)
        {
            // redundant?
            if (userData.SummonID is not SummonModel.AllSummons)
            {
                continue;
            }
            if ((!userData.CharacterName.Equals(characterName, ordinalComparison) || !isLocalPlayer)
                && (!userData.CharacterName.Equals(Others, ordinalComparison) || isLocalPlayer))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(SummonModel), modelId.ToString(cultureInfo), out _))
            {
                continue;
            }
            SetScale(summon, userData, summonName);
        }
    }

    private unsafe void ParseStruct(BattleChara* summon, string characterName, string summonName, int modelId)
    {
        foreach (var userData in config.SummonData)
        {
            if (!characterName.Equals(userData.CharacterName, ordinalComparison))
            {
                continue;
            }
            if (!Enum.TryParse(typeof(SummonModel), modelId.ToString(cultureInfo), out var model)
                || model is not SummonModel modelType
                || modelType != userData.SummonID)
            {
                continue;
            }
            SetScale(summon, userData, summonName);
        }
    }

    private void RefreshCache(string playerName, uint playerObjectId)
    {
        players.Clear();
        players.Enqueue(playerName);
        players.Enqueue(Others);
        Utilities.CachePlayerList(playerObjectId, players, BattleCharaSpan);
    }

    private unsafe void SetScale(BattleChara* summon, SummonStruct userData, string summonName)
    {
        var scale = userData.SummonSize switch
        {
            SummonSize.SmallModelScale => petDictionary[summonName].smallScale,
            SummonSize.MediumModelScale => petDictionary[summonName].mediumScale,
            SummonSize.LargeModelScale => petDictionary[summonName].largeScale,
            _ => throw new ArgumentException("Invalid SummonSize", paramName: userData.SummonSize.ToString()),
        };
        utilities.SetScale(summon, scale);
    }

#if DEBUG
    private unsafe void DevWindowThings()
    {
        DevWindow.IsOpen = true;
        DevWindow.Print("Actor pair count: " + summonDictionary.Count.ToString(cultureInfo));
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
