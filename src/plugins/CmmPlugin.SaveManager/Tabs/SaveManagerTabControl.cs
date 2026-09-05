using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using CatModManager.Theme;
using Avalonia.Platform.Storage;
using CmmPlugin.SaveManager.Services;

namespace CmmPlugin.SaveManager.Tabs;

public class SaveManagerTabControl : UserControl
{
    private readonly SaveManagerTabViewModel _vm;
    private readonly TextBlock               _statusText;
    private readonly TextBox                 _labelBox;

    public SaveManagerTabControl(SaveManagerTabViewModel vm)
    {
        _vm = vm;

        _statusText = new TextBlock
        {
            Margin       = new Thickness(8, 6),
            Foreground   = CmmPalette.Brushes.TextSubtle,
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap
        };

        _labelBox = new TextBox
        {
            Watermark = "Name this save (e.g. before the ending)",
            FontSize  = 12
        };

        var root = new DockPanel();
        DockPanel.SetDock(_statusText, Dock.Top);

        var saveBar = BuildSaveBar();
        DockPanel.SetDock(saveBar, Dock.Top);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);

        var autoBar = BuildAutoSaveBar();
        DockPanel.SetDock(autoBar, Dock.Bottom);

        root.Children.Add(_statusText);
        root.Children.Add(saveBar);
        root.Children.Add(autoBar);
        root.Children.Add(footer);
        root.Children.Add(BuildListBox());

        Content = root;

