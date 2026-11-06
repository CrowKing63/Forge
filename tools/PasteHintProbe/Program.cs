using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forge.App;

// Probe: instantiate PasteHintWindow and Show() it, asserting on rendered text content
// and geometry, then verify the window fades out and closes itself (no caller involvement).
// The WPF message loop must pump (app.Run on its own STA thread); the whole test sequence
// runs on the dispatcher thread via BeginInvoke. Waits use Task.Delay so the dispatcher
// keeps pumping — a Thread.Sleep here would starve the window's own auto-close timer.
internal static class Program
{
    [STAThread]
    static void Main()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "paste-hint-probe.log");
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

        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                Log("constructing PasteHintWindow (rect 100,100,400,80)");
                var hint = new PasteHintWindow(new Rect(100, 100, 400, 80));
                Log("constructed OK");

                Log("calling Show()");
                hint.Show();
                await Task.Delay(800); // let the entrance animation run
                Log("Show() returned OK");

                // Assert on rendered TEXT content as well as geometry.
                var texts = CollectTexts(hint);
                Log("rendered texts: " + string.Join(" | ", texts));
                if (!texts.Contains("Press Ctrl+V"))
                    throw new Exception("main line 'Press Ctrl+V' not found");
                Log("text assertions passed");

                // The clipboard glyph must be present: board + clip tab.
                var rects = CountRectangles(hint);
                if (rects < 2)
                    throw new Exception($"expected at least 2 rectangles (clipboard glyph), found {rects}");
                Log($"glyph assertion passed ({rects} rectangles)");

                // Geometry: the window is positioned so the CARD (150x72, inset by the shadow
                // padding) centers over the target rect — same convention as HammerOverlayWindow.
                var dpi = VisualTreeHelper.GetDpi(hint);
                var expectedLeft = (100 + 400 / 2 - 150 / 2) / dpi.DpiScaleX;
                var expectedTop = (100 + 80 / 2 - 72 / 2) / dpi.DpiScaleY;
                if (Math.Abs(hint.Left - expectedLeft) > 1 || Math.Abs(hint.Top - expectedTop) > 1)
                    throw new Exception($"position off: got ({hint.Left}, {hint.Top}), expected ({expectedLeft}, {expectedTop})");
                Log($"position assertion passed ({hint.Left}, {hint.Top})");

                // The window must close itself within its duration + margin.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (hint.IsLoaded && DateTime.UtcNow < deadline)
                    await Task.Delay(200);
                if (hint.IsLoaded)
                    throw new Exception("window did not auto-close within 10s");
                Log("auto-close verified");

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

    /// <summary>Counts every Rectangle in the window's visual tree.</summary>
    private static int CountRectangles(DependencyObject root)
    {
        var count = 0;
        void Walk(DependencyObject node)
        {
            if (node is System.Windows.Shapes.Rectangle)
                count++;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                    Walk(child);
        }
        Walk(root);
        return count;
    }

    /// <summary>Collects every TextBlock's text in the window's visual tree.</summary>
    private static List<string> CollectTexts(DependencyObject root)
    {
        var texts = new List<string>();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                texts.Add(tb.Text);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                    Walk(child);
        }
        Walk(root);
        return texts;
    }
}
