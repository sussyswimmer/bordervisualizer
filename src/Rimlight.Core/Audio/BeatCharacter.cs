namespace Rimlight.Core.Audio;

// How hard each detected beat lands, and how drum-driven the music is right now (Maxwell's feedback: calm songs move like
// a slow wave, rap and EDM bounce hard on every kick, a bounce is as big as the moment, and never big on a whisper).
//
// Round 1 graded each onset once its 35 ms window of audio had filled the FFT, from how far the 30–100 Hz kick band rose
// next to the music above the bass band, times the punch from before the onset. On real music (round 2's corpus of
// Creative Commons recordings with independently annotated kicks) that was late (the pulse peaked 46–53 ms after the kick,
// against 21–29 ms for the beat itself), smaller in full choruses than in sparse bars (a kick stands out less of a
// fuller mix), zero on the first kicks after every break (0, 0.3, 0.73, 1 on an EDM drop), and near zero on kicks over
// an 808's sustain. This class decides the pulse on the step the beat fires and only corrects it later:
//
// At the beat, grade = Sensitivity × Punch × evidence × size × loud × gain × mask, clamped:
//   evidence  KickEnvelope's time-domain reading, which has already seen the kick's attack: the kick band's rise next to
//             the music above it (smoothstep EvidenceFromDb..EvidenceAtDb), or over its own level just before
//             (RiseFromDb..RiseAtDb), whichever is larger. A rock kit under dense guitars rises a lot without standing
//             out; an 808 over its own sustain stands out without rising much.
//   size      the kick band's amplitude next to the recent confirmed kicks' (a peak that halves every SizeHalfLifeSeconds),
//             through SizeFrom..SizeAt: a ghost note bounces less than the kick.
//   loud      SongLoudness.Position, the largest over the last LoudHoldSeconds (the dip before a beat doesn't count,
//             and a whispered verse is quiet within a second after the chorus), through QuietFrom..LoudAt: nothing
//             13.5 dB or more under the song's loud parts (a whisper), full size within 8 dB of them. A groove keeps at
//             least LoudFloor × Punch through a quiet passage, faded out with the held position from LoudFloorAt to
//             LoudFloorFrom (16 dB under), so a fade-out or a near-silent passage doesn't keep bouncing (round 2's
//             review: 0.3-high pulses 36 dB down a fade).
//   gain      how heavy the groove is (Weight): LightGain for a folk or rock kit under guitars and voice, 1 for rap and
//             EDM. Maxwell: rap may bounce hard, but songs like Noah Kahan's or Hozier's should stay a wave. No measure
//             of a kick alone tells those apart (a folk kit's kicks peak as high against the song as an 808's: round
//             2), but the music around it does: rap and EDM keep the 30–100 Hz band at or above the music above 150 Hz
//             (808 tails, sub bass), while a folk, rock or pop-rock band keeps it 8–12 dB under (the corpus: rap and
//             EDM +2 to +5 dB over each track's groove, the folk and rock kits −8 to −12 dB). Weight is
//             smoothstep(WeightFromDb, WeightAtDb) of that ratio, over WeightSeconds while a groove plays (held through
//             a breakdown, so a drop is judged like the groove before it) and WeightFastSeconds before the first one; a
//             gap between songs clears it. AudioAnalyzer scales the pump and Level's speed by it too.
//   mask      within MaskSeconds after a pulse of MaskArm or more (graded at Sensitivity 1), a beat needs a fresh
//             attack: its kick band rose MaskRiseFromDb..MaskRiseAtDb over its own level. On the corpus 43 % of the
//             pulses of 0.3 or more away from an annotated kick came within 0.3 s after a full one (an off-beat bass
//             note, the 808 following the kick, a snare's low end), against 15 % of the kicks' own; the kick band's
//             rise tells them apart (a kick rises out of the last one's decay, a note under it doesn't). That cut them
//             from 23 to 15 a minute on rap and from 28 to 14 on EDM (round 2, with its re-fired beats: 31 and 30) for
//             3 % of the kicks' pulses. Without the mask every beat in a groove graded full: evidence, size and loud
//             all saturate on rap and EDM, and no reading at the step a beat fires tells most of those notes from kicks
//             (a held-out logistic fit of every fire-time reading: keeping 90 % of the kicks kept 74 % of them; waiting
//             for the FFT a frame or three later separates them better, at the latency round 1 was faulted for).
// One frame later, the FFT kick band has seen the kick too: its prominence over the music above (LaterProminenceFromDb..)
// or its rise (LaterRiseFromDb..) can raise the evidence, and in music (BeatDetector.IsMusic) an unmistakable kick
// (InstantFromDb..InstantAtDb of prominence) grades itself without any groove: the first kick of a drop with no groove
// before it lands a frame late instead of not at all. The grade only ever rises.
//
// Punch (0 no drums .. 1 a drum groove) is the context, from the onsets' confirmations once their OnsetWindowSeconds of
// audio has passed. Only onsets of music count, or ones that passed doc 03's own threshold (BeatResult.Strict): on brown
// noise or an ocean recording BeatDetector's looser guard used to fire on random kick-band swells, which were confirmed
// as kicks and built a full groove (round 2's review), while a kick over a steady hiss (the tests' kick tracks) is a
// kick:
//   strong  the kick band rose KickFromDb..KickAtDb over the music above the bass band (round 1's kick-likeness, which
//           a voice, a strum, a piano or a bass note under a song never reaches; 3..9 dB gave 1–2 % more of the corpus's
//           kicks a pulse but let the bass notes where a folk band's chorus comes in start a groove). Their decaying count
//           (RateSeconds) sets the groove through GrooveFrom..GrooveAt: about two in a few seconds make a groove, a lone
//           thump doesn't. A beat within EventSeconds of the one before it (possible only with a Beat spacing tuned
//           under it: a flam, a strum and its bass note) is the same musical event and counts only for what it adds.
//   weak    the kick band rose WeakRiseDb over its own level, and by at least WeakProminenceDb next to the music above
//           (a guitar's low string rises too, but stays well under the guitar itself). A fuller chorus hides its kicks
//           from the strong measure, so these keep a groove the strong ones established (up to its memory) as long as
//           they come at the rate of a drum part (KeepFrom..KeepAt), but can't start one: a calm song's plucks never
//           come at that rate.
//   After the last strong confirmation the punch holds HoldSeconds, then follows the groove, falling no faster than
//   ReleaseSeconds. The memory of a groove (its punch) holds MemoryHoldSeconds once the punch falls under it, then fades
//   with MemoryReleaseSeconds: a song comes back to it after a breakdown or a bridge. With a memory of ResumeMemory or
//   more, the first strong confirmation brings the punch straight back to it, and so does a kick that fires with
//   ResumeRiseDb of rise and ResumeProminenceDb of prominence (KickEnvelope), so the drop's first kick is graded with
//   the groove as it fires (round 2's review: an EDM drop's first three kicks graded 0.01, then the groove came back a
//   second in, on a non-kick); one that only shows that rise a frame later, under the pads arriving with it, is regraded
//   with the groove then. Round 2 held the memory 8 s, shorter than most breakdowns.
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
    internal const float MemoryHoldSeconds = 30f;      // an EDM breakdown, a bridge
    internal const float MemoryReleaseSeconds = 4f;
    internal const float ResumeMemory = 0.5f;
    internal const float ResumeRiseDb = 18f;
    internal const float ResumeProminenceDb = 0f;
    internal const float SizeFrom = 0.05f;
    internal const float SizeAt = 0.3f;
    internal const float SizeHalfLifeSeconds = 4f;
    internal const float QuietFrom = 0.25f;            // 13.5 dB under the reference (SongLoudness.RangeDb = 18)
    internal const float LoudAt = 0.55f;               // 8 dB under it
    internal const float LoudFloor = 0.3f;
    internal const float LoudFloorFrom = 0.1f;         // 16.2 dB under the reference: no floor
    internal const float LoudFloorAt = 0.3f;           // 12.6 dB under it: the whole floor
    internal const float LoudHoldSeconds = 0.75f;      // the dip between kicks at 80 BPM
    internal const float WeightFromDb = -7f;           // the kick band 7 dB under the music above it: light
    internal const float WeightAtDb = 0f;              // level with it: heavy
    internal const float WeightSeconds = 8f;
    internal const float WeightFastSeconds = 1.5f;
    internal const float LightGain = 0.5f;
    internal const float MaskSeconds = 0.3f;           // an eighth note at 100 BPM; a 90 BPM rap's syncopated 808 is out of it
    internal const float MaskArm = 0.5f;
    internal const float MaskRiseFromDb = 10f;
    internal const float MaskRiseAtDb = 20f;
    internal const float FastSeconds = 0.25f;
    internal const float SlowSeconds = 1.5f;
    internal const float WarmUpSeconds = 1f;
    internal const float AccentTolerance = 0.85f;
    internal const float AccentHalfLifeSeconds = 1.5f;
    internal const float TempoAccent = 0.7f;           // a tempo beat's rise is at least 0.6 × the strongest recent kick's
    internal const float KickContextSeconds = 4f;      // BeatDetector's stale-tempo time
    internal const float EventSeconds = 0.18f;         // the default BeatRefractorySeconds
    private const int MaxOnsets = 4;                   // onsets in their window at once
    private const int HoldBlocks = 15;                 // LoudHoldSeconds in blocks of 50 ms
    private const float Tiny = 1e-9f;                  // amplitudes under this are 0 (no subnormal floats)
    private const float TinyPower = 1e-30f;            // powers under this are 0
    private const float Negligible = 1e-4f;            // a rate, a punch or a pulse under this is 0

    // An onset still in its window: what it was graded with, and the kick band since.
    private struct Onset
    {
        public int Frame;
        public float Base, Den, Peak, SubPeak, Evidence, Size, Loud, Gain, Mask, Grade, AudioAge, Age;
        public bool Warm, Trusted, Reevaluated, Counted;
    }

    private readonly Onset[] onsets = new Onset[MaxOnsets];
    private int count, frame;
    private float strongRate, weakRate, memory, sinceHold = float.MaxValue, sinceMemory = float.MaxValue, sinceKick = float.MaxValue;
    private float sinceEvent = float.MaxValue, eventKick, sincePulse = float.MaxValue;
    private bool eventWeak;
    private readonly float[] heldBlocks = new float[HoldBlocks];   // the song position's largest per block
    private int heldHead;
    private float heldBlockSeconds, heldBlockMax;
    private float sizeReference, peakRise, heldPosition, lastPosition;
    private float fastAmp, slowAmp, lastAmplitude, audibleSeconds, levelBefore, levelTwoBefore;
    private float frameGrade, frameRaw, lastKickLike, lastGrade;
    private float fastSub, fastUpper, slowSub, slowUpper, quietSeconds;
    private bool rose, lastAudible, music, hasFast, hasSlow;

    // The graded, decaying pulse: AudioFeatures.Beat.
    public float Beat { get; private set; }

    // The same pulse at Sensitivity 1. Sensitivity is the listener's gain on Level and Beat (doc 03 §2); the groove's
    // pump (AudioAnalyzer) and the mask follow the music, so they read this one.
    public float RawBeat { get; private set; }

    // 0 (no drums) .. 1 (a drum groove).
    public float Punch { get; private set; }

    // 0 (a light groove: a folk or rock kit under guitars and voice) .. 1 (a heavy one: rap, EDM). See gain above.
    public float Weight { get; private set; }

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
    // kick-likeness, the latest grade and the groove's memory.
    internal float Drive => strongRate;
    internal float LastKickLike => lastKickLike;
    internal float LastGrade => lastGrade;
    internal float Memory => memory;

    // A new Process call (frame): onsets of earlier frames are re-evaluated on its first step with new audio.
    public void BeginFrame() => frame++;

    // One analysis step with new audio. fired: BeatDetector fired on this step. kickLevel: the FFT kick band's summed
    // magnitude (BeatResult.KickLevel). upperAmplitude: √ of the step's weighted power above the bass band
    // (BandAnalyzer.WeightedPower's Upper). audible: the window is above near-silence. position: SongLoudness.Position.
    // evidence: KickEnvelope's reading. dtSeconds: the step's share of frame time; audioSeconds: how much audio it
    // analyzed (the same unless packets arrive in bursts). isMusic: BeatDetector.IsMusic (onsets, not a steady noise).
    // strict: the beat passed doc 03's own threshold (BeatResult.Strict).
    public void Update(bool fired, float kickLevel, float upperAmplitude, bool audible, float position, in KickEvidence evidence,
        float dtSeconds, float audioSeconds, AudioTuning tuning, bool isMusic = true, bool strict = true)
    {
        float dt = Finite(dtSeconds);
        float audio = Finite(audioSeconds);
        float amplitude = Finite(upperAmplitude);
        if (!float.IsFinite(kickLevel) || kickLevel < 0) kickLevel = 0;
        if (!float.IsFinite(position)) position = 0;
        music = isMusic;
        TempoBeatAge = -1;
        Age(dt);
        Hold(position, dt);
        Weigh(audible, evidence, dt);
        for (int i = 0; i < count; i++)
        {
            ref Onset o = ref onsets[i];
            o.Peak = MathF.Max(o.Peak, kickLevel);
            o.AudioAge += audio;
            o.Age += dt;
            if (!o.Reevaluated && o.Frame != frame) Reevaluate(ref o, tuning, evidence);
        }
        for (int i = 0; i < count;)
        {
            if (onsets[i].AudioAge >= OnsetWindowSeconds) Confirm(i);
            else i++;
        }
        if (fired) Start(kickLevel, evidence, tuning, strict);
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
        float decay = MathF.Exp(-dt / tuning.BeatDecaySeconds);
        Beat = rose ? MathF.Max(Beat * decay, frameGrade) : Beat * decay;
        RawBeat = rose ? MathF.Max(RawBeat * decay, frameRaw) : RawBeat * decay;
        if (Beat < Negligible) Beat = 0;
        if (RawBeat < Negligible) RawBeat = 0;
        rose = false;
        frameGrade = frameRaw = 0;
    }

    public void Reset()
    {
        Array.Clear(onsets);
        count = frame = 0;
        strongRate = weakRate = memory = sizeReference = peakRise = heldPosition = lastPosition = 0;
        Array.Clear(heldBlocks);
        heldHead = 0;
        heldBlockSeconds = heldBlockMax = 0;
        sinceHold = sinceMemory = sinceKick = sinceEvent = sincePulse = float.MaxValue;
        eventKick = 0;
        eventWeak = false;
        fastAmp = slowAmp = lastAmplitude = audibleSeconds = levelBefore = levelTwoBefore = 0;
        frameGrade = frameRaw = lastKickLike = lastGrade = Beat = RawBeat = Punch = Weight = 0;
        fastSub = fastUpper = slowSub = slowUpper = quietSeconds = 0;
        TempoBeatAge = -1;
        rose = lastAudible = music = hasFast = hasSlow = false;
    }

    // A beat fired: grade it now, from what the time domain has already seen (nothing in the warm-up: a punch held from
    // before a gap must not grade the next song's first notes, whose kick band rises from the silence).
    private void Start(float kickLevel, in KickEvidence evidence, AudioTuning tuning, bool strict)
    {
        if (count == MaxOnsets) Confirm(0);
        ref Onset o = ref onsets[count++];
        o = default;
        o.Frame = frame;
        o.Warm = audibleSeconds >= WarmUpSeconds;
        o.Trusted = strict || music;
        o.Base = MathF.Min(levelBefore, levelTwoBefore);
        o.Den = MathF.Max(fastAmp, slowAmp);
        o.Peak = kickLevel;
        o.SubPeak = Finite(evidence.SubPeak);
        if (o.Warm && Resumes(evidence)) Resume();
        o.Evidence = MathF.Max(Smoothstep(EvidenceFromDb, EvidenceAtDb, evidence.ProminenceDb), Smoothstep(RiseFromDb, RiseAtDb, evidence.RiseDb));
        o.Size = sizeReference > 0 ? Smoothstep(SizeFrom, SizeAt, o.SubPeak / sizeReference) : 1;
        o.Loud = MathF.Max(Smoothstep(QuietFrom, LoudAt, heldPosition), LoudFloor * Punch * Smoothstep(LoudFloorFrom, LoudFloorAt, heldPosition));
        o.Gain = LightGain + (1 - LightGain) * Weight;
        o.Mask = sincePulse < MaskSeconds ? Smoothstep(MaskRiseFromDb, MaskRiseAtDb, evidence.RiseDb) : 1;
        float raw = o.Warm ? Clamp(Punch * o.Evidence * o.Size * o.Loud * o.Gain * o.Mask) : 0;
        o.Grade = Clamp(tuning.Sensitivity * raw);
        Raise(o.Grade, raw);
    }

    // One frame later: the FFT kick band has seen the kick too, and KickEnvelope (latest: this step's reading) more of
    // its attack. The grade only rises.
    private void Reevaluate(ref Onset o, AudioTuning tuning, in KickEvidence latest)
    {
        o.Reevaluated = true;
        if (!o.Warm) return;
        if (Resumes(latest)) Resume();
        float prominence = Prominence(o), rise = Rise(o);
        float evidence = MathF.Max(o.Evidence, MathF.Max(Smoothstep(LaterProminenceFromDb, LaterProminenceAtDb, prominence), Smoothstep(LaterRiseFromDb, LaterRiseAtDb, rise)));
        float instant = music ? Smoothstep(InstantFromDb, InstantAtDb, prominence) : 0;
        float raw = Clamp(MathF.Max(Punch, instant) * evidence * o.Size * o.Loud * o.Gain * o.Mask);
        float grade = Clamp(tuning.Sensitivity * raw);
        if (grade <= o.Grade) return;
        o.Grade = grade;
        Raise(grade, raw);
    }

    // The onset's window has closed: what it says about the music, then it is gone. A steady noise says nothing.
    private void Confirm(int index)
    {
        Onset o = onsets[index];
        for (int i = index + 1; i < count; i++) onsets[i - 1] = onsets[i];
        onsets[--count] = default;
        if (!o.Warm || !o.Trusted) return;
        float prominence = Prominence(o), rise = Rise(o), riseAmplitude = MathF.Max(o.Peak - o.Base, 0);
        float kick = Smoothstep(KickFromDb, KickAtDb, prominence);
        lastKickLike = kick;
        // A beat within EventSeconds of the one before it (a flam, a chord's second onset, a bass note after the strum,
        // with a Beat spacing tuned under it) is the same musical event: the counts take only what it adds.
        bool sameEvent = sinceEvent < EventSeconds;
        bool weak = rise >= WeakRiseDb && prominence >= WeakProminenceDb;
        strongRate += sameEvent ? MathF.Max(kick - eventKick, 0) : kick;
        eventKick = sameEvent ? MathF.Max(eventKick, kick) : kick;
        bool countWeak = weak && !(sameEvent && eventWeak);
        eventWeak = sameEvent ? eventWeak || weak : weak;
        sinceEvent = 0;
        if (kick >= 0.5f)
        {
            Resume();
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

    // KickEnvelope's reading of a kick that brings a remembered groove back (in music only).
    private bool Resumes(in KickEvidence evidence) => music && evidence.RiseDb >= ResumeRiseDb && evidence.ProminenceDb >= ResumeProminenceDb;

    // A groove the song had before comes back at once: the drop after a breakdown, the band after a bridge.
    private void Resume()
    {
        if (memory < ResumeMemory || Punch >= memory) return;
        Punch = memory;
        sinceHold = 0;
    }

    // raw: the grade at Sensitivity 1.
    private void Raise(float grade, float raw)
    {
        lastGrade = grade;
        if (raw >= MaskArm) sincePulse = 0;
        if (grade <= 0) return;
        frameGrade = MathF.Max(frameGrade, grade);
        frameRaw = MathF.Max(frameRaw, raw);
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
        sincePulse = sincePulse < float.MaxValue ? sincePulse + dt : sincePulse;
    }

    // The groove's weight (gain above): the kick band's power over the music above it, averaged over WeightFastSeconds
    // while anything audible plays and over WeightSeconds while a groove plays (from the fast average when the song's
    // first groove starts). GapSeconds of near-silence (between songs) clears both. Ratios of powers: the volume doesn't
    // move it. The fast averages start from 0, so their ratio weighs the song's first steps like any others: seeded with
    // the first step instead, a step between two kicks stood for the whole song so far, and a groove's weight took 9 s
    // to come up.
    private void Weigh(bool audible, in KickEvidence evidence, float dt)
    {
        if (!audible)
        {
            quietSeconds = MathF.Min(quietSeconds + dt, SongLoudness.GapSeconds);
            if (quietSeconds >= SongLoudness.GapSeconds) hasFast = hasSlow = false;
            return;
        }
        quietSeconds = 0;
        float sub = Finite(evidence.SubPower), up = Finite(evidence.UpperPower);
        if (!hasFast)
        {
            fastSub = fastUpper = 0;
            hasFast = true;
        }
        float fast = 1 - MathF.Exp(-dt / WeightFastSeconds);
        fastSub = Flushed(fastSub + (sub - fastSub) * fast);
        fastUpper = Flushed(fastUpper + (up - fastUpper) * fast);
        if (Punch >= 0.5f)
        {
            if (!hasSlow)
            {
                slowSub = fastSub;
                slowUpper = fastUpper;
                hasSlow = true;
            }
            else
            {
                float a = 1 - MathF.Exp(-dt / WeightSeconds);
                slowSub = Flushed(slowSub + (sub - slowSub) * a);
                slowUpper = Flushed(slowUpper + (up - slowUpper) * a);
            }
        }
        float s = hasSlow ? slowSub : fastSub, u = hasSlow ? slowUpper : fastUpper;
        Weight = s > 0 && u > 0 ? Smoothstep(WeightFromDb, WeightAtDb, 10 * MathF.Log10(s / u)) : s > 0 ? 1 : 0;
    }

    // How loud the music has just been: the song position's largest over the last LoudHoldSeconds, from HoldBlocks
    // blocks (the window is 0.75–0.8 s long). Round 2 held a new peak for LoudHoldSeconds and then let it fall: in a
    // steady groove a kick a hair under the loudest one so far didn't renew that hold, so it ran out before every other
    // kick, which was then graded from the dip before it (a kick track's alternate kicks at 0.3–0.6 of the others).
    private void Hold(float position, float dt)
    {
        const float block = LoudHoldSeconds / HoldBlocks;
        heldBlockMax = MathF.Max(heldBlockMax, position);
        heldBlockSeconds += dt;
        for (int i = 0; i < HoldBlocks && heldBlockSeconds >= block; i++)
        {
            heldBlocks[heldHead] = heldBlockMax;
            heldHead = (heldHead + 1) % HoldBlocks;
            heldBlockSeconds -= block;
            heldBlockMax = position;
        }
        if (heldBlockSeconds >= block) heldBlockSeconds = 0;      // a step longer than the whole window
        float held = heldBlockMax;
        for (int i = 0; i < HoldBlocks; i++) held = MathF.Max(held, heldBlocks[i]);
        heldPosition = held;
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

    private static float Flushed(float power) => power < TinyPower ? 0 : power;

    private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    internal static float Smoothstep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        if (float.IsNaN(t)) t = 0;
        return t * t * (3 - 2 * t);
    }
}
