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
    public const int MajorVersion = 1;
    public const int MinorVersion = 1;

    internal IReadOnlyList<PetStruct> localPlayerData = [];
    internal IReadOnlyList<PetStruct> cachedLocalPlayerData = [];
    private readonly Configuration config;
    private readonly IPlayerState playerState;
    internal static Action? attemptDataRefresh;
    private readonly PetScale plugin;
    private readonly IPluginLog log;
    private readonly IFramework framework;

    public readonly ICallGateProvider<(int, int)> ApiVersion;

    /// <summary>
    /// Collect PetScale local user data for IPC usage.
    /// </summary>
    public readonly ICallGateProvider<string> getPlayerData;

    /// <summary>
    /// Send PetScale user data back. 
    /// </summary>
    public readonly ICallGateProvider<string?, object> sendPlayerData;

    public IPCProvider(Configuration _config, IPlayerState _localPlayer, IDalamudPluginInterface _pluginInterface, PetScale _plugin, IPluginLog _log, IFramework _framework)
    {
        config = _config;
        playerState = _localPlayer;
        plugin = _plugin;
        log = _log;
        framework = _framework;

        attemptDataRefresh = RefreshPlayerData;
        ApiVersion = _pluginInterface.GetIpcProvider<(int, int)>($"{APINamespace}.{nameof(ApiVersion)}");
        ApiVersion.RegisterFunc(() => (MajorVersion, MinorVersion));
        getPlayerData = _pluginInterface.GetIpcProvider<string>($"{APINamespace}.{nameof(GetPlayerData)}");
        getPlayerData.RegisterFunc(GetPlayerData);
        sendPlayerData = _pluginInterface.GetIpcProvider<string?, object>($"{APINamespace}.{nameof(SendPlayerData)}");
        sendPlayerData.RegisterAction(SendPlayerData);
    }

    internal void SendPlayerData(string? userData)
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
        _ = framework.RunOnFrameworkThread(() => plugin.ApplyIPCPlayer(fullData));
    }

    internal void RefreshPlayerData()
    {
        if (!playerState.IsLoaded || playerState.EntityId == 0xE0000000)
        {
            return;
        }
        localPlayerData = [.. config.PetData.Where(player => player.ContentId == playerState.ContentId)];
        // first time assignment
        if (cachedLocalPlayerData.Count is 0)
        {
            cachedLocalPlayerData = localPlayerData;
        }
        // if sync isn't blocking, update cachedLocalPlayerData
    }

    internal string GetPlayerData()
    {
        RefreshPlayerData();
        var jsonSettings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All,
        };
        return JsonConvert.SerializeObject(cachedLocalPlayerData);
    }

    internal void Dispose()
    {
        getPlayerData.UnregisterFunc();
        sendPlayerData.UnregisterAction();
        ApiVersion.UnregisterFunc();
    }
}
