namespace Rimlight.Core.Audio;

// How hard each detected beat lands, and how drum-driven the music is right now (Maxwell's feedback, round 1: calm songs
// should move like a wave, rap may bounce hard, and a whispered line must never give a big bounce).
//
// BeatDetector (doc 03 §2) finds onsets in the bass band. Many aren't drums: a baritone's syllables, a guitar's or a
// piano's low strings, a bass guitar. Before round 1 each of them set Beat to 1, the same full pulse as a kick.
//
// Kick-likeness, 0..1, is one measure: the onset's prominence. That is the rise of the fixed kick band
// (BandAnalyzer.KickBins, 30–100 Hz) over the OnsetWindowSeconds of audio after the onset fires, from the lowest level of
// the two steps before it, against the amplitude of the music above the bass band (the larger of two averages of √ its
// weighted power from before the onset, over FastSeconds and SlowSeconds), in dB, through a smoothstep from KickFromDb
// to KickAtDb. A kick or an 808 puts a burst of sub-bass into the mix that is large next to everything else playing; a
// voice has almost nothing under 100 Hz, and a plucked or struck string adds a note to a mix that is already as loud.
// Measured on the procedural StyleTracks (60 fps, 10 ms packets; median, with the 10th and 90th percentiles):
//   808s (rap) 13.5 dB (p10 11; 12.2 and 9.4 at 44.1 kHz), EDM kicks 19, acoustic kicks entering a ballad 9.6 (p10 8.4),
//   the folk band's brush kit at the music's level 17 (p10 11), 6 dB under it 12 (p10 9), 12 dB under it 7 (p10 3,
//   p90 11), 20 dB under it 4 (p90 7);
//   a bass guitar 6 dB under the music −3 (p90 4.5), at the music's level 4 (p90 8), piano −9.5 (p90 −1), a strummed
//   and picked guitar −19 (p90 −2), the voice and guitar of the calm ballad −31 (p90 −21).
// One onset can't tell a soft kick from a loud bass note, but a run of them can. The drive follows the kick-likeness of
// the recent onsets, rising DriveRise of the way toward a more kick-like one and falling DriveFall of the way toward a less
// kick-like one (so syllables and off-beat bass notes between the kicks don't dilute a groove); DriveHoldSeconds after
// the last kick-like onset it fades with DriveReleaseSeconds. Punch = smoothstep(PunchFrom, PunchAt, drive): 1 in a drum
// groove, a fraction with soft drums, 0 with none or with tonal bass under the music. AudioAnalyzer blends Level between a
// slow wave (punch 0) and doc 03's punchy envelope (punch 1) with it, so a song gets punchier when the drums come in and
// calms down in a drumless verse, with no setting.
//
// Each onset's grade is kick-likeness × punch (as it was before this onset) × accent × loud × Sensitivity, clamped. One
// kick-like onset in a wave therefore gives no pulse; the next gives about 0.4 and the third its full size.
//   accent  the onset's rise over AccentTolerance × the strongest recent kick (a peak that halves every
//           AccentHalfLifeSeconds): kicks within 15 % of the strongest are full, a ghost note gives less.
//   loud    SongLoudness.Position, held over the dip between beats (it falls LoudReleasePerSecond), through a smoothstep
//           from QuietFrom to LoudAt: an onset 17 dB or more under the song's loud parts (a whisper) gives nothing, one
//           within about 10 dB of them its full size. It never falls under LoudFloor × punch: a drum groove keeps
//           bounces of that size, because a volume drop in the middle of a song looks like a quiet passage until the
//           song reference has turned over (about 25 s, SongLoudness).
// So a pulse's size follows how hard the drums hit and how loud the moment is: soft drums bounce softly, a whisper never
// bounces big, and a voice, a guitar, a piano or a bass guitar under a drumless song doesn't bounce.
//
// A beat's grade can still grow during its onset window (the kick fills the 35 ms window over a few steps). As before,
// Beat is per Process call (doc 03 §2): on a call where a grade rose, Beat is that grade (or the decaying pulse, if
// higher); on every other call it decays by exp(−dt / BeatDecaySeconds). A full kick in a groove reads exactly 1 on the
// frame it lands. The first WarmUpSeconds of audio after near-silence only fill the amplitude averages: an onset then
// has no music around it to be measured against. All state is preallocated; Update never allocates.
internal sealed class BeatCharacter
{
    internal const float KickFromDb = 4f;
    internal const float KickAtDb = 9f;
    internal const float FastSeconds = 0.25f;
    internal const float SlowSeconds = 1.5f;
    internal const float OnsetWindowSeconds = 0.035f;
    internal const float WarmUpSeconds = 1f;
    internal const float DriveRise = 0.5f;
    internal const float DriveFall = 0.1f;
    internal const float DriveHoldSeconds = 3f;        // a kick the detector misses at 40 BPM half-time leaves a 3 s gap
    internal const float DriveReleaseSeconds = 2.5f;   // drums gone: the wave is back after about 5–8 s
    internal const float PunchFrom = 0.3f;
    internal const float PunchAt = 0.75f;
    internal const float AccentTolerance = 0.85f;
    internal const float AccentHalfLifeSeconds = 1.5f;
    internal const float QuietFrom = 0.3f;             // 16.8 dB under the reference (SongLoudness.RangeDb = 24)
    internal const float LoudAt = 0.6f;                // 9.6 dB under it
    internal const float LoudFloor = 0.3f;
    internal const float LoudReleasePerSecond = 0.25f; // the held position falls 6 dB a second
    internal const float TempoAccent = 0.7f;           // a tempo beat's rise is at least 0.6 × the strongest recent kick's
    internal const float KickContextSeconds = 4f;      // BeatDetector's stale-tempo time
    private const float Tiny = 1e-9f;                  // flux under this is flushed to 0 (no subnormal floats)
    private const float Negligible = 1e-4f;            // a drive or a pulse under this is 0

