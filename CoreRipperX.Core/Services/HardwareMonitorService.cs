using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using CoreRipperX.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace CoreRipperX.Core.Services;

public class HardwareMonitorService : IHardwareMonitorService
{
    private readonly Computer _computer;
    private readonly List<CoreData> _coreDataList = new();
    private readonly Subject<IReadOnlyList<CoreData>> _coreDataSubject = new();
    private IDisposable? _timerSubscription;
    private IHardware? _cpu;
    private bool _isInitialized;
    private bool _disposed;
    private int _coreNumberOffset; // 0 for AMD, 1 for Intel

    public string CpuName { get; private set; } = "Unknown CPU";
    public int PhysicalCoreCount { get; private set; }
    public int LogicalCoreCount { get; private set; }
    public int SensorCount { get; private set; }
    public string? LastError { get; private set; }

    public IObservable<IReadOnlyList<CoreData>> CoreDataStream => _coreDataSubject.AsObservable();

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true
        };
    }

    private List<ISensor> GetAllSensors()
    {
        if (_cpu == null) return new List<ISensor>();

        var allSensors = _cpu.Sensors.ToList();
        foreach (var sub in _cpu.SubHardware)
        {
            allSensors.AddRange(sub.Sensors);
        }
        return allSensors;
    }

    private void Initialize()
    {
        if (_isInitialized || _disposed)
            return;

        try
        {
            _computer.Open();

            // Find the CPU hardware
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    _cpu = hardware;
                    _cpu.Update();
                    CpuName = hardware.Name;
                    break;
                }
            }

            LogicalCoreCount = Environment.ProcessorCount;

            if (_cpu == null)
            {
                LastError = "No CPU hardware found. Make sure to run as Administrator.";
                PhysicalCoreCount = LogicalCoreCount;
            }
            else
            {
                // Also update sub-hardware to ensure sensors are populated
                foreach (var sub in _cpu.SubHardware)
                {
                    sub.Update();
                }

                var allSensors = GetAllSensors();

                // Find clock sensors to detect physical core count and numbering scheme
                // Pattern: "Core #X" where X is a number, not containing "Effective"
                var clockSensors = allSensors
                    .Where(s => s.SensorType == SensorType.Clock &&
                           s.Name != null &&
                           Regex.IsMatch(s.Name, @"Core #\d+") &&
                           !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (clockSensors.Count > 0)
                {
                    PhysicalCoreCount = clockSensors.Count;

                    // Detect if 0-based (AMD) or 1-based (Intel)
                    var coreNumbers = clockSensors
                        .Select(s => {
                            var match = Regex.Match(s.Name!, @"Core #(\d+)");
                            return match.Success ? int.Parse(match.Groups[1].Value) : -1;
                        })
                        .Where(n => n >= 0)
                        .OrderBy(n => n)
                        .ToList();

                    // If minimum core number is 0, it's 0-based (AMD), otherwise 1-based (Intel)
                    _coreNumberOffset = coreNumbers.Count > 0 && coreNumbers[0] == 0 ? 0 : 1;
                }
                else
                {
                    // Fallback: assume logical = physical
                    PhysicalCoreCount = LogicalCoreCount;
                    _coreNumberOffset = 1;
                }
            }

            // Create core data entries per physical core
            for (int i = 0; i < PhysicalCoreCount; i++)
            {
                _coreDataList.Add(new CoreData
                {
                    CoreIndex = i,
                    CoreLabel = $"Core #{i}"
                });
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            LastError = $"Initialization failed: {ex.Message}";
        }
    }

    public void StartMonitoring(TimeSpan interval)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        StopMonitoring();

        // Initial update
        UpdateSensorData();

        _timerSubscription = Observable
            .Interval(interval)
            .Subscribe(_ => UpdateSensorData());
    }

    public void StopMonitoring()
    {
        _timerSubscription?.Dispose();
        _timerSubscription = null;
    }

    private void UpdateSensorData()
    {
        if (!_isInitialized || _cpu == null)
        {
            _coreDataSubject.OnNext(_coreDataList.AsReadOnly());
            return;
        }

        try
        {
            _cpu.Update();

            // Also update sub-hardware
            foreach (var sub in _cpu.SubHardware)
            {
                sub.Update();
            }

            var allSensors = GetAllSensors();
            SensorCount = allSensors.Count;

            // Collect sensor data
            // Key = physical core index (0-based internal)
            var clocks = new Dictionary<int, float>();
            // Key = (physical core index, thread index within core)
            var effectiveClocks = new Dictionary<(int core, int thread), float>();
            var loads = new Dictionary<(int core, int thread), float>();

            foreach (var sensor in allSensors)
            {
                var name = sensor.Name ?? "";
                var value = sensor.Value ?? 0f;

                switch (sensor.SensorType)
                {
                    case SensorType.Clock:
                        // Match "Core #X" where X is the core number
                        var clockMatch = Regex.Match(name, @"Core #(\d+)");
                        if (clockMatch.Success)
                        {
                            int sensorCoreNum = int.Parse(clockMatch.Groups[1].Value);
                            // Convert to 0-based internal index
                            int coreIndex = sensorCoreNum - _coreNumberOffset;

                            if (coreIndex >= 0 && coreIndex < PhysicalCoreCount)
                            {
                                if (name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Per-thread effective clock: "Core #X Thread #Y (Effective)"
                                    var threadMatch = Regex.Match(name, @"Thread #(\d+)");
                                    int threadIndex = threadMatch.Success ? int.Parse(threadMatch.Groups[1].Value) - 1 : 0;
                                    effectiveClocks[(coreIndex, threadIndex)] = value;
                                }
                                else
                                {
                                    clocks[coreIndex] = value;
                                }
                            }
                        }
                        break;

                    case SensorType.Load:
                        // Match "Core #X" and optionally "Thread #Y"
                        // Examples: "Core #1", "Core #1 Thread #1", "Core #1 Thread #2"
                        var loadMatch = Regex.Match(name, @"Core #(\d+)");
                        if (loadMatch.Success)
                        {
                            int sensorCoreNum = int.Parse(loadMatch.Groups[1].Value);
                            int coreIndex = sensorCoreNum - _coreNumberOffset;

                            // Check for thread number
                            var threadMatch = Regex.Match(name, @"Thread #(\d+)");
                            int threadIndex = threadMatch.Success ? int.Parse(threadMatch.Groups[1].Value) - 1 : 0;

                            if (coreIndex >= 0 && coreIndex < PhysicalCoreCount)
                            {
                                loads[(coreIndex, threadIndex)] = value;
                            }
                        }
                        break;
                }
            }

            // Update core data list
            for (int i = 0; i < _coreDataList.Count; i++)
            {
                var coreData = _coreDataList[i];

                coreData.ClockSpeed = clocks.GetValueOrDefault(i, 0f);
                coreData.EffectiveClockSpeed = effectiveClocks.GetValueOrDefault((i, 0), 0f);
                coreData.EffectiveClockSpeed2T = effectiveClocks.GetValueOrDefault((i, 1), 0f);

                coreData.Load1T = loads.GetValueOrDefault((i, 0), 0f);
                coreData.Load2T = loads.GetValueOrDefault((i, 1), 0f);
            }

            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        _coreDataSubject.OnNext(_coreDataList.AsReadOnly());
    }

    public IReadOnlyList<CoreData> GetCurrentCoreData()
    {
        if (!_isInitialized)
        {
            Initialize();
        }
        UpdateSensorData();
        return _coreDataList.AsReadOnly();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopMonitoring();
        _coreDataSubject.Dispose();
        _computer.Close();
    }
}
