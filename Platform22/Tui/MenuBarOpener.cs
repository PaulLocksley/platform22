namespace Platform22.Tui;

using System.Reflection;
using Terminal.Gui;

/// <summary>
/// Adapter for Terminal.Gui 1.x, whose menu-open entry point is not public.
/// Isolates the single reflection use; degrades to focus-only when the API moves.
/// </summary>
public static class MenuBarOpener
{
    private static readonly MethodInfo? OpenMenuMethod = typeof(MenuBar).GetMethod(
        "OpenMenu",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        [typeof(int), typeof(int), typeof(MenuBarItem)]);

    public static void OpenMenu(MenuBar? menuBar, int index)
    {
        if (menuBar is null || index < 0 || index >= menuBar.Menus.Length)
        {
            return;
        }

        menuBar.SetFocus();
        OpenMenuMethod?.Invoke(menuBar, [GetMenuX(index), -1, menuBar.Menus[index]]);
    }

    // Mirrors MenuBar's internal label layout to keep the popup under its title.
    public static int GetMenuX(int index)
    {
        var x = 1;
        var labels = new[] { "Providers", "Line view", "Station view", "Actions" };
        for (var i = 0; i < Math.Clamp(index, 0, labels.Length); i++)
        {
            x += labels[i].Length + 2;
        }

        return x;
    }
}
