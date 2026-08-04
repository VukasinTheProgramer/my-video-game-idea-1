using System.Linq;
using UnityEditor;
using UnityEngine;

// TextureImporter.spritesheet is marked obsolete in favour of ISpriteEditorDataProvider,
// but it still works and the provider API needs a lot more ceremony for what is
// one-shot, editor-only slicing. Revisit if Unity actually removes it.
#pragma warning disable 0618

/// <summary>
/// Shared LPC frame-slicing helper: grid-slices a raw animation strip (one row
/// per direction, 64px cells) into individual Sprites with a bottom-center pivot
/// so they line up with the corner-anchored grid the same way the base body does.
/// </summary>
public static class LpcSpriteSlicer
{
    /// <summary>Slices a single-row strip (e.g. hurt.png, or a pre-cropped per-direction file) into frameCount sprites.</summary>
    public static Sprite[] SliceRow(string path, int frameCount)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            Debug.LogError($"LpcSpriteSlicer: no asset at {path}");
            return new Sprite[0];
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = LpcSpriteFormat.FrameSize;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var metas = new SpriteMetaData[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            metas[i] = new SpriteMetaData
            {
                name = $"frame_{i}",
                rect = new Rect(i * LpcSpriteFormat.FrameSize, 0, LpcSpriteFormat.FrameSize, LpcSpriteFormat.FrameSize),
                pivot = new Vector2(0.5f, 0f),
                alignment = (int)SpriteAlignment.Custom
            };
        }
        importer.spritesheet = metas;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
            .ToArray();
    }

    /// <summary>Slices one direction-row (0=up,1=left,2=down,3=right) out of a full multi-row LPC sheet.</summary>
    public static Sprite[] SliceDirectionRow(string path, int directionRowIndex, int frameCount, int rowsInFile)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            Debug.LogError($"LpcSpriteSlicer: no asset at {path}");
            return new Sprite[0];
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = LpcSpriteFormat.FrameSize;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // existing metadata from other rows already sliced on this same file must be preserved
        var existing = importer.spritesheet ?? new SpriteMetaData[0];
        var keep = existing.Where(m => !m.name.StartsWith($"row{directionRowIndex}_")).ToArray();
        var newMeta = new SpriteMetaData[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            newMeta[i] = new SpriteMetaData
            {
                name = $"row{directionRowIndex}_frame_{i}",
                // LPC sheets are laid out top-down (row 0 = up), but Unity's texture
                // space has its origin at the BOTTOM-left, so the row index has to be
                // flipped - otherwise every direction comes out mirrored (0 gave the
                // bottom row, i.e. right-facing art, instead of up). This is the only
                // thing rowsInFile is for.
                rect = new Rect(
                    i * LpcSpriteFormat.FrameSize,
                    (rowsInFile - 1 - directionRowIndex) * LpcSpriteFormat.FrameSize,
                    LpcSpriteFormat.FrameSize,
                    LpcSpriteFormat.FrameSize),
                pivot = new Vector2(0.5f, 0f),
                alignment = (int)SpriteAlignment.Custom
            };
        }
        importer.spritesheet = keep.Concat(newMeta).ToArray();
        importer.SaveAndReimport();

        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .Where(s => s.name.StartsWith($"row{directionRowIndex}_"))
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
            .ToArray();
    }
}

#pragma warning restore 0618
