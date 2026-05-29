using System;
using System.Numerics;
using System.Collections.Generic;

using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Ipc;
using PetScale.Helpers;
using PetScale.IPC;

namespace PetScale.Windows;

public sealed class DevWindow : Window, IDisposable
{
    private static readonly List<string> PrintLines = [];
    private static readonly Queue<IGameObject> RedrawObjects = new();
#pragma warning disable S4487
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPCProvider ipcProvider;
#pragma warning restore
    private bool redrawWanted = false;

    private Action tabToDraw;
    private string cachedIPCData = string.Empty;

    private ICallGateSubscriber<string, object> onPlayerData;

    public DevWindow(IPluginLog _pluginLog, IDalamudPluginInterface _pluginInterface, IPCProvider _ipcProvider) : base("DevWindow - " + nameof(PetScale))
    {
        pluginInterface = _pluginInterface;
        log = _pluginLog;
        ipcProvider = _ipcProvider;
        tabToDraw = DrawModelTester;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        onPlayerData = _pluginInterface.GetIpcSubscriber<string, object>("PetScale.OnPlayerDataChanged");
        onPlayerData.Subscribe(OnIPCDataChanged);
    }

    public void Dispose()
    {
        onPlayerData.Unsubscribe(OnIPCDataChanged);
    }

    private void OnIPCDataChanged(string ipcData)
    {
        cachedIPCData = ipcData;
    }

    public override unsafe void Draw()
    {
        if (ImGui.BeginTabBar("###PETSCALETABBAR"))
        {
            if (ImGui.TabItemButton("Model Tester"))
            {
                tabToDraw = DrawModelTester;
            }
            if (ImGui.TabItemButton("IPC Tester"))
            {
                tabToDraw = DrawIPCTester;
            }

            ImGui.EndTabBar();
        }

        tabToDraw.Invoke();
    }

    public void DrawModelTester()
    {
        if (redrawWanted)
        {
            for (var i = 0; i < RedrawObjects.Count; i++)
            {
                var actor = RedrawObjects.Dequeue();
                Utilities.ToggleVisibility(actor);
            }
            redrawWanted = false;
        }
        if (ImGui.Button("Redraw Model"))
        {
            foreach (var actor in RedrawObjects)
            {
                Utilities.ToggleVisibility(actor);
            }
            redrawWanted = true;
        }
        foreach (var line in PrintLines)
        {
            ImGui.TextUnformatted(line);
        }
        PrintLines.Clear();
    }

    public void DrawIPCTester()
    {
        ImGui.Text("CachedIPCData: " + cachedIPCData);

        if (ImGui.Button("Ask Local Data"))
        {
            cachedIPCData = ipcProvider.GetPlayerData();
        }

        if (ImGui.Button("Clear Cache Data"))
        {
            cachedIPCData = string.Empty;
        }
    }

    public static void Print(string text)
    {
        PrintLines.Add(text);
    }

    public static unsafe void AddObjects(IGameObject? actor)
    {
        if (actor is null)
        {
            return;
        }
        if (RedrawObjects.Contains(actor))
        {
            return;
        }
        RedrawObjects.Enqueue(actor);
    }

    public static void Separator()
    {
        PrintLines.Add("--------------------------");
    }
}
