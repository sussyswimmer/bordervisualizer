namespace Rimlight.Core.Audio;

// How hard each detected beat lands, and how drum-driven the music is right now (Maxwell's feedback: calm songs move like
// a slow wave, rap and EDM bounce hard on every kick, a bounce is as big as the moment, and never big on a whisper).
//
// Round 1 graded each onset once its 35 ms window of audio had filled the FFT, from how far the 30–100 Hz kick band rose
// next to the music above the bass band, times the punch from before the onset. On real music (round 2's corpus of
// Creative Commons recordings with independently annotated kicks) that was late (the pulse peaked 46–53 ms after the kick,
// against 21–29 ms for the beat itself), smaller in full choruses than in sparse bars (a kick stands out less of a
// fuller mix), zero on the first kicks after every break (0, 0.3, 0.73, 1 on an EDM drop), and near zero on kicks over
// an 808's sustain. This class now decides the pulse on the step the beat fires and only corrects it later:
//
// At the beat, grade = Sensitivity × Punch × evidence × size × loud, clamped:
//   evidence  KickEnvelope's time-domain reading, which has already seen the kick's attack: the kick band's rise next to
//             the music above it (smoothstep EvidenceFromDb..EvidenceAtDb), or over its own level just before
//             (RiseFromDb..RiseAtDb), whichever is larger. A rock kit under dense guitars rises a lot without standing
//             out; an 808 over its own sustain stands out without rising much.
//   size      the kick band's amplitude next to the recent confirmed kicks' (a peak that halves every SizeHalfLifeSeconds),
//             through SizeFrom..SizeAt: a ghost note or a snare's low end bounces less than the kick.
//   loud      SongLoudness.Position, held over the dip between beats (LoudHoldSeconds, then it falls
//             LoudReleasePerSecond, so a whispered verse is quiet a second after the chorus), through
//             QuietFrom..LoudAt: nothing 13.5 dB or more under the song's loud parts (a whisper), full size within
//             8 dB of them; never under LoudFloor × Punch, so a groove keeps that much bounce through a quiet passage.
// One frame later, the FFT kick band has seen the kick too: its prominence over the music above (LaterProminenceFromDb..)
// or its rise (LaterRiseFromDb..) can raise the evidence, and an unmistakable kick (InstantFromDb..InstantAtDb of
// prominence) grades itself without any groove: the first kick of a drop lands a frame late instead of not at all. The
// grade only ever rises.
//
// Punch (0 no drums .. 1 a drum groove) is the context, from the onsets' confirmations once their OnsetWindowSeconds of
// audio has passed:
//   strong  the kick band rose KickFromDb..KickAtDb over the music above the bass band (round 1's kick-likeness, which
//           a voice, a strum, a piano or a bass note under a song never reaches; 3..9 dB gave 1–2 % more of the corpus's
//           kicks a pulse but let the bass notes where a folk band's chorus comes in start a groove). Their decaying count
//           (RateSeconds) sets the groove through GrooveFrom..GrooveAt: about two in a few seconds make a groove, a lone
//           thump doesn't. A beat that fires again within EventSeconds (BeatDetector's exceptions inside its refractory
//           time: a flam, a strum and its bass note) is the same musical event and counts only for what it adds.
//   weak    the kick band rose WeakRiseDb over its own level, and by at least WeakProminenceDb next to the music above
//           (a guitar's low string rises too, but stays well under the guitar itself). A fuller chorus hides its kicks
//           from the strong measure, so these keep a groove the strong ones established (up to its memory, which holds
//           MemoryHoldSeconds once the punch falls under it) as long as they come at the rate of a drum part
//           (KeepFrom..KeepAt), but can't start one: a calm song's plucks never come at that rate.
//   After the last strong confirmation the punch holds HoldSeconds, then follows the groove, falling no faster than
//   ReleaseSeconds.
// AudioAnalyzer also turns the punch into Level's pump (the light dips between kicks in a groove) and its time constants.
//
// Beat is per Process call (doc 03 §2): on a call where a grade rose, the largest grade (or the decaying pulse, if
// higher); on every other call it decays by exp(−dt / BeatDecaySeconds). The first WarmUpSeconds of audio after
// near-silence only fill the averages: an onset then has nothing around it to be measured against. All state is
// preallocated; nothing allocates.
internal sealed class BeatCharacter
{
    internal const float EvidenceFromDb = -10f;
    internal const float EvidenceAtDb = -5f;
    internal const float RiseFromDb = 12f;
    internal const float RiseAtDb = 20f;
    internal const float LaterProminenceFromDb = 3f;
    internal const float LaterProminenceAtDb = 9f;
    internal const float LaterRiseFromDb = 14f;
    internal const float LaterRiseAtDb = 22f;
    internal const float InstantFromDb = 8f;
    internal const float InstantAtDb = 14f;
    internal const float KickFromDb = 4.5f;
    internal const float KickAtDb = 10.5f;
    internal const float WeakRiseDb = 12f;
    internal const float WeakProminenceDb = -6f;
    internal const float OnsetWindowSeconds = 0.04f;
    internal const float RateSeconds = 3f;
    internal const float GrooveFrom = 1.1f;
    internal const float GrooveAt = 1.8f;
    internal const float KeepFrom = 2f;                // about one weak confirmation a second
    internal const float KeepAt = 3f;
    internal const float HoldSeconds = 3f;             // a kick the detector misses at 40 BPM half-time leaves a 3 s gap
    internal const float ReleaseSeconds = 2.5f;        // drums gone: the wave is back after about 5–8 s
    internal const float MemoryHoldSeconds = 8f;
    internal const float MemoryReleaseSeconds = 4f;
    internal const float SizeFrom = 0.05f;
    internal const float SizeAt = 0.3f;
    internal const float SizeHalfLifeSeconds = 4f;
    internal const float QuietFrom = 0.25f;            // 13.5 dB under the reference (SongLoudness.RangeDb = 18)
    internal const float LoudAt = 0.55f;               // 8 dB under it
    internal const float LoudFloor = 0.3f;
    internal const float LoudHoldSeconds = 0.75f;      // the dip between kicks at 80 BPM
    internal const float LoudReleasePerSecond = 2f;    // then the held position falls 36 dB a second
    internal const float FastSeconds = 0.25f;
    internal const float SlowSeconds = 1.5f;
    internal const float WarmUpSeconds = 1f;
    internal const float AccentTolerance = 0.85f;
    internal const float AccentHalfLifeSeconds = 1.5f;
    internal const float TempoAccent = 0.7f;           // a tempo beat's rise is at least 0.6 × the strongest recent kick's
    internal const float KickContextSeconds = 4f;      // BeatDetector's stale-tempo time
    internal const float EventSeconds = 0.18f;         // the default BeatRefractorySeconds
    private const int MaxOnsets = 4;                   // onsets in their window at once (beats are at least 50 ms apart)
    private const float Tiny = 1e-9f;                  // amplitudes under this are 0 (no subnormal floats)
    private const float Negligible = 1e-4f;            // a rate, a punch or a pulse under this is 0

