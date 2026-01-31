using System.ComponentModel;
using System.Windows;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;
using CoreRipperX.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRipperX.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private ISettingsService? _settingsService;
    private AppSettings? _appSettings;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        _settingsService = new SettingsService();
        _appSettings = _settingsService.Load();
        _appSettings.PropertyChanged += OnSettingsPropertyChanged;

        services.AddSingleton(_appSettings);
        services.AddSingleton<ISettingsService>(_settingsService);
        services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
        services.AddSingleton<IStressTestService, StressTestService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SystemMonitorViewModel>();
        services.AddSingleton<SettingsViewModel>();

        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_settingsService is not null && _appSettings is not null)
        {
            _settingsService.Save(_appSettings);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_appSettings is not null)
        {
            _appSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _settingsService?.Save(_appSettings);
        }

        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnExit(e);
    }
}
