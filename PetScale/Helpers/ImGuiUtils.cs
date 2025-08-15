using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ManagedFontAtlas;

namespace PetScale.Helpers;

public static class ImGuiUtils
{
    private static bool IsDrawSafe => PetScale.DrawAvailable;

    public static float GetStyleWidth()
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(GetStyleWidth)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        return (ImGui.GetStyle().FramePadding.X * 2) + (ImGui.GetStyle().ItemSpacing.X * 2) + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().ItemInnerSpacing.X;
    }

    public static void CenterText(string text, float horizontalSpace, int magicNumber = 4)
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(CenterText)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        // Right now magicNumber is setup to match the amount of columns in the given table that this is used for
        // Whether that's actually the apropriate way of getting an accurate center, or I just stumbled upon it
        // is between god and me, and I forgot
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((horizontalSpace - ImGui.CalcTextSize(text).X - (GetStyleWidth() / magicNumber)) / 2));
        ImGui.TextUnformatted(text);
    }

    public static Vector2 IconButtonSize(IFontHandle fontHandle, string icon)
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(IconButtonSize)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        using (fontHandle.Push())
        {
            return new Vector2(ImGuiHelpers.GetButtonSize(icon).X, ImGui.GetFrameHeight());
        }
    }

    // widthOffset is a pain in the ass, at 100% you want 0, <100% you want 1 or more, >100% it entirely depends on whether you get a non-repeating divison or not... maybe?
    // also this entirely varies for each icon, so good luck aligning everything
    public static bool IconButton(IFontHandle fontHandle, string icon, string buttonIDLabel, float widthOffset = 0f)
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(IconButton)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        using (fontHandle.Push())
        {
            var cursorScreenPos = ImGui.GetCursorScreenPos();
            var frameHeight = ImGui.GetFrameHeight();
            var result = ImGui.Button("##" + buttonIDLabel, new Vector2(ImGuiHelpers.GetButtonSize(icon).X, frameHeight));
            var pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X + widthOffset,
                cursorScreenPos.Y + (frameHeight / 2f) - (ImGui.CalcTextSize(icon).Y / 2f));
            ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon);

            return result;
        }
    }

    public static void CenterNextWindow(Vector2 windowSize, ImGuiCond cond = ImGuiCond.None)
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(CenterNextWindow)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - (windowSize.X / 2), center.Y - (windowSize.Y / 2)), cond);
    }

    public static void CenterCursor(Vector2 windowSize, Vector2? offset = null)
    {
        if (!IsDrawSafe)
        {
            throw new InvalidOperationException($"{nameof(CenterCursor)} called outside {nameof(WindowSystem.Draw)} instance");
        }
        var center = windowSize / 2;
        if (offset.HasValue)
        {
            center -= offset.Value / 2;
        }
        ImGui.SetCursorPos(center);
    }
}
