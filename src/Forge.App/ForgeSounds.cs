using System.IO;

namespace Forge.App;

/// <summary>
/// The "clang" strike played when a translation lands in the source app — the forge's
/// hammer hitting the anvil. Isolation (S16): this is the ONLY owner of the sound
/// effects; best-effort by design, a missing file or audio failure never interrupts
/// a translation.
/// </summary>
public static class ForgeSounds
{
    public static void PlayClang()
    {
        Play("clang.wav", "clang");
    }

    /// <summary>
    /// A short paper crinkle played when the result is staged on the clipboard — the
    /// translation handed over to the clipboard's paper board. Deliberately softer and
    /// non-metallic so it is told apart from the clang by sound alone.
    /// </summary>
    public static void PlayPaper()
    {
        Play("paper.wav", "paper");
    }

    private static void Play(string fileName, string label)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (!File.Exists(path))
                return;
            // SoundPlayer.Play() is async (returns immediately); the object stays alive for the
            // duration of the short clip and is collected by GC afterwards.
            new System.Media.SoundPlayer(path).Play();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"{label}: failed to play — {ex.Message}");
        }
    }
}
