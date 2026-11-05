using System.IO;
using System.Windows;
using Forge.App;

// Probe: instantiate HammerOverlayWindow and Show() it, logging every step.
// Reproduces the exact construction + show path from App.ShowHammerOverlay().
// The WPF message loop must pump (app.Run on its own STA thread); the whole test
// sequence runs on the dispatcher thread via BeginInvoke.
internal static class Program
{
    [STAThread]
    static void Main()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "hammer-probe.log");
        File.WriteAllText(logPath, "");

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }

        Log("probe started");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var tcs = new TaskCompletionSource<Exception?>();

        // The whole test sequence runs on the dispatcher thread; app.Run() (below) pumps
        // it on this same STA thread. Shutdown() inside the sequence ends Run().
        app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Log("constructing HammerOverlayWindow (rect 100,100,400,80)");
                var overlay = new HammerOverlayWindow(new Rect(100, 100, 400, 80));
                Log("constructed OK");

                Log("calling Show()");
                overlay.Show();
                Log("Show() returned OK");

                Thread.Sleep(1500); // let a few animation ticks run
                Log("calling Complete()");
                overlay.Complete();
                Thread.Sleep(600); // through the self-hide delay
                Log("closing window");
                overlay.Close();
                Log("probe succeeded");
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                Log($"PROBE FAILED: {ex}");
                tcs.SetResult(ex);
            }
            finally
            {
                app.Shutdown();
            }
        });

        app.Run();
        var failure = tcs.Task.GetAwaiter().GetResult();
        Environment.Exit(failure is null ? 0 : 1);
    }
}