        _vm.Refresh();
        SyncStatus();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SaveManagerTabViewModel.Status))       SyncStatus();
            if (e.PropertyName == nameof(SaveManagerTabViewModel.NewSlotLabel)) _labelBox.Text = _vm.NewSlotLabel;
        };
    }

    /// <summary>The name box and the Save button — the primary action, so it sits at the top.</summary>
    private Panel BuildSaveBar()
    {
        var save = new Button
        {
            Content = "💾  SAVE",
            Padding = new Thickness(10, 4),
            Margin  = new Thickness(4, 0, 0, 0)
        };
        save.Click += async (_, _) =>
        {
            _vm.NewSlotLabel = _labelBox.Text ?? "";
            await _vm.SaveCommand.ExecuteAsync(null);
        };

        var grid = new Grid { Margin = new Thickness(8, 2, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Grid.SetColumn(_labelBox, 0);
        Grid.SetColumn(save,      1);
        grid.Children.Add(_labelBox);
        grid.Children.Add(save);

        return grid;
    }

    /// <summary>
    /// Wraps a row builder in the one rule the list has to obey: no item, no row.
    ///
    /// Removing a slot makes the virtualizing panel recycle its container, and clearing a container
    /// runs the template once more with nothing in it. Deleting a save built a row for that null,
    /// dereferenced it, and took the app down — after the file had already been deleted, so it
    /// looked like deleting a save was what crashed.
    ///
    /// Separate from the builder so the rule can be tested without standing up the whole tab.
    /// </summary>
    public static FuncDataTemplate<SaveSlot> RowTemplate(Func<SaveSlot, Control> buildRow) =>
        new((slot, _) => slot is null ? new Panel() : buildRow(slot));

    private ListBox BuildListBox() =>
        new()
        {
            ItemsSource  = _vm.Slots,
            ItemTemplate = RowTemplate(slot =>
            {
                var grid = new Grid { Margin = new Thickness(2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var name = new TextBlock
                {
                    Text              = slot.Display,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming      = TextTrimming.CharacterEllipsis,
                    Foreground        = slot.Kind == SaveSlotKind.Manual ? CmmPalette.Brushes.TextOnAccent : CmmPalette.Brushes.TextSubtle
                };
                ToolTip.SetTip(name, $"{slot.CreatedAt:g}\n{slot.FilePath}");

                var when = new TextBlock
                {
                    Text              = slot.CreatedAt.ToString("MMM d, HH:mm"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground        = CmmPalette.Brushes.TextSubtle,
                    FontSize          = 11,
                    Margin            = new Thickness(8, 0)
                };

                var size = new TextBlock
                {
                    Text              = FormatSize(slot.SizeBytes),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground        = CmmPalette.Brushes.TextSubtle,
                    FontSize          = 11,
                    Margin            = new Thickness(0, 0, 8, 0)
                };

                var actions = BuildSlotActions(slot);

                Grid.SetColumn(name,    0);
                Grid.SetColumn(when,    1);
                Grid.SetColumn(size,    2);
                Grid.SetColumn(actions, 3);

                grid.Children.Add(name);
                grid.Children.Add(when);
                grid.Children.Add(size);
                grid.Children.Add(actions);

                return grid;
            })
        };

    /// <summary>
    /// Load and Delete, both behind a click-to-confirm.
    ///
    /// Both overwrite or destroy saves, and they live in a scrolling list where a misplaced click is
    /// easy. Rather than a modal — which a plugin control has no window to parent — the button
    /// changes to "Sure?" and only acts on the second click, reverting if the user moves away.
    ///
    /// The mechanism now comes from <see cref="CmmControls.ConfirmButton"/>. The copy that used to
    /// live here could be defeated by a double click: the second click of the gesture answered the
    /// question the first one had just posed, before it was on screen to read.
    /// </summary>
    private Panel BuildSlotActions(SaveSlot slot)
    {
        var load   = CmmControls.ConfirmButton("Load", "Load — sure?",   async () => await _vm.Load(slot));
        var delete = CmmControls.ConfirmButton("✕",    "Delete — sure?", () => _vm.Delete(slot));
        delete.Foreground = CmmPalette.Brushes.StatusDanger;

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        stack.Children.Add(load);
        stack.Children.Add(delete);
        return stack;
    }


    /// <summary>The auto-save switch and its interval, plus a line saying what it is doing.</summary>
    private Panel BuildAutoSaveBar()
    {
        var toggle = new CheckBox
        {
            Content           = "Auto-save every",
            IsChecked         = _vm.AutoSaveEnabled,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var minutes = new NumericUpDown
        {
            Value             = _vm.AutoSaveMinutes,
            Minimum           = GameSaveSettings.MinAutoSaveMinutes,
            Maximum           = 240,
            Increment         = 1,
            FormatString      = "0",
            // Wide enough for three digits *and* the spinner buttons. At 90 the buttons ate the
            // field, so "240" showed up clipped in the one control whose whole job is a number.
            Width             = 130,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        var unit = new TextBlock
        {
            Text              = "min",
            FontSize          = 11,
            Foreground        = CmmPalette.Brushes.TextSubtle,
            VerticalAlignment = VerticalAlignment.Center
        };

        var beforeLaunch = new CheckBox
        {
            Content           = "Back up before launching",
            IsChecked         = _vm.BackupBeforeLaunch,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        toggle.IsCheckedChanged       += (_, _) => _vm.AutoSaveEnabled    = toggle.IsChecked == true;
        beforeLaunch.IsCheckedChanged += (_, _) => _vm.BackupBeforeLaunch = beforeLaunch.IsChecked == true;
        minutes.ValueChanged   += (_, _) =>
        {
            if (minutes.Value is { } v) _vm.AutoSaveMinutes = (int)v;
        };

        var note = new TextBlock
        {
            Text         = _vm.AutoSaveStatus,
            FontSize     = 10,
            Foreground   = CmmPalette.Brushes.TextSubtle,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0)
        };
        // Switching profile reloads another game's settings, so the controls follow the view model
        // rather than only driving it.
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SaveManagerTabViewModel.AutoSaveStatus):  note.Text        = _vm.AutoSaveStatus;  break;
                case nameof(SaveManagerTabViewModel.AutoSaveEnabled): toggle.IsChecked = _vm.AutoSaveEnabled; break;
                case nameof(SaveManagerTabViewModel.AutoSaveMinutes): minutes.Value    = _vm.AutoSaveMinutes; break;
                case nameof(SaveManagerTabViewModel.BackupBeforeLaunch):
                    beforeLaunch.IsChecked = _vm.BackupBeforeLaunch; break;
            }
        };

        ToolTip.SetTip(toggle,
            "Takes a snapshot on a timer, but only when the saves have actually changed — so " +
            "nothing is written while you are idle or the game is closed. Keeps the last five, " +
            "separately from the saves you make yourself.");

        ToolTip.SetTip(beforeLaunch,
            "Takes a snapshot right before the game starts, into the same five-slot buffer. " +
            "Independent of the timer above — this one is the net for a new mod eating a playthrough.");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(toggle);
        row.Children.Add(minutes);
        row.Children.Add(unit);

        var stack = new StackPanel { Margin = new Thickness(8, 4) };
        stack.Children.Add(row);
        stack.Children.Add(beforeLaunch);
        stack.Children.Add(note);
        return stack;
    }

    private Panel BuildFooter()
    {
        var refresh = CmmControls.Button("↺ Refresh", () => _vm.Refresh());

        var choose = CmmControls.Button("📁 Save folder…", async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Where does this game keep its saves?",
                AllowMultiple = false
            });

            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) _vm.SetSaveFolder(path);
        });
        ToolTip.SetTip(choose, "Point CMM at this game's save folder — for games we don't detect, " +
                               "custom Wine prefixes, or saves on another disk.");

        // Named after what it does to your settings, not after the detection that follows. It was
        // "Auto-detect", which reads like a search — something you click to gain information, at no
        // cost — when it is the only button here that throws a setting away.
        //
        // Behind the same click-to-confirm as Load and Delete, because it belongs to that group: a
        // folder hunted down inside a Wine prefix is not something to lose to one stray click. And
        // disabled unless there is a choice to discard, so it is never a button that looks armed and
        // does nothing.
        var auto = CmmControls.ConfirmButton("Forget my folder", "Forget it — sure?",
                                        () => _vm.ClearSaveFolderOverride());
        auto.IsEnabled = _vm.HasSaveFolderOverride;
        ToolTip.SetTip(auto, "Discard the save folder you chose by hand and go back to the detected one.");

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SaveManagerTabViewModel.HasSaveFolderOverride))
                auto.IsEnabled = _vm.HasSaveFolderOverride;
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 4,
            Margin      = new Thickness(8, 4)
        };
        bar.Children.Add(refresh);
        bar.Children.Add(choose);
        bar.Children.Add(auto);
        return bar;
    }

    private void SyncStatus() => _statusText.Text = _vm.Status;

    private static string FormatSize(long bytes) =>
        bytes switch
        {
            < 1_024         => $"{bytes} B",
            < 1_024 * 1_024 => $"{bytes / 1_024} KB",
            _               => $"{bytes / (1_024 * 1_024)} MB"
        };
}
