using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Diz.DependencyManager;
using EFT;
using UnityEngine;
using HarmonyLib;
using NLog;
using static System.Threading.Tasks.Task;

namespace BundleLoader
{
    [Obfuscation(Exclude = true)]
    public class Loader : MelonMod
    {
        private static readonly Dictionary<string, string> CachedBundles = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> ModdedAssets = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> ModdedBundlePaths = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> ManifestCache = new Dictionary<string, string>();

        private static Type _loaderType;
        private static ConstructorInfo _bundleLockConstructor;
        private static PropertyInfo _loadState;
        private static PropertyInfo _loadStateProperty;
        private static FieldInfo _bundleField;
        private static FieldInfo _taskField;

        [Obfuscation(Feature = "constants,anti ildasm,ctrl flow,anti debug,ref proxy")]
        public override void OnApplicationStart()
        {

            var bundlesFolder = Path.Combine(AppContext.BaseDirectory, "Mods/Bundles/Local");

            if (!Directory.Exists(bundlesFolder))
                Directory.CreateDirectory(bundlesFolder);

            var files = Directory.GetFiles(bundlesFolder, "*.bundle", SearchOption.AllDirectories);

            foreach (var fileName in files)
            {
                var fullPath = Path.Combine(bundlesFolder, fileName).Replace('/', '\\');
                AssetBundle customBundle = null;
                try
                {
                    customBundle = AssetBundle.LoadFromFile(fullPath);
                    var assets = customBundle.GetAllAssetNames();
                    var bundlePath = Path.Combine(AppContext.BaseDirectory,
                        "EscapeFromTarkov_Data/StreamingAssets/Windows/", customBundle.name);
                    if (!File.Exists(bundlePath))
                    {
                        CachedBundles.Add(customBundle.name, fullPath);
                        MelonLogger.Log("Cached modded bundle " + customBundle.name);
                        var manifestPath = fullPath + ".manifest";
                        if (File.Exists(manifestPath))
                        {
                            ManifestCache.Add(customBundle.name, manifestPath);
                            MelonLogger.Log("Cached manifest for " + customBundle.name);
                        }
                    }
                    else
                    {
                        foreach (var assetName in assets)
                        {
                            //if (!moddedAssets.ContainsKey(assetName))
                            //    moddedAssets.Add(assetName, fullPath);
                            if (!ModdedAssets.ContainsKey(customBundle.name))
                                ModdedAssets.Add(customBundle.name, new List<string>());
                            if (!ModdedBundlePaths.ContainsKey(customBundle.name))
                                ModdedBundlePaths.Add(customBundle.name, fullPath);
                            if (!ModdedAssets[customBundle.name].Contains(assetName))
                                ModdedAssets[customBundle.name].Add(assetName);
                        }
                        MelonLogger.Log("Cached modded assets for " + customBundle.name);
                    }


                }
                catch (Exception e)
                {
                    MelonLogger.Log("Failed to Load modded bundle " + fullPath + ": " + e);
                }

                try { customBundle.Unload(true); } catch { }
            }
            DoPatching();
            base.OnApplicationStart();
        }

        public void DoPatching()
        {
            var harmony = new HarmonyLib.Harmony("com.configfreaks.bundlepatcher");


            var assembly = typeof(AbstractGame).Assembly;
            var types = assembly.GetTypes();

            var nodeInterfaceType = types.First(x => x.IsInterface && x.GetProperty("SameNameAsset") != null);

            _loaderType = types.First(x => x.IsClass && x.GetProperty("SameNameAsset") != null);
            _bundleLockConstructor = types.First(x => x.IsClass && x.GetProperty("MaxConcurrentOperations") != null).GetConstructors().First();
            _loadState = _loaderType.GetProperty("LoadState");
            _loadStateProperty = _loadState.PropertyType.GetProperty("Value");
            _bundleField = _loaderType.GetField("assetBundle_0", BindingFlags.Instance | BindingFlags.NonPublic);
            _taskField = _loaderType.GetField("task_0", BindingFlags.Instance | BindingFlags.NonPublic);

            var originalLoader = _loaderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).First(x => x.GetParameters().Length == 0 && x.ReturnType == typeof(Task));
            var _loaderConstructor = _loaderType.GetConstructors().First();
            var getNodeType = types
                .First(x => x.IsClass && x.GetMethod("GetNode") != null && string.IsNullOrWhiteSpace(x.Namespace)).MakeGenericType(nodeInterfaceType);
            var originalGetNodeConstructor = getNodeType.GetConstructors().First();

            var loaderPrefix = typeof(Loader).GetMethod("LoaderPrefix", BindingFlags.Static | BindingFlags.Public);
            var loaderConstructor =
                typeof(Loader).GetMethod(nameof(LoaderConstructor), BindingFlags.Static | BindingFlags.Public);
            var getNodeContructor =
                typeof(Loader).GetMethod(nameof(NodeConstructor), BindingFlags.Static | BindingFlags.Public);

