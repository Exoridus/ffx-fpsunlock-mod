using Fahrenheit.Mods.Vrr;
using Xunit;

public sealed class NativeFieldConfigTests
{
    [Fact]
    public void Native_field_profile_separates_native_arguments_from_presentation_holds()
    {
        var config = VrrConfig.ForNativeFieldProbe();
        Assert.True(config.NativeFieldProbe);
        Assert.Equal(30, config.TargetFramerateOverride);
        Assert.False(config.CharacterDelta);
        Assert.False(config.AtelWaits);
        Assert.False(config.Camera);
        Assert.False(config.MotionAdvanceLendFlag);
        Assert.False(config.AtelWorkerMotion);
        Assert.False(config.ChFacingTurnRate);
        Assert.False(config.SyncDataDisable);
        Assert.False(config.VideoOverride);
        Assert.True(config.VBlankRate);
        Assert.True(config.ParticleTimeline);
        Assert.True(config.TextureAnimation);
        Assert.True(config.Diagnostics);
    }

    [Fact]
    public void Ordinary_configuration_cannot_enable_the_diagnostic_path()
        => Assert.False(System.Text.Json.JsonSerializer.Deserialize<VrrConfig>("{\"NativeFieldProbe\":true}")!.NativeFieldProbe);
}
