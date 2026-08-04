/// <summary>
/// Layout constants for the Universal LPC spritesheet format this project's raw
/// character/equipment assets use. Shared between runtime (DirectionalSpriteAnimator,
/// for animation lengths) and the Editor-only slicer (for the pixel grid).
/// </summary>
public static class LpcSpriteFormat
{
    public const int FrameSize = 64;
    public const int WalkFrames = 9;
    public const int SlashFrames = 6;
    public const int HurtFrames = 6;
}
