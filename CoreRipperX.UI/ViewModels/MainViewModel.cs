using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreRipperX.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public SystemMonitorViewModel SystemMonitor { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel(SystemMonitorViewModel systemMonitor, SettingsViewModel settings)
    {
        SystemMonitor = systemMonitor;
        Settings = settings;
    }
}
