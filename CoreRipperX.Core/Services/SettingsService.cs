using System.Text.Json;
using CoreRipperX.Core.Models;

namespace CoreRipperX.Core.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDir = Path.Combine(appDataPath, "CoreRipperX");
        Directory.CreateDirectory(settingsDir);
        _settingsPath = Path.Combine(settingsDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);

            if (data is null)
            {
                return new AppSettings();
            }

            var algorithm = data.SelectedAlgorithm;

            return new AppSettings
            {
                RuntimePerCycleSeconds = data.RuntimePerCycleSeconds,
                CriticalDeviationPercent = data.CriticalDeviationPercent,
                SelectedAlgorithm = algorithm,
                PollingRateMs = data.PollingRateMs,
                CriticalTemperatureCelsius = data.CriticalTemperatureCelsius
            };
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var data = new SettingsData
        {
            RuntimePerCycleSeconds = settings.RuntimePerCycleSeconds,
            CriticalDeviationPercent = settings.CriticalDeviationPercent,
            SelectedAlgorithm = settings.SelectedAlgorithm,
            PollingRateMs = settings.PollingRateMs,
            CriticalTemperatureCelsius = settings.CriticalTemperatureCelsius
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private class SettingsData
    {
        public int RuntimePerCycleSeconds { get; set; } = 10;
        public double CriticalDeviationPercent { get; set; } = 1.0;
        public string SelectedAlgorithm { get; set; } = "AVX2 1T";
        public int PollingRateMs { get; set; } = 1000;
        public int CriticalTemperatureCelsius { get; set; } = 90;
    }
}
