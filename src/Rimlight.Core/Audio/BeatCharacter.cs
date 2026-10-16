namespace Rimlight.Core.Audio;

// How hard each detected beat lands, and how percussive the music is right now (Maxwell's feedback, round 1: calm songs
// should move like a wave, rap may bounce hard, and a whispered line must never give a big bounce).
//
// BeatDetector (doc 03 §2) finds bass-band onsets. In a drumless ballad those are a baritone's syllables (the fundamental,
// 98–147 Hz, lies in the 30–150 Hz beat band) and the guitar's low strings, and before this class each of them set Beat
// to 1, the same full pulse as a kick. Each onset is now graded, 0..1, as the product of:
//   kick-like  two things a kick has and a syllable or a plucked string hasn't, multiplied:
//              - the onset's bass flux over the broadband amplitude of the same window (√ BandAnalyzer.WeightedPower with
//                SongLoudness's weights). A kick, an 808 or an EDM kick puts a burst of sub energy into the mix that is
//                large next to everything else playing; a sung syllable or a plucked string adds a little to a band its
//                harmonics already fill. Measured on the procedural StyleTracks (10 ms steps): every onset of the
//                drumless ballads scores at most 0.31, the 808s at least 0.78, EDM kicks 0.98–1.25 and acoustic kicks
//                mostly above 1. Smoothstep from KickLikeFrom to KickLikeAt.
//              - the bass share of the onset's rise: bass flux / (bass + Mid band flux), summed over the onset window.
//                A voice's harmonics rise with its fundamental, so even a solo baritone, whose low syllables can carry
//                most of its energy in the beat band (bass flux over amplitude up to 0.7), scores at most 0.24; 808s
//                score at least 0.38, EDM kicks and bass notes at least 0.32, acoustic kicks mostly above 0.5.
//                Smoothstep from BassShareFrom to BassShareAt.
//   accent     the onset's flux over the strongest recent onset (a peak that halves every AccentHalfLifeSeconds), with
//              AccentTolerance: kicks within 15 % of the strongest are full, an off-beat bass note at half a kick's flux
//              lands at about 0.6, a ghost note lower.
//   loud       SongLoudness.Position through a smoothstep from QuietFrom to LoudAt: an onset in a passage 17 dB or more
//              under the song's loud parts (a whisper) gives nothing, one within 7 dB of them gives its full size. Kicks
//              sit at 0.74–1 when they fire (the momentary loudness lags a kick by a few tens of milliseconds).
// times Sensitivity, clamped. A beat's grade can still grow during the OnsetWindowSeconds after it fires (the kick fills
// the 43 ms window over a few steps). As before, Beat is per Process call (doc 03 §2): on a call where a grade rose, Beat
// is that grade (or the decaying pulse, if higher), on every other call it decays by exp(−dt / BeatDecaySeconds). A full
// kick therefore still reads exactly 1 on the frame it lands.
//
// Punch is how drum-driven the music is over the last seconds: every graded onset adds PunchStep × its kick-likeness,
// and PunchHoldSeconds after the last kick-like onset it fades with PunchReleaseSeconds. AudioAnalyzer blends Level
// between a slow wave (punch 0) and doc 03's punchy envelope (punch 1) with it, so a song gets punchier when the drums
// come in and calms down in a drumless verse, with no setting. All state is preallocated; Update never allocates.
internal sealed class BeatCharacter
{
    internal const float KickLikeFrom = 0.3f;
    internal const float KickLikeAt = 0.7f;
    internal const float BassShareFrom = 0.25f;
    internal const float BassShareAt = 0.35f;
    internal const float AccentTolerance = 0.85f;
    internal const float AccentHalfLifeSeconds = 1.5f;
    internal const float QuietFrom = 0.3f;             // 16.8 dB under the reference (SongLoudness.RangeDb = 24)
    internal const float LoudAt = 0.7f;                // 7.2 dB under it
    internal const float OnsetWindowSeconds = 0.035f;
    internal const float PunchStep = 0.5f;             // two kicks make a groove; one stray low thump doesn't
    internal const float PunchHoldSeconds = 1.5f;      // longer than the gap between kicks down to 40 BPM half-time
    internal const float PunchReleaseSeconds = 2.5f;   // drums gone: the wave is back after about 5–8 s

