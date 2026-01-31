using CoreRipperX.Core.Models;

namespace CoreRipperX.Core.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