            harmony.Patch(originalLoader, new HarmonyMethod(loaderPrefix));
            harmony.Patch(_loaderConstructor, new HarmonyMethod(loaderConstructor));
            harmony.Patch(originalGetNodeConstructor, new HarmonyMethod(getNodeContructor));

        }

        public static void CacheServerBundles()
        {
            try
            {
                var serverBundles = RequestManager.GetServerBundles();
                foreach (var bundle in serverBundles)
                {
                    var localPath = bundle.GetLocalBundlePath();
                    AssetBundle customBundle = null;


                    if (bundle.dependencyKeys.Length > 0)
                        File.WriteAllLines(localPath + ".manifest", bundle.dependencyKeys);

                    try
                    {
                        customBundle = AssetBundle.LoadFromFile(localPath);
                        var bundlePath = Path.Combine(AppContext.BaseDirectory,
                            "EscapeFromTarkov_Data/StreamingAssets/Windows/", customBundle.name);
                        if (!File.Exists(bundlePath))
                        {
                            CachedBundles.Add(customBundle.name, localPath);
                            MelonLogger.Log("Cached modded bundle " + customBundle.name);
                            var manifestPath = localPath + ".manifest";
                            if (File.Exists(manifestPath))
                            {
                                ManifestCache.Add(customBundle.name, manifestPath);
                                MelonLogger.Log("Cached manifest for " + customBundle.name);
                            }
                        }

                        else
                        {
                            var assets = customBundle.GetAllAssetNames();
                            foreach (var assetName in assets)
                            {
                                if (!ModdedAssets.ContainsKey(customBundle.name))
                                    ModdedAssets.Add(customBundle.name, new List<string>());
                                if (!ModdedBundlePaths.ContainsKey(customBundle.name))
                                    ModdedBundlePaths.Add(customBundle.name, localPath);
                                if (!ModdedAssets[customBundle.name].Contains(assetName))
                                    ModdedAssets[customBundle.name].Add(assetName);
                            }

                            MelonLogger.Log("Cached modded assets for " + customBundle.name);
                        }
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Log("Failed to Load modded bundle " + localPath + ": " + e);
                    }

                    try
                    {
                        customBundle.Unload(true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Log(e.ToString());
            }
        }

        public static bool NodeConstructor(ref object[] loadables, string defaultKey, [JetBrains.Annotations.CanBeNull] Func<string, bool> shouldExclude)
        {
            CacheServerBundles();
            var newInstances = new List<object>();

            foreach (var bundle in CachedBundles)
            {
                var bundleLock = _bundleLockConstructor.Invoke(new object[] { 1 });
                var loaderInstance =
                    Activator.CreateInstance(_loaderType, bundle.Key, string.Empty, null, bundleLock);
                SetPropertyValue(loaderInstance,
                    ManifestCache.ContainsKey(bundle.Key)
                        ? File.ReadAllLines(ManifestCache[bundle.Key])
                        : new string[] { }, "DependencyKeys");
                newInstances.Add(loaderInstance);
                MelonLogger.Log("Adding custom bundle " + bundle.Key + " to the game");
            }

            loadables = loadables.Concat(newInstances.ToArray()).ToArray();

            return true;
        }

        public static bool LoaderConstructor(object __instance, string key, string rootPath, AssetBundleManifest manifest, GInterface251 bundleLock, ref string ___string_1, ref string ___string_0, ref Task ___task_0, ref GInterface251 ___ginterface251_0)
        {
            SetPropertyValue(__instance, key, "Key");
            ___string_1 = rootPath + key;
            ___string_0 = Path.GetFileNameWithoutExtension(key);
            if (manifest != null)
                SetPropertyValue(__instance, manifest.GetDirectDependencies(key), "DependencyKeys");
            SetPropertyValue(__instance, new GClass2166<ELoadState>(ELoadState.Unloaded), "LoadState");
            ___task_0 = null;
            ___ginterface251_0 = bundleLock;

            return false;
        }

        public static bool LoggerPrefix(string nlogFormat, string unityFormat, LogLevel logLevel, params object[] args)
        {
            MelonLogger.Log($"[{logLevel.Name}] {string.Format(nlogFormat, args)}");
            return true;
        }

        // ReSharper disable once UnusedMember.Global
        [Obfuscation(Exclude = false)]
        public static bool LoaderPrefix(object __instance, GInterface251 ___ginterface251_0, bool ___bool_0, ref Task ___task_0, ref string ___string_1, string ___string_0, ref Task __result)
        {
            try
            {
                if (!File.Exists(___string_1) && CachedBundles.ContainsKey(___string_1))
                    ___string_1 = CachedBundles[___string_1];

                ___task_0 = LoadTarkovBundle(__instance, ___ginterface251_0, ___bool_0, ___string_1, ___string_0);
                __result = ___task_0;
            }
            catch (Exception e)
            {
                MelonLogger.Log(e.ToString());
            }

            return false;
        }

        private static string GetObjectTree(object instance, int depth = -1)
        {
            var instanceType = instance.GetType();
            var builder = new StringBuilder(instanceType.Name + "\n");
            var props = instanceType.GetProperties();
            var fields = instanceType.GetFields();
            foreach (var prop in props)
            {
                //MelonLogger.Log(instanceType.Name + " " + prop.PropertyType.Name + " " + prop.Name);
                try
                {
                    var memberType = prop.PropertyType;
                    object value = null;
                    try
                    {
                        value = prop.GetMethod == null ? "No GET accessor" : prop.GetValue(instance);
                    }
                    catch { }
                    if (value == instance)
                        continue;

                    if (value == null || memberType == typeof(string) || Convert.ToString(value) != Convert.ToString(value.GetType()) || depth == 0)
                    {
                        builder.AppendLine($"{memberType.Name} {prop.Name} = {value}");
                    }
                    else if (value is IEnumerable enumVal)
                    {
                        builder.AppendLine($"{memberType.Name} {prop.Name} = [");
                        foreach (var item in enumVal)
                        {
                            var itemType = item.GetType();
                            if (Convert.ToString(item) != Convert.ToString(item.GetType()))
                            {
                                builder.Append($"\n\t{Convert.ToString(item)}");
                            }
                            else if (itemType.GetProperties().Length > 0 || itemType.GetFields().Length > 0)
                            {
                                var subTree = GetObjectTree(item, depth - 1);
                                var indentedTree = string.Join("\n", subTree.Split('\n').Select(x => "\t" + x));
                                builder.Append(indentedTree);
                            }
                        }

                        builder.AppendLine("\n]");
                    }
                    else if (memberType.GetProperties().Length > 0 || memberType.GetFields().Length > 0)
                    {
                        builder.AppendLine($"{memberType.Name} {prop.Name} = ");
                        var subTree = GetObjectTree(value, depth - 1);
                        var indentedTree = string.Join("\n", subTree.Split('\n').Select(x => "\t" + x));
                        builder.AppendLine(indentedTree);
                    }
                }
                catch (Exception e)
                {

                    MelonLogger.LogError(e.ToString());
                }
            }

            foreach (var field in fields)
            {
                //MelonLogger.Log(instanceType.Name + " " + field.FieldType.Name + " " + field.Name);
                try
                {
                    var memberType = field.FieldType;
                    object value = null;
                    try
                    {
                        value = field.GetValue(instance);
                    }
                    catch { }

                    if (value == instance)
                        continue;
                    if (value == null || memberType == typeof(string) || Convert.ToString(value) != Convert.ToString(value.GetType()) || depth == 0)
                    {
                        builder.AppendLine($"{memberType.Name} {field.Name} = {value}");
                    }
                    else if (value is IEnumerable enumVal)
                    {
                        builder.AppendLine($"{memberType.Name} {field.Name} = [");
                        foreach (var item in enumVal)
                        {
                            var itemType = item.GetType();
                            if (Convert.ToString(item) != Convert.ToString(item.GetType()))
                            {
                                builder.Append("\n\t" + Convert.ToString(item));
                            }
                            else if (itemType.GetProperties().Length > 0 || itemType.GetFields().Length > 0)
                            {
                                var subTree = GetObjectTree(item, depth - 1);
                                var indentedTree = string.Join("\n", subTree.Split('\n').Select(x => "\t" + x));
                                builder.Append(indentedTree);
                            }
                        }

                        builder.AppendLine("\n]");
                    }
                    else if (value != null &&
                             (memberType.GetProperties().Length > 0 || memberType.GetFields().Length > 0))
                    {
                        builder.AppendLine($"{memberType.Name} {field.Name} = ");
                        var subTree = GetObjectTree(value, depth - 1);
                        var indentedTree = string.Join("\n", subTree.Split('\n').Select(x => "\t" + x));
                        builder.AppendLine(indentedTree);
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.LogError(e.ToString());
                }
            }

            return builder.ToString();
        }
        [ObfuscationAttribute(Exclude = false)]
        private static async Task LoadTarkovBundle(object instance, GInterface251 gInterface239, bool bool_0, string bundleFilePath, string bundleVirtualPath)
        {
            var key = GetPropertyValue(instance, "Key").ToString();
            var loadStateInstance = _loadState.GetValue(instance);

            while (gInterface239.IsLocked)
            {
                if (!bool_0)
                {
                    _taskField.SetValue(instance, null);
                    return;
                }

                await Delay(100);
            }

            if (!bool_0)
            {
                _taskField.SetValue(instance, null);
                return;
            }

            gInterface239.Lock();

            if (ModdedAssets.ContainsKey(key))
            {


                MelonLogger.Log($"Patching bundle {key} with modded assets...");

                //AssetBundle assetBundle;
                var request = AssetBundle.LoadFromFileAsync(ModdedBundlePaths[key]);
                if (request == null)
                {
                    MelonLogger.LogError("Error: could not load bundle: " + key);
                    _loadStateProperty.SetValue(loadStateInstance, ELoadState.Failed);
                    gInterface239.Unlock();
                    _taskField.SetValue(instance, null);
                    return;
                }

                while (!request.isDone && request.progress < 1)
                {
                    SetPropertyValue(instance, request.progress * 0.5f, "Progress");
                    await Delay(100);
                }

                var assetBundle = request.assetBundle;

                if (assetBundle == null)
                {
                    MelonLogger.LogError("Error: could not load bundle: " + key +
                                         " at path " + bundleFilePath);
                    _loadStateProperty.SetValue(loadStateInstance, ELoadState.Failed);
                    gInterface239.Unlock();
                    SetPropertyValue(instance, 0f, "Progress");
                    _taskField.SetValue(instance, null);
                    return;
                }

                SetPropertyValue(instance, 0.5f, "Progress");

                var assetsRequest = assetBundle.LoadAllAssetsAsync();
                while (!assetsRequest.isDone && assetsRequest.progress < 1)
                    await Delay(100);
                var assetsList = assetsRequest.allAssets;

                SetPropertyValue(instance, 0.9f, "Progress");

                var sameName = assetsList.Where(x => x.name == bundleVirtualPath);
                if (sameName.Any())
                    SetPropertyValue(instance, sameName.First(), "SameNameAsset");


                SetPropertyValue(instance, assetsList.ToArray(), "Assets");
                _loadStateProperty.SetValue(loadStateInstance, ELoadState.Loaded);
                gInterface239.Unlock();
                SetPropertyValue(instance, 1f, "Progress");
                _taskField.SetValue(instance, null);
                _bundleField.SetValue(instance, assetBundle);
            }
            else
            {
                var request = AssetBundle.LoadFromFileAsync(bundleFilePath);
                if (request == null)
                {
                    MelonLogger.LogError("Error: could not load bundle: " + key);
                    _loadStateProperty.SetValue(loadStateInstance, ELoadState.Failed);
                    gInterface239.Unlock();
                    _taskField.SetValue(instance, null);
                    return;
                }

                while (!request.isDone && request.progress < 1)
                {
                    SetPropertyValue(instance, request.progress * 0.5f, "Progress");
                    await Delay(100);
                }

                var assetBundle = request.assetBundle;

                if (assetBundle == null)
                {
                    MelonLogger.LogError("Error: could not load bundle: " + key +
                                         " at path " + bundleFilePath);
                    _loadStateProperty.SetValue(loadStateInstance, ELoadState.Failed);
                    gInterface239.Unlock();
                    SetPropertyValue(instance, 0f, "Progress");
                    _taskField.SetValue(instance, null);
                    return;
                }

                SetPropertyValue(instance, 0.5f, "Progress");
                var assetBundleRequest = assetBundle.LoadAllAssetsAsync();
                while (!assetBundleRequest.isDone && assetBundleRequest.progress < 1)
                {
                    SetPropertyValue(instance, 0.5f + assetBundleRequest.progress * 0.4f, "Progress");
                    await Delay(100);
                }

                SetPropertyValue(instance, assetBundleRequest.allAssets, "Assets");

                foreach (var asset in assetBundleRequest.allAssets)
                {
                    if (asset.name != bundleVirtualPath) continue;
                    SetPropertyValue(instance, asset, "SameNameAsset");
                    _taskField.SetValue(instance, null);
                    break;
                }

                _loadStateProperty.SetValue(loadStateInstance, ELoadState.Loaded);
                gInterface239.Unlock();
                SetPropertyValue(instance, 1f, "Progress");
                _taskField.SetValue(instance, null);
                _bundleField.SetValue(instance, assetBundle);
            }
        }

        private static object GetPropertyValue(object instance, string propName)
        {
            var prop = instance.GetType().GetProperty(propName);
            return prop.GetValue(instance);
        }

        private static void SetPropertyValue(object instance, object value, string propName)
        {
            if (string.IsNullOrWhiteSpace(propName))
                return;

            var prop = instance.GetType().GetProperty(propName);
            prop.SetValue(instance, value);
        }
    }
}
