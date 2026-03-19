using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CatModManager.Core.Services;
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
    private readonly ILogService _log;
    private readonly ContentControl _stepContent;
    private readonly TextBlock _stepIndicator;
    private readonly Button _btnBack;
    private readonly Button _btnNext;
    private readonly Button _btnInstall;

    public FomodWizardWindow(FomodModuleConfig config, ILogService log)
    {
        _log = log;
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

    private void Render()
    {
        _stepIndicator.Text = _vm.TotalSteps > 0
            ? $"Step {_vm.CurrentStepNumber} of {_vm.TotalSteps}"
            : "No steps — click Install to proceed.";

        _btnBack.IsEnabled = _vm.CanGoBack;
        _btnNext.IsVisible = !_vm.IsLastStep && _vm.TotalSteps > 0;
        _btnInstall.IsVisible = _vm.IsLastStep || _vm.TotalSteps == 0;

        _stepContent.Content = _vm.CurrentStep != null ? BuildStepPanel(_vm.CurrentStep) : null;
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
                GroupName = $"{step.Name}::{group.Name}",
                IsEnabled = group.Type != GroupType.SelectAll
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    _vm.TogglePlugin(step, group, plugin);
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
            cb.IsCheckedChanged += (_, _) => _vm.TogglePlugin(step, group, plugin);
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

        return row;
    }

    private void Finish()
    {
        var mapping = _vm.BuildFileMapping();
        _log.Log($"[FOMOD] Installation confirmed: {mapping.Count} file entries selected.");
        Close(InstallResult.Success(mapping));
    }
}
