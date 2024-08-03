using System.Runtime.InteropServices;

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CoreRipperX
{
    internal class Program
    {
        [DllImport("Kernel32.dll")]
        public static extern bool SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

        [DllImport("Kernel32.dll")]
        public static extern IntPtr GetCurrentThread();


        static void Main(string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int runtimeSeconds))
            {
                Console.WriteLine("Please provide the runtime in seconds as the first argument.");
                return;
            }

            int numCores = Environment.ProcessorCount;

            for (int i = 0; i < numCores; i++)
            {
                int core = i;
                var cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;

                Task task = Task.Run(() => HeavyLoad(core, token), token);

                // Warte die angegebene Zeit
                Thread.Sleep(runtimeSeconds * 1000);
                cts.Cancel();

                try
                {
                    task.Wait();
                }
                catch (AggregateException ex)
                {
                    ex.Handle(e => e is OperationCanceledException);
                }
            }
        }

        static void HeavyLoad(int core, CancellationToken token)
        {
            IntPtr affinityMask = new IntPtr(1 << core);
            SetThreadAffinityMask(GetCurrentThread(), affinityMask);

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    // Call the AVX2 function
                    var vec = Vector256<int>.Zero;
                    for (int i = 0; i < 1000000000; i++)
                    {
                        vec = Avx2.Add(vec, Vector256<int>.One);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Core {core} task canceled.");
            }
        }
    }
}
