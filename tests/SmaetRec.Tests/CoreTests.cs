using SmaetRec.Core;

namespace SmaetRec.Tests;

public class LayoutTests
{
    [Theory]
    [InlineData(LayoutPreset.SideBySide)]
    [InlineData(LayoutPreset.PictureInPicture)]
    public void Presets_keep_both_layers_inside_the_canvas(LayoutPreset preset)
    {
        var scene = new Scene();
        scene.ApplyPreset(preset);
        foreach (var l in scene.LayersBottomToTop)
        {
            Assert.True(l.CenterX - l.Width / 2 >= 0 && l.CenterX + l.Width / 2 <= Scene.CanvasWidth, $"{l.Kind} x");
            Assert.True(l.CenterY - l.Height / 2 >= 0 && l.CenterY + l.Height / 2 <= Scene.CanvasHeight, $"{l.Kind} y");
        }
    }

    [Fact]
    public void SideBySide_does_not_overlap()
    {
        var scene = new Scene();
        scene.ApplyPreset(LayoutPreset.SideBySide);
        Assert.True(scene.Screen.CenterX + scene.Screen.Width / 2 < scene.Phone.CenterX - scene.Phone.Width / 2);
    }

    [Fact]
    public void HitTest_respects_rotation()
    {
        var l = new Layer { CenterX = 500, CenterY = 500, Width = 400, Aspect = 4, RotationDeg = 90 };
        Assert.True(l.Contains(new Vec2(500, 680)));   // along the rotated long axis
        Assert.False(l.Contains(new Vec2(680, 500)));  // would be inside if not rotated
    }

    [Fact]
    public void Resize_follows_pointer_and_keeps_aspect()
    {
        var l = new Layer { CenterX = 960, CenterY = 540, Width = 400, Aspect = 2 };
        l.ResizeTo(new Vec2(960 + 300, 540 + 150));
        Assert.Equal(600, l.Width, 3);
        Assert.Equal(300, l.Height, 3);
        l.ResizeTo(new Vec2(960, 540));
        Assert.Equal(Layer.MinWidth, l.Width);
    }

    [Fact]
    public void Rotate_snaps_to_right_angles()
    {
        var l = new Layer { CenterX = 100, CenterY = 100, Width = 50 };
        l.RotateTo(new Vec2(200, 102));
        Assert.Equal(90, l.RotationDeg);
        l.RotateTo(new Vec2(200, 200));
        Assert.Equal(135, l.RotationDeg, 3);
    }

    [Fact]
    public void Selected_layer_handles_win_over_other_layers()
    {
        var scene = new Scene();
        scene.ApplyPreset(LayoutPreset.PictureInPicture);
        var corner = scene.Phone.HandlePosition(LayerHandle.TopLeft);
        var (layer, handle) = scene.HitTest(corner, scene.Phone, 12);
        Assert.Same(scene.Phone, layer);
        Assert.Equal(LayerHandle.TopLeft, handle);
        var (top, _) = scene.HitTest(scene.Phone.Center, null, 12);
        Assert.Same(scene.Phone, top); // phone is above the screen
    }

    [Fact]
    public void Phone_landscape_swaps_shape_and_keeps_area()
    {
        var scene = new Scene();
        double area = scene.Phone.Width * scene.Phone.Height;
        scene.SetPhoneLandscape(true);
        Assert.True(scene.Phone.Width > scene.Phone.Height);
        Assert.Equal(area, scene.Phone.Width * scene.Phone.Height, 3);
    }

    [Fact]
    public void Fit_letterboxes()
    {
        var r = RectD.Fit(2, new RectD(0, 0, 100, 100));
        Assert.Equal(new RectD(0, 25, 100, 50), r);
    }
}

public class AnnotationTests
{
    private static Annotation Line(double x0, double y0, double x1, double y1, AnnotationKind kind = AnnotationKind.Arrow) =>
        new() { Kind = kind, Points = { new Vec2(x0, y0), new Vec2(x1, y1) } };