    // An onset still in its window: what it was graded with, and the kick band since.
    private struct Onset
    {
        public int Frame;
        public float Base, Den, Peak, SubPeak, Evidence, Size, Loud, Grade, AudioAge, Age;
        public bool Warm, Reevaluated, Counted;
    }

    private readonly Onset[] onsets = new Onset[MaxOnsets];
    private int count, frame;
    private float strongRate, weakRate, memory, sinceHold = float.MaxValue, sinceMemory = float.MaxValue, sinceKick = float.MaxValue;
    private float sinceEvent = float.MaxValue, eventKick;
    private bool eventWeak;
    private float sizeReference, peakRise, heldPosition, heldSeconds, lastPosition;
    private float fastAmp, slowAmp, lastAmplitude, audibleSeconds, levelBefore, levelTwoBefore;
    private float frameGrade, lastKickLike, lastGrade;
    private bool rose, lastAudible;

    // The graded, decaying pulse: AudioFeatures.Beat.
    public float Beat { get; private set; }

    // 0 (no drums) .. 1 (a drum groove).
    public float Punch { get; private set; }

    // The tempo (BeatDetector.AddTempoBeat). Where kicks play (a confirmed kick within KickContextSeconds), only
    // confirmed, accented kicks are tempo beats: TempoBeatAge is the seconds since one fired, on the step its window
    // closed, and −1 on every other step. Elsewhere AudioAnalyzer counts each beat as it fires and calls CountedAtFire.
    public bool KickContext => sinceKick <= KickContextSeconds;
    public float TempoBeatAge { get; private set; } = -1;

