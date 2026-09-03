using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CatModManager.PluginSdk;
using CmmPlugin.FomodInstaller.Models;

namespace CmmPlugin.FomodInstaller.Wizard;

/// <summary>
/// Code-only Avalonia Window that walks the user through FOMOD installation steps.
/// Call ShowDialog&lt;InstallResult?&gt;(parentWindow) to run.
/// </summary>
public class FomodWizardWindow : Window
{
    private readonly FomodWizardViewModel _vm;
    private readonly IPluginLogger _log;
    private readonly IArchiveExtractor _extractor;
    private readonly string _archivePath;
    /// <summary>
    /// Image paths in ModuleConfig.xml are relative to the module root, which is the wrapper folder
    /// when the archive has one — "My Little Nanako 3" asks for "01-00.png" and ships it as
    /// "nana311/01-00.png". Without the prefix no image resolves and every preview silently
    /// vanishes, since a missing picture is not treated as an error.
    /// </summary>
    private readonly string _wrapperPrefix;

    /// <summary>
    /// Preview images, already in memory by the time the window is built, and decoded to Bitmaps
    /// only when a row asks for one.
    ///
    /// Each row used to read its own image straight from the archive, inside the synchronous
    /// Render(). In a solid .7z every such read decodes the whole stream from the start, so Cridow's
    /// skin set — 335 MB compressed, 1 GB unpacked, 16 previews — spent about ten seconds per
    /// picture, all of it on the UI thread. FomodParser.Read now collects them in the same pass that
    /// reads the config, which costs no more than reading the config alone.
    /// </summary>
    private readonly System.Collections.Generic.IReadOnlyDictionary<string, byte[]> _previewBytes;
    private readonly System.Collections.Generic.Dictionary<string, Bitmap?> _decoded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl _stepContent;
    private readonly TextBlock _stepIndicator;
    private readonly Button _btnBack;
    private readonly Button _btnNext;
    private readonly Button _btnInstall;

    public FomodWizardWindow(
        FomodModuleConfig config, IPluginLogger log, IArchiveExtractor extractor,
        string archivePath = "",
        System.Collections.Generic.IReadOnlyDictionary<string, byte[]>? previews = null)
    {
        _log = log;
        _extractor = extractor;
        _archivePath = archivePath;
        _previewBytes = previews ?? new System.Collections.Generic.Dictionary<string, byte[]>();
        _wrapperPrefix = config.WrapperPrefix ?? string.Empty;
        _vm = new FomodWizardViewModel(config);

        Title = $"Install: {config.ModuleName}";
        Width = 640;
        Height = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Header
        var header = new TextBlock
        {
            Text = config.ModuleName,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(16, 12, 16, 4)
        };

        _stepIndicator = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(16, 0, 16, 8)
        };

