using System;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Resolves the xemu folder where the Chihiro core creates blank cards. The
/// native core uses SDL_GetPrefPath("xemu", "xemu"), so the path sits under the
/// platform's per-user data folder rather than the shell's folder.
/// </summary>
public static class ChihiroCardPaths
{
    /// <summary>Returns the xemu Chihiro cards folder.</summary>
    /// <returns>An absolute folder path.</returns>
    public static string ResolveCardsDirectory()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support");
        }
        else
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share");
            }
        }

        return Path.Combine(root, "xemu", "xemu", "cards");
    }
}