    public void CountedAtFire()
    {
        if (count > 0) onsets[count - 1].Counted = true;
    }

    // For tests and the replay harness: the count of strong confirmations behind the punch, the latest confirmed onset's
    // kick-likeness and the latest grade.
    internal float Drive => strongRate;
    internal float LastKickLike => lastKickLike;
    internal float LastGrade => lastGrade;

    // A new Process call (frame): onsets of earlier frames are re-evaluated on its first step with new audio.
    public void BeginFrame() => frame++;

    // One analysis step with new audio. fired: BeatDetector fired on this step. kickLevel: the FFT kick band's summed
    // magnitude (BeatResult.KickLevel). upperAmplitude: √ of the step's weighted power above the bass band
    // (BandAnalyzer.WeightedPower's Upper). audible: the window is above near-silence. position: SongLoudness.Position.
    // evidence: KickEnvelope's reading. dtSeconds: the step's share of frame time; audioSeconds: how much audio it
    // analyzed (the same unless packets arrive in bursts).
    public void Update(bool fired, float kickLevel, float upperAmplitude, bool audible, float position, in KickEvidence evidence,
        float dtSeconds, float audioSeconds, AudioTuning tuning)
    {
        float dt = Finite(dtSeconds);
        float audio = Finite(audioSeconds);
        float amplitude = Finite(upperAmplitude);
        if (!float.IsFinite(kickLevel) || kickLevel < 0) kickLevel = 0;
        if (!float.IsFinite(position)) position = 0;
        TempoBeatAge = -1;
        Age(dt);
        Hold(position, dt);
        for (int i = 0; i < count; i++)
        {
            ref Onset o = ref onsets[i];
            o.Peak = MathF.Max(o.Peak, kickLevel);
            o.AudioAge += audio;
            o.Age += dt;
            if (!o.Reevaluated && o.Frame != frame) Reevaluate(ref o, tuning);
        }
        for (int i = 0; i < count;)
        {
            if (onsets[i].AudioAge >= OnsetWindowSeconds) Confirm(i);
            else i++;
        }
        if (fired) Start(kickLevel, evidence, tuning);
        levelTwoBefore = levelBefore;
        levelBefore = kickLevel;
        lastPosition = position;
        lastAmplitude = amplitude;
        lastAudible = audible;
        Listen(dt);
    }

    // Steps without new audio (frames between packets, as with 50 ms Bluetooth packets): time goes on.
    public void Idle(float dtSeconds)
    {
        float dt = Finite(dtSeconds);
        TempoBeatAge = -1;
        Age(dt);
        for (int i = 0; i < count; i++) onsets[i].Age += dt;
        Hold(lastPosition, dt);
        Listen(dt);
    }

    // Once per Process call, after its steps: the pulse the frame shows.
    public void EndFrame(float dtSeconds, AudioTuning tuning)
    {
        float dt = Finite(dtSeconds);
        float decayed = Beat * MathF.Exp(-dt / tuning.BeatDecaySeconds);
        Beat = rose ? MathF.Max(decayed, frameGrade) : decayed;
        if (Beat < Negligible) Beat = 0;
        rose = false;
        frameGrade = 0;
    }