    [Fact]
    public void Undo_and_redo_walk_through_history()
    {
        var store = new AnnotationStore();
        store.Add(Line(0, 0, 100, 0));
        store.Add(Line(0, 50, 100, 50));
        Assert.Equal(2, store.Count);
        store.Undo();
        Assert.Single(store.Snapshot());
        store.Undo();
        Assert.Empty(store.Snapshot());
        Assert.False(store.CanUndo);
        store.Redo(); store.Redo();
        Assert.Equal(2, store.Count);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void New_drawing_clears_redo()
    {
        var store = new AnnotationStore();
        store.Add(Line(0, 0, 100, 0));
        store.Undo();
        store.Add(Line(0, 0, 0, 100));
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void Eraser_removes_topmost_hit_and_is_undoable()
    {
        var store = new AnnotationStore();
        store.Add(Line(0, 0, 100, 0));
        store.Add(Line(50, -50, 50, 50));
        Assert.True(store.EraseAt(new Vec2(50, 0), 5));
        Assert.Single(store.Snapshot());
        Assert.Equal(100, store.Snapshot()[0].Points[1].X);
        Assert.False(store.EraseAt(new Vec2(500, 500), 5));
        store.Undo();
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Accidental_clicks_are_not_kept()
    {
        var store = new AnnotationStore();
        store.Add(Line(10, 10, 11, 11, AnnotationKind.Rectangle));
        store.Add(new Annotation { Kind = AnnotationKind.Text, Text = "  ", Points = { new Vec2(0, 0) } });
        Assert.Equal(0, store.Count);
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void In_progress_stroke_is_visible_but_not_in_history()
    {
        var store = new AnnotationStore();
        var a = new Annotation { Kind = AnnotationKind.Pen, Points = { new Vec2(1, 1) } };
        store.SetInProgress(a);
        Assert.Single(store.Snapshot());
        Assert.Equal(0, store.Count);
        a.Points.Add(new Vec2(5, 5));
        store.Add(a);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Shape_outlines_hit_only_near_the_edge()
    {
        var rect = Line(0, 0, 200, 100, AnnotationKind.Rectangle);
        Assert.True(rect.HitTest(new Vec2(0, 50), 4));
        Assert.False(rect.HitTest(new Vec2(100, 50), 4));
        var ellipse = Line(0, 0, 200, 100, AnnotationKind.Ellipse);
        Assert.True(ellipse.HitTest(new Vec2(100, 0), 4));
        Assert.False(ellipse.HitTest(new Vec2(100, 50), 4));
        var blur = Line(0, 0, 200, 100, AnnotationKind.Blur);
        Assert.True(blur.HitTest(new Vec2(100, 50), 4));
    }
}

public class ProjectTests
{
    [Fact]
    public void Project_round_trips_layout_drawings_and_look()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ProjectFile.Extension);
        try
        {
            var scene = new Scene();
            scene.ApplyPreset(LayoutPreset.PictureInPicture);
            scene.Phone.RotationDeg = 33; scene.Phone.CenterX = 321;
            scene.Effects.BackgroundIndex = 3; scene.Effects.Shadows = false;
            scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Text, Text = "مرحبا", Color = 0xFF00FF00, Points = { new Vec2(5, 6) } });
            scene.Annotations.Add(new Annotation { Kind = AnnotationKind.Pen, Thickness = 9, Points = { new Vec2(1, 2), new Vec2(3, 4) } });
            ProjectFile.Save(path, scene);

            var loaded = new Scene();
            Assert.True(ProjectFile.TryLoad(path, loaded));
            Assert.Equal(33, loaded.Phone.RotationDeg);
            Assert.Equal(321, loaded.Phone.CenterX);
            Assert.Equal(LayoutPreset.PictureInPicture, loaded.LastPreset);
            Assert.Equal(3, loaded.Effects.BackgroundIndex);
            Assert.False(loaded.Effects.Shadows);
            var items = loaded.Annotations.Snapshot();
            Assert.Equal(2, items.Length);
            Assert.Equal("مرحبا", items[0].Text);
            Assert.Equal(0xFF00FF00u, items[0].Color);
            Assert.Equal(new Vec2(3, 4), items[1].Points[1]);
            Assert.False(loaded.Annotations.CanUndo);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Broken_or_foreign_files_are_rejected_without_changing_the_scene()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ProjectFile.Extension);
        try
        {
            var scene = new Scene();
            double before = scene.Phone.CenterX;
            File.WriteAllText(path, "{ not json");
            Assert.False(ProjectFile.TryLoad(path, scene));
            File.WriteAllText(path, "{\"App\":\"other\",\"Scene\":{}}");
            Assert.False(ProjectFile.TryLoad(path, scene));
            Assert.False(ProjectFile.TryLoad(path + ".missing", scene));
            Assert.Equal(before, scene.Phone.CenterX);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Settings_normalize_bad_values()
    {
        var s = new AppSettings { Language = "fr", Fps = 24, ReceiverName = " " };
        s.Normalize();
        Assert.Equal("ar", s.Language);
        Assert.Equal(30, s.Fps);
        Assert.Equal("SMAET REC", s.ReceiverName);
        Assert.Equal((3840, 2160), (new AppSettings { Quality = VideoQuality.Uhd4K }.OutputWidth, new AppSettings { Quality = VideoQuality.Uhd4K }.OutputHeight));
    }

    [Fact]
    public void Recording_names_never_collide()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smaet-" + Guid.NewGuid());
        try
        {
            var now = new DateTime(2026, 1, 2, 3, 4, 5);
            var a = AppPaths.NewRecordingPath(dir, now);
            File.WriteAllText(a, "");
            var b = AppPaths.NewRecordingPath(dir, now);
            Assert.NotEqual(a, b);
            Assert.EndsWith(".mp4", b);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ZoomAndTextTests
{
    [Fact]
    public void Zoom_animates_to_target_and_stays_inside_the_canvas()
    {
        var z = new ZoomController();
        z.ZoomBy(2, new Vec2(0, 0));
        for (int i = 0; i < 200 && z.Step(1 / 60.0); i++) { }
        Assert.Equal(2, z.Current, 2);
        var r = z.VisibleRegion();
        Assert.Equal(0, r.X, 3);
        Assert.Equal(0, r.Y, 3);
        Assert.Equal(Scene.CanvasWidth / 2, r.Width, 1);
        z.ZoomBy(100);
        Assert.Equal(ZoomController.MaxZoom, z.Target);
        z.Reset();
        for (int i = 0; i < 400 && z.Step(1 / 60.0); i++) { }
        Assert.Equal(new RectD(0, 0, Scene.CanvasWidth, Scene.CanvasHeight), z.VisibleRegion());
    }

    [Fact]
    public void Every_text_has_arabic_and_english()
    {
        foreach (var (key, (ar, en)) in Loc.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ar), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.Equal(ar.Count(c => c == '{'), en.Count(c => c == '{'));
        }
    }

    [Fact]
    public unsafe void Nv12_converts_grey_and_colour()
    {
        // 2x2 picture: Y plane 2 rows, UV plane 1 row. Mid grey then pure-ish red.
        byte[] src = { 126, 126, 126, 126, 128, 128 };
        byte[] dst = new byte[16];
        fixed (byte* s = src) fixed (byte* d = dst) PixelConvert.Nv12ToBgra(s, 2, 2, 2, 2, d, 8);
        Assert.InRange(dst[0], 126, 130); Assert.InRange(dst[1], 126, 130); Assert.InRange(dst[2], 126, 130); Assert.Equal(255, dst[3]);
        byte[] red = { 63, 63, 63, 63, 102, 240 };
        fixed (byte* s = red) fixed (byte* d = dst) PixelConvert.Nv12ToBgra(s, 2, 2, 2, 2, d, 8);
        Assert.True(dst[2] > 240 && dst[1] < 20 && dst[0] < 20, $"{dst[2]},{dst[1]},{dst[0]}");
    }
}
