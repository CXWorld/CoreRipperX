# CoreRipperX README

## Overview
CoreRipperX is a small CPU stress-testing tool designed to evaluate the performance and stability of individual CPU cores by leveraging AVX2 operations. The tool sequentially puts heavy computational loads on each core for a specified time period, switching thread affinity to the next core after each interval until all cores have been tested.

## Features
- **AVX2 Operation Load**: Utilizes AVX2 instructions to impose a significant workload on each CPU core.
- **Core Cycling**: Automatically cycles through all CPU cores, ensuring each core is tested.
- **Configurable Time Period**: Allows users to specify the duration of the load on each core.

## Requirements
- **Operating System**: Windows, Linux, or macOS
- **CPU**: Processor with AVX2 support
- **.NET 8**: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Installation
1. **Download**: Obtain the latest release from the [[CoreRipperX GitHub repository](https://github.com/CXWorld/CoreRipperX/releases)].
2. **Extract**: Unzip the downloaded file to your desired location.

## Usage
1. **Command Line Execution**: Open a terminal or command prompt in the directory containing the CoreRipperX executable.
2. **Run the Tool**: Use the following command format to start the tool:
    ```
    ./CoreRipperX <time_period_in_sceconds>
    ```

### Example
To run CoreRipperX with a time period of 30 seconds per core:
```
./CoreRipperX 30
```

## Notes
- Ensure all important work is saved before running CoreRipperX as it will place significant load on your CPU.
- The tool may cause the system to become unresponsive during operation, especially on systems with fewer cores.

## License
CoreRipperX is licensed under the MIT License. See the `LICENSE` file for more details.

## Contact
For issues, questions, or suggestions, please open an issue on the [GitHub repository](https://github.com/CXWorld/CoreRipperX/issues).

---

Thank you for using CoreRipperX!
