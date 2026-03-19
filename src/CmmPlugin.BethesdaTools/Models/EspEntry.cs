using CommunityToolkit.Mvvm.ComponentModel;

namespace CmmPlugin.BethesdaTools.Models;

public partial class EspEntry : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private int _loadOrder;

    public string Extension => System.IO.Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    public bool IsMaster => Extension is "ESM" or "ESL";

    public EspEntry() { }

    public EspEntry(string fileName, bool isEnabled, int loadOrder)
    {
        FileName = fileName;
        IsEnabled = isEnabled;
        LoadOrder = loadOrder;
    }
}
