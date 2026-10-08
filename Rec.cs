using System.Collections.Generic;
using BepInEx.Configuration;

namespace NepFix
{
    /// Рекомендуемые значения, которые показываются под настройками в меню F10.
    internal static class Rec
    {
        static Dictionary<ConfigEntryBase, string> map;

        public static string Get(ConfigEntryBase e)
        {
            if (map == null) Build();
            return e != null && map.TryGetValue(e, out var s) ? s : null;
        }

        static void Build()
        {
            var S = Plugin.S;
            map = new()
            {
                [S.FpsUnlock] = "вкл", [S.FpsLimit] = "как монитор", [S.VSync] = "выкл", [S.MaxQueuedFrames] = "как в игре",
                [S.QualityLevel] = "Ultra", [S.FullResTextures] = "вкл", [S.Anisotropic] = "16x", [S.FourBoneSkinning] = "вкл",
                [S.TextureSharpness] = "-0,50", [S.HdrRendering] = "вкл",
                [S.Msaa] = "4x", [S.PostAa] = "SMAA выс.", [S.FsrPreset] = "выкл, если FPS хватает", [S.RenderScale] = "1,00–1,25", [S.FsrSharpness] = "0,40",
                [S.ShadowsOverride] = "вкл", [S.ShadowResolution] = "4096", [S.ShadowDistanceMin] = "60 м", [S.ShadowDistanceMul] = "1,5x",
                [S.ShadowCascades] = "4", [S.SoftShadows] = "вкл", [S.AdditionalLightShadows] = "вкл", [S.Ssao] = "вкл",
                [S.LodBias] = "2,50", [S.SmallObjectDistance] = "3,0x", [S.FarClipMul] = "1,50x", [S.DisableMapReduction] = "вкл",
                [S.OutlineWidth] = "авто", [S.CharacterShadows] = "вкл", [S.WeaponSmooth] = "вкл", [S.WeaponFade] = "140 мс", [S.AnimBlendMin] = "180 мс",
                [S.MotionInterp] = "вкл", [S.BikeHandling] = "1,20x", [S.BikeSpeed] = "как в игре", [S.BikeWallSoft] = "30%", [S.BikeLedgeSmooth] = "120 мс",
                [S.BikeProbe] = "выкл", [S.MapVoiceInterval] = "40 с",
                [S.AdaptiveFrameTiming] = "вкл", [S.UnitStuckFix] = "вкл", [S.RigidbodyInterpolation] = "вкл", [S.SyncPhysicsToFps] = "выкл", [S.AnimatorAlwaysAnimate] = "выкл",
                [S.SmoothUnits] = "вкл", [S.SmoothTime] = "40 мс", [S.FootIK] = "вкл", [S.ClothRate] = "как в игре", [S.VerboseLog] = "вкл",
                [S.PhysicsNoAutoSync] = "выкл", [S.FastLoading] = "вкл", [S.AnimatorCulling] = "выкл",
                [S.ForceLightShadows] = "вкл", [S.MaxShadowedLights] = "4", [S.AmbientMul] = "1,00x", [S.CharAmbient] = "как в игре", [S.AmbientAdd] = "нет",
                [S.ReflectionMul] = "1,00x", [S.SsaoIntensityMul] = "1,5x", [S.SsaoRadiusMul] = "1,00x", [S.SsaoHighQuality] = "вкл",
                [S.FxEnabled] = "вкл", [S.FxSplit] = "выкл", [S.FxInMenus] = "выкл", [S.FxCharStrength] = "15%", [S.FxGiIntensity] = "1,00", [S.FxGiRadius] = "2,00 м",
                [S.FxAoIntensity] = "0,60", [S.FxContactLength] = "0,35 м", [S.FxContactIntensity] = "0,70",
                [S.FxSsr] = "вкл", [S.FxSsrIntensity] = "50%", [S.FxSsrDistance] = "15 м", [S.FxSsrMode] = "вода и мокрое", [S.FxSsrBlur] = "вкл",
                [S.FxIndoorShadows] = "вкл", [S.FxOverheadAlways] = "выкл", [S.FxBlobStrength] = "55%", [S.FxIndoorLength] = "0,8 м", [S.FxFadeDistance] = "40 м", [S.FxIndoorSunContact] = "выкл",
                [S.FxTemporal] = "0,85", [S.FxRays] = "2", [S.FxDebug] = "выкл", [S.FxDepthSource] = "авто", [S.FxEvent] = "до прозрачных", [S.PipelineTest] = "выкл",
            };
        }
    }
}
