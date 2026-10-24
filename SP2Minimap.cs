using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SP2Minimap
{
    [BepInPlugin("com.sp2.minimap", "SP2 Minimap", "2.9.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;
        public static ConfigFile Cfg;
        public static bool MinimapVisible = true;
        public static bool Minimized = false;
        public static bool GuiVisible = true;
        public static bool IsFullscreen = false;
        public static bool PlacementMode = false;
        public static bool NavEnabled = false;
        public static MinimapRenderer Renderer;
        public static MinimapGui Gui;

        public static Component LocalAircraft;
        public static float CmdThrottle = -1f;      // <0 = 不接管
        public static float CmdPitch, CmdRoll, CmdYaw;  // -1..1，写入原版输入通道
        public static bool NavActive;
        public static float BlockMouseInputUntil = 0f;
        public static bool BlockMouseInput => Time.unscaledTime < BlockMouseInputUntil;

        public static ConfigEntry<KeyCode> CfgKeyToggle, CfgKeyGui, CfgKeyFullscreen;
        public static ConfigEntry<KeyCode> CfgKeyAddWaypointAtCenter, CfgKeyToggleNav;
        public static ConfigEntry<float> CfgOffsetX, CfgOffsetY, CfgSizePx;
        public static ConfigEntry<float> CfgRangeM, CfgIconScale;
        public static ConfigEntry<float> CfgFullscreenMargin, CfgMiniScrollStep;

        // 已固化的常量（不再暴露配置）
        public const float ResetRangeM = 500f;
        public const float CameraHeightDefault = 10000f;
        public static readonly Color SeaColorDefault = new Color(0.10f, 0.30f, 0.50f, 1f);
        public static ConfigEntry<bool> CfgRotateWithHeading, CfgFlipY;
        public static ConfigEntry<bool> CfgShowTerrain, CfgShowSpawn, CfgShowAirport;
        public static ConfigEntry<bool> CfgShowTarget, CfgShowCustomOBJ, CfgShowAircraftMarker, CfgShowLabels, CfgShowTraffic;
        public static ConfigEntry<int> CfgTerrainLayerMask;
        public static ConfigEntry<bool> CfgUseAllLayers;
        public static ConfigEntry<string> CfgExcludeLayers, CfgCustomOBJPrefix, CfgAirportKeywords;
        public static ConfigEntry<bool> CfgShowWaypoints;
        public static ConfigEntry<float> CfgWaypointIconScale;
        public static ConfigEntry<string> CfgUnitSystem;
        public static ConfigEntry<float> CfgArrivalTimeSec;
        public static ConfigEntry<float> CfgPanelX, CfgPanelY, CfgPanelW, CfgPanelH;

        public static ConfigEntry<string> CfgHdMode, CfgAltMode, CfgIasMode;
        public static ConfigEntry<float> CfgFixedHD, CfgFixedAlt, CfgFixedIAS;

        public static ConfigEntry<float> CfgThrottleKp, CfgThrottleKi;
        public static ConfigEntry<float> CfgApMaxBank, CfgApPitchUpMax, CfgApPitchDownMax;
        public static ConfigEntry<bool> CfgApLowManeuver;
        public static ConfigEntry<float> CfgApLowPitchUp, CfgApLowPitchDown;
        public static ConfigEntry<float> CfgApHdgToBank, CfgApAltToPitch, CfgApVsDamp, CfgApRespGain;
        public static ConfigEntry<float> CfgApRollKp, CfgApRollKd, CfgApPitchKp, CfgApPitchKd, CfgApSmooth;
        public static ConfigEntry<float> CfgAiTargetLeadKm;



        void Awake()
        {
            Instance = this; Log = Logger; Cfg = Config;

            CfgKeyToggle = Cfg.Bind("Key", "ToggleMinimap", KeyCode.F6, "");
            CfgKeyGui = Cfg.Bind("Key", "ToggleGui", KeyCode.F7, "");
            CfgKeyFullscreen = Cfg.Bind("Key", "ToggleFullscreen", KeyCode.F5, "");
            CfgKeyAddWaypointAtCenter = Cfg.Bind("Key", "AddWaypointAtCenter", KeyCode.F8, "");
            CfgKeyToggleNav = Cfg.Bind("Key", "ToggleNav", KeyCode.F9, "");

            CfgOffsetX = Cfg.Bind("Layout", "OffsetX", 20f, "");
            CfgOffsetY = Cfg.Bind("Layout", "OffsetY", 20f, "");
            CfgSizePx = Cfg.Bind("Layout", "SizePx", 256f, new ConfigDescription("", new AcceptableValueRange<float>(64f, 1024f)));
            CfgFullscreenMargin = Cfg.Bind("Layout", "FullscreenMargin", 40f, "");

            // ★ 无限缩放
            CfgRangeM = Cfg.Bind("Layout", "RangeM", 500f,
                new ConfigDescription("缩放范围（1m ~ 500km）", new AcceptableValueRange<float>(1f, 500000f)));
            CfgIconScale = Cfg.Bind("Layout", "IconScale", 1f, new ConfigDescription("", new AcceptableValueRange<float>(0.3f, 3f)));
            CfgMiniScrollStep = Cfg.Bind("Layout", "MiniScrollStep", 1.15f, "");

            CfgRotateWithHeading = Cfg.Bind("Behavior", "RotateWithHeading", true, "");
            CfgFlipY = Cfg.Bind("Behavior", "FlipY", true, "");

            CfgShowTerrain = Cfg.Bind("Layers", "ShowTerrain", true, "");
            CfgShowSpawn = Cfg.Bind("Layers", "ShowSpawn", true, "");
            CfgShowAirport = Cfg.Bind("Layers", "ShowAirport", true, "");
            CfgShowTarget = Cfg.Bind("Layers", "ShowTarget", true, "");
            CfgShowCustomOBJ = Cfg.Bind("Layers", "ShowCustomOBJ", true, "");
            CfgShowAircraftMarker = Cfg.Bind("Layers", "ShowAircraftMarker", true, "");
            CfgShowTraffic = Cfg.Bind("Layers", "ShowTraffic", true, "显示其他玩家 / AI 飞机");
            CfgShowLabels = Cfg.Bind("Layers", "ShowLabels", true, "");

            CfgTerrainLayerMask = Cfg.Bind("Detection", "TerrainLayerMask", -1, "");
            CfgUseAllLayers = Cfg.Bind("Detection", "UseAllLayers", true, "");
            CfgExcludeLayers = Cfg.Bind("Detection", "ExcludeLayers", "", "");
            CfgCustomOBJPrefix = Cfg.Bind("Detection", "CustomOBJPrefix", "WorldModelRoot", "");
            CfgAirportKeywords = Cfg.Bind("Detection", "AirportKeywords", "Runway,Hangar,Terminal,Tower,Airport,Airstrip", "");

            CfgShowWaypoints = Cfg.Bind("Waypoint", "ShowWaypoints", true, "");
            CfgWaypointIconScale = Cfg.Bind("Waypoint", "IconScale", 1f, new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 3f)));
            CfgUnitSystem = Cfg.Bind("Waypoint", "UnitSystem", "Metric", "Metric / Imperial / Nautical");
            CfgArrivalTimeSec = Cfg.Bind("Waypoint", "ArrivalTimeSec", 2.0f,
                new ConfigDescription("Pass 阈值 = TAS × 秒（最小 50m）", new AcceptableValueRange<float>(0.2f, 10f)));

            CfgPanelX = Cfg.Bind("Panel", "X", -1f, "");
            CfgPanelY = Cfg.Bind("Panel", "Y", -1f, "");
            CfgPanelW = Cfg.Bind("Panel", "W", 380f, "");
            CfgPanelH = Cfg.Bind("Panel", "H", 760f, "");

            CfgHdMode = Cfg.Bind("Target", "HdMode", "WAYPOINT", "FIXED=固定值 / WAYPOINT=航点方向");
            CfgFixedHD = Cfg.Bind("Target", "FixedHD", 0f, new ConfigDescription("固定 heading（度，0=北）", new AcceptableValueRange<float>(0f, 360f)));
            CfgAltMode = Cfg.Bind("Target", "AltMode", "WAYPOINT", "FIXED=固定值 / WAYPOINT=航点高度");
            CfgFixedAlt = Cfg.Bind("Target", "FixedAlt", 1000f, new ConfigDescription("固定高度（米）", new AcceptableValueRange<float>(0f, 20000f)));
            CfgIasMode = Cfg.Bind("Target", "IasMode", "WAYPOINT", "FIXED=固定值 / WAYPOINT=航点速度");
            CfgFixedIAS = Cfg.Bind("Target", "FixedIAS", 100f, new ConfigDescription("固定 IAS（m/s）", new AcceptableValueRange<float>(10f, 400f)));

            CfgThrottleKp = Cfg.Bind("Throttle", "Kp", 0.02f, "");
            CfgThrottleKi = Cfg.Bind("Throttle", "Ki", 0.0005f, "");
            CfgAiTargetLeadKm = Cfg.Bind("Throttle", "AimLeadKm", 10f,
                new ConfigDescription("固定方向时目标距离（km）", new AcceptableValueRange<float>(1f, 50f)));

            CfgApMaxBank = Cfg.Bind("AutoPilot", "MaxBankDeg", 35f,
                new ConfigDescription("坡度上限（度，左右对称）", new AcceptableValueRange<float>(5f, 60f)));

            // 抬头/低头分开两个键，方向写死，不会因为填负数而反掉
            CfgApPitchUpMax = Cfg.Bind("AutoPilot", "PitchUpMaxDeg", 30f,
                new ConfigDescription("抬头角上限（度）", new AcceptableValueRange<float>(0f, 60f)));
            CfgApPitchDownMax = Cfg.Bind("AutoPilot", "PitchDownMaxDeg", 15f,
                new ConfigDescription("低头角上限（度）", new AcceptableValueRange<float>(0f, 60f)));

            // 低机动模式：俯仰限制收紧
            CfgApLowManeuver = Cfg.Bind("AutoPilot", "LowManeuver", false,
                "低机动模式（俯仰限制收紧）");
            CfgApLowPitchUp = Cfg.Bind("AutoPilot", "LowPitchUpDeg", 15f,
                new ConfigDescription("低机动：抬头上限（度）", new AcceptableValueRange<float>(0f, 45f)));
            CfgApLowPitchDown = Cfg.Bind("AutoPilot", "LowPitchDownDeg", 8f,
                new ConfigDescription("低机动：低头上限（度）", new AcceptableValueRange<float>(0f, 45f)));

            // 外环增益：目标值 → 目标姿态
            CfgApHdgToBank = Cfg.Bind("AutoPilot", "HdgToBank", 3.0f,
                new ConfigDescription("航向误差→坡度（度/度）。3.0=偏差 10° 压 30° 坡", new AcceptableValueRange<float>(0.5f, 10f)));
            CfgApAltToPitch = Cfg.Bind("AutoPilot", "AltToPitchGain", 0.35f,
                new ConfigDescription("高度误差→俯仰（度/米）。0.35=每 100m 给 35°", new AcceptableValueRange<float>(0.05f, 1.5f)));

            CfgApVsDamp = Cfg.Bind("AutoPilot", "VsDamp", 1.0f,
                new ConfigDescription("升降速度阻尼（度 per m/s），抑制过冲", new AcceptableValueRange<float>(0f, 5f)));

            CfgApRespGain = Cfg.Bind("AutoPilot", "RespGain", 1.0f,
                new ConfigDescription("误差→角度 总增益（同时缩放航向→坡度和高度→俯仰）", new AcceptableValueRange<float>(0.1f, 4f)));

            CfgApRollKp = Cfg.Bind("AutoPilot", "RollKp", 0.023f,
                new ConfigDescription("滚转比例增益", new AcceptableValueRange<float>(0.001f, 0.2f)));
            CfgApRollKd = Cfg.Bind("AutoPilot", "RollKd", 0.010f,
                new ConfigDescription("滚转阻尼增益", new AcceptableValueRange<float>(0f, 0.2f)));
            CfgApPitchKp = Cfg.Bind("AutoPilot", "PitchKp", 0.040f,
                new ConfigDescription("俯仰比例增益", new AcceptableValueRange<float>(0.001f, 0.3f)));
            CfgApPitchKd = Cfg.Bind("AutoPilot", "PitchKd", 0.010f,
                new ConfigDescription("俯仰阻尼增益", new AcceptableValueRange<float>(0f, 0.3f)));
            CfgApSmooth = Cfg.Bind("AutoPilot", "SmoothTau", 0.12f,
                new ConfigDescription("输出平滑时间常数（秒），越大越柔", new AcceptableValueRange<float>(0.02f, 0.5f)));

            var go = new GameObject("SP2Minimap");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Renderer = go.AddComponent<MinimapRenderer>();
            Gui = go.AddComponent<MinimapGui>();

            // 自动驾驶作为原版 AI 的额外控制模式接入（MinimapAutopilot : AiControlSystem），
            // 不再打 Harmony 补丁，也不自己抢 AircraftControls 的输入覆盖。

            Log.LogInfo("SP2 Minimap v2.9 loaded");
        }

        void Update()
        {
            if (Input.GetKeyDown(CfgKeyToggle.Value))
            {
                if (Minimized) { Minimized = false; MinimapVisible = true; if (Renderer != null) Renderer.SetVisible(true); }
                else { MinimapVisible = !MinimapVisible; if (Renderer != null) Renderer.SetVisible(MinimapVisible); }
            }
            if (Input.GetKeyDown(CfgKeyGui.Value)) GuiVisible = !GuiVisible;
            if (Input.GetKeyDown(CfgKeyFullscreen.Value))
            {
                IsFullscreen = !IsFullscreen; PlacementMode = false;
                if (IsFullscreen && Minimized) Minimized = false;
                if (Renderer != null && !IsFullscreen) Renderer.MapPanOffset = Vector3.zero;
            }
            if (Input.GetKeyDown(CfgKeyAddWaypointAtCenter.Value) && Renderer != null)
                Renderer.AddWaypointAhead(500f);
            // Enter（或配置的 F9）切换自动驾驶；文本框有焦点时忽略
            bool apKey = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(CfgKeyToggleNav.Value);
            if (apKey && GUIUtility.keyboardControl == 0) ToggleAutopilot();
        }

        public static void ToggleAutopilot()
        {
            NavEnabled = !NavEnabled;
            if (NavEnabled) AttachAutopilot();
            else DetachAutopilot();
            Log.LogInfo("[AP] " + (NavEnabled ? "ON" : "OFF"));
        }

        static void AttachAutopilot()
        {
            try
            {
                var mgr = Assets.Scripts.Flight.AI.AiManagerScript.Instance;
                if (mgr == null) { Log.LogError("[AP] AiManagerScript.Instance == null"); NavEnabled = false; return; }

                var ai = mgr.PlayerAiScript;
                if (ai == null)
                {
                    ai = mgr.SetPlayerAsAiControllable(new MinimapAutopilot());
                    if (ai == null)
                    {
                        Log.LogError("[AP] SetPlayerAsAiControllable 返回 null（当前没有本机飞机？）");
                        NavEnabled = false;
                        return;
                    }
                    ai.SetTarget(ai.AiAircraftScript, true);
                }
                else
                {
                    ai.SetAiControlSystem(new MinimapAutopilot());
                }

                ai.InputOverrideEnabled = true;
                ai.AutoDespawn = false;

                // 原版 AI 会把一堆无关通道也接管（起落架、刹车、武器…）。
                // 这里只保留俯仰/横滚/偏航/油门，其余交还给玩家。
                try
                {
                    var c = ai.AiAircraftScript.Controls;
                    var gi = Assets.Scripts.Input.GameInputs.Instance;
                    c.SetInputOverride(gi.Yaw, null);        // 方向舵不接管
                    // 原版对「本机玩家」会主动清掉油门覆盖（573-577 行），
                    // 所以这里必须自己装回来，否则 IAS 完全不受控。
                    c.SetInputOverride(gi.Throttle, () => Mathf.Clamp01(Plugin.CmdThrottle));
                    c.SetInputOverride(gi.LandingGear, null);
                    c.SetInputOverride(gi.Vtol, null);
                    c.SetInputOverride(gi.Brake, null);
                    c.SetInputOverride(gi.FireGuns, null);
                    c.SetInputOverride(gi.FireWeapons, null);
                    c.SetInputOverride(gi.LaunchCountermeasures, null);
                    c.SetInputOverride(gi.NextTarget, null);
                    c.SetInputOverride(gi.PreviousTarget, null);
                    c.SetInputOverride(gi.NextWeapon, null);
                    c.SetInputOverride(gi.PreviousWeapon, null);
                    c.SetInputOverride(gi.CycleTargetingMode, null);
                }
                catch (Exception e2) { Log.LogWarning("[AP] 清理附加通道: " + e2.Message); }

                NavActive = true;
                Log.LogInfo("[AP] 已接管：自定义 ControlSystem 挂在原版 AI 上");
            }
            catch (Exception e)
            {
                Log.LogError("[AP] Attach: " + e);
                NavEnabled = false;
            }
        }

        public static void DetachAutopilot()
        {
            NavActive = false;
            CmdThrottle = -1f;
            CmdPitch = CmdRoll = CmdYaw = 0f;
            try
            {
                var mgr = Assets.Scripts.Flight.AI.AiManagerScript.Instance;
                if (mgr != null && mgr.PlayerAiScript != null)
                    mgr.PlayerAiScript.InputOverrideEnabled = false;
            }
            catch (Exception e) { Log.LogWarning("[AP] Detach: " + e.Message); }
            Log.LogInfo("[AP] 已交还手动控制");
        }
    }

    // ★ 自动驾驶：作为原版 AI 的一个额外控制模式接入。
    //   走 AiManagerScript.SetPlayerAsAiControllable + 自定义 AiControlSystem，
    //   输入覆盖由原版 AI 自己安装/卸载，不再和它抢 Controls。
    public class MinimapAutopilot : Assets.Scripts.Flight.AI.ControlSystems.AiControlSystem
    {
        public override void Initialize(Assets.Scripts.Flight.AI.AiControlledAircraftScript aiControlledAircraft)
        {
            base.Initialize(aiControlledAircraft);
            // 基类有非虚方法（GetVtol 等）会解引用 _currentControlFunction，给个安全默认
            var cf = new Assets.Scripts.Flight.AI.ControlFunctions.AiCfFlyToLocation();
            cf.Initialize(this);
            _currentControlFunction = cf;
        }

        public override void OnUpdate() { }
        public override void OnLateUpdate() { }
        public override void OnFixedUpdate() { }
        public override void OnFirstFrameLateUpdate() { }
        public override void OnDrawGizmos() { }

        public override float GetPitch() { return Mathf.Clamp(Plugin.CmdPitch, -1f, 1f); }
        public override float GetRoll() { return Mathf.Clamp(Plugin.CmdRoll, -1f, 1f); }
        public override float GetYaw() { return 0f; }
        public override float GetThrottle() { return Mathf.Clamp01(Plugin.CmdThrottle); }
        public override float GetBrake() { return 0f; }

        public override bool GetFireGuns() { return false; }
        public override bool GetFireWeapons() { return false; }
        public override bool GetLaunchCountermeasures() { return false; }
        public override bool CycleTargetingMode() { return false; }
        public override bool GetSwitchNextTarget() { return false; }
        public override bool GetSwitchPrevTarget() { return false; }
        public override bool GetSwitchNextWeapon() { return false; }
        public override bool GetSwitchPrevWeapon() { return false; }
        public override bool LandingGearDown() { return true; }
        public override float LeadTarget() { return 1f; }
        public override Vector3 LeadTargetSourceVelocity() { return Vector3.zero; }
        public override Vector3? GetTargetOverridePosition() { return null; }
    }

    public enum UnitKind { Metric, Imperial, Nautical }
    public static class UnitSystem
    {
        public static UnitKind Kind
        {
            get
            {
                var s = (Plugin.CfgUnitSystem.Value ?? "Metric").Trim();
                if (s.Equals("Imperial", StringComparison.OrdinalIgnoreCase)) return UnitKind.Imperial;
                if (s.Equals("Nautical", StringComparison.OrdinalIgnoreCase)) return UnitKind.Nautical;
                return UnitKind.Metric;
            }
        }
        const float FT_PER_M = 3.28084f, M_PER_NM = 1852f, M_PER_MI = 1609.34f;
        const float MPH_PER_MPS = 2.23694f, KTS_PER_MPS = 1.94384f;
        public static float MetersToDisplay(float m) => Kind == UnitKind.Metric ? m : m * FT_PER_M;
        public static float DisplayToMeters(float v) => Kind == UnitKind.Metric ? v : v / FT_PER_M;
        public static string HeightUnitLabel => Kind == UnitKind.Metric ? "m" : "ft";
        public static float MpsToDisplay(float mps)
        {
            switch (Kind) { case UnitKind.Imperial: return mps * MPH_PER_MPS; case UnitKind.Nautical: return mps * KTS_PER_MPS; default: return mps * 3.6f; }
        }
        public static float DisplayToMps(float v)
        {
            switch (Kind) { case UnitKind.Imperial: return v / MPH_PER_MPS; case UnitKind.Nautical: return v / KTS_PER_MPS; default: return v / 3.6f; }
        }
        public static string SpeedUnitLabel
        {
            get { switch (Kind) { case UnitKind.Imperial: return "mph"; case UnitKind.Nautical: return "kts"; default: return "km/h"; } }
        }
        public static string FormatDistance(float m)
        {
            switch (Kind)
            {
                case UnitKind.Imperial:
                    if (m >= M_PER_MI) return (m / M_PER_MI).ToString("F1") + " mi";
                    return (m * FT_PER_M).ToString("F0") + " ft";
                case UnitKind.Nautical:
                    if (m >= M_PER_NM) return (m / M_PER_NM).ToString("F2") + " nm";
                    return (m * FT_PER_M).ToString("F0") + " ft";
                default:
                    if (m >= 1000f) return (m / 1000f).ToString("F1") + " km";
                    return m.ToString("F0") + " m";
            }
        }
        public static string FormatSpeed(float mps) => MpsToDisplay(mps).ToString("F0") + " " + SpeedUnitLabel;
    }

    public static class AircraftDataReflect
    {
        static Component _aircraft;
        static PropertyInfo _pHeading, _pPitchAngle, _pRollAngle;
        static PropertyInfo _pIAS, _pTAS, _pGS, _pAOA, _pAOS;
        static PropertyInfo _pVelocity, _pGForce;
        static PropertyInfo _pPitchRate, _pRollRate, _pYawRate;
        static PropertyInfo _pMainCockpit;
        static bool _init;

        public static void Bind(Component ac)
        {
            if (ac == null || ac == _aircraft) return;
            _aircraft = ac;
            var t = ac.GetType();
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _pHeading = t.GetProperty("Heading", F);
            _pPitchAngle = t.GetProperty("PitchAngle", F);
            _pRollAngle = t.GetProperty("RollAngle", F);
            _pIAS = t.GetProperty("IAS", F);
            _pTAS = t.GetProperty("TAS", F);
            _pGS = t.GetProperty("GS", F);
            _pAOA = t.GetProperty("AngleOfAttack", F);
            _pAOS = t.GetProperty("AngleOfSlip", F);
            _pVelocity = t.GetProperty("Velocity", F);
            _pGForce = t.GetProperty("GForce", F);
            _pPitchRate = t.GetProperty("PitchRate", F);
            _pRollRate = t.GetProperty("RollRate", F);
            _pYawRate = t.GetProperty("YawRate", F);
            _pMainCockpit = t.GetProperty("MainCockpit", F);
            _init = _pHeading != null && _pMainCockpit != null;
        }
        public static bool Ready => _init;
        static float F(PropertyInfo p) { if (p == null || _aircraft == null) return 0f; try { return Convert.ToSingle(p.GetValue(_aircraft)); } catch { return 0f; } }
        static Vector3 V(PropertyInfo p) { if (p == null || _aircraft == null) return Vector3.zero; try { return (Vector3)p.GetValue(_aircraft); } catch { return Vector3.zero; } }
        static Component C(PropertyInfo p) { if (p == null || _aircraft == null) return null; try { return p.GetValue(_aircraft) as Component; } catch { return null; } }

        public static float Heading => F(_pHeading);
        public static float PitchAngle => F(_pPitchAngle);
        public static float RollAngle => F(_pRollAngle);
        public static float IAS => F(_pIAS);
        public static float TAS => F(_pTAS);
        public static float GS => F(_pGS);
        public static float AOA => F(_pAOA);
        public static float AOS => F(_pAOS);
        public static Vector3 Velocity => V(_pVelocity);
        public static float GForce => F(_pGForce);
        public static float PitchRate => F(_pPitchRate);
        public static float RollRate => F(_pRollRate);
        public static float YawRate => F(_pYawRate);
        public static Component MainCockpit => C(_pMainCockpit);
        public static Transform CockpitTransform { get { var c = MainCockpit; return c != null ? c.transform : null; } }
        public static Vector3 CurrentPosition { get { var ct = CockpitTransform; return ct != null ? ct.position : Vector3.zero; } }
        public static float SpeedMps => Velocity.magnitude;
    }

    public class Waypoint
    {
        public int Id;
        public Vector3 GlobalPos, WorldPos;
        public float Altitude, Speed;
        public bool Enabled = true;
        public string Label;
        public float DistanceM, BearingDeg;
    }

    public enum MinimapCategory { Terrain, SpawnPoint, Airport, Target, CustomOBJ }
    public class MinimapEntry
    {
        public Transform Transform;
        public Vector3 WorldPos;
        public MinimapCategory Category;
        public string Label;
    }

    public class MinimapRenderer : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public RenderTexture Tex { get; private set; }
        public Vector3 CurrentCenter { get; private set; }
        public float CurrentHeading { get; private set; }
        public Vector3 AircraftPosition { get; private set; }
        public Vector3 MapPanOffset = Vector3.zero;
        public Component DebugLocalAircraft { get { return GetLocalAircraft(); } }

        public float DebugHeadingErrDeg, DebugAltErrM;
        public float DebugTargetHD, DebugTargetAlt, DebugTargetIAS;
        public float DebugCurrentHD, DebugCurrentAlt, DebugCurrentIAS;
        public float DebugTargetSpeed, DebugCurrentSpeed, DebugSpeedErr;
        public float DebugTAS, DebugArrivalThresholdM;
        public float DebugIAS, DebugAOA, DebugGForce;
        public float DebugSeaLevelLocal, DebugCamY, DebugFarClip;
        public float DebugHorizDist;
        public float DebugDot;
        public float DebugRoll, DebugPitch;
        public string DebugActiveLabel = "";

        // 自动驾驶状态
        public bool DebugApAttached;
        public bool DebugApHasOverrides;
        public bool DebugApInvertRoll;
        public bool DebugApInvertPitch;
        public string DebugApStatus = "?";

        public readonly List<MinimapEntry> SpawnEntries = new List<MinimapEntry>();
        public readonly List<MinimapEntry> AirportEntries = new List<MinimapEntry>();
        public readonly List<MinimapEntry> TargetEntries = new List<MinimapEntry>();
        public readonly List<MinimapEntry> CustomEntries = new List<MinimapEntry>();
        public readonly List<MinimapEntry> AiEntries = new List<MinimapEntry>();
        public readonly List<MinimapEntry> PlayerEntries = new List<MinimapEntry>();
        public readonly List<Waypoint> Waypoints = new List<Waypoint>();

        int _nextWaypointId = 1;
        GameObject _camObj;
        int _lastSize = -1;
        float _rescanTimer = 0f;
        Vector3 _lastFloatingOffset;
        float _throttleIntegral = 0f;
        bool _hasPrev;
        float _prevPitch, _prevBank;
        float _pitchRateLp, _bankRateLp;
        float _rollOut, _pitchOut;

        static Vector3 _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

        static bool _typesResolved;
        static Type _tFlightScene, _tGameWorld, _tAircraft;
        static PropertyInfo _pFlightSceneInstance, _pFlightSceneLocalPlayer;
        static PropertyInfo _pFlightSceneStartLocationMgr, _pFlightSceneTargetRegistry;
        static PropertyInfo _pGameWorldInstance, _pGameWorldFloatingOriginOffset, _pGameWorldSeaLevel;
        static PropertyInfo _pGameWorldFloatingOriginSeaLevel;
        static FieldInfo _fPlayerAircraft;
        static PropertyInfo _pPlayerAircraft;
        static FieldInfo _fAcWorldBodies;

        static PropertyInfo _pRbLinearVelocity, _pRbVelocity;
        public static Vector3 GetRigidbodyVelocity(Rigidbody rb)
        {
            if (rb == null) return Vector3.zero;
            try
            {
                if (_pRbLinearVelocity == null)
                {
                    _pRbLinearVelocity = typeof(Rigidbody).GetProperty("linearVelocity", BindingFlags.Public | BindingFlags.Instance);
                    _pRbVelocity = typeof(Rigidbody).GetProperty("velocity", BindingFlags.Public | BindingFlags.Instance);
                }
                if (_pRbLinearVelocity != null) return (Vector3)_pRbLinearVelocity.GetValue(rb);
                if (_pRbVelocity != null) return (Vector3)_pRbVelocity.GetValue(rb);
            }
            catch { }
            return Vector3.zero;
        }

        void Start()
        {
            _camObj = new GameObject("MinimapCam");
            _camObj.transform.SetParent(transform, false);
            Cam = _camObj.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.orthographicSize = Plugin.CfgRangeM.Value;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Plugin.SeaColorDefault;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 30000f;
            Cam.depth = -100; Cam.allowHDR = false; Cam.allowMSAA = false;
            Cam.cullingMask = ~0; Cam.enabled = false;
            _lastFloatingOffset = GetFloatingOriginOffset();
            ResolveTypes();
            Rescan();
        }

        public void SetVisible(bool visible) { if (_camObj != null) _camObj.SetActive(visible); }

        int DesiredRTSize()
        {
            if (Plugin.IsFullscreen)
                return Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) - (int)(Plugin.CfgFullscreenMargin.Value * 2f), 256, 2048);
            return Mathf.Max(64, (int)Plugin.CfgSizePx.Value);
        }

        void LateUpdate()
        {
            if (!Plugin.MinimapVisible || Cam == null) return;
            AircraftDataReflect.Bind(GetLocalAircraft());

            int desired = DesiredRTSize();
            if (desired != _lastSize) RebuildRT(desired);

            Vector3 offset = GetFloatingOriginOffset();
            if ((offset - _lastFloatingOffset).sqrMagnitude > 0.5f)
            {
                foreach (var wp in Waypoints) wp.WorldPos = wp.GlobalPos - offset;
                _lastFloatingOffset = offset;
            }

            AircraftPosition = GetAircraftPosition();
            if (!Plugin.IsFullscreen) MapPanOffset = Vector3.zero;
            CurrentCenter = AircraftPosition + MapPanOffset;
            CurrentHeading = GetAircraftHeading();

            float seaLevelLocal = GetFloatingOriginSeaLevelOrZero();
            DebugSeaLevelLocal = seaLevelLocal;

            // ★ 相机高度自动 max(Range×2) 保证 far clip 覆盖
            // 海平面剪切已固化为开启
            float camH = Mathf.Max(Plugin.CameraHeightDefault, Plugin.CfgRangeM.Value * 2f);
            float camY = seaLevelLocal + camH;
            float farClip = camH + 0.5f;
            DebugCamY = camY; DebugFarClip = farClip;
            Cam.backgroundColor = Plugin.SeaColorDefault;
            Cam.transform.position = new Vector3(CurrentCenter.x, camY, CurrentCenter.z);
            Cam.farClipPlane = farClip;

            Vector3 up;
            if (Plugin.CfgRotateWithHeading.Value)
            {
                float rad = CurrentHeading * Mathf.Deg2Rad;
                up = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                if (up.sqrMagnitude < 1e-6f) up = Vector3.forward;
            }
            else up = Vector3.forward;
            Cam.transform.rotation = Quaternion.LookRotation(Vector3.down, up);
            Cam.orthographicSize = Plugin.CfgRangeM.Value;

            int mask = 0;
            if (Plugin.CfgShowTerrain.Value)
            {
                if (Plugin.CfgUseAllLayers.Value)
                {
                    mask = ~0;
                    var ac = GetLocalAircraft();
                    if (ac != null)
                        foreach (var t in ac.GetComponentsInChildren<Transform>())
                            mask &= ~(1 << t.gameObject.layer);
                    foreach (var l in ParseLayers(Plugin.CfgExcludeLayers.Value))
                        if (l >= 0 && l < 32) mask &= ~(1 << l);
                }
                else mask = Plugin.CfgTerrainLayerMask.Value;
            }
            Cam.cullingMask = mask;

            _rescanTimer -= Time.unscaledDeltaTime;
            if (_rescanTimer <= 0f) { _rescanTimer = 2f; Rescan(); }

            UpdateWaypointDistances();

            Plugin.LocalAircraft = GetLocalAircraft();
            Plugin.NavActive = Plugin.NavEnabled;
        }

        // AP 控制环跑在固定步长上，与渲染帧率解耦
        void FixedUpdate()
        {
            if (Plugin.NavEnabled) ComputeControl();
        }

        void ComputeControl()
        {
            var mgr = Assets.Scripts.Flight.AI.AiManagerScript.Instance;
            var ai = mgr != null ? mgr.PlayerAiScript : null;

            DebugApAttached = ai != null;
            DebugApHasOverrides = ai != null && ai.AiAircraftScript != null && ai.AiAircraftScript.HasInputOverrides;

            if (ai == null) { DebugApStatus = "未接入 AI（按 Enter 开启）"; return; }
            if (!ai.InputOverrideEnabled) { DebugApStatus = "AI 输入覆盖已关闭"; return; }

            var body = ai.AiRigidbody;
            if (body == null) { DebugApStatus = "机体为空"; return; }
            Transform bt = body.transform;

            // 固定步长：与渲染帧率解耦（FixedUpdate 调用）
            float dt = Mathf.Clamp(Time.fixedDeltaTime, 0.005f, 0.1f);

            // ---------- 姿态角（机体坐标，不用驾驶舱部件） ----------
            float pitchAng = Mathf.Asin(Mathf.Clamp(bt.forward.y, -1f, 1f)) * Mathf.Rad2Deg;   // 抬头为正
            float bankRight = Mathf.Asin(Mathf.Clamp(-bt.right.y, -1f, 1f)) * Mathf.Rad2Deg;   // 右倾为正
            float curHd = Mathf.Atan2(bt.forward.x, bt.forward.z) * Mathf.Rad2Deg;

            float rawPitchRate = _hasPrev ? Mathf.DeltaAngle(_prevPitch, pitchAng) / dt : 0f;
            float rawBankRate = _hasPrev ? Mathf.DeltaAngle(_prevBank, bankRight) / dt : 0f;
            _prevPitch = pitchAng;
            _prevBank = bankRight;
            _hasPrev = true;

            // 角速度低通：抑制固定步长采样带来的微分噪声，低帧率下尤其重要
            float rateA = 1f - Mathf.Exp(-dt / 0.12f);
            _pitchRateLp += (rawPitchRate - _pitchRateLp) * rateA;
            _bankRateLp += (rawBankRate - _bankRateLp) * rateA;

            float pitchRate = _pitchRateLp;
            float bankRate = _bankRateLp;

            // ---------- 目标 ----------
            var wp = GetActiveWaypoint();
            bool noWp = wp == null;
            DebugActiveLabel = wp != null ? wp.Label : "(none)";

            string hdMode = Plugin.CfgHdMode.Value;
            string altMode = Plugin.CfgAltMode.Value;
            string iasMode = Plugin.CfgIasMode.Value;
            if (hdMode == "WAYPOINT" && noWp) hdMode = "FIXED";
            if (altMode == "WAYPOINT" && noWp) altMode = "FIXED";
            if (iasMode == "WAYPOINT" && noWp) iasMode = "FIXED";

            Vector3 pos = bt.position;
            float seaLevelLocal = GetFloatingOriginSeaLevelOrZero();

            float targetAltM = altMode == "FIXED" ? Plugin.CfgFixedAlt.Value : wp.Altitude;
            DebugTargetAlt = targetAltM;

            float targetHd;
            if (hdMode == "FIXED")
            {
                targetHd = Plugin.CfgFixedHD.Value;
            }
            else
            {
                Vector3 toWp = new Vector3(wp.WorldPos.x, 0f, wp.WorldPos.z) - new Vector3(pos.x, 0f, pos.z);
                targetHd = toWp.sqrMagnitude > 1f
                    ? Mathf.Atan2(toWp.x, toWp.z) * Mathf.Rad2Deg
                    : wp.BearingDeg;
            }
            if (targetHd < 0f) targetHd += 360f;
            DebugTargetHD = targetHd;

            float targetSpeedMps = iasMode == "FIXED" ? Plugin.CfgFixedIAS.Value : wp.Speed;
            float curIAS = AircraftDataReflect.IAS;
            float curAltM = pos.y - seaLevelLocal;

            // ---------- 外环：航向 → 目标坡度（右为正） ----------
            float respGain = Mathf.Max(0.05f, Plugin.CfgApRespGain.Value);
            float hdErr = Mathf.DeltaAngle(curHd, targetHd);
            float maxBank = Mathf.Abs(Plugin.CfgApMaxBank.Value);
            float desiredBankRight = Mathf.Clamp(hdErr * Plugin.CfgApHdgToBank.Value * respGain, -maxBank, maxBank);

            // ---------- 外环：高度 → 目标俯仰（抬头为正） ----------
            float altErr = targetAltM - curAltM;
            float desiredPitch = altErr * Plugin.CfgApAltToPitch.Value * respGain
                               - body.linearVelocity.y * Plugin.CfgApVsDamp.Value;

            // 方向写死：低头侧永远取负，抬头侧永远取正
            float downLimit = -Mathf.Abs(Plugin.CfgApPitchDownMax.Value);
            float upLimit = Mathf.Abs(Plugin.CfgApPitchUpMax.Value);
            if (Plugin.CfgApLowManeuver.Value)
            {
                downLimit = -Mathf.Abs(Plugin.CfgApLowPitchDown.Value);
                upLimit = Mathf.Abs(Plugin.CfgApLowPitchUp.Value);
            }
            desiredPitch = Mathf.Clamp(desiredPitch, downLimit, upLimit);

            // ---------- 内环 ----------
            // 滚转：正输入 = 右滚
            float bankErr = desiredBankRight - bankRight;
            float rollTarget = bankErr * Plugin.CfgApRollKp.Value - bankRate * Plugin.CfgApRollKd.Value;

            // 俯仰：正输入 = 低头；要抬头就得输出负值
            float pitchErr = desiredPitch - pitchAng;
            float pitchTarget = -(pitchErr * Plugin.CfgApPitchKp.Value - pitchRate * Plugin.CfgApPitchKd.Value);

            // ---------- 输出平滑（一阶低通） ----------
            float tau = Mathf.Max(0.02f, Plugin.CfgApSmooth.Value);
            float a = 1f - Mathf.Exp(-dt / tau);
            _rollOut += (Mathf.Clamp(rollTarget, -1f, 1f) - _rollOut) * a;
            _pitchOut += (Mathf.Clamp(pitchTarget, -1f, 1f) - _pitchOut) * a;

            Plugin.CmdRoll = Mathf.Clamp(_rollOut, -1f, 1f);
            Plugin.CmdPitch = Mathf.Clamp(_pitchOut, -1f, 1f);

            // ---------- 油门 ----------
            float speedErr = targetSpeedMps - curIAS;
            _throttleIntegral += speedErr * dt;
            _throttleIntegral = Mathf.Clamp(_throttleIntegral, -300f, 300f);
            float ff = Mathf.Clamp01(targetSpeedMps / 150f);
            Plugin.CmdThrottle = Mathf.Clamp01(ff + speedErr * Plugin.CfgThrottleKp.Value + _throttleIntegral * Plugin.CfgThrottleKi.Value);

            // ---------- 调试 ----------
            DebugApStatus = (noWp ? "无航点→固定 " : "AP ")
                          + "坡" + desiredBankRight.ToString("F0")
                          + " 俯" + desiredPitch.ToString("F0");
            DebugTargetSpeed = targetSpeedMps;
            DebugCurrentSpeed = curIAS;
            DebugSpeedErr = speedErr;
            DebugTargetIAS = targetSpeedMps;
            DebugCurrentIAS = curIAS;
            DebugAltErrM = altErr;
            DebugCurrentHD = curHd;
            DebugCurrentAlt = curAltM;
            DebugHeadingErrDeg = hdErr;
            DebugIAS = curIAS;
            DebugAOA = AircraftDataReflect.AOA;
            DebugGForce = AircraftDataReflect.GForce;
            DebugTAS = AircraftDataReflect.TAS;
            DebugRoll = bankRight;
            DebugPitch = pitchAng;

            if (wp != null) CheckPass(wp, pos, targetSpeedMps);
        }

        void CheckPass(Waypoint wp, Vector3 pos, float tasMps)
        {
            Vector3 toWp = wp.WorldPos - pos;
            float horizSq = toWp.x * toWp.x + toWp.z * toWp.z;
            float horizDist = Mathf.Sqrt(horizSq);
            DebugHorizDist = horizDist;

            float threshold = Mathf.Max(50f, tasMps * Plugin.CfgArrivalTimeSec.Value);
            DebugArrivalThresholdM = threshold;

            Vector3 fwd = AircraftDataReflect.CockpitTransform != null
                ? AircraftDataReflect.CockpitTransform.forward : Vector3.forward;
            Vector3 fwdH = new Vector3(fwd.x, 0f, fwd.z);
            Vector3 toWpH = new Vector3(toWp.x, 0f, toWp.z);
            float dot = -99f;
            if (fwdH.sqrMagnitude > 1e-4f && toWpH.sqrMagnitude > 1e-4f)
                dot = Vector3.Dot(fwdH.normalized, toWpH.normalized);
            DebugDot = dot;

            bool distPass = horizDist < threshold;
            bool flewPast = horizDist < threshold * 3f && dot < -0.3f;

            if (distPass || flewPast)
            {
                Plugin.Log.LogInfo(string.Format(
                    "[Nav] PASSED #{0} {1} horiz={2:F0}m thr={3:F0}m dot={4:F2} reason={5}",
                    wp.Id, wp.Label, horizDist, threshold, dot,
                    distPass ? "dist" : "past"));
                wp.Enabled = false;
                _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            }
        }

        void RebuildRT(int size)
        {
            _lastSize = size;
            if (Tex != null) { Cam.targetTexture = null; Tex.Release(); Destroy(Tex); Tex = null; }
            Tex = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            Tex.name = "MinimapRT"; Tex.Create();
            Cam.targetTexture = Tex; Cam.enabled = true;
        }

        static int[] ParseLayers(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new int[0];
            var parts = s.Split(',');
            var res = new List<int>();
            foreach (var p in parts) { int v; if (int.TryParse(p.Trim(), out v)) res.Add(v); }
            return res.ToArray();
        }

        void ResolveTypes()
        {
            if (_typesResolved) return;
            _tFlightScene = AccessTools.TypeByName("Assets.Scripts.Flight.FlightSceneScript");
            _tGameWorld = AccessTools.TypeByName("Assets.Scripts.GameWorld");
            _tAircraft = AccessTools.TypeByName("Assets.Scripts.Craft.AircraftScript");
            if (_tFlightScene != null)
            {
                _pFlightSceneInstance = _tFlightScene.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _pFlightSceneLocalPlayer = _tFlightScene.GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pFlightSceneStartLocationMgr = _tFlightScene.GetProperty("StartLocationManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pFlightSceneTargetRegistry = _tFlightScene.GetProperty("TargetRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_pFlightSceneLocalPlayer != null)
            {
                var lpType = _pFlightSceneLocalPlayer.PropertyType;
                _fPlayerAircraft = lpType.GetField("Aircraft", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pPlayerAircraft = lpType.GetProperty("Aircraft", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_tGameWorld != null)
            {
                _pGameWorldInstance = _tGameWorld.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _pGameWorldFloatingOriginOffset = _tGameWorld.GetProperty("FloatingOriginOffset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pGameWorldSeaLevel = _tGameWorld.GetProperty("SeaLevel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _pGameWorldFloatingOriginSeaLevel = _tGameWorld.GetProperty("FloatingOriginSeaLevel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_tAircraft != null)
                _fAcWorldBodies = _tAircraft.GetField("WorldRigidBodies", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _typesResolved = true;
        }

        object GetFlightSceneInstance() { try { ResolveTypes(); return _pFlightSceneInstance?.GetValue(null); } catch { return null; } }
        object GetLocalPlayer()
        {
            try { var fs = GetFlightSceneInstance(); if (fs == null) return null; return _pFlightSceneLocalPlayer?.GetValue(fs); }
            catch { return null; }
        }
        public Component GetLocalAircraft()
        {
            var lp = GetLocalPlayer();
            if (lp == null) return null;
            try
            {
                if (_fPlayerAircraft != null) return _fPlayerAircraft.GetValue(lp) as Component;
                if (_pPlayerAircraft != null) return _pPlayerAircraft.GetValue(lp) as Component;
            }
            catch { }
            return null;
        }
        Transform GetWorldBodies()
        {
            var ac = GetLocalAircraft();
            if (ac == null) return null;
            if (_fAcWorldBodies != null)
            {
                var t = _fAcWorldBodies.GetValue(ac) as Transform;
                if (t != null && t.gameObject.activeInHierarchy) return t;
            }
            return null;
        }
        public Vector3 GetAircraftPosition()
        {
            var p = AircraftDataReflect.CurrentPosition;
            if (p.sqrMagnitude > 0.01f) return p;
            var t = GetWorldBodies();
            if (t != null) return t.position;
            return Vector3.zero;
        }
        public float GetAircraftHeading()
        {
            if (AircraftDataReflect.Ready)
            {
                float h = AircraftDataReflect.Heading;
                if (h != 0f || AircraftDataReflect.PitchAngle != 0f) return h;
            }
            var t = GetWorldBodies();
            if (t != null)
            {
                Vector3 f = t.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-6f) return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            return 0f;
        }
        public Vector3 GetFloatingOriginOffset()
        {
            try
            {
                var inst = _pGameWorldInstance?.GetValue(null);
                if (inst != null)
                {
                    object o = _pGameWorldFloatingOriginOffset?.GetValue(inst);
                    if (o is Vector3 v) return v;
                }
            }
            catch { }
            return Vector3.zero;
        }
        public float GetFloatingOriginSeaLevelOrZero()
        {
            try
            {
                var inst = _pGameWorldInstance?.GetValue(null);
                if (inst == null) return 0f;
                if (_pGameWorldFloatingOriginSeaLevel != null)
                {
                    object o = _pGameWorldFloatingOriginSeaLevel.GetValue(inst);
                    var nf = o as float?;
                    if (nf.HasValue) return nf.Value;
                }
                var off = GetFloatingOriginOffset();
                if (_pGameWorldSeaLevel != null)
                {
                    object so = _pGameWorldSeaLevel.GetValue(inst);
                    var snf = so as float?;
                    if (snf.HasValue) return snf.Value - off.y;
                }
                return -off.y;
            }
            catch { return 0f; }
        }

        public void AddWaypoint(Vector3 scenePos, float altitudeM, float speedMps)
        {
            Vector3 offset = GetFloatingOriginOffset();
            var wp = new Waypoint
            {
                Id = _nextWaypointId++,
                GlobalPos = scenePos + offset,
                WorldPos = scenePos,
                Altitude = altitudeM,
                Speed = speedMps,
                Enabled = true,
                Label = "WP" + _nextWaypointId,
            };
            wp.GlobalPos.y = altitudeM + offset.y;
            Waypoints.Add(wp);
            _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        }
        public void AddWaypointAhead(float distance)
        {
            Vector3 pos = CurrentCenter + Quaternion.Euler(0, CurrentHeading, 0) * Vector3.forward * distance;
            float alt = CurrentCenter.y - GetFloatingOriginSeaLevelOrZero();
            float spd = 100f;
            var ac = GetLocalAircraft();
            if (ac != null) { var rb = ac.GetComponentInChildren<Rigidbody>(); if (rb != null) spd = Mathf.Max(50f, GetRigidbodyVelocity(rb).magnitude); }
            AddWaypoint(new Vector3(pos.x, alt, pos.z), alt, spd);
        }
        public void RemoveWaypoint(Waypoint w) { Waypoints.Remove(w); _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); }
        public void ClearWaypoints() { Waypoints.Clear(); _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); }
        public void MoveWaypointTo(Waypoint wp, Vector3 newWorldPosXZ, float keepAltitude)
        {
            if (wp == null) return;
            Vector3 offset = GetFloatingOriginOffset();
            wp.Altitude = keepAltitude;
            wp.WorldPos = new Vector3(newWorldPosXZ.x, keepAltitude, newWorldPosXZ.z);
            wp.GlobalPos = new Vector3(newWorldPosXZ.x + offset.x, keepAltitude + offset.y, newWorldPosXZ.z + offset.z);
            _lastSetTarget = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        }
        public Waypoint GetActiveWaypoint() { foreach (var w in Waypoints) if (w.Enabled) return w; return null; }
        void UpdateWaypointDistances()
        {
            foreach (var w in Waypoints)
            {
                var d = w.WorldPos - CurrentCenter;
                w.DistanceM = d.magnitude;
                w.BearingDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
        }

        public void Rescan()
        {
            SpawnEntries.Clear(); AirportEntries.Clear(); TargetEntries.Clear(); CustomEntries.Clear();
            AiEntries.Clear(); PlayerEntries.Clear();
            try { ScanSpawnPoints(); ScanAirports(); ScanCustomOBJs(); ScanTargets(); ScanAircraft(); }
            catch (Exception e) { Plugin.Log.LogWarning("Rescan: " + e.Message); }
        }

        // 扫描场景里除本机以外的飞机：AI 和其他玩家
        void ScanAircraft()
        {
            try
            {
                var scene = Assets.Scripts.Flight.FlightSceneScript.Instance;
                if (scene == null || scene.AircraftContainer == null) return;

                Component local = GetLocalAircraft();

                foreach (Transform child in scene.AircraftContainer)
                {
                    if (child == null) continue;
                    var ac = child.GetComponentInChildren<Assets.Scripts.Craft.AircraftScript>(true);
                    if (ac == null || ac == local) continue;

                    var cockpit = ac.MainCockpit;
                    if (cockpit == null) continue;

                    bool isAi = ac.GetComponent<Assets.Scripts.Flight.AI.AiControlledAircraftScript>() != null;
                    string name = ac.Aircraft != null ? ac.Aircraft.Name : "?";

                    var entry = new MinimapEntry
                    {
                        Transform = cockpit.transform,
                        WorldPos = cockpit.transform.position,
                        Category = MinimapCategory.Target,
                        Label = (isAi ? "AI " : "PLR ") + name
                    };
                    if (isAi) AiEntries.Add(entry); else PlayerEntries.Add(entry);
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("ScanAircraft: " + e.Message); }
        }

        void ScanSpawnPoints()
        {
            try
            {
                var fs = GetFlightSceneInstance();
                if (fs == null) return;
                var slm = _pFlightSceneStartLocationMgr?.GetValue(fs);
                if (slm == null) return;
                object locations = null;
                var sT = slm.GetType();
                var prop = sT.GetProperty("Locations");
                if (prop != null) locations = prop.GetValue(slm);
                else
                {
                    var fld = sT.GetField("Locations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? sT.GetField("_locations", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fld != null) locations = fld.GetValue(slm);
                }
                if (!(locations is IEnumerable en)) return;
                Vector3 originOffset = GetFloatingOriginOffset();
                foreach (var loc in en)
                {
                    if (loc == null) continue;
                    var lt = loc.GetType();
                    object posObj = lt.GetField("Position")?.GetValue(loc) ?? lt.GetProperty("Position")?.GetValue(loc);
                    if (!(posObj is Vector3 v)) continue;
                    object idObj = lt.GetProperty("Id")?.GetValue(loc) ?? lt.GetField("Id")?.GetValue(loc);
                    SpawnEntries.Add(new MinimapEntry { WorldPos = v - originOffset, Category = MinimapCategory.SpawnPoint, Label = idObj as string ?? "Spawn" });
                }
            }
            catch { }
        }
        void ScanAirports()
        {
            var kws = SplitLower(Plugin.CfgAirportKeywords.Value);
            if (kws.Length == 0) return;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                var n = r.gameObject.name.ToLowerInvariant();
                bool match = false;
                for (int i = 0; i < kws.Length; i++) if (n.Contains(kws[i])) { match = true; break; }
                if (!match) continue;
                AirportEntries.Add(new MinimapEntry { Transform = r.transform, WorldPos = r.bounds.center, Category = MinimapCategory.Airport, Label = r.gameObject.name });
            }
        }
        void ScanCustomOBJs()
        {
            var kws = SplitLower(Plugin.CfgCustomOBJPrefix.Value);
            if (kws.Length == 0) return;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var n = t.gameObject.name.ToLowerInvariant();
                bool match = false;
                for (int i = 0; i < kws.Length; i++) if (n.Contains(kws[i])) { match = true; break; }
                if (!match) continue;
                Vector3 center = t.position;
                var mf = t.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    center = mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
                CustomEntries.Add(new MinimapEntry { Transform = t, WorldPos = center, Category = MinimapCategory.CustomOBJ, Label = t.gameObject.name });
            }
        }
        void ScanTargets()
        {
            try
            {
                var fs = GetFlightSceneInstance();
                if (fs != null)
                {
                    var reg = _pFlightSceneTargetRegistry?.GetValue(fs);
                    if (reg != null)
                    {
                        var rt = reg.GetType();
                        object raw = null;
                        foreach (var nm in new[] { "All", "Targets", "RegisteredTargets", "Entries" })
                        {
                            var pp = rt.GetProperty(nm, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (pp != null) { raw = pp.GetValue(reg); break; }
                        }
                        if (raw == null)
                        {
                            var ff = rt.GetField("_targets", BindingFlags.NonPublic | BindingFlags.Instance)
                                   ?? rt.GetField("_allTargets", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (ff != null) raw = ff.GetValue(reg);
                        }
                        if (raw is IEnumerable en)
                        {
                            foreach (var item in en)
                            {
                                if (item == null) continue;
                                Component c = item as Component;
                                if (c == null)
                                {
                                    var tp = item.GetType().GetProperty("Transform") ?? item.GetType().GetProperty("Target");
                                    if (tp != null) c = tp.GetValue(item) as Component;
                                }
                                if (c != null)
                                    TargetEntries.Add(new MinimapEntry { Transform = c.transform, WorldPos = c.transform.position, Category = MinimapCategory.Target, Label = c.gameObject.name });
                            }
                        }
                    }
                }
            }
            catch { }
            if (TargetEntries.Count == 0 && _tAircraft != null)
            {
                try
                {
                    var local = GetLocalAircraft();
                    foreach (var a in FindObjectsByType(_tAircraft, FindObjectsSortMode.None))
                    {
                        if (a == null) continue;
                        var c = a as Component;
                        if (c == null || c == local) continue;
                        TargetEntries.Add(new MinimapEntry { Transform = c.transform, WorldPos = c.transform.position, Category = MinimapCategory.Target, Label = c.gameObject.name });
                    }
                }
                catch { }
            }
        }
        static string[] SplitLower(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new string[0];
            var parts = s.Split(',');
            var res = new List<string>();
            foreach (var p in parts) { var t = p.Trim().ToLowerInvariant(); if (t.Length > 0) res.Add(t); }
            return res.ToArray();
        }
    }

    // ==================== GUI ====================
    public class MinimapGui : MonoBehaviour
    {
        Texture2D _dotTex, _triTex, _squareTex, _wpTex;
        Rect _rect, _panelRect;
        Vector2 _scroll;
        GUIStyle _labelStyle, _wpStyle, _wpNumStyle;
        bool _isDragging, _isDraggingPanel, _isResizingPanel;
        Vector2 _dragStart, _panelDragStart, _panelResizeStart;
        float _dragStartOffsetX, _dragStartOffsetY, _panelStartX, _panelStartY, _panelStartW, _panelStartH;
        Vector3 _dragStartPanOffset;
        Waypoint _draggingWaypoint = null;
        Vector2 _lastWpMousePos;
        bool _lastFullscreen;
        int _editingWaypointId = -1;

        string _tfHD, _tfAlt, _tfIAS;
        bool _tfHDE, _tfAltE, _tfIASE;

        // 数字输入框：回车或失焦时应用；聚焦期间不被外部值覆盖
        void NumberField(string id, ref string buf, ref bool editing,
                         float value, float min, float max, string fmt, float width, Action<float> apply)
        {
            bool focused = GUI.GetNameOfFocusedControl() == id;
            if (!focused && !editing) buf = value.ToString(fmt);

            GUI.SetNextControlName(id);
            string txt = GUILayout.TextField(buf, GUILayout.Width(width));

            if (GUI.GetNameOfFocusedControl() == id)
            {
                buf = txt;
                editing = true;
                if (Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    float v;
                    if (float.TryParse(buf, out v)) apply(Mathf.Clamp(v, min, max));
                    editing = false;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            else if (editing)
            {
                float v;
                if (float.TryParse(buf, out v)) apply(Mathf.Clamp(v, min, max));
                editing = false;
            }
        }

        void Start()
        {
            _dotTex = MakeCircleTex(Color.white, 16);
            _triTex = MakeTriangleTex(Color.white, 16);
            _squareTex = MakeSquareTex(Color.white, 16);
            _wpTex = MakeDiamondTex(Color.white, 16);
            _lastFullscreen = Plugin.IsFullscreen;
        }
        void Update()
        {
            if (_lastFullscreen != Plugin.IsFullscreen) { _lastFullscreen = Plugin.IsFullscreen; _isDragging = false; _draggingWaypoint = null; }
        }
        static Texture2D MakeCircleTex(Color color, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                { float dx = x - r + 0.5f, dy = y - r + 0.5f; tex.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) <= r ? color : Color.clear); }
            tex.Apply(); return tex;
        }
        static Texture2D MakeTriangleTex(Color color, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float cx = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1);
                float w = (1f - t) * size * 0.5f + 1f;
                for (int x = 0; x < size; x++) tex.SetPixel(x, y, Mathf.Abs(x - cx) <= w ? color : Color.clear);
            }
            tex.Apply(); return tex;
        }
        static Texture2D MakeSquareTex(Color color, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, (x < 2 || x >= size - 2 || y < 2 || y >= size - 2) ? color : Color.clear);
            tex.Apply(); return tex;
        }
        static Texture2D MakeDiamondTex(Color color, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float cx = size * 0.5f, cy = size * 0.5f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Mathf.Abs(x - cx) + Mathf.Abs(y - cy) <= cx * 0.9f ? color : Color.clear);
            tex.Apply(); return tex;
        }

        void OnGUI()
        {
            InitMcp();
            if (Plugin.GuiVisible) DrawPanel();
            if (!Plugin.MinimapVisible) return;
            if (Plugin.Minimized) { DrawMinimizedButton(); return; }
            if (Plugin.IsFullscreen) DrawFullscreen(); else DrawMini();
        }

        void DrawMinimizedButton()
        {
            float sz = 44f;
            var btnRect = new Rect(Plugin.CfgOffsetX.Value, Plugin.CfgOffsetY.Value, sz, sz);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(btnRect.x - 2, btnRect.y - 2, sz + 4, sz + 4), _dotTex, ScaleMode.StretchToFill);
            GUI.color = new Color(0.25f, 0.55f, 0.9f, 0.95f);
            GUI.DrawTexture(btnRect, _dotTex, ScaleMode.StretchToFill);
            GUI.color = new Color(0.4f, 0.75f, 1f, 1f);
            GUI.DrawTexture(new Rect(btnRect.x + 3, btnRect.y + 3, sz - 6, sz - 6), _dotTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.Label(btnRect, "地图", st);
            GUI.color = old;
            if (GUI.Button(btnRect, GUIContent.none, GUIStyle.none)) Plugin.Minimized = false;
        }

        void DrawMini()
        {
            var r = Plugin.Renderer;
            if (r == null) return;
            float size = Plugin.CfgSizePx.Value;
            _rect = new Rect(Plugin.CfgOffsetX.Value, Plugin.CfgOffsetY.Value, size, size);
            HandleMiniInput();
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(_rect, Texture2D.whiteTexture);
            GUI.color = old;
            if (r.Tex != null) GUI.DrawTexture(_rect, r.Tex, ScaleMode.StretchToFill, false);
            DrawBorder(_rect, Plugin.PlacementMode ? new Color(1f, 0.75f, 0.2f, 1f) : new Color(0.3f, 0.7f, 1f, 0.8f), Plugin.PlacementMode ? 2f : 1f);
            DrawAllIcons(r);
            DrawAircraftIcon(r);
            DrawCloseButton(_rect);
            if (Plugin.PlacementMode)
            {
                Rect hint = new Rect(_rect.x, _rect.yMax - 18f, _rect.width, 18f);
                FillRect(hint, new Color(0.85f, 0.55f, 0.05f, 0.85f));
                GUIStyle st = new GUIStyle(_mcpStatus);
                st.alignment = TextAnchor.MiddleCenter;
                st.normal.textColor = Color.black;
                GUI.Label(hint, "选点模式：点击地图添加航点", st);
            }
        }

        void DrawFullscreen()
        {
            var r = Plugin.Renderer;
            if (r == null) return;
            var oldC = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.92f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = oldC;
            float margin = Plugin.CfgFullscreenMargin.Value;
            float size = Mathf.Min(Screen.width, Screen.height) - 2f * margin;
            float left = Mathf.Max(margin, (Screen.width - size - 380f) * 0.5f);
            _rect = new Rect(left, (Screen.height - size) * 0.5f, size, size);
            HandleFullscreenInput();
            if (r.Tex != null) GUI.DrawTexture(_rect, r.Tex, ScaleMode.StretchToFill, false);
            DrawBorder(_rect, Plugin.PlacementMode ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(0.3f, 0.7f, 1f, 0.9f), 2f);
            DrawAllIcons(r);
            DrawAircraftIcon(r);
            if (Plugin.PlacementMode)
            {
                var banner = new Rect(_rect.x, _rect.yMax - 40f, _rect.width, 40f);
                GUI.color = new Color(1f, 0.6f, 0.1f, 0.85f);
                GUI.DrawTexture(banner, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var st = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.black;
                GUI.Label(banner, "【选点模式】点击地图加航路点 · 拖动航路点可移动", st);
            }
            DrawFullscreenToolbar();
            DrawFullscreenEditPanel();
        }

        void DrawAircraftIcon(MinimapRenderer r)
        {
            if (_triTex == null || !Plugin.CfgShowAircraftMarker.Value) return;
            Vector2 sp = WorldToMap(r.AircraftPosition, r.CurrentCenter, Plugin.CfgRangeM.Value, r.CurrentHeading);
            float cx = _rect.x + _rect.width * 0.5f + sp.x;
            float cy = _rect.y + _rect.height * 0.5f + sp.y;
            bool inView = cx >= _rect.x && cx <= _rect.xMax && cy >= _rect.y && cy <= _rect.yMax;
            if (inView)
            {
                float sz = 18f * Plugin.CfgIconScale.Value;
                float angle = Plugin.CfgRotateWithHeading.Value ? 0f : r.GetAircraftHeading();
                var old = GUI.color;
                GUI.color = Color.white;
                var m = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(new Vector3(cx, cy, 0f), Quaternion.Euler(0f, 0f, angle), Vector3.one);
                GUI.DrawTexture(new Rect(-sz * 0.5f, -sz * 0.5f, sz, sz), _triTex);
                GUI.matrix = m;
                GUI.color = old;
            }
            else
            {
                Vector2 dir = new Vector2(cx - (_rect.x + _rect.width * 0.5f), cy - (_rect.y + _rect.height * 0.5f));
                Vector2 half = new Vector2(_rect.width * 0.5f - 10f, _rect.height * 0.5f - 10f);
                float s = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-6f), half.y / Mathf.Max(Mathf.Abs(dir.y), 1e-6f));
                Vector2 edge = new Vector2(_rect.x + _rect.width * 0.5f + dir.x * s, _rect.y + _rect.height * 0.5f + dir.y * s);
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                float sz = 16f * Plugin.CfgIconScale.Value;
                var old = GUI.color;
                GUI.color = new Color(1f, 0.9f, 0.2f, 0.9f);
                var m = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(edge, Quaternion.Euler(0f, 0f, ang), Vector3.one);
                GUI.DrawTexture(new Rect(-sz * 0.5f, -sz * 0.5f, sz, sz), _triTex);
                GUI.matrix = m;
                GUI.color = old;
            }
        }

        void DrawFullscreenToolbar()
        {
            var r = Plugin.Renderer;
            InitMcp();

            var bar = new Rect(0, 0, Screen.width, 42);
            FillRect(bar, new Color(0.07f, 0.075f, 0.085f, 0.95f));
            FillRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), ColBezel);

            GUILayout.BeginArea(new Rect(10, 7, Screen.width - 20, 28));
            GUILayout.BeginHorizontal();

            if (BtnW(Plugin.PlacementMode ? "退出选点" : "选点模式", 100f, Plugin.PlacementMode))
                Plugin.PlacementMode = !Plugin.PlacementMode;

            if (BtnW(Plugin.NavEnabled ? "AP ON" : "AP OFF", 90f, Plugin.NavEnabled))
            {
                Plugin.ToggleAutopilot();
                if (!Plugin.NavEnabled) { Plugin.NavActive = false; Plugin.CmdThrottle = -1f; }
            }

            if (BtnW("回飞机", 70f)) { if (r != null) r.MapPanOffset = Vector3.zero; }
            if (BtnW("清空", 60f)) { if (r != null) r.ClearWaypoints(); }

            GUILayout.Space(10);
            if (r != null)
            {
                GUILayout.Label("R " + UnitSystem.FormatDistance(Plugin.CfgRangeM.Value), _mcpStatus, GUILayout.Width(130));
                GUILayout.Label("HDG " + r.CurrentHeading.ToString("F0") + "°", _mcpStatus, GUILayout.Width(80));
                GUILayout.Label("WP " + r.Waypoints.Count, _mcpStatus, GUILayout.Width(60));
            }

            GUILayout.FlexibleSpace();
            if (BtnW("最小化", 70f)) { Plugin.Minimized = true; Plugin.IsFullscreen = false; }
            if (BtnW("退出", 60f)) Plugin.IsFullscreen = false;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawFullscreenEditPanel()
        {
            var r = Plugin.Renderer;
            if (r == null) return;
            InitMcp();

            var panelRect = new Rect(Screen.width - 380, 50, 370, Screen.height - 60);
            FillRect(panelRect, new Color(0.10f, 0.11f, 0.125f, 0.97f));
            FrameRect(panelRect, ColBezel, 1f);

            Rect tb = new Rect(panelRect.x, panelRect.y, panelRect.width, 22f);
            FillRect(tb, ColPanelBg);
            FillRect(new Rect(tb.x, tb.y, 3f, tb.height), ColAmber);
            GUI.Label(new Rect(tb.x + 10f, tb.y, tb.width - 10f, tb.height), "AUTOPILOT / WAYPOINTS", _mcpTitle);

            GUILayout.BeginArea(new Rect(panelRect.x + 8, panelRect.y + 26, panelRect.width - 16, panelRect.height - 34));
            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawTargetControls(r);
            DrawWaypointListContent(r);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPanel()
        {
            float w = Mathf.Max(340f, Plugin.CfgPanelW.Value);
            float h = Mathf.Max(400f, Plugin.CfgPanelH.Value);
            float x = Plugin.CfgPanelX.Value < 0 ? Screen.width - w - 20 : Plugin.CfgPanelX.Value;
            float y = Plugin.CfgPanelY.Value < 0 ? 20 : Plugin.CfgPanelY.Value;
            _panelRect = new Rect(x, y, w, h);
            InitMcp();

            FillRect(_panelRect, new Color(0.10f, 0.11f, 0.125f, 0.97f));
            FrameRect(_panelRect, ColBezel, 1f);

            Rect titleBar = new Rect(_panelRect.x, _panelRect.y, _panelRect.width, 22f);
            FillRect(titleBar, ColPanelBg);
            FillRect(new Rect(titleBar.x, titleBar.y, 3f, titleBar.height), ColAmber);
            GUI.Label(new Rect(titleBar.x + 10f, titleBar.y, titleBar.width - 40f, titleBar.height),
                      "SP2 MINIMAP  v2.9", _mcpTitle);

            if (FlatButton(new Rect(_panelRect.xMax - 22, _panelRect.y + 3, 18, 16), "x", false))
            { Plugin.GuiVisible = false; return; }
            HandlePanelDrag(new Rect(_panelRect.x, _panelRect.y, _panelRect.width - 30, 22));
            HandlePanelResize(new Rect(_panelRect.xMax - 16, _panelRect.yMax - 16, 16, 16));

            GUILayout.BeginArea(new Rect(_panelRect.x + 10, _panelRect.y + 25, _panelRect.width - 20, _panelRect.height - 35));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Space(4);
            Rect navR = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            if (FlatButton(navR, Plugin.NavEnabled ? "AP  ENGAGED    (F9)" : "AP  STANDBY    (F9)", Plugin.NavEnabled))
            {
                Plugin.ToggleAutopilot();
                if (!Plugin.NavEnabled) { Plugin.NavActive = false; Plugin.CmdThrottle = -1f; }
            }

            float cw = Mathf.Max(240f, w - 44f);
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            if (BtnW(Plugin.IsFullscreen ? "退出全屏" : "全屏", cw * 0.58f))
            {
                Plugin.IsFullscreen = !Plugin.IsFullscreen;
                Plugin.PlacementMode = false;
                if (Plugin.IsFullscreen) Plugin.Minimized = false;
                if (Plugin.Renderer != null && !Plugin.IsFullscreen) Plugin.Renderer.MapPanOffset = Vector3.zero;
            }
            if (BtnW("选点模式", cw * 0.40f, Plugin.PlacementMode))
                Plugin.PlacementMode = !Plugin.PlacementMode;
            GUILayout.EndHorizontal();

            var r = Plugin.Renderer;
            DrawTargetControls(r);

            SectionLabel("UNITS");
            GUILayout.BeginHorizontal();
            float uw = (cw - 8f) / 3f;
            if (BtnW("METRIC", uw, Plugin.CfgUnitSystem.Value == "Metric")) Plugin.CfgUnitSystem.Value = "Metric";
            if (BtnW("IMPERIAL", uw, Plugin.CfgUnitSystem.Value == "Imperial")) Plugin.CfgUnitSystem.Value = "Imperial";
            if (BtnW("NAUTICAL", uw, Plugin.CfgUnitSystem.Value == "Nautical")) Plugin.CfgUnitSystem.Value = "Nautical";
            GUILayout.EndHorizontal();

            SectionLabel("MAP");
            GUILayout.Label("RANGE   " + UnitSystem.FormatDistance(Plugin.CfgRangeM.Value), _mcpStatus);
            Plugin.CfgRangeM.Value = GUILayout.HorizontalSlider(Plugin.CfgRangeM.Value, 1f, 500000f, GUILayout.Height(20));
            GUILayout.BeginHorizontal();
            float zw = (cw - 8f) / 3f;
            if (BtnW("RESET", zw)) Plugin.CfgRangeM.Value = Plugin.ResetRangeM;
            if (BtnW("ZOOM +", zw)) Plugin.CfgRangeM.Value = Mathf.Clamp(Plugin.CfgRangeM.Value * 0.5f, 1f, 500000f);
            if (BtnW("ZOOM -", zw)) Plugin.CfgRangeM.Value = Mathf.Clamp(Plugin.CfgRangeM.Value * 2f, 1f, 500000f);
            GUILayout.EndHorizontal();

            DrawWaypointListContent(Plugin.Renderer);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void HandlePanelDrag(Rect titleRect)
        {
            Event e = Event.current;
            if (e == null) return;
            Vector2 mp = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 0 && titleRect.Contains(mp))
            {
                _isDraggingPanel = true; _panelDragStart = mp;
                _panelStartX = Plugin.CfgPanelX.Value < 0 ? Screen.width - Plugin.CfgPanelW.Value - 20 : Plugin.CfgPanelX.Value;
                _panelStartY = Plugin.CfgPanelY.Value < 0 ? 20 : Plugin.CfgPanelY.Value;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _isDraggingPanel)
            {
                Vector2 d = mp - _panelDragStart;
                Plugin.CfgPanelX.Value = Mathf.Clamp(_panelStartX + d.x, 0f, Screen.width - 100f);
                Plugin.CfgPanelY.Value = Mathf.Clamp(_panelStartY + d.y, 0f, Screen.height - 60f);
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _isDraggingPanel)
            {
                _isDraggingPanel = false; Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f; e.Use();
            }
        }

        void HandlePanelResize(Rect resizeRect)
        {
            Event e = Event.current;
            if (e == null) return;
            Vector2 mp = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 0 && resizeRect.Contains(mp))
            {
                _isResizingPanel = true; _panelResizeStart = mp;
                _panelStartW = Plugin.CfgPanelW.Value; _panelStartH = Plugin.CfgPanelH.Value;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _isResizingPanel)
            {
                Vector2 d = mp - _panelResizeStart;
                Plugin.CfgPanelW.Value = Mathf.Clamp(_panelStartW + d.x, 300f, Screen.width - 40f);
                Plugin.CfgPanelH.Value = Mathf.Clamp(_panelStartH + d.y, 300f, Screen.height - 40f);
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _isResizingPanel)
            {
                _isResizingPanel = false; Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f; e.Use();
            }
        }

    private void HandleMiniInput()
    {
        //IL_0015: Unknown result type (might be due to invalid IL or missing references)
        //IL_001a: Unknown result type (might be due to invalid IL or missing references)
        //IL_001c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0022: Invalid comparison between Unknown and I4
        //IL_002a: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a2: Unknown result type (might be due to invalid IL or missing references)
        //IL_004f: Unknown result type (might be due to invalid IL or missing references)
        //IL_01b3: Unknown result type (might be due to invalid IL or missing references)
        //IL_01b9: Invalid comparison between Unknown and I4
        //IL_00c0: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c5: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c9: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f5: Unknown result type (might be due to invalid IL or missing references)
        //IL_0106: Unknown result type (might be due to invalid IL or missing references)
        //IL_031c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0322: Invalid comparison between Unknown and I4
        //IL_0123: Unknown result type (might be due to invalid IL or missing references)
        //IL_0124: Unknown result type (might be due to invalid IL or missing references)
        //IL_01e5: Unknown result type (might be due to invalid IL or missing references)
        //IL_01e7: Unknown result type (might be due to invalid IL or missing references)
        //IL_01ec: Unknown result type (might be due to invalid IL or missing references)
        //IL_01fb: Unknown result type (might be due to invalid IL or missing references)
        //IL_0200: Unknown result type (might be due to invalid IL or missing references)
        //IL_0208: Unknown result type (might be due to invalid IL or missing references)
        //IL_020d: Unknown result type (might be due to invalid IL or missing references)
        //IL_021a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0221: Unknown result type (might be due to invalid IL or missing references)
        //IL_0229: Unknown result type (might be due to invalid IL or missing references)
        //IL_0230: Unknown result type (might be due to invalid IL or missing references)
        //IL_0237: Unknown result type (might be due to invalid IL or missing references)
        //IL_023f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0256: Unknown result type (might be due to invalid IL or missing references)
        //IL_0257: Unknown result type (might be due to invalid IL or missing references)
        //IL_016e: Unknown result type (might be due to invalid IL or missing references)
        //IL_016f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0153: Unknown result type (might be due to invalid IL or missing references)
        //IL_0288: Unknown result type (might be due to invalid IL or missing references)
        //IL_028a: Unknown result type (might be due to invalid IL or missing references)
        //IL_028f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0294: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a1: Unknown result type (might be due to invalid IL or missing references)
        //IL_02d6: Unknown result type (might be due to invalid IL or missing references)
        Event current = Event.current;
        if (current == null)
        {
            return;
        }
        Vector2 mousePosition = current.mousePosition;
        if ((int)current.type == 6 && _rect.Contains(mousePosition))
        {
            float num = Mathf.Max(1.01f, Plugin.CfgMiniScrollStep.Value);
            float num2 = ((current.delta.y > 0f) ? num : (1f / num));
            Plugin.CfgRangeM.Value = Mathf.Clamp(Plugin.CfgRangeM.Value * num2, 1f, 500000f);
            current.Use();
        }
        else if ((int)current.type == 0 && current.button == 0)
        {
            Rect val = CloseButtonRect();
            if (val.Contains(mousePosition))
            {
                Plugin.Minimized = true;
                _isDragging = false;
                current.Use();
            }
            else if (_rect.Contains(mousePosition))
            {
                Waypoint waypointAtScreen = GetWaypointAtScreen(mousePosition);
                if (waypointAtScreen != null)
                {
                    _draggingWaypoint = waypointAtScreen;
                    _lastWpMousePos = mousePosition;
                    Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                    current.Use();
                }
                else if (current.shift || Plugin.PlacementMode)
                {
                    AddWaypointFromScreen(mousePosition);
                    current.Use();
                }
                else
                {
                    _isDragging = true;
                    _dragStart = mousePosition;
                    _dragStartOffsetX = Plugin.CfgOffsetX.Value;
                    _dragStartOffsetY = Plugin.CfgOffsetY.Value;
                    Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                    current.Use();
                }
            }
        }
        else if ((int)current.type == 3 && current.button == 0)
        {
            if (_draggingWaypoint != null)
            {
                Vector3 val2 = ScreenDeltaToWorld(mousePosition - _lastWpMousePos, Plugin.Renderer.CurrentHeading);
                Vector3 worldPos = _draggingWaypoint.WorldPos;
                Plugin.Renderer.MoveWaypointTo(_draggingWaypoint, new Vector3(worldPos.x + val2.x, worldPos.y, worldPos.z + val2.z), _draggingWaypoint.Altitude);
                _lastWpMousePos = mousePosition;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
            else if (_isDragging)
            {
                Vector2 val3 = mousePosition - _dragStart;
                Plugin.CfgOffsetX.Value = Mathf.Clamp(_dragStartOffsetX + val3.x, 0f, (float)Screen.width - _rect.width);
                Plugin.CfgOffsetY.Value = Mathf.Clamp(_dragStartOffsetY + val3.y, 0f, (float)Screen.height - _rect.height);
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
        }
        else if ((int)current.type == 1 && current.button == 0)
        {
            _isDragging = false;
            _draggingWaypoint = null;
            Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
            current.Use();
        }
    }

    private void HandleFullscreenInput()
    {
        //IL_0015: Unknown result type (might be due to invalid IL or missing references)
        //IL_001a: Unknown result type (might be due to invalid IL or missing references)
        //IL_001b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0033: Unknown result type (might be due to invalid IL or missing references)
        //IL_006b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0071: Invalid comparison between Unknown and I4
        //IL_0079: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f3: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a0: Unknown result type (might be due to invalid IL or missing references)
        //IL_0108: Unknown result type (might be due to invalid IL or missing references)
        //IL_01b3: Unknown result type (might be due to invalid IL or missing references)
        //IL_01b9: Invalid comparison between Unknown and I4
        //IL_011c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0139: Unknown result type (might be due to invalid IL or missing references)
        //IL_013a: Unknown result type (might be due to invalid IL or missing references)
        //IL_0183: Unknown result type (might be due to invalid IL or missing references)
        //IL_0184: Unknown result type (might be due to invalid IL or missing references)
        //IL_018b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0190: Unknown result type (might be due to invalid IL or missing references)
        //IL_0168: Unknown result type (might be due to invalid IL or missing references)
        //IL_02ca: Unknown result type (might be due to invalid IL or missing references)
        //IL_02d0: Invalid comparison between Unknown and I4
        //IL_01e5: Unknown result type (might be due to invalid IL or missing references)
        //IL_01e7: Unknown result type (might be due to invalid IL or missing references)
        //IL_01ec: Unknown result type (might be due to invalid IL or missing references)
        //IL_01f7: Unknown result type (might be due to invalid IL or missing references)
        //IL_01fc: Unknown result type (might be due to invalid IL or missing references)
        //IL_0204: Unknown result type (might be due to invalid IL or missing references)
        //IL_0209: Unknown result type (might be due to invalid IL or missing references)
        //IL_0212: Unknown result type (might be due to invalid IL or missing references)
        //IL_0219: Unknown result type (might be due to invalid IL or missing references)
        //IL_0221: Unknown result type (might be due to invalid IL or missing references)
        //IL_0228: Unknown result type (might be due to invalid IL or missing references)
        //IL_022f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0237: Unknown result type (might be due to invalid IL or missing references)
        //IL_024e: Unknown result type (might be due to invalid IL or missing references)
        //IL_024f: Unknown result type (might be due to invalid IL or missing references)
        //IL_027d: Unknown result type (might be due to invalid IL or missing references)
        //IL_027f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0284: Unknown result type (might be due to invalid IL or missing references)
        //IL_0289: Unknown result type (might be due to invalid IL or missing references)
        //IL_028c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0294: Unknown result type (might be due to invalid IL or missing references)
        //IL_0299: Unknown result type (might be due to invalid IL or missing references)
        //IL_029d: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a2: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a4: Unknown result type (might be due to invalid IL or missing references)
        //IL_02a9: Unknown result type (might be due to invalid IL or missing references)
        Event current = Event.current;
        if (current == null)
        {
            return;
        }
        Vector2 mousePosition = current.mousePosition;
        if (mousePosition.y < 42f || mousePosition.x > (float)(Screen.width - 380))
        {
            return;
        }
        MinimapRenderer renderer = Plugin.Renderer;
        if ((Object)(object)renderer == (Object)null)
        {
            return;
        }
        if ((int)current.type == 6 && _rect.Contains(mousePosition))
        {
            float num = Mathf.Max(1.01f, Plugin.CfgMiniScrollStep.Value);
            float num2 = ((current.delta.y > 0f) ? num : (1f / num));
            Plugin.CfgRangeM.Value = Mathf.Clamp(Plugin.CfgRangeM.Value * num2, 1f, 500000f);
            current.Use();
        }
        else if ((int)current.type == 0 && current.button == 0 && _rect.Contains(mousePosition))
        {
            Waypoint waypointAtScreen = GetWaypointAtScreen(mousePosition);
            if (waypointAtScreen != null)
            {
                _draggingWaypoint = waypointAtScreen;
                _lastWpMousePos = mousePosition;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
            else if (Plugin.PlacementMode)
            {
                AddWaypointFromScreen(mousePosition);
                current.Use();
            }
            else
            {
                _isDragging = true;
                _dragStart = mousePosition;
                _dragStartPanOffset = renderer.MapPanOffset;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
        }
        else if ((int)current.type == 3 && current.button == 0)
        {
            if (_draggingWaypoint != null)
            {
                Vector3 val = ScreenDeltaToWorld(mousePosition - _lastWpMousePos, renderer.CurrentHeading);
                Vector3 worldPos = _draggingWaypoint.WorldPos;
                renderer.MoveWaypointTo(_draggingWaypoint, new Vector3(worldPos.x + val.x, worldPos.y, worldPos.z + val.z), _draggingWaypoint.Altitude);
                _lastWpMousePos = mousePosition;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
            else if (_isDragging)
            {
                Vector2 sDelta = mousePosition - _dragStart;
                Vector3 val2 = ScreenDeltaToWorld(sDelta, renderer.CurrentHeading);
                renderer.MapPanOffset = _dragStartPanOffset - val2;
                Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
                current.Use();
            }
        }
        else if ((int)current.type == 1 && current.button == 0)
        {
            _isDragging = false;
            _draggingWaypoint = null;
            Plugin.BlockMouseInputUntil = Time.unscaledTime + 0.2f;
            current.Use();
        }
    }

    private Waypoint GetWaypointAtScreen(Vector2 mp)
    {
        //IL_0038: Unknown result type (might be due to invalid IL or missing references)
        //IL_003e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0053: Unknown result type (might be due to invalid IL or missing references)
        //IL_0058: Unknown result type (might be due to invalid IL or missing references)
        //IL_0077: Unknown result type (might be due to invalid IL or missing references)
        //IL_009e: Unknown result type (might be due to invalid IL or missing references)
        //IL_00aa: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b5: Unknown result type (might be due to invalid IL or missing references)
        MinimapRenderer renderer = Plugin.Renderer;
        if ((Object)(object)renderer == (Object)null)
        {
            return null;
        }
        foreach (Waypoint waypoint in renderer.Waypoints)
        {
            Vector2 val = WorldToMap(waypoint.WorldPos, renderer.CurrentCenter, Plugin.CfgRangeM.Value, renderer.CurrentHeading);
            float num = _rect.x + _rect.width * 0.5f + val.x;
            float num2 = _rect.y + _rect.height * 0.5f + val.y;
            float num3 = num - mp.x;
            float num4 = num2 - mp.y;
            if (num3 * num3 + num4 * num4 < 144f)
            {
                return waypoint;
            }
        }
        return null;
    }

    private Vector3 ScreenDeltaToWorld(Vector2 sDelta, float heading)
    {
        //IL_001e: Unknown result type (might be due to invalid IL or missing references)
        //IL_003d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0033: Unknown result type (might be due to invalid IL or missing references)
        //IL_0099: Unknown result type (might be due to invalid IL or missing references)
        //IL_009e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0089: Unknown result type (might be due to invalid IL or missing references)
        //IL_008e: Unknown result type (might be due to invalid IL or missing references)
        //IL_00a2: Unknown result type (might be due to invalid IL or missing references)
        float num = Plugin.CfgRangeM.Value * 2f / _rect.width;
        float num2 = sDelta.x * num;
        float num3 = (Plugin.CfgFlipY.Value ? ((0f - sDelta.y) * num) : (sDelta.y * num));
        if (Plugin.CfgRotateWithHeading.Value)
        {
            float num4 = heading * ((float)Math.PI / 180f);
            float num5 = Mathf.Cos(num4);
            float num6 = Mathf.Sin(num4);
            return new Vector3(num2 * num5 + num3 * num6, 0f, (0f - num2) * num6 + num3 * num5);
        }
        return new Vector3(num2, 0f, num3);
    }

    private void AddWaypointFromScreen(Vector2 screenPos)
    {
        //IL_005b: Unknown result type (might be due to invalid IL or missing references)
        //IL_005c: Unknown result type (might be due to invalid IL or missing references)
        //IL_005d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0068: Unknown result type (might be due to invalid IL or missing references)
        //IL_006d: Unknown result type (might be due to invalid IL or missing references)
        //IL_006f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0074: Unknown result type (might be due to invalid IL or missing references)
        //IL_0075: Unknown result type (might be due to invalid IL or missing references)
        //IL_007a: Unknown result type (might be due to invalid IL or missing references)
        //IL_007c: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f2: Unknown result type (might be due to invalid IL or missing references)
        //IL_00fa: Unknown result type (might be due to invalid IL or missing references)
        //IL_0100: Unknown result type (might be due to invalid IL or missing references)
        //IL_00db: Unknown result type (might be due to invalid IL or missing references)
        //IL_00e0: Unknown result type (might be due to invalid IL or missing references)
        MinimapRenderer renderer = Plugin.Renderer;
        if ((Object)(object)renderer == (Object)null)
        {
            return;
        }
        Vector2 val = default(Vector2);
        val = new Vector2(_rect.x + _rect.width * 0.5f, _rect.y + _rect.height * 0.5f);
        Vector3 val2 = ScreenDeltaToWorld(screenPos - val, renderer.CurrentHeading);
        Vector3 val3 = renderer.CurrentCenter + val2;
        float num = renderer.CurrentCenter.y - renderer.GetFloatingOriginSeaLevelOrZero();
        val3.y = num + renderer.GetFloatingOriginSeaLevelOrZero();
        float speedMps = 100f;
        Component localAircraft = renderer.GetLocalAircraft();
        if ((Object)(object)localAircraft != (Object)null)
        {
            Rigidbody componentInChildren = localAircraft.GetComponentInChildren<Rigidbody>();
            if ((Object)(object)componentInChildren != (Object)null)
            {
                Vector3 rigidbodyVelocity = MinimapRenderer.GetRigidbodyVelocity(componentInChildren);
                speedMps = Mathf.Max(50f, rigidbodyVelocity.magnitude);
            }
        }
        renderer.AddWaypoint(new Vector3(val3.x, num, val3.z), num, speedMps);
    }

    private void DrawAllIcons(MinimapRenderer r)
    {
        //IL_0002: Unknown result type (might be due to invalid IL or missing references)
        //IL_0007: Unknown result type (might be due to invalid IL or missing references)
        //IL_003e: Unknown result type (might be due to invalid IL or missing references)
        //IL_0048: Unknown result type (might be due to invalid IL or missing references)
        //IL_007d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0087: Unknown result type (might be due to invalid IL or missing references)
        //IL_00bc: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c6: Unknown result type (might be due to invalid IL or missing references)
        //IL_00fb: Unknown result type (might be due to invalid IL or missing references)
        //IL_0105: Unknown result type (might be due to invalid IL or missing references)
        //IL_0126: Unknown result type (might be due to invalid IL or missing references)
        Vector3 currentCenter = r.CurrentCenter;
        float currentHeading = r.CurrentHeading;
        float value = Plugin.CfgRangeM.Value;
        if (Plugin.CfgShowSpawn.Value)
        {
            DrawIcons(r.SpawnEntries, new Color(1f, 0.9f, 0.2f), 8f, currentCenter, value, currentHeading, _dotTex);
        }
        if (Plugin.CfgShowAirport.Value)
        {
            DrawIcons(r.AirportEntries, new Color(0.3f, 0.7f, 1f), 10f, currentCenter, value, currentHeading, _squareTex);
        }
        if (Plugin.CfgShowTarget.Value)
        {
            DrawIcons(r.TargetEntries, new Color(1f, 0.3f, 0.3f), 8f, currentCenter, value, currentHeading, _dotTex);
        }
        if (Plugin.CfgShowCustomOBJ.Value)
        {
            DrawIcons(r.CustomEntries, new Color(0.3f, 1f, 0.3f), 8f, currentCenter, value, currentHeading, _squareTex);
        }
        if (Plugin.CfgShowTraffic.Value)
        {
            DrawIcons(r.PlayerEntries, new Color(0.35f, 0.90f, 1.00f), 9f, currentCenter, value, currentHeading, _triTex);
            DrawIcons(r.AiEntries, new Color(1.00f, 0.45f, 0.25f), 9f, currentCenter, value, currentHeading, _triTex);
        }
        if (Plugin.CfgShowWaypoints.Value)
        {
            DrawWaypoints(r, currentCenter, value, currentHeading);
        }
    }

        void DrawWaypoints(MinimapRenderer r, Vector3 center, float range, float heading)
        {
            if (r.Waypoints.Count == 0) return;

            if (_wpStyle == null)
            {
                _wpStyle = new GUIStyle(GUI.skin.label);
                _wpStyle.fontSize = 11;
                _wpStyle.fontStyle = FontStyle.Bold;
            }
            _wpStyle.normal.textColor = ColWp;

            var active = r.GetActiveWaypoint();
            float sz = 12f * Plugin.CfgWaypointIconScale.Value;

            // ---- 航线：本机 → 航点 1 → 航点 2 … ----
            Vector2 prev = new Vector2(_rect.center.x, _rect.center.y) + WorldToMap(r.AircraftPosition, center, range, heading);
            for (int i = 0; i < r.Waypoints.Count; i++)
            {
                var wp = r.Waypoints[i];
                if (wp == null) continue;
                Vector2 p = new Vector2(_rect.center.x, _rect.center.y) + WorldToMap(wp.WorldPos, center, range, heading);
                DrawMapLine(prev, p, wp == active ? ColWpActive : ColWpLine, wp == active ? 2f : 1.2f);
                prev = p;
            }

            // ---- 航点图标 ----
            for (int i = 0; i < r.Waypoints.Count; i++)
            {
                var wp = r.Waypoints[i];
                if (wp == null) continue;

                Vector3 rel = wp.WorldPos - center;
                if (rel.sqrMagnitude > range * range * 4f) continue;

                Vector2 sp = WorldToMap(wp.WorldPos, center, range, heading);
                float x = _rect.x + _rect.width * 0.5f + sp.x;
                float y = _rect.y + _rect.height * 0.5f + sp.y;
                if (x < _rect.x || x > _rect.xMax || y < _rect.y || y > _rect.yMax) continue;

                bool dragging = _draggingWaypoint == wp;
                Color c;
                if (!wp.Enabled) c = new Color(0.55f, 0.50f, 0.62f, 0.60f);
                else if (dragging) c = Color.white;
                else if (wp == active) c = ColWpActive;
                else c = ColWp;

                Color old = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(new Rect(x - sz * 0.5f, y - sz * 0.5f, sz, sz), _wpTex);
                GUI.color = old;

                // 航点编号（菱形中心）
                if (_wpNumStyle == null)
                {
                    _wpNumStyle = new GUIStyle(GUI.skin.label);
                    _wpNumStyle.fontSize = 9;
                    _wpNumStyle.fontStyle = FontStyle.Bold;
                    _wpNumStyle.alignment = TextAnchor.MiddleCenter;
                }
                _wpNumStyle.normal.textColor = wp.Enabled ? Color.black : new Color(0.15f, 0.15f, 0.15f, 0.7f);
                GUI.Label(new Rect(x - sz * 0.5f, y - sz * 0.5f, sz, sz), (i + 1).ToString(), _wpNumStyle);

                if (Plugin.CfgShowLabels.Value)
                {
                    GUI.Label(new Rect(x + sz * 0.5f + 2f, y - 9f, 150f, 16f),
                        wp.Label + " " + UnitSystem.FormatDistance(wp.DistanceM)
                        + (!wp.Enabled ? " (off)" : (wp == active ? " [A]" : "")), _wpStyle);
                }
            }
        }

        // IMGUI 直线（裁剪到小地图范围内）
        void DrawMapLine(Vector2 a, Vector2 b, Color c, float w)
        {
            Rect clip = new Rect(_rect.x + 1f, _rect.y + 1f, _rect.width - 2f, _rect.height - 2f);
            if (!ClipSegment(clip, ref a, ref b)) return;

            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;

            Matrix4x4 oldM = GUI.matrix;
            Color oldC = GUI.color;

            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), Texture2D.whiteTexture);

            GUI.matrix = oldM;
            GUI.color = oldC;
        }

        // Cohen–Sutherland 线段裁剪
        static bool ClipSegment(Rect r, ref Vector2 a, ref Vector2 b)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x;
            float dy = b.y - a.y;

            if (!ClipEdge(-dx, a.x - r.x, ref t0, ref t1)) return false;
            if (!ClipEdge(dx, r.xMax - a.x, ref t0, ref t1)) return false;
            if (!ClipEdge(-dy, a.y - r.y, ref t0, ref t1)) return false;
            if (!ClipEdge(dy, r.yMax - a.y, ref t0, ref t1)) return false;

            Vector2 o = a;
            if (t1 < 1f) b = new Vector2(o.x + t1 * dx, o.y + t1 * dy);
            if (t0 > 0f) a = new Vector2(o.x + t0 * dx, o.y + t0 * dy);
            return true;
        }

        static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
            float t = q / p;
            if (p < 0f)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }

    private Rect CloseButtonRect()
    {
        //IL_002d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0032: Unknown result type (might be due to invalid IL or missing references)
        //IL_0035: Unknown result type (might be due to invalid IL or missing references)
        return new Rect(_rect.xMax - 20f, _rect.y + 2f, 18f, 18f);
    }

        private void DrawCloseButton(Rect rect)
        {
            Rect r = CloseButtonRect();
            FillRect(r, new Color(0.42f, 0.14f, 0.14f, 0.95f));
            FrameRect(r, new Color(0.78f, 0.32f, 0.32f, 1f), 1f);
            GUI.Label(r, "x", _mcpBtn);
        }

    private void DrawIcons(List<MinimapEntry> entries, Color color, float baseSize, Vector3 center, float range, float heading, Texture2D tex)
    {
        //IL_002d: Unknown result type (might be due to invalid IL or missing references)
        //IL_0032: Unknown result type (might be due to invalid IL or missing references)
        //IL_004c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0051: Unknown result type (might be due to invalid IL or missing references)
        //IL_005f: Expected O, but got Unknown
        //IL_006a: Unknown result type (might be due to invalid IL or missing references)
        //IL_00bd: Unknown result type (might be due to invalid IL or missing references)
        //IL_00af: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c2: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c4: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c6: Unknown result type (might be due to invalid IL or missing references)
        //IL_00c8: Unknown result type (might be due to invalid IL or missing references)
        //IL_00cd: Unknown result type (might be due to invalid IL or missing references)
        //IL_00ef: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f1: Unknown result type (might be due to invalid IL or missing references)
        //IL_00f7: Unknown result type (might be due to invalid IL or missing references)
        //IL_00fc: Unknown result type (might be due to invalid IL or missing references)
        //IL_011b: Unknown result type (might be due to invalid IL or missing references)
        //IL_0142: Unknown result type (might be due to invalid IL or missing references)
        //IL_0196: Unknown result type (might be due to invalid IL or missing references)
        //IL_01b3: Unknown result type (might be due to invalid IL or missing references)
        //IL_01c0: Unknown result type (might be due to invalid IL or missing references)
        //IL_0238: Unknown result type (might be due to invalid IL or missing references)
        if (entries == null || entries.Count == 0 || (Object)(object)tex == (Object)null)
        {
            return;
        }
        float num = baseSize * Plugin.CfgIconScale.Value;
        Color color2 = GUI.color;
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10
            };
            _labelStyle.normal.textColor = Color.white;
        }
        foreach (MinimapEntry entry in entries)
        {
            if (entry == null)
            {
                continue;
            }
            Vector3 val = (((Object)(object)entry.Transform != (Object)null) ? entry.Transform.position : entry.WorldPos);
            Vector3 val2 = val - center;
            if (val2.sqrMagnitude > range * range * 4f)
            {
                continue;
            }
            Vector2 val3 = WorldToMap(val, center, range, heading);
            float num2 = _rect.x + _rect.width * 0.5f + val3.x;
            float num3 = _rect.y + _rect.height * 0.5f + val3.y;
            if (!(num2 < _rect.x) && !(num2 > _rect.xMax) && !(num3 < _rect.y) && !(num3 > _rect.yMax))
            {
                GUI.color = color;
                GUI.DrawTexture(new Rect(num2 - num * 0.5f, num3 - num * 0.5f, num, num), (Texture)(object)tex);
                GUI.color = color2;
                if (Plugin.CfgShowLabels.Value && !string.IsNullOrEmpty(entry.Label))
                {
                    string text = ((entry.Label.Length > 20) ? entry.Label.Substring(0, 20) : entry.Label);
                    GUI.Label(new Rect(num2 + num * 0.5f + 2f, num3 - 6f, 200f, 14f), text, _labelStyle);
                }
            }
        }
    }

    private Vector2 WorldToMap(Vector3 world, Vector3 center, float range, float heading)
    {
        //IL_0001: Unknown result type (might be due to invalid IL or missing references)
        //IL_0007: Unknown result type (might be due to invalid IL or missing references)
        //IL_000f: Unknown result type (might be due to invalid IL or missing references)
        //IL_0015: Unknown result type (might be due to invalid IL or missing references)
        //IL_00af: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b4: Unknown result type (might be due to invalid IL or missing references)
        //IL_00b8: Unknown result type (might be due to invalid IL or missing references)
        float num = world.x - center.x;
        float num2 = world.z - center.z;
        if (Plugin.CfgRotateWithHeading.Value && Mathf.Abs(heading) > 0.01f)
        {
            float num3 = heading * ((float)Math.PI / 180f);
            float num4 = Mathf.Cos(num3);
            float num5 = Mathf.Sin(num3);
            float num6 = num * num4 - num2 * num5;
            float num7 = num * num5 + num2 * num4;
            num = num6;
            num2 = num7;
        }
        float num8 = _rect.width / (range * 2f);
        float num9 = num * num8;
        float num10 = num2 * num8;
        if (Plugin.CfgFlipY.Value)
        {
            num10 = 0f - num10;
        }
        return new Vector2(num9, num10);
    }

    private void DrawBorder(Rect r, Color c, float t)
    {
        //IL_0001: Unknown result type (might be due to invalid IL or missing references)
        //IL_0006: Unknown result type (might be due to invalid IL or missing references)
        //IL_0007: Unknown result type (might be due to invalid IL or missing references)
        //IL_0024: Unknown result type (might be due to invalid IL or missing references)
        //IL_004c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0072: Unknown result type (might be due to invalid IL or missing references)
        //IL_009a: Unknown result type (might be due to invalid IL or missing references)
        //IL_00aa: Unknown result type (might be due to invalid IL or missing references)
        Color color = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), (Texture)(object)Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), (Texture)(object)Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), (Texture)(object)Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), (Texture)(object)Texture2D.whiteTexture);
        GUI.color = color;
    }

        // ================= MCP（Mode Control Panel）风格 =================
        static readonly Color ColRowBg = new Color(0.175f, 0.185f, 0.205f, 1f);
        static readonly Color ColBezel = new Color(0.34f, 0.36f, 0.40f, 1f);
        static readonly Color ColDisplay = new Color(0.015f, 0.020f, 0.025f, 1f);
        static readonly Color ColAmber = new Color(1.00f, 0.78f, 0.22f, 1f);
        static readonly Color ColGreen = new Color(0.40f, 1.00f, 0.50f, 1f);
        static readonly Color ColDim = new Color(0.86f, 0.88f, 0.92f, 1f);
        static readonly Color ColText = new Color(0.95f, 0.96f, 0.98f, 1f);
        static readonly Color ColBtn = new Color(0.20f, 0.21f, 0.24f, 1f);
        static readonly Color ColBtnOn = new Color(0.20f, 0.48f, 0.28f, 1f);
        static readonly Color ColPanelBg = new Color(0.10f, 0.11f, 0.125f, 1f);

        // 航点：紫色标准菱形
        static readonly Color ColWp = new Color(0.80f, 0.40f, 1.00f, 1f);
        static readonly Color ColWpActive = new Color(1.00f, 0.62f, 1.00f, 1f);
        static readonly Color ColWpLine = new Color(0.72f, 0.36f, 0.98f, 0.60f);

        GUIStyle _mcpTitle, _mcpLabel, _mcpDisplay, _mcpBtn, _mcpBtnOn, _mcpStatus;
        bool _mcpReady;

        void InitMcp()
        {
            if (_mcpReady) return;
            _mcpReady = true;

            Font mono = null;
            try { mono = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Courier New", "Lucida Console" }, 20); }
            catch { mono = null; }

            _mcpTitle = new GUIStyle(GUI.skin.label);
            _mcpTitle.fontSize = 12;
            _mcpTitle.fontStyle = FontStyle.Bold;
            _mcpTitle.normal.textColor = ColText;

            _mcpLabel = new GUIStyle(GUI.skin.label);
            _mcpLabel.fontSize = 11;
            _mcpLabel.normal.textColor = ColDim;

            _mcpStatus = new GUIStyle(GUI.skin.label);
            if (mono != null) _mcpStatus.font = mono;
            _mcpStatus.fontSize = 11;
            _mcpStatus.normal.textColor = ColDim;

            _mcpDisplay = new GUIStyle(GUI.skin.textField);
            if (mono != null) _mcpDisplay.font = mono;
            _mcpDisplay.fontSize = 22;
            _mcpDisplay.fontStyle = FontStyle.Bold;
            _mcpDisplay.alignment = TextAnchor.MiddleRight;
            _mcpDisplay.padding = new RectOffset(6, 8, 2, 2);
            _mcpDisplay.clipping = TextClipping.Clip;
            _mcpDisplay.fixedHeight = 0f;
            _mcpDisplay.normal.background = null;
            _mcpDisplay.focused.background = null;
            _mcpDisplay.hover.background = null;
            _mcpDisplay.active.background = null;
            _mcpDisplay.normal.textColor = ColAmber;
            _mcpDisplay.focused.textColor = ColAmber;
            _mcpDisplay.hover.textColor = ColAmber;
            _mcpDisplay.active.textColor = ColAmber;

            _mcpBtn = new GUIStyle(GUI.skin.label);
            _mcpBtn.fontSize = 12;
            _mcpBtn.alignment = TextAnchor.MiddleCenter;
            _mcpBtn.normal.textColor = ColText;

            _mcpBtnOn = new GUIStyle(_mcpBtn);
            _mcpBtnOn.normal.textColor = ColGreen;
        }

        static void FillRect(Rect r, Color c)
        {
            Color o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = o;
        }

        static void FrameRect(Rect r, Color c, float t)
        {
            FillRect(new Rect(r.x, r.y, r.width, t), c);
            FillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            FillRect(new Rect(r.x, r.y, t, r.height), c);
            FillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        bool FlatButton(Rect r, string text, bool on)
        {
            FillRect(r, on ? ColBtnOn : ColBtn);
            FrameRect(r, on ? ColGreen : ColBezel, 1f);
            GUI.Label(r, text, on ? _mcpBtnOn : _mcpBtn);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }


        // 统一按钮：固定宽度
        bool BtnW(string text, float w, bool on = false, float h = 24f)
        {
            Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            return FlatButton(rect, text, on);
        }

        // 统一按钮：占满宽度
        bool Btn(string text, bool on = false, float h = 24f)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, h, GUILayout.ExpandWidth(true));
            return FlatButton(rect, text, on);
        }

        // 统一分区标题
        void SectionLabel(string text)
        {
            GUILayout.Space(6);
            Rect h = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            FillRect(h, ColPanelBg);
            FillRect(new Rect(h.x, h.y, 3f, h.height), ColAmber);
            GUI.Label(new Rect(h.x + 10f, h.y, h.width - 10f, h.height), text, _mcpTitle);
        }

        // 一个完整通道：标签 + 数码窗（可直接输入）+ −/+
        void McpChannel(string id, string label, string unit, ref string buf, ref bool editing,
                        float shown, float min, float max, float step, string fmt,
                        Action<float> apply, string tgtAct,
                        bool isFixed, Action toFixed, Action toWpt)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 66f, GUILayout.ExpandWidth(true));
            FillRect(row, ColRowBg);

            float pad = 7f;
            float modeW = 52f;

            Rect lab = new Rect(row.x + pad, row.y + 5f, row.width - modeW - pad * 3f, 18f);
            GUI.Label(lab, label + "  " + unit + "    " + tgtAct, _mcpLabel);

            Rect modeR = new Rect(row.xMax - pad - modeW, row.y + 4f, modeW, 18f);
            if (FlatButton(modeR, isFixed ? "FIXED" : "WPT", isFixed))
            {
                if (isFixed) toWpt(); else toFixed();
            }

            float btnW = 30f, gap = 5f;
            Rect disp = new Rect(row.x + pad, row.y + 28f, row.width - pad * 2f - (btnW + gap) * 2f, 32f);
            FillRect(disp, ColDisplay);
            FrameRect(disp, ColBezel, 1f);

            bool focused = GUI.GetNameOfFocusedControl() == id;
            if (!focused && !editing) buf = shown.ToString(fmt);

            GUI.SetNextControlName(id);
            string txt = GUI.TextField(new Rect(disp.x + 2f, disp.y + 1f, disp.width - 4f, disp.height - 2f), buf, _mcpDisplay);

            if (GUI.GetNameOfFocusedControl() == id)
            {
                buf = txt;
                editing = true;
                if (Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    float v;
                    if (float.TryParse(buf, out v)) apply(Mathf.Clamp(v, min, max));
                    editing = false;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            else if (editing)
            {
                float v;
                if (float.TryParse(buf, out v)) apply(Mathf.Clamp(v, min, max));
                editing = false;
            }

            Rect minus = new Rect(disp.xMax + gap, disp.y, btnW, disp.height);
            Rect plus = new Rect(minus.xMax + gap, disp.y, btnW, disp.height);
            if (FlatButton(minus, "-", false)) apply(Mathf.Clamp(shown - step, min, max));
            if (FlatButton(plus, "+", false)) apply(Mathf.Clamp(shown + step, min, max));
        }

        // 分段条形图，可点击设置
        void McpBar(string label, ConfigEntry<float> cfg, float min, float max, string fmt)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            FillRect(row, ColRowBg);

            float pad = 7f;
            GUI.Label(new Rect(row.x + pad, row.y + 2f, row.width * 0.6f, 16f), label, _mcpLabel);

            Rect valR = new Rect(row.x + row.width * 0.6f, row.y + 2f, row.width * 0.4f - pad, 16f);
            GUIStyle right = new GUIStyle(_mcpStatus);
            right.alignment = TextAnchor.MiddleRight;
            GUI.Label(valR, cfg.Value.ToString(fmt), right);

            Rect bar = new Rect(row.x + pad, row.y + 19f, row.width - pad * 2f, 9f);
            FillRect(bar, ColDisplay);
            FrameRect(bar, ColBezel, 1f);

            int seg = 24;
            float on = Mathf.InverseLerp(min, max, cfg.Value);
            float sw = (bar.width - 2f) / seg;
            for (int i = 0; i < seg; i++)
            {
                float f = (i + 0.5f) / seg;
                Color c = f <= on ? ColGreen : new Color(0.18f, 0.20f, 0.22f, 1f);
                FillRect(new Rect(bar.x + 1f + i * sw, bar.y + 1f, sw - 1f, bar.height - 2f), c);
            }

            if (Event.current.type == EventType.MouseDown && bar.Contains(Event.current.mousePosition))
            {
                float t = Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width);
                cfg.Value = Mathf.Lerp(min, max, t);
                Event.current.Use();
            }
        }

        void DrawTargetControls(MinimapRenderer r)
        {
            if (r == null) return;
            InitMcp();

            GUILayout.Space(6);

            Rect hdr = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            FillRect(hdr, ColPanelBg);
            FillRect(new Rect(hdr.x, hdr.y, 3f, hdr.height), Plugin.NavEnabled ? ColGreen : ColDim);
            GUI.Label(new Rect(hdr.x + 10f, hdr.y, hdr.width - 90f, hdr.height), "MODE CONTROL PANEL", _mcpTitle);
            GUIStyle st = Plugin.NavEnabled ? _mcpBtnOn : _mcpLabel;
            GUI.Label(new Rect(hdr.xMax - 78f, hdr.y, 72f, hdr.height),
                      Plugin.NavEnabled ? "ENGAGED" : "STANDBY", st);

            GUILayout.Space(4);

            McpChannel("mcp_hd", "HDG", "deg",
                ref _tfHD, ref _tfHDE,
                Plugin.CfgFixedHD.Value, 0f, 360f, 1f, "F0",
                delegate (float v) { Plugin.CfgFixedHD.Value = v; },
                "T/A " + r.DebugTargetHD.ToString("F0") + "/" + r.DebugCurrentHD.ToString("F0"),
                Plugin.CfgHdMode.Value == "FIXED",
                delegate { Plugin.CfgHdMode.Value = "FIXED"; },
                delegate { Plugin.CfgHdMode.Value = "WAYPOINT"; });

            GUILayout.Space(3);

            McpChannel("mcp_alt", "ALT", UnitSystem.HeightUnitLabel,
                ref _tfAlt, ref _tfAltE,
                UnitSystem.MetersToDisplay(Plugin.CfgFixedAlt.Value),
                0f, UnitSystem.MetersToDisplay(15000f),
                Mathf.Max(1f, UnitSystem.MetersToDisplay(100f)), "F0",
                delegate (float v) { Plugin.CfgFixedAlt.Value = UnitSystem.DisplayToMeters(v); },
                "T/A " + r.DebugTargetAlt.ToString("F0") + "/" + r.DebugCurrentAlt.ToString("F0") + "m",
                Plugin.CfgAltMode.Value == "FIXED",
                delegate { Plugin.CfgAltMode.Value = "FIXED"; },
                delegate { Plugin.CfgAltMode.Value = "WAYPOINT"; });

            GUILayout.Space(3);

            McpChannel("mcp_ias", "IAS", UnitSystem.SpeedUnitLabel,
                ref _tfIAS, ref _tfIASE,
                UnitSystem.MpsToDisplay(Plugin.CfgFixedIAS.Value),
                UnitSystem.MpsToDisplay(10f), UnitSystem.MpsToDisplay(300f),
                Mathf.Max(1f, UnitSystem.MpsToDisplay(5f)), "F0",
                delegate (float v) { Plugin.CfgFixedIAS.Value = UnitSystem.DisplayToMps(v); },
                "T/A " + UnitSystem.FormatSpeed(r.DebugTargetIAS) + "/" + UnitSystem.FormatSpeed(r.DebugCurrentIAS),
                Plugin.CfgIasMode.Value == "FIXED",
                delegate { Plugin.CfgIasMode.Value = "FIXED"; },
                delegate { Plugin.CfgIasMode.Value = "WAYPOINT"; });

            GUILayout.Space(6);
            McpBar("PASS  TAS x s", Plugin.CfgArrivalTimeSec, 0.2f, 10f, "F1");
            GUILayout.Space(3);
            McpBar("RESPONSE GAIN", Plugin.CfgApRespGain, 0.1f, 4f, "F2");

            GUILayout.Space(5);
            Rect lm = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            bool low = Plugin.CfgApLowManeuver.Value;
            string lmText = low
                ? "LOW MANEUVER   ON   up " + Plugin.CfgApLowPitchUp.Value.ToString("F0")
                  + " / dn " + Plugin.CfgApLowPitchDown.Value.ToString("F0")
                : "LOW MANEUVER   OFF";
            if (FlatButton(lm, lmText, low))
                Plugin.CfgApLowManeuver.Value = !low;

            GUILayout.Space(4);
            Rect foot = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            GUI.Label(foot,
                "THR " + Plugin.CmdThrottle.ToString("F2")
                + "   ERR " + r.DebugSpeedErr.ToString("F1") + "m/s"
                + "   PASS " + r.DebugArrivalThresholdM.ToString("F0") + "m", _mcpStatus);
        }

        void DrawWaypointListContent(MinimapRenderer r)
        {
            if (r == null) return;
            InitMcp();

            GUILayout.Space(6);
            Rect hdr = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            FillRect(hdr, ColPanelBg);
            FillRect(new Rect(hdr.x, hdr.y, 3f, hdr.height), ColAmber);
            GUI.Label(new Rect(hdr.x + 10f, hdr.y, hdr.width - 10f, hdr.height), "WAYPOINTS", _mcpTitle);

            if (r.Waypoints.Count == 0)
            {
                GUILayout.Space(4);
                GUILayout.Label("  (空)", _mcpLabel);
                return;
            }

            var active = r.GetActiveWaypoint();
            for (int i = 0; i < r.Waypoints.Count; i++)
            {
                var wp = r.Waypoints[i];
                if (wp == null) continue;

                GUILayout.BeginHorizontal();
                if (BtnW(wp.Enabled ? "ON" : "OFF", 38f, wp.Enabled)) wp.Enabled = !wp.Enabled;

                GUI.enabled = (i > 0);
                if (BtnW("↑", 26f))
                {
                    var tmp = r.Waypoints[i - 1]; r.Waypoints[i - 1] = r.Waypoints[i]; r.Waypoints[i] = tmp;
                    GUI.enabled = true; return;
                }
                GUI.enabled = (i < r.Waypoints.Count - 1);
                if (BtnW("↓", 26f))
                {
                    var tmp = r.Waypoints[i + 1]; r.Waypoints[i + 1] = r.Waypoints[i]; r.Waypoints[i] = tmp;
                    GUI.enabled = true; return;
                }
                GUI.enabled = true;

                string marker = (wp == active) ? "▶ " : "   ";
                GUILayout.Label(marker + wp.Label, _mcpStatus, GUILayout.Width(62));
                GUILayout.Label(UnitSystem.FormatDistance(wp.DistanceM), _mcpStatus, GUILayout.Width(66));
                if (BtnW("编辑", 44f)) _editingWaypointId = wp.Id;
                if (BtnW("×", 26f)) { r.RemoveWaypoint(wp); return; }
                GUILayout.EndHorizontal();

                if (_editingWaypointId == wp.Id)
                {
                    GUILayout.Space(2);
                    GUILayout.Label("高度 (" + UnitSystem.HeightUnitLabel + "): "
                        + UnitSystem.MetersToDisplay(wp.Altitude).ToString("F0"), _mcpLabel);
                    float altDisp = UnitSystem.MetersToDisplay(wp.Altitude);
                    altDisp = GUILayout.HorizontalSlider(altDisp, 0f, UnitSystem.MetersToDisplay(20000f));
                    wp.Altitude = UnitSystem.DisplayToMeters(altDisp);
                    wp.WorldPos = new Vector3(wp.WorldPos.x, wp.Altitude, wp.WorldPos.z);

                    GUILayout.Label("速度 (" + UnitSystem.SpeedUnitLabel + "): "
                        + UnitSystem.MpsToDisplay(wp.Speed).ToString("F0"), _mcpLabel);
                    float spdDisp = UnitSystem.MpsToDisplay(wp.Speed);
                    spdDisp = GUILayout.HorizontalSlider(spdDisp, UnitSystem.MpsToDisplay(10f), UnitSystem.MpsToDisplay(400f));
                    wp.Speed = UnitSystem.DisplayToMps(spdDisp);

                    if (BtnW("关闭", 60f)) _editingWaypointId = -1;
                    GUILayout.Space(4);
                }
            }
        }
    }
}