        var separator = new Border
        {
            Height = 1,
            Background = Brushes.Gray,
            Opacity = 0.3,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // Step content area
        _stepContent = new ContentControl { Margin = new Thickness(8) };

        // Scrollable content
        var scroll = new ScrollViewer { Content = _stepContent };

        // Footer buttons
        _btnBack = new Button { Content = "← Back", Width = 90 };
        _btnNext = new Button { Content = "Next →", Width = 90 };
        _btnInstall = new Button { Content = "Install", Width = 90 };
        var btnCancel = new Button { Content = "Cancel", Width = 90 };

        _btnBack.Click += (_, _) => { _vm.GoBack(); Render(); };
        _btnNext.Click += (_, _) => { _vm.GoNext(); Render(); };
        _btnInstall.Click += (_, _) => Finish();
        btnCancel.Click += (_, _) => Close(null);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 8)
        };
        footer.Children.Add(btnCancel);
        footer.Children.Add(_btnBack);
        footer.Children.Add(_btnNext);
        footer.Children.Add(_btnInstall);

        var footerBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Brushes.Gray,
            Child = footer
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_stepIndicator, Dock.Top);
        DockPanel.SetDock(separator, Dock.Top);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_stepIndicator);
        root.Children.Add(separator);
        root.Children.Add(footerBorder);
        root.Children.Add(scroll);

        Content = root;
        Render();
    }

    /// <summary>
    /// The preview for an image path from the config, decoded on first use and kept. Returns null
    /// when the archive had no such entry, which is not an error — a FOMOD may reference a picture
    /// it does not ship, and a missing one simply does not appear.
    /// </summary>
    private Bitmap? Preview(string imagePath)
    {
        var key = Parser.FomodParser.NormalizeKey(imagePath);
        if (_decoded.TryGetValue(key, out var cached)) return cached;

        Bitmap? bmp = null;
        if (_previewBytes.TryGetValue(key, out var bytes))
        {
            try { bmp = new Bitmap(new MemoryStream(bytes)); }
            catch { /* a preview that will not decode is not worth failing the wizard for */ }
        }

        _decoded[key] = bmp;
        return bmp;
    }

    private void Render()
    {
        // The step's own name is preferred, but it is optional in the format and often blank — 43
        // blank ones in a row leave the user with no idea where they are, so fall back to the first
        // group's name, which authoring tools do fill in.
        string label = _vm.CurrentStep?.Name is { Length: > 0 } n
            ? n
            : _vm.CurrentStep?.Groups.FirstOrDefault()?.Name ?? string.Empty;

        _stepLabel = label;
        UpdateNavigation();

        _stepContent.Content = _vm.CurrentStep != null ? BuildStepPanel(_vm.CurrentStep) : null;
    }

    /// <summary>
    /// Refreshes the step counter and the footer buttons without rebuilding the step's controls.
    ///
    /// Needed on every selection change, because a choice can set a flag that makes the remaining
    /// steps disappear — so the same click that ticks a radio button can turn "Next" into "Install".
    /// Deliberately not a full Render(): rebuilding the panel would recreate the very control whose
    /// change handler is running and re-enter through its IsCheckedChanged.
    /// </summary>
    private string _stepLabel = string.Empty;

    private void UpdateNavigation()
    {
        _stepIndicator.Text = _vm.TotalSteps > 0
            ? (_stepLabel.Length > 0
                ? $"Step {_vm.CurrentStepNumber} of {_vm.TotalSteps} — {_stepLabel}"
                : $"Step {_vm.CurrentStepNumber} of {_vm.TotalSteps}")
            : "No steps — click Install to proceed.";

        _btnBack.IsEnabled = _vm.CanGoBack;
        _btnNext.IsVisible = !_vm.IsLastStep && _vm.TotalSteps > 0;
        _btnInstall.IsVisible = _vm.IsLastStep || _vm.TotalSteps == 0;
    }

    private Panel BuildStepPanel(FomodInstallStep step)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(8) };

        foreach (var group in step.Groups)
        {
            // Group header
            panel.Children.Add(new TextBlock
            {
                Text = group.Name,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Group type hint
            string hint = group.Type switch
            {
                GroupType.SelectExactlyOne => "Select one:",
                GroupType.SelectAtLeastOne => "Select at least one:",
                GroupType.SelectAll        => "All required:",
                GroupType.SelectAtMostOne  => "Select at most one:",
                _                          => "Select any:"
            };
            panel.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Brushes.Gray });

            bool isSingle = group.Type is GroupType.SelectExactlyOne or GroupType.SelectAtMostOne;
            var selected = _vm.GetSelection(step, group);

            foreach (var plugin in group.Plugins)
            {
                var row = BuildPluginRow(step, group, plugin, isSingle, selected);
                panel.Children.Add(row);
            }

            panel.Children.Add(new Border { Height = 1, Background = Brushes.Gray, Opacity = 0.2 });
        }

        return panel;
    }

    private Panel BuildPluginRow(
        FomodInstallStep step, FomodGroup group,
        FomodPlugin plugin, bool isSingle,
        System.Collections.Generic.HashSet<string> selected)
    {
        var row = new StackPanel { Spacing = 4, Margin = new Thickness(8, 2) };

        // Radio or Checkbox
        Control selector;
        if (isSingle)
        {
            var radio = new RadioButton
            {
                Content = plugin.Name,
                IsChecked = selected.Contains(plugin.Name),
                GroupName = _vm.GroupKey(step, group),
                IsEnabled = group.Type != GroupType.SelectAll
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true) return;
                _vm.TogglePlugin(step, group, plugin);
                UpdateNavigation();
            };
            selector = radio;
        }
        else
        {
            var cb = new CheckBox
            {
                Content = plugin.Name,
                IsChecked = selected.Contains(plugin.Name),
                IsEnabled = group.Type != GroupType.SelectAll
            };
            cb.IsCheckedChanged += (_, _) => { _vm.TogglePlugin(step, group, plugin); UpdateNavigation(); };
            selector = cb;
        }

        row.Children.Add(selector);

        if (!string.IsNullOrEmpty(plugin.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = plugin.Description,
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (!string.IsNullOrEmpty(plugin.ImagePath))
        {
            var bmp = Preview(_wrapperPrefix + plugin.ImagePath);
            if (bmp != null)
                row.Children.Add(new Image
                {
                    Source    = bmp,
                    MaxHeight = 120,
                    Stretch   = Stretch.Uniform,
                    Margin    = new Thickness(20, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                });
        }

        return row;
    }

    private void Finish()
    {
        var mapping = _vm.BuildFileMapping();
        _log.Log($"[FOMOD] Installation confirmed: {mapping.Count} file entries selected.");
        Close(InstallResult.Success(mapping));
    }
}
