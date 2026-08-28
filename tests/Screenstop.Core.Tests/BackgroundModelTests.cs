using Screenstop.Core.Background;
using Screenstop.Rendering;
using SkiaSharp;
using Xunit;

namespace Screenstop.Core.Tests;

public class BackgroundModelTests
{
    private static readonly SizeD Content = new(1600, 1000);

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "ScreenstopTests", Guid.NewGuid().ToString("N"));

    // ------------------------------------------------------------ layout

    [Fact]
    public void Layout_without_background_matches_content_size()
    {
        var layout = BackgroundLayout.Make(Content, new BackgroundSettings());

        Assert.Equal(Content.Width, layout.CanvasSize.Width);
        Assert.Equal(Content.Height, layout.CanvasSize.Height);
        Assert.Equal(0, layout.Padding);
        Assert.Equal(0, layout.ImageRect.X);
        Assert.Equal(0, layout.ImageRect.Y);
    }

    [Fact]
    public void Layout_with_solid_background_adds_padding_and_centers_image()
    {
        var settings = new BackgroundSettings
        {
            Style = BackgroundStyle.Solid("blue"),
            Padding = 0.10,
        };

        var layout = BackgroundLayout.Make(Content, settings);

        Assert.True(layout.CanvasSize.Width > Content.Width);
        Assert.True(layout.CanvasSize.Height > Content.Height);
        Assert.True(layout.Padding > 0);
        // Centered: equal breathing room on both sides.
        double leftGap = layout.ImageRect.X;
        double rightGap = layout.CanvasSize.Width - layout.ImageRect.MaxX;
        Assert.Equal(leftGap, rightGap, 3);
    }

    [Fact]
    public void Layout_with_border_only_grows_by_the_ring()
    {
        var settings = new BackgroundSettings();
        settings.Border.IsEnabled = true;
        settings.Border.Thickness = 0.01;

        var layout = BackgroundLayout.Make(Content, settings);
        double ring = settings.Border.PixelThickness(Content.Width, Content.Height);

        Assert.Equal(Content.Width + ring * 2, layout.CanvasSize.Width, 3);
        Assert.Equal(Content.Height + ring * 2, layout.CanvasSize.Height, 3);
    }

    [Fact]
    public void Layout_aspect_ratio_expands_canvas()
    {
        var settings = new BackgroundSettings
        {
            Style = BackgroundStyle.Solid("black"),
            Padding = 0.05,
            AspectRatio = BackgroundAspectRatio.SixteenNine,
        };

        var layout = BackgroundLayout.Make(Content, settings);
        double ratio = layout.CanvasSize.Width / layout.CanvasSize.Height;

        Assert.Equal(16.0 / 9.0, ratio, 3);
    }

    // ------------------------------------------------------------ settings

    [Fact]
    public void Clone_produces_an_equal_independent_copy()
    {
        var original = new BackgroundSettings
        {
            Style = BackgroundStyle.Gradient("aurora"),
            Padding = 0.2,
        };
        original.Camera.TiltXDegrees = 12;
        original.Border.IsEnabled = true;

        var clone = original.Clone();
        clone.Padding = 0.01;
        clone.Camera.TiltXDegrees = 0;

        Assert.Equal(original, original.Clone());
        Assert.NotEqual(original.Padding, clone.Padding);
        Assert.Equal(12, original.Camera.TiltXDegrees);
    }

    [Fact]
    public void PresetComparable_ignores_disabled_blur_and_border()
    {
        var a = new BackgroundSettings { Style = BackgroundStyle.Solid("red") };
        var b = new BackgroundSettings { Style = BackgroundStyle.Solid("red") };
        b.Border.IsEnabled = false;
        b.Border.Thickness = 0.05;
        b.ProgressiveBlur.IsEnabled = false;
        b.ProgressiveBlur.Strength = 30;

        Assert.True(a.PresetComparable().Equals(b.PresetComparable()));
    }

    [Fact]
    public void HasRenderableContent_reflects_active_effects()
    {
        var settings = new BackgroundSettings();
        Assert.False(settings.HasRenderableContent);

        settings.Style = BackgroundStyle.Solid("green");
        Assert.True(settings.HasRenderableContent);
    }

    // ------------------------------------------------------------ style

    [Fact]
    public void Solid_style_resolves_its_color()
    {
        var style = BackgroundStyle.Solid("blue");
        Assert.Equal(BackgroundColor.Blue, style.ResolveColor());
        Assert.Null(style.ResolveGradient());
    }

    [Fact]
    public void Gradient_style_resolves_its_gradient()
    {
        var style = BackgroundStyle.Gradient("aurora");
        Assert.Equal("aurora", style.ResolveGradient()?.GradientId);
        Assert.Null(style.ResolveColor());
    }

    [Fact]
    public void Unknown_ids_resolve_to_null()
    {
        Assert.Null(BackgroundStyle.Solid("nope").ResolveColor());
        Assert.Null(BackgroundStyle.Gradient("nope").ResolveGradient());
    }

    [Fact]
    public void Built_in_libraries_have_unique_ids()
    {
        Assert.Equal(BackgroundColor.PlainPresets.Count,
            BackgroundColor.PlainPresets.Select(c => c.ColorId).Distinct().Count());
        Assert.Equal(BackgroundGradient.Presets.Count,
            BackgroundGradient.Presets.Select(g => g.GradientId).Distinct().Count());
        Assert.True(BackgroundGradient.Presets.Count >= 16);
    }

    // ------------------------------------------------------------ shadow

    [Fact]
    public void Shadow_layer_is_null_at_zero_strength()
    {
        Assert.Null(ShadowStyle.Soft.Layer(0, 1000));
    }

    [Fact]
    public void Shadow_layer_grows_with_strength()
    {
        var small = ShadowStyle.Soft.Layer(0.2, 1000) ?? throw new InvalidOperationException("expected a layer");
        var large = ShadowStyle.Soft.Layer(0.9, 1000) ?? throw new InvalidOperationException("expected a layer");

        Assert.True(large.Radius > small.Radius);
        Assert.True(large.Alpha <= 0.5);
    }

    // ------------------------------------------------------------ camera

    [Fact]
    public void Default_camera_has_no_effect()
    {
        var camera = new CameraSettings();
        Assert.True(camera.IsDefault);
        Assert.False(camera.HasEffect);
    }

    [Fact]
    public void Camera_upgrade_moves_legacy_fov_to_v2()
    {
        var camera = new CameraSettings { ProjectionVersion = 1, FieldOfViewDegrees = 45 };
        camera.UpgradeProjectionIfNeeded();

        Assert.Equal(2, camera.ProjectionVersion);
        Assert.Equal(24, camera.FieldOfViewDegrees);
    }

    [Fact]
    public void Camera_zoom_counts_as_an_effect()
    {
        var camera = new CameraSettings { Zoom = 1.2 };
        Assert.True(camera.HasEffect);
    }

    // ------------------------------------------------------------ border

    [Fact]
    public void Border_thickness_is_a_fraction_of_the_shortest_edge()
    {
        var border = new BorderSettings { IsEnabled = true, Thickness = 0.01 };
        Assert.Equal(10, border.PixelThickness(1600, 1000), 3);
    }

    [Fact]
    public void Disabled_border_has_no_thickness()
    {
        var border = new BorderSettings { IsEnabled = false, Thickness = 0.01 };
        Assert.Equal(0, border.PixelThickness(1600, 1000));
    }

    // ------------------------------------------------------------ alignment

    [Fact]
    public void Center_alignment_rounds_all_corners()
    {
        var m = BackgroundAlignment.Center.CornerRadiusMultipliers();
        Assert.Equal(1, m.TopLeft);
        Assert.Equal(1, m.TopRight);
        Assert.Equal(1, m.BottomLeft);
        Assert.Equal(1, m.BottomRight);
    }

    [Fact]
    public void TopLeading_alignment_keeps_only_bottom_left_round()
    {
        var m = BackgroundAlignment.TopLeading.CornerRadiusMultipliers();
        Assert.Equal(0, m.TopLeft);
        Assert.Equal(0, m.TopRight);
        Assert.Equal(0, m.BottomLeft);
        Assert.Equal(1, m.BottomRight);
    }

    // ------------------------------------------------------------ overlay card layout

    [Fact]
    public void Card_layout_sanitize_places_every_action_exactly_once()
    {
        var store = new OverlayCardLayoutStore(TempDirectory());
        var layout = new OverlayCardLayout
        {
            TopLeading = CardAction.Copy,
            TopTrailing = CardAction.Copy, // duplicate
            BottomLeading = null,
            BottomTrailing = CardAction.Save,
            Center = new List<CardAction> { CardAction.Annotate, CardAction.View },
            Hidden = new List<CardAction>(),
        };

        store.Save(layout);

        var all = new List<CardAction?>
            {
                store.Layout.TopLeading,
                store.Layout.TopTrailing,
                store.Layout.BottomLeading,
                store.Layout.BottomTrailing,
            }
            .Where(a => a is not null)
            .Select(a => a!.Value)
            .Concat(store.Layout.Center)
            .Concat(store.Layout.Hidden)
            .ToList();

        Assert.Equal(Enum.GetValues<CardAction>().Length, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Card_layout_center_is_capped_at_three()
    {
        var store = new OverlayCardLayoutStore(TempDirectory());
        var layout = new OverlayCardLayout
        {
            TopLeading = null,
            TopTrailing = null,
            BottomLeading = null,
            BottomTrailing = null,
            Center = new List<CardAction>
            {
                CardAction.Copy, CardAction.Save, CardAction.Annotate,
                CardAction.View, CardAction.Delete,
            },
            Hidden = new List<CardAction>(),
        };

        store.Save(layout);
        Assert.Equal(OverlayCardLayout.MaxCenterActions, store.Layout.Center.Count);
    }

    // ------------------------------------------------------------ preset store

    [Fact]
    public void Preset_store_rejects_duplicate_names()
    {
        var store = new BackgroundPresetStore(TempDirectory());
        var settings = new BackgroundSettings { Style = BackgroundStyle.Solid("blue") };

        Assert.NotNull(store.SavePreset("My Preset", settings));
        Assert.Null(store.SavePreset("my preset", settings));
    }

    [Fact]
    public void Preset_store_normalizes_names()
    {
        Assert.Equal("frosted lake", BackgroundPresetStore.NormalizedName("  frosted   lake  "));
    }

    [Fact]
    public void Preset_store_round_trips_to_disk()
    {
        string directory = TempDirectory();
        var store = new BackgroundPresetStore(directory);
        var settings = new BackgroundSettings
        {
            Style = BackgroundStyle.Gradient("aurora"),
            Padding = 0.15,
        };
        store.SavePreset("Aurora Shot", settings);

        var reloaded = new BackgroundPresetStore(directory);
        var preset = reloaded.ByName("Aurora Shot");

        Assert.NotNull(preset);
        Assert.Equal("aurora", preset!.Background.Style.GradientId);
        Assert.Equal(0.15, preset.Background.Padding);
    }

    // ------------------------------------------------------------ glass border

    [Fact]
    public void Glass_border_renders_directional_gradient_ring()
    {
        using var content = GlassSolidContent(400, 300);
        var settings = new BackgroundSettings
        {
            Style = BackgroundStyle.Solid("black"),
            Padding = 0.14,
            Border = new BorderSettings
            {
                IsEnabled = true,
                Style = BorderStyle.Glass,
                Color = RgbaColor.White,
                Thickness = 0.03,
                Opacity = 0.5,
            },
        };

        using var result = BackgroundRenderer.Compose(content, settings);
        var layout = BackgroundLayout.Make(new SizeD(400, 300), settings);
        double borderWidth = settings.Border.PixelThickness(400, 300);
        var image = layout.ImageRect;

        int midX = (int)image.MidX;
        var topRing = result.GetPixel(midX, (int)(image.MinY - borderWidth / 2));
        var bottomRing = result.GetPixel(midX, (int)(image.MaxY + borderWidth / 2));

        Assert.True(topRing.Red > 60, $"top ring too dark: {topRing}");
        Assert.True(Math.Abs(topRing.Red - topRing.Blue) < 22, $"glass ring should be neutral: {topRing}");
        Assert.True(topRing.Red > bottomRing.Red, $"expected top {topRing} brighter than bottom {bottomRing}");
    }

    [Fact]
    public void Glass_border_style_survives_clone_and_equality()
    {
        var border = new BorderSettings
        {
            IsEnabled = true,
            Style = BorderStyle.Glass,
            Thickness = 0.006,
            Opacity = 0.5,
        };

        var clone = border.Clone();
        Assert.Equal(border, clone);
        Assert.Equal(BorderStyle.Glass, clone.Style);

        clone.Style = BorderStyle.Solid;
        Assert.NotEqual(border, clone);
    }

    private static SKBitmap GlassSolidContent(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(90, 120, 200));
        return bitmap;
    }
}
