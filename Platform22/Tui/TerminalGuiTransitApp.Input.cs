namespace Platform22.Tui;

using Terminal.Gui;

public sealed partial class TerminalGuiTransitApp
{
    private void HandleKey(KeyEvent keyEvent)
    {
        if (!filterEditing && IsMenuShortcut(keyEvent, 'P', Key.P))
        {
            ShowProviderPicker();
            return;
        }

        if (!filterEditing && IsMenuShortcut(keyEvent, 'L', Key.L))
        {
            ShowLinePicker();
            return;
        }

        if (!filterEditing && IsMenuShortcut(keyEvent, 'S', Key.S))
        {
            ShowStationPicker();
            return;
        }

        var key = NormalizeKey(keyEvent);

        if (filterEditing)
        {
            HandleFilterKey(keyEvent);
            Refresh();
            return;
        }

        if (key == '+')
        {
            zoom = Math.Min(180, zoom + 12);
            Refresh();
            return;
        }

        if (key == '-')
        {
            zoom = Math.Max(40, zoom - 12);
            Refresh();
            return;
        }

        switch (key)
        {
            case '/':
                filterEditing = true;
                Refresh();
                return;
            case 'h':
                panX = Math.Max(0, panX - 4);
                Refresh();
                return;
            case 'l':
                panX += 4;
                Refresh();
                return;
            case 'k':
                panY = Math.Max(0, panY - 1);
                Refresh();
                return;
            case 'j':
                panY += 1;
                Refresh();
                return;
            case 'r':
                RefreshData();
                return;
            case 'q':
                Application.RequestStop();
                return;
        }

        switch (keyEvent.Key)
        {
            case Key.Esc:
                Application.RequestStop();
                return;
            case Key.Tab:
                SwitchMode(mode == TransitTuiMode.Lines ? TransitTuiMode.Stations : TransitTuiMode.Lines);
                break;
            case Key.Backspace:
                break;
            case Key.CursorDown:
                selectedIndex++;
                selectedItemId = null;
                panX = 0;
                panY = 0;
                break;
            case Key.CursorUp:
                selectedIndex = Math.Max(0, selectedIndex - 1);
                selectedItemId = null;
                panX = 0;
                panY = 0;
                break;
            default:
                break;
        }

        Refresh();
    }

    private void HandleFilterKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key is Key.Enter or Key.Esc)
        {
            filterEditing = false;
            return;
        }

        if (keyEvent.Key == Key.Backspace)
        {
            if (filter.Length > 0)
            {
                filter = filter[..^1];
                selectedIndex = 0;
            }

            return;
        }

        var character = NormalizeKey(keyEvent);
        if (!char.IsControl(character))
        {
            filter += character;
            selectedIndex = 0;
            panX = 0;
            panY = 0;
        }
    }

    private static char NormalizeKey(KeyEvent keyEvent)
    {
        if (keyEvent.KeyValue != 0)
        {
            return char.ToLowerInvariant((char)keyEvent.KeyValue);
        }

        return keyEvent.Key switch
        {
            Key.H => 'h',
            Key.J => 'j',
            Key.K => 'k',
            Key.L => 'l',
            Key.R => 'r',
            Key.Q => 'q',
            Key.CharMask => '\0',
            _ => '\0'
        };
    }

    private static bool IsMenuShortcut(KeyEvent keyEvent, char keyValue, Key key)
    {
        return keyEvent.KeyValue == keyValue || (keyEvent.Key == key && keyEvent.KeyValue != char.ToLowerInvariant(keyValue));
    }
}
