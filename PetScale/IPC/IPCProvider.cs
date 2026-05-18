using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Dalamud.Plugin.Services;
using Dalamud.Plugin.Ipc;
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
    internal readonly ConcurrentQueue<uint> removedIPCPlayers = [];


    /// <summary>
    /// Collects the local user's configured pets, to be distributed alongside other character data upon sync via <see cref="setPlayerData"/>.
    /// </summary>
    public readonly ICallGateProvider<IReadOnlyList<PetStruct>> getPlayerData;

    /// <summary>
    /// Adds newly synced target to the list of ipc targets to scale, needs the data gathered from <see cref="getPlayerData"/>.
    /// </summary>
    public readonly ICallGateProvider<uint, IReadOnlyList<PetStruct>, object?> setPlayerData;

    /// <summary>
    /// Call with 0 to remove all, whenever the local user gets disconnected from sync, otherwise specific synced user that isn't received by the local user
    /// </summary>
    public readonly ICallGateProvider<uint, object?> removePlayerData;

    public IPCProvider(Configuration _config, IPlayerState _localPlayer, IDalamudPluginInterface _pluginInterface, PetScale _plugin, IPluginLog _log)
    {
        config = _config;
        playerState = _localPlayer;
        plugin = _plugin;
        log = _log;

        attemptDataRefresh = RefreshPlayerData;
        getPlayerData = _pluginInterface.GetIpcProvider<IReadOnlyList<PetStruct>>($"{APINamespace}.{nameof(GetPlayerData)}");
        getPlayerData.RegisterFunc(GetPlayerData);
        setPlayerData = _pluginInterface.GetIpcProvider<uint, IReadOnlyList<PetStruct>, object?>($"{APINamespace}.{nameof(SetPlayerData)}");
        setPlayerData.RegisterAction(SetPlayerData);
        removePlayerData = _pluginInterface.GetIpcProvider<uint, object?>($"{APINamespace}.{nameof(RemovePlayerData)}");
        removePlayerData.RegisterAction(RemovePlayerData);
    }

    internal void RefreshPlayerData()
    {
        if (playerState.IsLoaded && playerState.EntityId != 0xE0000000)
        {
            localPlayerData = [.. config.PetData.Where(player => player.ContentId == playerState.ContentId)];
        }
    }
    internal IReadOnlyList<PetStruct> GetPlayerData()
    {
        RefreshPlayerData();
        return localPlayerData;
    }

    internal void SetPlayerData(uint playerEntityId, IReadOnlyList<PetStruct> playerData)
    {
        try
        {
            plugin.ipcPlayers.TryAdd(playerEntityId, playerData);
        }
        catch (Exception ex)
        {
            log.Error($"{nameof(plugin.ipcPlayers)} couldn't be accessed by {setPlayerData.GetContext()?.SourcePlugin?.Name}\n{ex.Message}");
        }
    }

    // bad, handle removal on framework
    internal void RemovePlayerData(uint playerEntityId)
    {
        removedIPCPlayers.Enqueue(playerEntityId);
    }

    internal void Dispose()
    {
        getPlayerData.UnregisterFunc();
        setPlayerData.UnregisterAction();
        removePlayerData.UnregisterAction();
    }
}
