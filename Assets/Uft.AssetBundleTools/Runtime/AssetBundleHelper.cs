#nullable enable

using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Uft.AssetBundleTools
{
    public class AssetBundleHelper : IDisposable
    {
        public const string DEFAULT_ASSET_PATH_BASE = "Assets/AssetBundle/";
        public static string DefaultBundleNameResolver(string assetPath, string assetPathBase)
        {
            var ext = Path.GetExtension(assetPath).ToLower();

            // NOTE: シーンと非シーンは同一 AssetBundle に混在不可。シーンは全て一つにまとめる
            if (ext == ".unity") return "scenes";

            // NOTE: assetPathBase あるいは Assets/ はバンドル名の考慮から外す
            if (!assetPathBase.EndsWith("/")) assetPathBase += "/";
            if (assetPath.StartsWith(assetPathBase, StringComparison.OrdinalIgnoreCase))
                assetPath = assetPath[assetPathBase.Length..];
            else if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                assetPath = assetPath["Assets/".Length..];

            var firstSlash = assetPath.IndexOf('/');
            return (firstSlash >= 0 ? assetPath[..firstSlash] : "assets").ToLower();
        }
        public static readonly string DEFAULT_LOADING_ROOT_PATH = Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        public static readonly Dictionary<RuntimePlatform, string> DefaultPlatformFolderNames = new()
        {
            { RuntimePlatform.WindowsEditor, "Windows" },
            { RuntimePlatform.WindowsPlayer, "Windows" },
            { RuntimePlatform.OSXEditor,     "OSX" },
            { RuntimePlatform.OSXPlayer,     "OSX" },
            { RuntimePlatform.LinuxEditor,   "Linux" },
            { RuntimePlatform.LinuxPlayer,   "Linux" },
            { RuntimePlatform.IPhonePlayer,  "iOS" },
            { RuntimePlatform.Android,       "Android" },
            { RuntimePlatform.PS4,           "PS4" },
            { RuntimePlatform.PS5,           "PS5" },
            { RuntimePlatform.Switch,        "Switch" },
        };
#if UNITY_EDITOR
        public static readonly Dictionary<BuildTarget, string> DefaultBuildTargetFolderNames = new()
        {
            { BuildTarget.StandaloneWindows64, "Windows" },
            { BuildTarget.StandaloneOSX,       "OSX"     },
            { BuildTarget.StandaloneLinux64,   "Linux"   },
            { BuildTarget.iOS,                 "iOS"     },
            { BuildTarget.Android,             "Android" },
            { BuildTarget.PS4,                 "PS4"     },
            { BuildTarget.PS5,                 "PS5"     },
            { BuildTarget.Switch,              "Switch"  },
        };
#endif

        static readonly string NAME_TAG = $"[{nameof(AssetBundleHelper)}]";

        protected readonly string _assetPathBase;
        protected readonly Func<string, string, string> _bundleNameResolver;
        protected readonly string _loadingRootPath;
        protected readonly Dictionary<RuntimePlatform, string> _platformFolderNames;
        protected readonly Action<string>? _logInfo;
        protected readonly Action<string>? _logError;

        protected readonly Dictionary<string, AssetBundle> _bundleCache = new();
        protected readonly Dictionary<string, Dictionary<string, string>> _assetNameMapCache = new();
        readonly Dictionary<string, UniTask<AssetBundle?>> _loadingTasks = new();

        public AssetBundleHelper(
            string? assetPathBase = null,
            Func<string, string, string>? bundleNameResolver = null,
            string? loadingRootPath = null,
            Dictionary<RuntimePlatform, string>? platformFolderNames = null,
            Action<string>? logInfo = null,
            Action<string>? logError = null)
        {
            this._assetPathBase = (assetPathBase ?? DEFAULT_ASSET_PATH_BASE).TrimEnd('/') + "/";
            this._bundleNameResolver = bundleNameResolver ?? DefaultBundleNameResolver;
            this._loadingRootPath = loadingRootPath ?? DEFAULT_LOADING_ROOT_PATH;
            this._platformFolderNames = platformFolderNames ?? DefaultPlatformFolderNames;
            this._logInfo = logInfo;
            this._logError = logError;
        }

        public void Dispose()
        {
            foreach (var bundle in this._bundleCache.Values)
            {
                if (bundle != null)
                {
                    bundle.Unload(false);
                }
            }
            this._bundleCache.Clear();
        }

#if UNITY_EDITOR
        // Assign & build

        public async UniTask<int> AssignBundleNamesAsync(Action<string>? onProgress = null)
        {
            var guids = AssetDatabase.FindAssets("", new[] { this._assetPathBase.TrimEnd('/') });
            int total = guids.Length;
            const int progressYieldSize = 100;
            int updateCount = 0;

            for (int i = 0; i < total; i++)
            {
                if ((i + 1) % progressYieldSize == 0)
                {
                    onProgress?.Invoke($"名前割り当て中... {i + 1}/{total}");
                    await UniTask.Yield();
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetDatabase.IsValidFolder(assetPath)) continue;

                var ext = Path.GetExtension(assetPath).ToLower();
                if (ext == ".cs") continue;

                var bundleName = this._bundleNameResolver(assetPath, this._assetPathBase);
                var importer   = AssetImporter.GetAtPath(assetPath);
                if (importer == null || importer.assetBundleName == bundleName) continue;

                importer.assetBundleName = bundleName;
                updateCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.RemoveUnusedAssetBundleNames();
            this._logInfo?.Invoke($"{NAME_TAG} {updateCount} assets updated.");
            return updateCount;
        }

        /// <summary>BuildPipeline.BuildAssetBundles はメインスレッド専用同期API かつ 非同期版はない。そのためこちらは同期メソッド</summary>
        public void Build(BuildTarget buildTarget, string? outputBase, string platformFolder)
        {
            var outputPath = Path.Combine(outputBase ?? ".", platformFolder);
            Directory.CreateDirectory(outputPath);
            BuildPipeline.BuildAssetBundles(outputPath, BuildAssetBundleOptions.None, buildTarget);
            AssetDatabase.Refresh();
            this._logInfo?.Invoke($"{NAME_TAG} Build complete: {outputPath}");
        }
#endif

        // Load & use

        public T? LoadFromAssetBundle<T>(string assetPath)
            where T : UnityEngine.Object
        {
            AssetBundle? GetOrLoadBundle(string bundleName)
            {
                if (this._bundleCache.TryGetValue(bundleName, out var cached)) return cached;
                var bundlePath = Path.Combine(this._loadingRootPath, this.GetPlatformFolder(), bundleName);
                this._logInfo?.Invoke($"{NAME_TAG} Loading bundle: {bundlePath}");
                return this.RegisterLoadedBundle(bundleName, AssetBundle.LoadFromFile(bundlePath), bundlePath);
            }

            var bundleName = this._bundleNameResolver(assetPath, this._assetPathBase);
            var bundle = GetOrLoadBundle(bundleName);
            if (bundle == null) return null;
            var fullName = this.ResolveAssetName(bundleName, assetPath);
            return bundle.LoadAsset<T>(fullName);
        }

        public async UniTask<T?> LoadFromAssetBundleAsync<T>(string assetPath, CancellationToken ct)
            where T : UnityEngine.Object
        {
            UniTask<AssetBundle?> GetOrLoadBundleAsync(string bundleName, CancellationToken ct)
            {
                if (this._bundleCache.TryGetValue(bundleName, out var cached)) return UniTask.FromResult<AssetBundle?>(cached);
                if (this._loadingTasks.TryGetValue(bundleName, out var ongoing)) return ongoing;

                async UniTask<AssetBundle?> LoadAsync(string bundleName, CancellationToken ct)
                {
                    try
                    {
                        var bundlePath = Path.Combine(this._loadingRootPath, this.GetPlatformFolder(), bundleName);
                        this._logInfo?.Invoke($"{NAME_TAG} Loading bundle: {bundlePath}");
                        return this.RegisterLoadedBundle(bundleName, await AssetBundle.LoadFromFileAsync(bundlePath).ToUniTask(cancellationToken: ct), bundlePath);
                    }
                    finally
                    {
                        this._loadingTasks.Remove(bundleName);
                    }
                }

                var task = LoadAsync(bundleName, ct).Preserve(); // NOTE: Preserve() で多重await対応
                this._loadingTasks[bundleName] = task;
                return task;
            }

            var bundleName = this._bundleNameResolver(assetPath, this._assetPathBase);
            var bundle = await GetOrLoadBundleAsync(bundleName, ct);
            if (bundle == null) return null;
            var fullName = this.ResolveAssetName(bundleName, assetPath);
            return (T?)await bundle.LoadAssetAsync<T>(fullName).ToUniTask(cancellationToken: ct);
        }

        AssetBundle? RegisterLoadedBundle(string bundleName, AssetBundle? bundle, string bundlePath)
        {
            if (bundle != null)
            {
                this.CreateAssetNameCache(bundleName, bundle);
                this._bundleCache[bundleName] = bundle;
            }
            else
            {
                this._logError?.Invoke($"{NAME_TAG} Failed to load bundle: {bundlePath}");
            }
            return bundle;
        }

        protected virtual void CreateAssetNameCache(string bundleName, AssetBundle bundle)
        {
            var prefix = this._assetPathBase;
            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fullName in bundle.GetAllAssetNames())
            {
                nameMap[fullName] = fullName;
                nameMap[Path.ChangeExtension(fullName, null)] = fullName;

                if (fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var withoutPrefix = fullName[prefix.Length..];
                    nameMap[withoutPrefix] = fullName;
                    nameMap[Path.ChangeExtension(withoutPrefix, null)] = fullName;
                }
            }
            this._assetNameMapCache[bundleName] = nameMap;
        }

        protected virtual string GetPlatformFolder()
        {
            return this._platformFolderNames.TryGetValue(Application.platform, out var folder)
                ? folder
                : Application.platform.ToString();
        }

        protected virtual string ResolveAssetName(string bundleName, string assetPath)
        {
            if (this._assetNameMapCache.TryGetValue(bundleName, out var nameMap) && nameMap.TryGetValue(assetPath, out var fullName))
                return fullName;
            this._logError?.Invoke($"{NAME_TAG} Asset not found in nameMap: {assetPath} (bundle: {bundleName})");
            return assetPath;
        }
    }
}
