namespace Rimlight.Core.Audio;

// IsSilent (doc 03 §2): true once the raw time-domain RMS has stayed below SilenceThresholdDb for
// SilenceHoldMs; false again as soon as it rises above SilenceExitThresholdDb (hysteresis).
internal sealed class SilenceDetector
{
    private float belowSeconds;

    public bool IsSilent { get; private set; }

    // rms: linear RMS of the samples that arrived this frame (0 when the analyzer treats a packet gap as silence).
    public bool Update(float rms, float dtSeconds, AudioTuning tuning)
    {
        float db = 20 * MathF.Log10(rms + 1e-12f);
        if (IsSilent)
        {
            if (db > tuning.SilenceExitThresholdDb)
            {
                IsSilent = false;
                belowSeconds = 0;
            }
        }
        else if (db < tuning.SilenceThresholdDb)
        {
            belowSeconds += dtSeconds;
            if (belowSeconds * 1000 >= tuning.SilenceHoldMs) IsSilent = true;
        }
        else
        {
            belowSeconds = 0;
        }
        return IsSilent;
    }

    public void Reset()
    {
        IsSilent = false;
        belowSeconds = 0;
    }
}
