using System.Windows;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;
using CoreRipperX.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRipperX.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        services.AddSingleton<AppSettings>();
        services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
        services.AddSingleton<IStressTestService, StressTestService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SystemMonitorViewModel>();
        services.AddSingleton<SettingsViewModel>();

        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnExit(e);
    }
}
