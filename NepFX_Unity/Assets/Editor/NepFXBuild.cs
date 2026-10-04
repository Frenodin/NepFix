using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NepFX
{
    public static class Build
    {
        const string ShaderPath = "Assets/NepFX/NepFX_Lighting.shader";

        [MenuItem("NepFX/Собрать пакет шейдеров")]
        public static void BuildBundles()
        {
            string outDir = GetArg("-nepfxOut") ?? Path.GetFullPath("Build");
            Directory.CreateDirectory(outDir);

            // игра использует только Direct3D 11
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) { Debug.LogError("NepFX: шейдер не найден: " + ShaderPath); EditorApplication.Exit(2); return; }
            var msgs = ShaderUtil.GetShaderMessages(shader);
            bool err = false;
            foreach (var m in msgs) { Debug.Log($"NepFX shader {m.severity}: {m.message} ({m.file}:{m.line})"); if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) err = true; }
            if (err) { Debug.LogError("NepFX: ошибки компиляции шейдера"); if (Application.isBatchMode) EditorApplication.Exit(3); return; }

            var builds = new System.Collections.Generic.List<AssetBundleBuild> { new AssetBundleBuild { assetBundleName = "nepfx.bundle", assetNames = new[] { ShaderPath } } };
            // сторонние idle-анимации: все FBX из Assets/Idles
            var idles = new System.Collections.Generic.List<string>();
            if (AssetDatabase.IsValidFolder("Assets/Idles"))
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Idles" })) idles.Add(AssetDatabase.GUIDToAssetPath(g));
            // принудительный переимпорт: настройки импорта в NepIdleImport могли измениться
            foreach (var p in idles) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            if (idles.Count > 0) builds.Add(new AssetBundleBuild { assetBundleName = "nepidle.bundle", assetNames = idles.ToArray() });
            Debug.Log("NepFX: анимаций в Assets/Idles: " + idles.Count);
            var manifest = BuildPipeline.BuildAssetBundles(outDir, builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
            if (manifest == null) { Debug.LogError("NepFX: сборка не удалась"); if (Application.isBatchMode) EditorApplication.Exit(4); return; }
            Debug.Log("NepFX: готово -> " + Path.Combine(outDir, "nepfx.bundle"));
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
