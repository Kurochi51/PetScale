using System.Numerics;
using System.Collections.Generic;

using ImGuiNET;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Objects.Types;
using PetScale.Helpers;

namespace PetScale.Windows;

public class DevWindow : Window
{
    private static readonly List<string> PrintLines = [];
    private static readonly Queue<IGameObject> RedrawObjects = new();
#pragma warning disable S4487
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
#pragma warning restore
    private bool redrawWanted = false;

    public DevWindow(IPluginLog _pluginLog, IDalamudPluginInterface _pluginInterface) : base("DevWindow - " + nameof(PetScale))
    {
        pluginInterface = _pluginInterface;
        log = _pluginLog;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override unsafe void Draw()
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
