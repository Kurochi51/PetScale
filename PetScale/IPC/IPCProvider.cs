using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;

using Dalamud.Plugin.Services;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility;
using Dalamud.Plugin;
using PetScale.Structs;

namespace PetScale.IPC;

public class IPCProvider
{
    public const string APINamespace = "PetScale";
    internal IReadOnlyList<PetStruct> localPlayerData = [];
    private readonly Configuration config;
    private readonly IPlayerState playerState;
    internal static Action? attemptDataRefresh;
    private readonly PetScale plugin;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    internal readonly ConcurrentQueue<uint> removedIPCPlayers = [];


    /// <summary>
    /// Collect PetScale local user data for IPC usage.
    /// </summary>
    public readonly ICallGateProvider<uint, string> getPlayerData;

    /// <summary>
    /// Send PetScale user data back. 
    /// </summary>
    public readonly ICallGateProvider<uint, string?, object> sendPlayerData;

    /// <summary>
    /// Apply PetScale data sent via <see cref="sendPlayerData"/> for the provided entityId on the next framework update.
    /// </summary>
    public readonly ICallGateProvider<uint, object> applyScaleData;


    public IPCProvider(Configuration _config, IPlayerState _localPlayer, IDalamudPluginInterface _pluginInterface, PetScale _plugin, IPluginLog _log, IFramework _framework)
    {
        config = _config;
        playerState = _localPlayer;
        plugin = _plugin;
        log = _log;
        framework = _framework;

        attemptDataRefresh = RefreshPlayerData;
        getPlayerData = _pluginInterface.GetIpcProvider<uint, string>($"{APINamespace}.{nameof(GetPlayerData)}");
        getPlayerData.RegisterFunc(GetPlayerData);
        sendPlayerData = _pluginInterface.GetIpcProvider<uint, string?, object>($"{APINamespace}.{nameof(SendPlayerData)}");
        sendPlayerData.RegisterAction(SendPlayerData);
        applyScaleData = _pluginInterface.GetIpcProvider<uint, object>($"{APINamespace}.{nameof(ApplyScaleData)}");
        applyScaleData.RegisterAction(ApplyScaleData);
    }

    internal void ApplyScaleData(uint entityId)
    {
        var syncData = plugin.ipcPlayers[entityId];
        _ = framework.RunOnFrameworkThread(() => plugin.ApplyIPCPlayer(entityId, syncData));
    }

    internal void SendPlayerData(uint entityId, string? userData)
    {
        if (userData.IsNullOrWhitespace())
        {
            return;
        }
        var fullData = JsonConvert.DeserializeObject<IReadOnlyList<PetStruct>>(userData);
        if (fullData is null)
        {
            return;
        }
        plugin.ipcUsers.TryAdd(entityId, fullData);
    }

    internal void RefreshPlayerData()
    {
        if (playerState.IsLoaded && playerState.EntityId != 0xE0000000)
        {
            localPlayerData = [.. config.PetData.Where(player => player.ContentId == playerState.ContentId)];
        }
    }

    internal string GetPlayerData(uint entityId)
    {
        if (playerState.EntityId != entityId)
        {
            return string.Empty;
        }
        RefreshPlayerData();
        return JsonConvert.SerializeObject(localPlayerData);
    }

    internal void Dispose()
    {
        getPlayerData.UnregisterFunc();
        sendPlayerData.UnregisterAction();
        applyScaleData.UnregisterAction();
    }
}
