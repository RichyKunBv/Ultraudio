using System;

namespace Ultraudio.Services;

/// <summary>
/// Wraps BASS FFT data retrieval and normalizes it to a float array
/// suitable for the spectrum visualizer.
/// </summary>
public class SpectrumAnalyzer
{
    private const int FftBins = 1024; // Half of FFT_SIZE=2048
    private const int DisplayBars = 64;

    private readonly float[] _rawFft = new float[FftBins];
    private readonly float[] _barValues = new float[DisplayBars];
    private readonly float[] _peakValues = new float[DisplayBars];
    private readonly float[] _peakDecay = new float[DisplayBars];

    private const float PeakHoldFrames = 30f;
    private const float DecayRate = 0.015f;

    private readonly AudioEngine _engine;

    public SpectrumAnalyzer(AudioEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Call this every frame (60fps target).
    /// Returns the processed bar values and peak hold values.
    /// </summary>
    public (float[] bars, float[] peaks) Update()
    {
        bool hasData = _engine.GetFFTData(_rawFft);

        for (int bar = 0; bar < DisplayBars; bar++)
        {
            float barValue = 0f;

            if (hasData)
            {
                // Map DisplayBars to FFT bins (logarithmic-ish grouping)
                int startBin = (int)(Math.Pow(bar / (double)DisplayBars, 1.5) * FftBins * 0.5);
                int endBin   = (int)(Math.Pow((bar + 1) / (double)DisplayBars, 1.5) * FftBins * 0.5);
                endBin = Math.Max(endBin, startBin + 1);
                endBin = Math.Min(endBin, FftBins - 1);

                float sum = 0f;
                for (int b = startBin; b < endBin; b++)
                    sum = Math.Max(sum, _rawFft[b]);

                // Normalize: BASS FFT values are typically 0..1 but can exceed
                // Convert to dB and scale to 0..1 range
                float db = sum > 0 ? 20f * (float)Math.Log10(sum) : -80f;
                barValue = Math.Clamp((db + 80f) / 80f, 0f, 1f);
            }

            // Smooth bar with slight attack/decay
            if (barValue > _barValues[bar])
                _barValues[bar] = barValue * 0.7f + _barValues[bar] * 0.3f; // fast attack
            else
                _barValues[bar] = barValue * 0.1f + _barValues[bar] * 0.9f; // slow decay

            // Peak hold
            if (barValue >= _peakValues[bar])
            {
                _peakValues[bar] = barValue;
                _peakDecay[bar] = PeakHoldFrames;
            }
            else
            {
                _peakDecay[bar]--;
                if (_peakDecay[bar] <= 0)
                    _peakValues[bar] = Math.Max(0, _peakValues[bar] - DecayRate);
            }
        }

        return (_barValues, _peakValues);
    }

    public int BarCount => DisplayBars;
}
