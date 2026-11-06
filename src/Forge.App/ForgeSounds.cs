using System.IO;

namespace Forge.App;

/// <summary>
/// The "clang" strike played when a translation lands in the source app — the forge's
/// hammer hitting the anvil. Isolation (S16): this is the ONLY owner of the sound
/// effect; best-effort by design, a missing file or audio failure never interrupts
/// a translation.
/// </summary>
public static class ForgeSounds
{
    public static void PlayClang()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "clang.wav");
            if (!File.Exists(path))
                return;
            // SoundPlayer.Play() is async (returns immediately); the object stays alive for the
            // duration of the short clip and is collected by GC afterwards.
            new System.Media.SoundPlayer(path).Play();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"clang: failed to play — {ex.Message}");
        }
    }
}