    private float peakFlux, onsetFlux, onsetKickLike, onsetBass, onsetMid, onsetGrade, onsetAge, frameGrade, sinceKick = float.MaxValue;
    private bool inOnset, rose;

    // The graded, decaying pulse: AudioFeatures.Beat.
    public float Beat { get; private set; }

    // 0 (no drums) .. 1 (a drum groove).
    public float Punch { get; private set; }

    // The grade of the latest onset so far, for tests.
    internal float LastGrade => onsetGrade;

    // One analysis step. fired: BeatDetector fired on this step. flux, midFlux: its bass and Mid band flux (0 on steps it
    // didn't analyze). amplitude: √ of the step's weighted power. position: SongLoudness.Position.
    public void Update(bool fired, float flux, float midFlux, float amplitude, float position, float dtSeconds, AudioTuning tuning)
    {
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        peakFlux *= MathF.Exp(-MathF.Log(2) * dt / AccentHalfLifeSeconds);
        if (fired)
        {
            if (inOnset) Finish();                  // a new beat inside the window (refractory 0) ends the last one
            inOnset = true;
            onsetAge = onsetFlux = onsetKickLike = onsetBass = onsetMid = onsetGrade = 0;
        }
        else if (inOnset)
        {
            onsetAge += dt;
        }

        if (inOnset)
        {
            if (flux > 0 && float.IsFinite(flux))
            {
                onsetFlux = MathF.Max(onsetFlux, flux);
                onsetKickLike = MathF.Max(onsetKickLike, amplitude > 0 ? flux / amplitude : 0);
                onsetBass += flux;
            }
            if (midFlux > 0 && float.IsFinite(midFlux)) onsetMid += midFlux;
            float kick = KickLike();
            float accent = MathF.Min(1, onsetFlux / (AccentTolerance * MathF.Max(peakFlux, onsetFlux) + 1e-12f));
            float loud = Smoothstep(QuietFrom, LoudAt, position);
            float grade = Math.Clamp(tuning.Sensitivity * kick * accent * loud, 0, 1);
            // Rise to a grade that grew, but never hold the pulse: a grade reached on the first step decays from there.
            if (grade > onsetGrade)
            {
                onsetGrade = grade;
                frameGrade = MathF.Max(frameGrade, grade);
                rose = true;
            }
            if (onsetAge >= OnsetWindowSeconds) Finish();
        }

        sinceKick = sinceKick < float.MaxValue ? sinceKick + dt : sinceKick;
        if (sinceKick > PunchHoldSeconds) Punch *= MathF.Exp(-dt / PunchReleaseSeconds);
    }

    // Steps without new audio: the onset window ages, nothing else changes.
    public void Idle(float dtSeconds, AudioTuning tuning) => Update(false, 0, 0, 0, 0, dtSeconds, tuning);

    // Once per Process call, after its steps: the pulse the frame shows.
    public void EndFrame(float dtSeconds, AudioTuning tuning)
    {
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        float decayed = Beat * MathF.Exp(-dt / tuning.BeatDecaySeconds);
        Beat = rose ? MathF.Max(decayed, frameGrade) : decayed;
        rose = false;
        frameGrade = 0;
    }

    public void Reset()
    {
        peakFlux = onsetFlux = onsetKickLike = onsetBass = onsetMid = onsetGrade = onsetAge = frameGrade = Beat = Punch = 0;
        sinceKick = float.MaxValue;
        inOnset = rose = false;
    }

    private void Finish()
    {
        inOnset = false;
        peakFlux = MathF.Max(peakFlux, onsetFlux);
        float kick = KickLike();
        Punch = MathF.Min(1, Punch + PunchStep * kick);
        if (kick >= 0.5f) sinceKick = 0;
    }

    private float KickLike() =>
        Smoothstep(KickLikeFrom, KickLikeAt, onsetKickLike) * Smoothstep(BassShareFrom, BassShareAt, onsetBass / (onsetBass + onsetMid + 1e-12f));

    private static float Smoothstep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
