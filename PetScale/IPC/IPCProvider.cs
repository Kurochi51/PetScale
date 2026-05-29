using System;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;

using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility;
using Dalamud.Plugin;

using PetScale.Enums;
using PetScale.Structs;
using PetScale.Helpers;

namespace PetScale.IPC;

public class IPCProvider
{
    public const string APINamespace = "PetScale";
    public const int MajorVersion = 1;
    public const int MinorVersion = 1;

    internal IReadOnlyList<PetStruct> localPlayerData = [];
    internal string cachedLocalPlayerData = string.Empty;
    private readonly Configuration config;
    private readonly IPlayerState playerState;
    internal static Action? attemptDataRefresh;
    private bool dataHasChanged;
    private bool ready;
    private readonly PetScale plugin;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;

    public readonly ICallGateProvider<(int, int)> ApiVersion;
    public readonly ICallGateProvider<object> Ready;
    public readonly ICallGateProvider<object> Disposing;
    public readonly ICallGateProvider<bool> Enabled;

    /// <summary>
    /// Collect PetScale local user data for IPC usage.
    /// </summary>
    public readonly ICallGateProvider<string> getPlayerData;

    /// <summary>
    /// On PetScale local player data changed.
    /// </summary>
    public readonly ICallGateProvider<string, object> playerDataChanged;

    /// <summary>
    /// Send PetScale user data back. 
    /// </summary>
    public readonly ICallGateProvider<string?, object> setPlayerData;

    /// <summary>
    /// Clear the player data for the given objectIndex
    /// </summary>
    public readonly ICallGateProvider<ushort, object> clearPlayerData;

    public IPCProvider(Configuration _config, IPlayerState _localPlayer, IDalamudPluginInterface _pluginInterface, PetScale _plugin, IPluginLog _log, IFramework _framework, IObjectTable _objectTable)
    {
        config = _config;
        playerState = _localPlayer;
        plugin = _plugin;
        log = _log;
        framework = _framework;
        objectTable = _objectTable;

        attemptDataRefresh = RefreshPlayerData;

        // NOTIFIERS:
        playerDataChanged = _pluginInterface.GetIpcProvider<string, object>($"{APINamespace}.OnPlayerDataChanged");
        Ready = _pluginInterface.GetIpcProvider<object>($"{APINamespace}.Ready");
        Disposing = _pluginInterface.GetIpcProvider<object>($"{APINamespace}.Disposing");

        // FUNCTIONS:
        ApiVersion = _pluginInterface.GetIpcProvider<(int, int)>($"{APINamespace}.ApiVersion");
        ApiVersion.RegisterFunc(() => (MajorVersion, MinorVersion));

        getPlayerData = _pluginInterface.GetIpcProvider<string>($"{APINamespace}.GetPlayerData");
        getPlayerData.RegisterFunc(GetPlayerData);

        Enabled = _pluginInterface.GetIpcProvider<bool>($"{APINamespace}.Enabled");
        Enabled.RegisterFunc(IsReady);

        // ACTIONS:
        setPlayerData = _pluginInterface.GetIpcProvider<string?, object>($"{APINamespace}.SetPlayerData");
        setPlayerData.RegisterAction(SetPlayerData);

        clearPlayerData = _pluginInterface.GetIpcProvider<ushort, object>($"{APINamespace}.ClearPlayerData");
        clearPlayerData.RegisterAction(ClearPlayerData);

        ready = true;

        Ready.SendMessage();
    }

    internal unsafe void ClearPlayerData(ushort objectIndex)
    {
        try
        {
            log.Verbose($"ClearPlayerData IPC: {objectIndex}");

            _ = framework.Run(() =>
            {
                if (objectTable.Length <= objectIndex)
                {
                    return;
                }

                if (objectTable[objectIndex] is not IPlayerCharacter pc)
                {
                    return;
                }

                // Clearing the data implies finding a pet owned by the player at said objectIndex,
                // checking that it's a supported pet, and blindly setting size defaults
                foreach (var chara in CharacterManager.Instance()->BattleCharas)
                {
                    if (chara.Value is null)
                    {
                        continue;
                    }
                    if (chara.Value->ObjectKind is not ObjectKind.BattleNpc || chara.Value->OwnerId != pc.EntityId)
                    {
                        continue;
                    }
                    var petToClean = chara.Value;
                    if (!PetScale.petModelSet.Contains((PetModel)petToClean->ModelContainer.ModelCharaId))
                    {
                        continue;
                    }
                    Utilities.SetScale(petToClean,
                        Utilities.GetDefaultScale((PetModel)petToClean->ModelContainer.ModelCharaId,
                        PetScale.vanillaPetSizeMap[(PetModel)petToClean->ModelContainer.ModelCharaId]));
                }
            });
        }
        catch (Exception e)
        {
            log.Error(e, "Error in clear IPC");
        }
    }

    internal bool IsReady()
    {
        return ready;
    }

    internal void OnSaveHasChanged()
    {
        RefreshPlayerData();

        if (!dataHasChanged)
        {
            return;
        }

        dataHasChanged = false;

        playerDataChanged.SendMessage(cachedLocalPlayerData);

        log.Verbose($"Sending IPC: {cachedLocalPlayerData}");
    }

    internal void SetPlayerData(string? userData)
    {
        if (userData.IsNullOrWhitespace())
        {
            return;
        }
        log.Verbose($"SetPlayerData IPC: {userData}");
        var fullData = JsonConvert.DeserializeObject<IReadOnlyList<PetStruct>>(userData);
        if (fullData is null)
        {
            return;
        }
        _ = framework.RunOnFrameworkThread(() => plugin.ApplyIPCPlayer(fullData));
    }

    internal void RefreshPlayerData()
    {
        dataHasChanged = false;

        if (!playerState.IsLoaded || playerState.EntityId == 0xE0000000)
        {
            return;
        }

        localPlayerData = [.. config.PetData.Where(player => player.ContentId == playerState.ContentId)];

        var newLocalPlayerData = JsonConvert.SerializeObject(localPlayerData, new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All,
        });

        if (string.Equals(newLocalPlayerData, cachedLocalPlayerData, StringComparison.Ordinal))
        {
            return;
        }

        dataHasChanged = true;

        cachedLocalPlayerData = newLocalPlayerData;
    }

    internal string GetPlayerData()
    {
        log.Verbose($"GetPlayerData IPC: {cachedLocalPlayerData}");
        return cachedLocalPlayerData;
    }

    internal void Dispose()
    {
        ready = false;

        Disposing.SendMessage();

        getPlayerData.UnregisterFunc();
        setPlayerData.UnregisterAction();
        ApiVersion.UnregisterFunc();
        Enabled.UnregisterFunc();
        clearPlayerData.UnregisterAction();
    }
}
