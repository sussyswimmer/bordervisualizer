using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 1: each beat's grade (kick-like × accent × loud × Sensitivity) and the punch. Fed directly,
// one 10 ms step per frame. Expected values are the documented thresholds written out.
public sealed class BeatCharacterTests
{
    private const float Step = 0.01f;
    private static readonly AudioTuning Tuning = new();

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    // One onset: it fires, then four more steps with the same measures close its 35 ms window (it finishes 40 ms after
    // it fired). Returns the pulse on the frame it fired.
    private static float Onset(BeatCharacter c, float flux, float midFlux, float amplitude, float position, AudioTuning? tuning = null)
    {
        tuning ??= Tuning;
        c.Update(true, flux, midFlux, amplitude, position, Step, tuning);
        c.EndFrame(Step, tuning);
        float beat = c.Beat;
        for (int i = 0; i < 4; i++)
        {
            c.Update(false, flux, midFlux, amplitude, position, Step, tuning);
            c.EndFrame(Step, tuning);
        }
        return beat;
    }

    private static void Wait(BeatCharacter c, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++)
        {
            c.Update(false, 0, 0, 1, 0.9f, Step, Tuning);
            c.EndFrame(Step, Tuning);
        }
    }

    [Fact]
    public void AKickInALoudPassageIsAFullPulse()
    {
        // Bass flux / amplitude 1 (≥ 0.7), bass share 0.83 (≥ 0.35), loud (0.9 ≥ 0.7): exactly 1 on its frame.
        var c = new BeatCharacter();
        Assert.Equal(1f, Onset(c, 0.5f, 0.1f, 0.5f, 0.9f));
    }

    [Theory]
    [InlineData(0.5f, 0.1f, 1.0f, 0.5f)]    // kick-like 0.5: halfway from 0.3 to 0.7
    [InlineData(0.25f, 0.0f, 1.0f, 0f)]     // under 0.3: a syllable or a pluck in a mix
    [InlineData(0.5f, 1.1666667f, 0.5f, 0.5f)]  // bass share 0.3: halfway from 0.25 to 0.35
    [InlineData(0.5f, 2.5f, 0.5f, 0f)]      // bass share 0.17: a solo voice's syllable
    public void KickLikenessNeedsSubEnergyThatOutweighsTheMixAndTheHarmonics(float flux, float midFlux, float amplitude, float expected)
    {
        var c = new BeatCharacter();
        float smooth = expected is 0 or 1 ? expected : Smooth(0.5f);
        Assert.Equal(smooth, Onset(c, flux, midFlux, amplitude, 0.9f), 3);
    }

    [Fact]
    public void AnOnsetIsGradedAgainstTheStrongestRecentOne()
    {
        // A kick at flux 1, then 0.25 s later an off-beat bass note at half its flux. The kick's flux counts from the end
        // of its window (40 ms), and halves every 1.5 s: 0.5 / (0.85 × 2^(−0.21 / 1.5)).
        var c = new BeatCharacter();
        Assert.Equal(1f, Onset(c, 1f, 0.1f, 1f, 0.9f));
        Wait(c, 0.25f - 5 * Step);
        float expected = 0.5f / (0.85f * MathF.Pow(2, -0.21f / 1.5f));
        Assert.Equal(expected, Onset(c, 0.5f, 0.05f, 0.5f, 0.9f), 2);
        // Within 15 % of the strongest counts as full.
        var d = new BeatCharacter();
        Onset(d, 1f, 0.1f, 1f, 0.9f);
        Wait(d, 0.5f);
        Assert.Equal(1f, Onset(d, 0.85f * 0.85f, 0.05f, 0.6f, 0.9f));
    }

    [Theory]
    [InlineData(0.3f, 0f)]
    [InlineData(0.2f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.7f, 1f)]
    public void AKickFarUnderTheSongsLoudPartsGivesLittle(float position, float expected)
    {
        var c = new BeatCharacter();
        Assert.Equal(expected, Onset(c, 0.5f, 0.1f, 0.5f, position), 3);
    }

    [Fact]
    public void SensitivityScalesTheGradeAndClamps()
    {
        var half = new BeatCharacter();
        var twice = new BeatCharacter();
        Assert.Equal(0.5f, Onset(half, 0.5f, 0.1f, 0.5f, 0.9f, Tuning with { Sensitivity = 0.5f }), 3);
        Assert.Equal(1f, Onset(twice, 0.5f, 0.1f, 0.5f, 0.9f, Tuning with { Sensitivity = 2f }));
        // At 2, a half-loud kick (0.5) is full.
        var loud = new BeatCharacter();
        Assert.Equal(1f, Onset(loud, 0.5f, 0.1f, 0.5f, 0.5f, Tuning with { Sensitivity = 2f }));
    }

    [Fact]
    public void BeatIsTheGradeOnItsFrameThenDecaysEveryFrame()
    {
        // As doc 03 §2: the value on the frame the beat lands, then exp(−dt / BeatDecaySeconds) per frame.
        var c = new BeatCharacter();
        c.Update(true, 0.5f, 0.1f, 0.5f, 0.9f, Step, Tuning);
        c.Update(false, 0.5f, 0.1f, 0.5f, 0.9f, Step, Tuning);   // a second step in the same frame
        c.EndFrame(2 * Step, Tuning);
        Assert.Equal(1f, c.Beat);
        c.EndFrame(0.12f, Tuning);
        Assert.Equal(MathF.Exp(-1), c.Beat, 4);
    }

    [Fact]
    public void AGradeThatGrowsRaisesThePulseButNeverHoldsIt()
    {
        var c = new BeatCharacter();
        c.Update(true, 0.25f, 0.05f, 0.5f, 0.9f, Step, Tuning);  // kick-like 0.5 so far
        c.EndFrame(Step, Tuning);
        Assert.Equal(0.5f, c.Beat, 3);
        c.Update(false, 0.5f, 0.05f, 0.5f, 0.9f, Step, Tuning);  // the kick fills the window: 1
        c.EndFrame(Step, Tuning);
        Assert.Equal(1f, c.Beat);
        c.Update(false, 0.5f, 0.05f, 0.5f, 0.9f, Step, Tuning);  // no higher: decays
        c.EndFrame(Step, Tuning);
        Assert.Equal(MathF.Exp(-Step / 0.12f), c.Beat, 5);
    }

    [Fact]
    public void PunchRisesWithKicksHoldsAndFades()
    {
        var c = new BeatCharacter();
        Onset(c, 0.5f, 0.1f, 0.5f, 0.9f);
        Assert.Equal(0.5f, c.Punch, 3);                // one kick is half a groove
        Onset(c, 0.5f, 0.1f, 0.5f, 0.9f);
        Assert.Equal(1f, c.Punch);
        Wait(c, 1.4f);
        Assert.Equal(1f, c.Punch);                     // held for 1.5 s after the last kick-like onset
        Wait(c, 0.1f + 2.5f);
        Assert.Equal(MathF.Exp(-1), c.Punch, 2);       // then fades with τ = 2.5 s
        // Onsets that aren't kick-like add nothing and don't renew the hold.
        var d = new BeatCharacter();
        for (int i = 0; i < 10; i++) Onset(d, 0.1f, 0.5f, 1f, 0.9f);
        Assert.Equal(0f, d.Punch);
    }

    [Fact]
    public void ResetAndBadInputLeaveAQuietFiniteState()
    {
        var c = new BeatCharacter();
        Onset(c, 0.5f, 0.1f, 0.5f, 0.9f);
        c.Update(true, float.NaN, float.PositiveInfinity, 0, float.NaN, float.NaN, Tuning);
        c.EndFrame(float.NaN, Tuning);
        Assert.True(float.IsFinite(c.Beat) && float.IsFinite(c.Punch));
        c.Reset();
        Assert.Equal(0f, c.Beat);
        Assert.Equal(0f, c.Punch);
    }
}
