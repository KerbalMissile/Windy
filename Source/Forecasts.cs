using System;
using UnityEngine;

namespace Windy
{
    // Simple procedural wind generator used by the Wind class.
    // No gusts and no jetstreams right now (per your request).
    public class Forecasts
    {
        // Small struct to pass wind values around
        public struct ForecastData
        {
            public float windSpeed;
            public float windDirection;
            public float altitude;
            public string description;
        }

        // Seeds so the noise is deterministic per game session
        private static float seedTime;
        private static float seedAlt;
        private static float seedDir;

        // Call once at game start (or when plugin starts)
        public static void Initialize()
        {
            seedTime = UnityEngine.Random.Range(0f, 10000f);
            seedAlt  = UnityEngine.Random.Range(0f, 10000f);
            seedDir  = UnityEngine.Random.Range(0f, 10000f);
        }

        // Current wind at altitude and time
        public static ForecastData GetCurrentWind(double altitude, double currentTime)
        {
            return CalculateWind(altitude, currentTime);
        }

        // Forecast minutesAhead into the future
        public static ForecastData GetForecast(double altitude, double currentTime, float minutesAhead)
        {
            double futureTime = currentTime + (minutesAhead * 60.0);
            return CalculateWind(altitude, futureTime);
        }

        // Multi-octave Perlin (fBm) helper to make wind more natural
        private static float FBmNoise(float x, float y, int octaves)
        {
            float amp = 1f;
            float freq = 1f;
            float sum = 0f;
            float max = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
                max += amp;
                amp *= 0.5f;
                freq *= 2f;
            }

            if (max == 0f) return 0f;
            return sum / max; // normalized 0..1
        }

        // The actual wind calculation (simple and deterministic)
        private static ForecastData CalculateWind(double altitude, double time)
        {
            // Read user's max setting; if it fails, use a safe default
            float userMax = GameDifficulty.GetMaxWindSpeed();
            if (userMax <= 1f) userMax = 25f; // fallback safe cap

            // Tuning constants (easy to tweak)
            const float BaseMean = 3.0f;    // baseline wind floor (m/s)
            const float BaseVar = 10.0f;    // how much above the floor the noise can go
            const float TimeScale = 0.008f; // speed of time evolution
            const float AltScale = 0.0006f; // altitude sampling scale
            const int Octaves = 4;          // octaves for fBm

            float t = (float)time;
            float altF = (float)altitude;

            // 1) Base procedural speed (fBm)
            float noise = FBmNoise(seedTime + t * TimeScale, seedAlt + altF * AltScale, Octaves);
            float rawSpeed = BaseMean + (noise * BaseVar); // ~3..13 m/s typical before scaling

            // 2) Altitude shear: wind tends to increase with altitude a bit
            float shearFactor = 1f + (altF / 5000f); // small increase per 5 km
            float speedAfterShear = rawSpeed * shearFactor;

            // 3) Respect user's max setting strictly
            float finalSpeed = Mathf.Clamp(speedAfterShear, 0.0f, userMax);

            // 4) Direction (also smooth using fBm)
            float dirNoise = FBmNoise(seedDir + t * (TimeScale * 0.9f), seedAlt + altF * (AltScale * 0.5f), 3);
            float direction = Mathf.Repeat(dirNoise * 360f, 360f); // 0..360 degrees

            // 5) Simple description for UI
            string desc = "Stable";
            if (finalSpeed > userMax * 0.8f) desc = "Strong";
            else if (finalSpeed > userMax * 0.45f) desc = "Breezy";

            ForecastData d;
            d.windSpeed = finalSpeed;
            d.windDirection = direction;
            d.altitude = altF;
            d.description = desc;
            return d;
        }
    }
}