    public void Reset()
    {
        Array.Clear(onsets);
        count = frame = 0;
        strongRate = weakRate = memory = sizeReference = peakRise = heldPosition = heldSeconds = lastPosition = 0;
        sinceHold = sinceMemory = sinceKick = sinceEvent = float.MaxValue;
        eventKick = 0;
        eventWeak = false;
        fastAmp = slowAmp = lastAmplitude = audibleSeconds = levelBefore = levelTwoBefore = 0;
        frameGrade = lastKickLike = lastGrade = Beat = Punch = 0;
        TempoBeatAge = -1;
        rose = lastAudible = false;
    }

    // A beat fired: grade it now, from what the time domain has already seen (nothing in the warm-up: a punch held from
    // before a gap must not grade the next song's first notes, whose kick band rises from the silence).
    private void Start(float kickLevel, in KickEvidence evidence, AudioTuning tuning)
    {
        if (count == MaxOnsets) Confirm(0);
        ref Onset o = ref onsets[count++];
        o = default;
        o.Frame = frame;
        o.Warm = audibleSeconds >= WarmUpSeconds;
        o.Base = MathF.Min(levelBefore, levelTwoBefore);
        o.Den = MathF.Max(fastAmp, slowAmp);
        o.Peak = kickLevel;
        o.SubPeak = Finite(evidence.SubPeak);
        o.Evidence = MathF.Max(Smoothstep(EvidenceFromDb, EvidenceAtDb, evidence.ProminenceDb), Smoothstep(RiseFromDb, RiseAtDb, evidence.RiseDb));
        o.Size = sizeReference > 0 ? Smoothstep(SizeFrom, SizeAt, o.SubPeak / sizeReference) : 1;
        o.Loud = MathF.Max(Smoothstep(QuietFrom, LoudAt, heldPosition), LoudFloor * Punch);
        o.Grade = o.Warm ? Clamp(tuning.Sensitivity * Punch * o.Evidence * o.Size * o.Loud) : 0;
        Raise(o.Grade);
    }

    // One frame later: the FFT kick band has seen the kick too. The grade only rises.
    private void Reevaluate(ref Onset o, AudioTuning tuning)
    {
        o.Reevaluated = true;
        if (!o.Warm) return;
        float prominence = Prominence(o), rise = Rise(o);
        float evidence = MathF.Max(o.Evidence, MathF.Max(Smoothstep(LaterProminenceFromDb, LaterProminenceAtDb, prominence), Smoothstep(LaterRiseFromDb, LaterRiseAtDb, rise)));
        float instant = Smoothstep(InstantFromDb, InstantAtDb, prominence);
        float grade = Clamp(tuning.Sensitivity * MathF.Max(Punch, instant) * evidence * o.Size * o.Loud);
        if (grade <= o.Grade) return;
        o.Grade = grade;
        Raise(grade);
    }

    // The onset's window has closed: what it says about the music, then it is gone.
    private void Confirm(int index)
    {
        Onset o = onsets[index];
        for (int i = index + 1; i < count; i++) onsets[i - 1] = onsets[i];
        onsets[--count] = default;
        if (!o.Warm) return;
        float prominence = Prominence(o), rise = Rise(o), riseAmplitude = MathF.Max(o.Peak - o.Base, 0);
        float kick = Smoothstep(KickFromDb, KickAtDb, prominence);
        lastKickLike = kick;
        // A beat BeatDetector re-triggered inside its refractory time (a flam, a chord's second onset, a bass note
        // after the strum) is the same musical event as the one before it: the counts take only what it adds.
        bool sameEvent = sinceEvent < EventSeconds;
        bool weak = rise >= WeakRiseDb && prominence >= WeakProminenceDb;
        strongRate += sameEvent ? MathF.Max(kick - eventKick, 0) : kick;
        eventKick = sameEvent ? MathF.Max(eventKick, kick) : kick;
        bool countWeak = weak && !(sameEvent && eventWeak);
        eventWeak = sameEvent ? eventWeak || weak : weak;
        sinceEvent = 0;
        if (kick >= 0.5f)
        {
            sinceHold = sinceKick = 0;
            sizeReference = MathF.Max(sizeReference, o.SubPeak);
            float accent = riseAmplitude / (AccentTolerance * MathF.Max(peakRise, riseAmplitude) + 1e-12f);
            if (!o.Counted && accent >= TempoAccent) TempoBeatAge = o.Age;
            peakRise = MathF.Max(peakRise, riseAmplitude);
        }
        if (countWeak)
        {
            weakRate += 1;
            // At a drum part's rate, in a groove the strong ones established, they hold the punch like a strong one.
            if (memory > 0.5f && weakRate >= KeepFrom) sinceHold = 0;
        }
    }

