using System;
using System.Reflection;
using UnityEngine;

namespace Windy
{
    public class GameDifficulty : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Windy"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override string Section { get { return "Windy"; } }
        public override int SectionOrder { get { return 1; } }
        public override string DisplaySection { get { return Section; } }
        public override bool HasPresets { get { return false; } }

        [GameParameters.CustomParameterUI("Enable Wind", toolTip = "Turn all Windy effects on or off.", autoPersistance = true)]
        public bool windEnabled = true;

        [GameParameters.CustomIntParameterUI("Max Wind Speed (m/s)", toolTip = "Maximum wind speed.", minValue = 0, maxValue = 100, stepSize = 5, autoPersistance = true)]
        public int maxWindSpeed = 25;

        [GameParameters.CustomParameterUI("Enable Headwind Lift", toolTip = "Headwinds increase lift on wings.", autoPersistance = true)]
        public bool enableHeadwindLift = true;

        [GameParameters.CustomIntParameterUI("Headwind Lift (%)", toolTip = "Multiplier for extra lift.", minValue = 0, maxValue = 500, stepSize = 10, autoPersistance = true)]
        public int headwindLiftPercent = 150;

        // feature toggles
        [GameParameters.CustomParameterUI("Enable Jet Streams (WIP)", toolTip = "Work in progress.", autoPersistance = true)]
        public bool enableJetStreams = true;

        [GameParameters.CustomParameterUI("Enable Wind Shear", toolTip = "Apply vertical wind shear effects.", autoPersistance = true)]
        public bool enableWindShear = true;

        [GameParameters.CustomParameterUI("Enable Gusts (WIP)", toolTip = "Work in progress.", autoPersistance = true)]
        public bool enableGusts = true;

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            if (member.Name == "windEnabled")
            {
                return true;
            }
            // hide everything else when wind is off
            return windEnabled;
        }

        public static GameDifficulty GetSettings()
        {
            if (HighLogic.CurrentGame == null)
            {
                return null;
            }
            return HighLogic.CurrentGame.Parameters.CustomParams<GameDifficulty>();
        }

        public static bool IsWindEnabled()
        {
            GameDifficulty settings = GetSettings();
            if (settings == null) return false;
            return settings.windEnabled;
        }

        public static float GetMaxWindSpeed()
        {
            GameDifficulty settings = GetSettings();
            if (settings == null) return 25f;
            return (float)settings.maxWindSpeed;
        }

        public static bool AreHeadwindLiftEnabled()
        {
            GameDifficulty settings = GetSettings();
            if (settings == null) return false;
            return settings.enableHeadwindLift;
        }

        public static float GetHeadwindLiftMultiplier()
        {
            GameDifficulty settings = GetSettings();
            if (settings == null) return 1.0f;
            return (float)settings.headwindLiftPercent / 100f;
        }

        // helpers for feature toggles
        public static bool AreJetStreamsEnabled()
        {
            GameDifficulty settings = GetSettings();
            return settings != null && settings.windEnabled && settings.enableJetStreams;
        }

        public static bool IsWindShearEnabled()
        {
            GameDifficulty settings = GetSettings();
            return settings != null && settings.windEnabled && settings.enableWindShear;
        }

        public static bool AreGustsEnabled()
        {
            GameDifficulty settings = GetSettings();
            return settings != null && settings.windEnabled && settings.enableGusts;
        }
    }
}