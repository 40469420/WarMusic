using System.Text.Json;
using System.Windows;
using WarMusic.Models;
using WarMusic.Services;

namespace WarMusic.UnitTests;

public sealed class OverlayTests
{
    [Fact]
    public void OffScreenPlacementMovesBackOntoTheVirtualDisplay()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        var fitted = OverlayPlacement.Fit(new Rect(8000, -400, 312, 140), screen);
        Assert.InRange(fitted.X, screen.Left, screen.Right - 80);
        Assert.InRange(fitted.Y, screen.Top, screen.Bottom - 40);
    }

    [Fact]
    public void MissingMonitorStillKeepsAUsableOrigin()
    {
        var fitted = OverlayPlacement.Fit(new Rect(double.NaN, double.NaN, double.NaN, double.NaN), Rect.Empty);
        Assert.Equal(OverlayPlacement.DefaultLeft, fitted.X);
        Assert.Equal(OverlayPlacement.DefaultTop, fitted.Y);
        Assert.Equal(OverlayPlacement.DefaultWidth, fitted.Width);
    }

    [Fact]
    public void SizeAndOpacityStayWithinSensibleLimits()
    {
        Assert.Equal(OverlayPlacement.MinWidth, OverlayPlacement.ClampWidth(10));
        Assert.Equal(OverlayPlacement.MaxWidth, OverlayPlacement.ClampWidth(2000));
        Assert.Equal(OverlayPlacement.DefaultWidth, OverlayPlacement.ClampWidth(double.NaN));
        Assert.Equal(OverlayPlacement.MinOpacity, OverlayPlacement.ClampOpacity(0));
        Assert.Equal(OverlayPlacement.MaxOpacity, OverlayPlacement.ClampOpacity(2));
        Assert.Equal(OverlayPlacement.DefaultOpacity, OverlayPlacement.ClampOpacity(double.NaN));
    }

    [Fact]
    public void OverlayLabelsDoNotClaimTheGameIsReceivingAudio()
    {
        var hud = OverlayHudState.Describe(true, false, true, "Spotify", false, false, null, false, 0);
        Assert.Equal("Spotify", hud.Headline);
        Assert.Equal("Application audio · capture only", hud.Kind);
        Assert.Equal("Music muted", hud.Music);
        Assert.Equal("Cable connected", hud.Cable);
        Assert.False(hud.LocalPlayback);
    }

    [Fact]
    public void LocalPlaybackIsDistinctFromApplicationCapture()
    {
        var hud = OverlayHudState.Describe(true, true, true, "Spotify", true, false, "Convoy ambience", true, 2);
        Assert.Equal("Convoy ambience", hud.Headline);
        Assert.Equal("Local sound playing", hud.Kind);
        Assert.Equal("Spotify", hud.Application);
        Assert.True(hud.Ducking);
        Assert.True(hud.CanPlayNext);
        Assert.True(hud.LocalPlayback);
    }

    [Fact]
    public void PausedLocalSoundDoesNotLookLikeApplicationControl()
    {
        var hud = OverlayHudState.Describe(true, true, true, "Spotify", true, true, "Radio check", false, 0);
        Assert.Equal("Local sound · paused", hud.Kind);
        Assert.False(hud.CanPlayNext);
        Assert.Equal("Music enabled", hud.Music);
    }

    [Fact]
    public void DisconnectedCableAndIdleBoardStayExplicit()
    {
        var hud = OverlayHudState.Describe(false, false, false, null, false, false, "Nothing playing", false, 0);
        Assert.Equal("No local sound", hud.Headline);
        Assert.Equal("Idle", hud.Kind);
        Assert.Equal("Cable disconnected", hud.Cable);
        Assert.Equal("No application connected", hud.Application);
        Assert.Equal("Music not routed", hud.Music);
    }

    [Fact]
    public void OverlaySettingsSurviveSaveAndInvalidValuesAreClamped()
    {
        using var workspace = new TestWorkspace();
        var settings = new Settings
        {
            OverlayLeft = 120,
            OverlayTop = 40,
            OverlayWidth = 12,
            OverlayHeight = 900,
            OverlayOpacity = 2,
            OverlayExpanded = true,
            OverlayToggleKey = "Control+Shift+H",
            OverlayInteractKey = " ",
        };

        Store.Sanitize(settings);
        Store.Save(settings);
        var loaded = Store.Load();

        Assert.Equal(120, loaded.OverlayLeft);
        Assert.Equal(40, loaded.OverlayTop);
        Assert.Equal(OverlayPlacement.MinWidth, loaded.OverlayWidth);
        Assert.Equal(OverlayPlacement.MaxHeight, loaded.OverlayHeight);
        Assert.Equal(OverlayPlacement.MaxOpacity, loaded.OverlayOpacity);
        Assert.True(loaded.OverlayExpanded);
        Assert.Equal("Control+Shift+H", loaded.OverlayToggleKey);
        Assert.Equal(OverlayPlacement.DefaultInteractKey, loaded.OverlayInteractKey);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Store.Data, "settings.json")));
        Assert.Equal(OverlayPlacement.MaxOpacity, json.RootElement.GetProperty(nameof(Settings.OverlayOpacity)).GetDouble());
    }

    [Fact]
    public void ScreenPointsUnpackNegativeMonitorCoordinates()
    {
        int packed = unchecked((short)-120 & 0xFFFF | ((short)40 << 16));
        OverlayNative.ScreenPoint(new IntPtr(packed), out int x, out int y);
        Assert.Equal(-120, x);
        Assert.Equal(40, y);
        Assert.True(OverlayNative.Contains(4, 6, 48, 22));
        Assert.False(OverlayNative.Contains(-1, 6, 48, 22));
        Assert.False(OverlayNative.Contains(4, 40, 48, 22));
    }

    [Fact]
    public void InvalidOverlayHotkeysFallBackWithoutThrowing()
    {
        Assert.Equal(OverlayPlacement.DefaultToggleKey, OverlayPlacement.NormalizeHotkey("", OverlayPlacement.DefaultToggleKey));
        Assert.Equal(OverlayPlacement.DefaultInteractKey, OverlayPlacement.NormalizeHotkey("not a key", OverlayPlacement.DefaultInteractKey));
        Assert.Equal("Control+Shift+U", OverlayPlacement.NormalizeHotkey("Control+Shift+U", OverlayPlacement.DefaultInteractKey));
    }
}