    private float fastAmp, slowAmp, audibleSeconds, peakRise, drive, heldPosition, lastPosition, lastAmplitude, sinceKick = float.MaxValue;
    private float onsetDen, onsetRise, onsetLow, onsetPunch, onsetLoud, onsetGrade, onsetAge, onsetSeconds, frameGrade, levelBefore, levelTwoBefore;
    private bool inOnset, onsetWarm, onsetCounted, rose, lastAudible;

    // The graded, decaying pulse: AudioFeatures.Beat.
    public float Beat { get; private set; }

    // 0 (no drums, or tonal bass alone) .. 1 (a drum groove).
    public float Punch { get; private set; }

    // The tempo (BeatDetector.AddTempoBeat). Where kicks play (a kick-like onset within KickContextSeconds), only
    // kick-like, accented onsets are tempo beats: TempoBeatAge is the seconds since one fired, on the step its onset window
    // closed, and −1 on every other step. Elsewhere AudioAnalyzer counts each beat as it fires and calls CountedAtFire.
    public bool KickContext => sinceKick <= KickContextSeconds;
    public float TempoBeatAge { get; private set; } = -1;
    public void CountedAtFire() => onsetCounted = true;

    // For tests: the drive behind Punch, the kick-likeness and grade of the latest onset so far.
    internal float Drive => drive;
    internal float LastKickLike => KickLike();
    internal float LastGrade => onsetGrade;

    // One analysis step with new audio. fired: BeatDetector fired on this step. kickLevel: the kick band's summed
    // magnitude (BeatResult.KickLevel). upperAmplitude: √ of the step's weighted power above the bass band (BandAnalyzer
    // .WeightedPower's Upper). audible: the window is above near-silence. position: SongLoudness.Position. dtSeconds: the
    // step's share of frame time; audioSeconds: how much audio it analyzed (the same unless packets arrive in bursts).
    public void Update(bool fired, float kickLevel, float upperAmplitude, bool audible, float position, float dtSeconds, float audioSeconds, AudioTuning tuning)
    {
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        float audio = float.IsFinite(audioSeconds) && audioSeconds > 0 ? audioSeconds : 0;
        float amplitude = float.IsFinite(upperAmplitude) && upperAmplitude > 0 ? upperAmplitude : 0;
        if (!float.IsFinite(kickLevel) || kickLevel < 0) kickLevel = 0;
        if (!float.IsFinite(position)) position = 0;
        TempoBeatAge = -1;
        Age(dt, audio);
        float held = MathF.Max(position, heldPosition - LoudReleasePerSecond * dt);
        if (fired)
        {
            if (inOnset) Finish();                  // a new beat inside the window (refractory 0) ends the last one
            inOnset = true;
            onsetCounted = false;
            onsetWarm = audibleSeconds >= WarmUpSeconds;
            onsetDen = MathF.Max(fastAmp, slowAmp);
            onsetPunch = Punch;
            // How loud the music has just been, held over the dip before a beat and taken before this onset can raise it.
            onsetLoud = MathF.Max(Smoothstep(QuietFrom, LoudAt, heldPosition), LoudFloor * Punch);
            // The rise is measured from the lowest level of the two steps before it: the step before the one that fired
            // may already hold the start of the attack (with 50 ms packets three 17 ms steps arrive at once), and a bass
            // note that stops or ducks as the kick lands mustn't cancel the kick's rise in the same bins.
            onsetLow = MathF.Min(levelBefore, levelTwoBefore);
            onsetAge = onsetSeconds = onsetRise = onsetGrade = 0;
        }
        if (inOnset)
        {
            onsetRise = MathF.Max(onsetRise, kickLevel - onsetLow);
            onsetLow = MathF.Min(onsetLow, kickLevel);
            Grade(tuning);
        }
        levelTwoBefore = levelBefore;
        levelBefore = kickLevel;
        heldPosition = held;
        lastPosition = position;
        lastAmplitude = amplitude;
        lastAudible = audible;
        Listen(dt);
    }

