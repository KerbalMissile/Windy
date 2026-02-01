using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KSP;

namespace Windy
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Wind : MonoBehaviour
    {
        // simple static instance (old-compiler friendly)
        public static Wind Instance = null;

        // Wind state
        private Vector3 windDirection;       // Direction the wind is blowing TO
        private float currentWindSpeed;      // Speed in m/s
        private float currentWindHeading;    // Heading in degrees (FROM)

        // Physics tuning
        private const float DragCoefficient = 2.0f;
        private const float AreaScale = 1.2f;
        private const float MaxLateralAccel = 15f;

        // Headwind-lift tuning
        private const float WingAreaPerPart = 2.0f;
        private const float LiftCoefficient = 0.45f;
        private const float MaxHeadwindLiftAccel = 2.0f;

        // Configurable body scales (bodyName -> scale)
        private Dictionary<string, float> bodyScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Timers
        private float updateTimer = 0f;
        private float writeTimer = 0f;

        private string dataFilePath;

        // simple old-style properties for external read
        public float CurrentWindSpeed { get { return currentWindSpeed; } }
        public float CurrentWindHeading { get { return currentWindHeading; } }

        void Awake()
        {
            Instance = this;
            dataFilePath = Path.Combine(KSPUtil.ApplicationRootPath, "WindyData.txt");
            LoadBodyScales();
        }

        void Start()
        {
            Forecasts.Initialize();
        }

        void Update()
        {
            if (!GameDifficulty.IsWindEnabled())
            {
                currentWindSpeed = 0f;
                return;
            }

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                currentWindSpeed = 0f;
                return;
            }

            if (!IsWindBody(v.mainBody) || IsInSpace(v))
            {
                currentWindSpeed = 0f;
                return;
            }

            updateTimer += Time.deltaTime;
            if (updateTimer >= 0.5f)
            {
                updateTimer = 0f;
                UpdateWind();
            }

            writeTimer += Time.deltaTime;
            if (writeTimer >= 0.2f)
            {
                writeTimer = 0f;
                WriteDataFile();
            }
        }

        // Populate defaults and then attempt to read user config nodes to override/add bodies
        private void LoadBodyScales()
        {
            bodyScales.Clear();

            // Built-in defaults (whitelist)
            bodyScales["Kerbin"] = 1.0f;
            bodyScales["Duna"] = 0.45f;
            bodyScales["Eve"] = 1.8f;
            bodyScales["Jool"] = 2.6f;
            bodyScales["Laythe"] = 0.6f;

            // Outer Planets Mod defaults (requested mappings)
            bodyScales["Sarnus"] = 2.6f;
            bodyScales["Urlum"] = 2.6f;
            bodyScales["Neidon"] = 2.6f;
            bodyScales["Tekto"] = 0.6f;
            bodyScales["Thatmo"] = 0.02f;

            // Now look for user configuration to override or add custom bodies.
            ConfigNode[] cfgNodes = GameDatabase.Instance.GetConfigNodes("WindyBodies");
            if (cfgNodes == null || cfgNodes.Length == 0)
            {
                cfgNodes = GameDatabase.Instance.GetConfigNodes("WINDY_BODIES");
            }

            if (cfgNodes != null && cfgNodes.Length > 0)
            {
                int i;
                for (i = 0; i < cfgNodes.Length; i++)
                {
                    ConfigNode cfg = cfgNodes[i];
                    ConfigNode[] bodyNodes = cfg.GetNodes("BODY");
                    int j;
                    for (j = 0; j < bodyNodes.Length; j++)
                    {
                        ConfigNode bodyNode = bodyNodes[j];
                        string name = bodyNode.GetValue("name");
                        string scaleStr = bodyNode.GetValue("scale");
                        if (string.IsNullOrEmpty(name)) continue;

                        float scale = 0f;
                        if (!string.IsNullOrEmpty(scaleStr))
                        {
                            float.TryParse(scaleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
                        }

                        bodyScales[name] = scale;
                    }
                }
            }
        }

        // Only bodies explicitly listed in bodyScales and with scale > 0 have wind.
        private bool IsWindBody(CelestialBody body)
        {
            if (body == null) return false;
            float s;
            if (bodyScales.TryGetValue(body.bodyName, out s))
            {
                return s > 0f;
            }
            return false;
        }

        // Return scale if present, otherwise 0
        private float GetBodyWindScale(CelestialBody body)
        {
            if (body == null) return 0f;
            float s;
            if (bodyScales.TryGetValue(body.bodyName, out s))
            {
                return s;
            }
            return 0f;
        }

        private bool IsInSpace(Vessel v)
        {
            if (v == null || v.mainBody == null) return true;
            if (v.altitude >= v.mainBody.atmosphereDepth) return true;
            if (v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.ESCAPING) return true;
            return false;
        }

        private void UpdateWind()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody == null) return;

            Forecasts.ForecastData d = Forecasts.GetCurrentWind(v.altitude, Planetarium.GetUniversalTime());

            float scale = GetBodyWindScale(v.mainBody);
            if (scale <= 0f)
            {
                currentWindSpeed = 0f;
                currentWindHeading = 0f;
                windDirection = Vector3.zero;
                return;
            }

            float raw = d.windSpeed * scale;

            float limit = GameDifficulty.GetMaxWindSpeed();
            if (limit <= 1f) limit = 25f;
            currentWindSpeed = Mathf.Min(raw, limit);

            currentWindHeading = d.windDirection;
            float rad = currentWindHeading * Mathf.Deg2Rad;
            windDirection = new Vector3(-Mathf.Sin(rad), 0f, -Mathf.Cos(rad)).normalized;
        }

        void FixedUpdate()
        {
            if (!GameDifficulty.IsWindEnabled()) return;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody == null || !IsWindBody(v.mainBody) || IsInSpace(v)) return;

            Part root = v.rootPart;
            if (root == null || root.rb == null) return;

            float massKg = v.GetTotalMass() * 1000f;
            float rho = (float)v.atmDensity;
            if (rho <= 0f || massKg <= 0f) return;

            // 1) SIDEWAYS DRAG (the push)
            if (currentWindSpeed > 0.01f)
            {
                float qSide = 0.5f * rho * currentWindSpeed * currentWindSpeed;
                float areaSide = v.vesselSize.magnitude * v.vesselSize.magnitude * AreaScale;
                float forceSide = qSide * DragCoefficient * areaSide;
                float accelSide = Mathf.Min(forceSide / massKg, MaxLateralAccel);
                root.rb.AddForce(windDirection * accelSide, ForceMode.Acceleration);
            }

            // 2) HEADWIND LIFT (safe, total-lift approach)
            if (GameDifficulty.AreHeadwindLiftEnabled())
            {
                // Convert double vector velocity to float vector
                Vector3 surfVel = (Vector3)v.GetSrfVelocity();
                Vector3 windVec = windDirection * currentWindSpeed;
                Vector3 airVel = surfVel - windVec;

                float surfSpeed = (float)v.srfSpeed;
                float airSpeed = airVel.magnitude;

                float deltaSquare = (airSpeed * airSpeed) - (surfSpeed * surfSpeed);

                if (deltaSquare > 1.0f)
                {
                    float deltaQ = 0.5f * rho * deltaSquare;

                    float totalWingArea = 0f;
                    try
                    {
                        // explicit, safe wing detection for older compilers / KSP versions
                        foreach (Part p in v.parts)
                        {
                            if (p == null) continue;

                            // get part title safely
                            string title = "";
                            if (p.partInfo != null && p.partInfo.title != null)
                            {
                                title = p.partInfo.title.ToLower();
                            }

                            bool isWing = false;
                            if (title.Contains("wing") || title.Contains("fin"))
                            {
                                isWing = true;
                            }
                            else
                            {
                                // check modules for lifting surface in a safe loop
                                int k;
                                for (k = 0; k < p.Modules.Count; k++)
                                {
                                    PartModule pm = p.Modules[k];
                                    if (pm == null) continue;
                                    if (pm.moduleName == "ModuleLiftingSurface")
                                    {
                                        isWing = true;
                                        break;
                                    }
                                }
                            }

                            if (isWing)
                            {
                                totalWingArea += WingAreaPerPart;
                            }
                        }
                    }
                    catch
                    {
                        totalWingArea = v.vesselSize.magnitude * v.vesselSize.magnitude * AreaScale;
                    }

                    if (totalWingArea <= 0f)
                    {
                        totalWingArea = v.vesselSize.magnitude * v.vesselSize.magnitude * AreaScale;
                    }

                    float multiplier = GameDifficulty.GetHeadwindLiftMultiplier();
                    if (multiplier <= 0f) multiplier = 1.0f;

                    float liftNewtons = deltaQ * LiftCoefficient * totalWingArea * multiplier;

                    float liftAccel = liftNewtons / massKg;
                    liftAccel = Mathf.Min(liftAccel, MaxHeadwindLiftAccel);

                    root.rb.AddForce(root.transform.up.normalized * liftAccel, ForceMode.Acceleration);
                }
            }
        }

        private void WriteDataFile()
        {
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                CelestialBody b = (v != null) ? v.mainBody : null;

                if (!IsWindBody(b) || IsInSpace(v))
                {
                    double unixNow = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                    using (StreamWriter w = new StreamWriter(dataFilePath, false))
                    {
                        w.WriteLine("speed=0.00");
                        w.WriteLine("direction_deg=0.0");
                        w.WriteLine("timestamp_unix=" + unixNow.ToString("F3"));
                        w.WriteLine("altitude=0");
                        w.WriteLine("body=" + (b != null ? b.bodyName : "None"));
                        w.WriteLine("forecast_5min_speed=0.00");
                        w.WriteLine("forecast_5min_dir=0.0");
                        w.WriteLine("forecast_10min_speed=0.00");
                        w.WriteLine("forecast_10min_dir=0.0");
                        w.WriteLine("forecast_15min_speed=0.00");
                        w.WriteLine("forecast_15min_dir=0.0");
                    }
                    return;
                }

                double ut = Planetarium.GetUniversalTime();
                double alt = (v != null) ? v.altitude : 0.0;

                Forecasts.ForecastData f5 = Forecasts.GetForecast(alt, ut, 5f);
                Forecasts.ForecastData f10 = Forecasts.GetForecast(alt, ut, 10f);
                Forecasts.ForecastData f15 = Forecasts.GetForecast(alt, ut, 15f);

                float scale = GetBodyWindScale(b);
                float maxWind = GameDifficulty.GetMaxWindSpeed();
                if (maxWind <= 1f) maxWind = 25f;

                float f5s = Mathf.Min((float)(f5.windSpeed * scale), maxWind);
                float f10s = Mathf.Min((float)(f10.windSpeed * scale), maxWind);
                float f15s = Mathf.Min((float)(f15.windSpeed * scale), maxWind);

                double unixNow2 = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                using (StreamWriter w = new StreamWriter(dataFilePath, false))
                {
                    w.WriteLine("speed=" + currentWindSpeed.ToString("F2"));
                    w.WriteLine("direction_deg=" + currentWindHeading.ToString("F1"));
                    w.WriteLine("timestamp_unix=" + unixNow2.ToString("F3"));
                    w.WriteLine("altitude=" + alt.ToString("F0"));
                    w.WriteLine("body=" + b.bodyName);

                    w.WriteLine("forecast_5min_speed=" + f5s.ToString("F2"));
                    w.WriteLine("forecast_5min_dir=" + f5.windDirection.ToString("F1"));
                    w.WriteLine("forecast_10min_speed=" + f10s.ToString("F2"));
                    w.WriteLine("forecast_10min_dir=" + f10.windDirection.ToString("F1"));
                    w.WriteLine("forecast_15min_speed=" + f15s.ToString("F2"));
                    w.WriteLine("forecast_15min_dir=" + f15.windDirection.ToString("F1"));
                }
            }
            catch { }
        }
    }
}