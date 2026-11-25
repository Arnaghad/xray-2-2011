using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Shell;

public sealed class AvaloniaImageAtlas
{
    public const string DefaultAtlasUri = "avares://xray.editor.avalonia_controls/Assets/images16x16.png";
    public const int IconSize = 16;

    private readonly Bitmap? m_atlasBitmap;
    private readonly int m_iconCount;
    private readonly Dictionary<int, IImage> m_cache = new();
    private IImage? m_fallback;

    public AvaloniaImageAtlas(string assetUri = DefaultAtlasUri, int expectedCount = (int)images16x16.images16x16_count)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(assetUri));
            m_atlasBitmap = new Bitmap(stream);
            int slices = m_atlasBitmap.PixelSize.Width / IconSize;
            m_iconCount = Math.Max(slices, expectedCount);
        }
        catch
        {
            m_atlasBitmap = null;
            m_iconCount = expectedCount;
        }
    }

    public static AvaloniaImageAtlas Shared { get; } = new();

    public int IconCount => m_iconCount;
    public bool HasAtlasBitmap => m_atlasBitmap != null;

    public IImage GetIcon(images16x16 icon) => GetIcon((int)icon);

    public IImage GetIcon(int index)
    {
        if (m_cache.TryGetValue(index, out var cached))
            return cached;

        IImage created;
        if (m_atlasBitmap != null && index >= 0 && (index + 1) * IconSize <= m_atlasBitmap.PixelSize.Width && m_atlasBitmap.PixelSize.Height >= IconSize)
        {
            created = new CroppedBitmap(m_atlasBitmap, new PixelRect(index * IconSize, 0, IconSize, IconSize));
        }
        else
        {
            created = GetFallbackIcon();
        }

        m_cache[index] = created;
        return created;
    }

    private IImage GetFallbackIcon()
    {
        if (m_fallback != null)
            return m_fallback;

        var bmp = new WriteableBitmap(
            new PixelSize(IconSize, IconSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
        {
            int count = IconSize * IconSize;
            for (int i = 0; i < count; ++i)
                Marshal.WriteInt32(fb.Address, i * 4, unchecked((int)0xFF4080C0u));
        }
        m_fallback = bmp;
        return m_fallback;
    }
}
