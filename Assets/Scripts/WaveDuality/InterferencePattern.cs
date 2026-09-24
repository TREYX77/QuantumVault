using System.Collections.Generic;
using UnityEngine;

// A contiguous stretch of wall, in metres from the centre.
public struct Band
{
    public float start;
    public float end;

    public float Width => end - start;
    public float Centre => (start + end) * 0.5f;

    public Band(float start, float end)
    {
        this.start = start;
        this.end = end;
    }
}

// Double-slit maths. Wavelength is a game value in metres, not physical light: fringe
// spacing is wavelength * distance / separation, so real nanometres would put the fringes
// micrometres apart. Pure functions, no MonoBehaviour.
public static class InterferencePattern
{
    // Unobserved. Single-slit envelope times the two-slit fringes.
    public static float WaveIntensity(float x, float wavelength, float slitSeparation,
                                      float slitWidth, float screenDistance, int slitCount)
    {
        if (wavelength <= 0.0001f) return 0f;

        float sinTheta = x / Mathf.Sqrt(x * x + screenDistance * screenDistance);

        float beta = Mathf.PI * slitWidth * sinTheta / wavelength;
        float envelope = Sinc(beta);
        envelope *= envelope;

        if (slitCount < 2) return envelope;

        float alpha = Mathf.PI * slitSeparation * sinTheta / wavelength;
        float fringe = Mathf.Cos(alpha);

        return envelope * fringe * fringe;
    }

    // Observed. Knowing which slit each photon took destroys the fringes, leaving a plain
    // bump behind each slit. This difference is the whole mechanic.
    public static float ParticleIntensity(float x, float slitSeparation, float slitWidth,
                                          float screenDistance, float gateDistance, int slitCount)
    {
        // Slit images spread as they project onto the wall.
        float spread = gateDistance > 0.01f ? (gateDistance + screenDistance) / gateDistance : 1f;
        float sigma = Mathf.Max(0.1f, slitWidth * spread);

        if (slitCount < 2) return Gaussian(x, 0f, sigma);

        float offset = slitSeparation * 0.5f * spread;

        return Mathf.Max(Gaussian(x, -offset, sigma), Gaussian(x, offset, sigma));
    }

    // Intensity across the wall, normalised so the brightest point reads 1.
    public static float[] Sample(int samples, float width, bool observed, float wavelength,
                                 float slitSeparation, float slitWidth, float screenDistance,
                                 float gateDistance, int slitCount)
    {
        samples = Mathf.Max(8, samples);

        float[] values = new float[samples];
        float peak = 0f;

        for (int i = 0; i < samples; i++)
        {
            float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, i / (float)(samples - 1));

            values[i] = observed
                ? ParticleIntensity(x, slitSeparation, slitWidth, screenDistance, gateDistance, slitCount)
                : WaveIntensity(x, wavelength, slitSeparation, slitWidth, screenDistance, slitCount);

            peak = Mathf.Max(peak, values[i]);
        }

        if (peak > 0.0001f)
        {
            for (int i = 0; i < samples; i++) values[i] /= peak;
        }

        return values;
    }

    // Stretches above the threshold become openings. Anything narrower than the player is
    // filled back in, so dim outer fringes read as solid rather than as slots too thin to enter.
    public static List<Band> ToOpenings(float[] intensity, float width, float threshold, float minWidth)
    {
        List<Band> openings = new List<Band>();

        if (intensity == null || intensity.Length < 2) return openings;

        bool inside = false;
        float start = 0f;

        for (int i = 0; i < intensity.Length; i++)
        {
            float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, i / (float)(intensity.Length - 1));
            bool bright = intensity[i] >= threshold;

            if (bright && !inside)
            {
                inside = true;
                start = x;
            }
            else if (!bright && inside)
            {
                inside = false;
                AddIfWideEnough(openings, start, x, minWidth);
            }
        }

        if (inside) AddIfWideEnough(openings, start, width * 0.5f, minWidth);

        return openings;
    }

    // The solid stretches are whatever the openings left over.
    public static List<Band> ToSolids(List<Band> openings, float width)
    {
        List<Band> solids = new List<Band>();
        float cursor = -width * 0.5f;

        foreach (Band opening in openings)
        {
            if (opening.start > cursor) solids.Add(new Band(cursor, opening.start));

            cursor = Mathf.Max(cursor, opening.end);
        }

        if (cursor < width * 0.5f) solids.Add(new Band(cursor, width * 0.5f));

        return solids;
    }

    private static void AddIfWideEnough(List<Band> bands, float start, float end, float minWidth)
    {
        if (end - start >= minWidth) bands.Add(new Band(start, end));
    }

    private static float Sinc(float v)
    {
        return Mathf.Abs(v) < 0.00001f ? 1f : Mathf.Sin(v) / v;
    }

    private static float Gaussian(float x, float centre, float sigma)
    {
        float d = (x - centre) / sigma;

        return Mathf.Exp(-0.5f * d * d);
    }
}
