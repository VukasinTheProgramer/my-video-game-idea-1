using UnityEngine;

/// <summary>Single place every runtime-created Text picks its font from - was
/// Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf") copy-pasted at every
/// call site, so switching the game's font meant hunting down each one.</summary>
public static class UIFonts
{
    private const string ResourcePath = "Fonts/LowresPixel-Regular";

    private static Font cached;

    public static Font Default
    {
        get
        {
            if (cached == null)
            {
                cached = Resources.Load<Font>(ResourcePath);
                if (cached == null)
                {
                    Debug.LogError($"UIFonts: no font at Resources/{ResourcePath} - falling back to LegacyRuntime.");
                    cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            return cached;
        }
    }
}
