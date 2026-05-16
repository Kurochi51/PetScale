using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using PetScale.Structs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace PetScale.IPC;

public class IPCProvider
{
    public const string APINamespace = "PetScale";
    public IReadOnlyList<PetStruct> localPlayerData = [];
    private readonly Configuration config;
    private readonly IPlayerState playerState;
    internal static Action? attemptDataRefresh;
    private readonly PetScale plugin;
    private readonly IPluginLog log;

    
    /// <summary>
    /// Collects the local user's configured pets, to be distributed alongside other character data upon sync via <see cref="setPlayerData"/>.
    /// </summary>
    private readonly ICallGateProvider<IReadOnlyList<PetStruct>> getPlayerData;

    /// <summary>
    /// Adds newly synced target to the list of ipc targets to scale, needs the data gathered from <see cref="getPlayerData"/>.
    /// </summary>
    private readonly ICallGateProvider<uint, IReadOnlyList<PetStruct>, object?> setPlayerData;

    /// <summary>
    /// Call with 0 to remove all, whenever the local user gets disconnected from sync, otherwise specific synced user that isn't received by the local user
    /// </summary>
    private readonly ICallGateProvider<uint, object?> removePlayerData;

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
    public IReadOnlyList<PetStruct> GetPlayerData()
    {
        RefreshPlayerData();
        return localPlayerData;
    }

    public void SetPlayerData(uint playerEntityId, IReadOnlyList<PetStruct> playerData)
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
    public void RemovePlayerData(uint playerEntityId)
    {
        if (playerEntityId > 0)
        {
            plugin.ipcPlayers.TryRemove(playerEntityId, out _);
            return;
        }
        plugin.ipcPlayers.Clear();
    }

    internal void Dispose()
    {
        getPlayerData.UnregisterFunc();
        setPlayerData.UnregisterAction();
        removePlayerData.UnregisterAction();
    }
}