    // Steps without new audio (frames between packets, as with 50 ms Bluetooth packets): time goes on as with the last
    // window, which still holds the same spectrum.
    public void Idle(float dtSeconds, AudioTuning tuning)
    {
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        TempoBeatAge = -1;
        Age(dt, 0);
        if (inOnset) Grade(tuning);
        heldPosition = MathF.Max(lastPosition, heldPosition - LoudReleasePerSecond * dt);
        Listen(dt);
    }

    // The music around the next onsets. Near-silence (a gap, a stop) starts the averages and the warm-up afresh.
    private void Listen(float dt)
    {
        if (!lastAudible)
        {
            audibleSeconds = 0;
            return;
        }
        if (audibleSeconds == 0)
        {
            fastAmp = slowAmp = lastAmplitude;
        }
        else
        {
            fastAmp += (lastAmplitude - fastAmp) * (1 - MathF.Exp(-dt / FastSeconds));
            slowAmp += (lastAmplitude - slowAmp) * (1 - MathF.Exp(-dt / SlowSeconds));
        }
        audibleSeconds = MathF.Min(audibleSeconds + dt, WarmUpSeconds + 1);
    }

    // Once per Process call, after its steps: the pulse the frame shows.
    public void EndFrame(float dtSeconds, AudioTuning tuning)
    {
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        float decayed = Beat * MathF.Exp(-dt / tuning.BeatDecaySeconds);
        Beat = rose ? MathF.Max(decayed, frameGrade) : decayed;
        if (Beat < Negligible) Beat = 0;
        rose = false;
        frameGrade = 0;
    }

    public void Reset()
    {
        fastAmp = slowAmp = audibleSeconds = peakRise = drive = heldPosition = lastPosition = lastAmplitude = 0;
        onsetDen = onsetRise = onsetLow = onsetPunch = onsetLoud = onsetGrade = onsetAge = onsetSeconds = frameGrade = levelBefore = levelTwoBefore = Beat = Punch = 0;
        sinceKick = float.MaxValue;
        TempoBeatAge = -1;
        inOnset = onsetWarm = onsetCounted = rose = lastAudible = false;
    }

    // Time passes. The onset window is measured in audio (audioSeconds: the samples analyzed), not in frame time: with
    // 50 ms packets a kick's attack can land in one packet and its body in the next, 50 ms of frames later.
    private void Age(float dt, float audioSeconds)
    {
        peakRise *= MathF.Exp(-MathF.Log(2) * dt / AccentHalfLifeSeconds);
        if (peakRise < Tiny) peakRise = 0;
        if (inOnset)
        {
            onsetSeconds += dt;
            onsetAge += audioSeconds;
            if (onsetAge > OnsetWindowSeconds) Finish();
        }
        sinceKick = sinceKick < float.MaxValue ? sinceKick + dt : sinceKick;
        if (sinceKick > DriveHoldSeconds)
        {
            drive *= MathF.Exp(-dt / DriveReleaseSeconds);
            if (drive < Negligible) drive = 0;
        }
        Punch = Smoothstep(PunchFrom, PunchAt, drive);
    }

    // The grade so far: it may rise while the kick fills the window, and the pulse rises with it, but never holds.
    private void Grade(AudioTuning tuning)
    {
        float kick = KickLike();
        float accent = MathF.Min(1, onsetRise / (AccentTolerance * MathF.Max(peakRise, onsetRise) + 1e-12f));
        float grade = Math.Clamp(tuning.Sensitivity * kick * onsetPunch * accent * onsetLoud, 0, 1);
        if (grade > onsetGrade)
        {
            onsetGrade = grade;
            frameGrade = MathF.Max(frameGrade, grade);
            rose = true;
        }
    }

    private void Finish()
    {
        inOnset = false;
        float kick = KickLike();
        float accent = onsetRise / (AccentTolerance * MathF.Max(peakRise, onsetRise) + 1e-12f);
        if (!onsetCounted && kick >= 0.5f && accent >= TempoAccent) TempoBeatAge = onsetSeconds;
        drive += (kick - drive) * (kick > drive ? DriveRise : DriveFall);
        if (drive < Negligible) drive = 0;
        if (kick >= 0.5f)
        {
            sinceKick = 0;
            peakRise = MathF.Max(peakRise, onsetRise);
        }
        Punch = Smoothstep(PunchFrom, PunchAt, drive);
    }

    // The onset's prominence in dB through the smoothstep; 0 while warming up.
    private float KickLike()
    {
        if (!onsetWarm || onsetRise <= 0) return 0;
        return Smoothstep(KickFromDb, KickAtDb, 20 * MathF.Log10(onsetRise / MathF.Max(onsetDen, 1e-9f)));
    }

    internal static float Smoothstep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