    private void Raise(float grade)
    {
        lastGrade = grade;
        if (grade <= 0) return;
        frameGrade = MathF.Max(frameGrade, grade);
        rose = true;
    }

    // Time passes: the counts fade, the punch follows them (held while confirmations come), the memory of a groove holds.
    private void Age(float dt)
    {
        float fade = MathF.Exp(-dt / RateSeconds);
        strongRate *= fade;
        weakRate *= fade;
        if (strongRate < Negligible) strongRate = 0;
        if (weakRate < Negligible) weakRate = 0;
        float groove = MathF.Max(Smoothstep(GrooveFrom, GrooveAt, strongRate), MathF.Min(memory, Smoothstep(KeepFrom, KeepAt, weakRate)));
        sinceHold = sinceHold < float.MaxValue ? sinceHold + dt : sinceHold;
        Punch = sinceHold <= HoldSeconds ? MathF.Max(Punch, groove) : MathF.Max(groove, Punch * MathF.Exp(-dt / ReleaseSeconds));
        if (Punch < Negligible) Punch = 0;
        sinceMemory = sinceMemory < float.MaxValue ? sinceMemory + dt : sinceMemory;
        if (Punch >= memory)
        {
            memory = Punch;
            sinceMemory = 0;
        }
        else if (sinceMemory > MemoryHoldSeconds)
        {
            memory *= MathF.Exp(-dt / MemoryReleaseSeconds);
            if (memory < Negligible) memory = 0;
        }
        sizeReference *= MathF.Exp(-MathF.Log(2) * dt / SizeHalfLifeSeconds);
        if (sizeReference < Tiny) sizeReference = 0;
        peakRise *= MathF.Exp(-MathF.Log(2) * dt / AccentHalfLifeSeconds);
        if (peakRise < Tiny) peakRise = 0;
        sinceKick = sinceKick < float.MaxValue ? sinceKick + dt : sinceKick;
        sinceEvent = sinceEvent < float.MaxValue ? sinceEvent + dt : sinceEvent;
    }

    // How loud the music has just been: the song position's peak, held LoudHoldSeconds over the dip before a beat, then
    // falling LoudReleasePerSecond to follow the position.
    private void Hold(float position, float dt)
    {
        if (position >= heldPosition)
        {
            heldPosition = position;
            heldSeconds = 0;
            return;
        }
        heldSeconds = MathF.Min(heldSeconds + dt, LoudHoldSeconds + 1);
        if (heldSeconds > LoudHoldSeconds) heldPosition = MathF.Max(position, heldPosition - LoudReleasePerSecond * dt);
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

    // The FFT kick band's rise since the onset over the music above the bass band before it, and over its own level, dB.
    private static float Prominence(in Onset o) => 20 * MathF.Log10(MathF.Max(o.Peak - o.Base, 1e-12f) / MathF.Max(o.Den, Tiny));

    private static float Rise(in Onset o) => 20 * MathF.Log10(MathF.Max(o.Peak, 1e-12f) / MathF.Max(o.Base, 1e-12f));

    private static float Finite(float value) => float.IsFinite(value) && value > 0 ? value : 0;

    private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    internal static float Smoothstep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        if (float.IsNaN(t)) t = 0;
        return t * t * (3 - 2 * t);
    }
}
