#nullable enable

using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Uft.AssetBundleTools.Editor
{
    /// <summary>継承して各フィールドの値の初期値を入れたバージョンを用意すると便利です。</summary>
    public class AssetBundleToolsWindowBase : EditorWindow
    {
        // EditorWindow 定型文 =============================================

        const string TITLE = "Uft.AssetBundleTools";

        [MenuItem("Tools/" + TITLE, priority = 21062000 + 715, secondaryPriority = 10)]
        public static void Open() => Open<AssetBundleToolsWindowBase>();

        public static void Open<T>() where T : EditorWindow
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            T? window = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetType() == typeof(T))
                {
                    window = all[i];
                    break;
                }
            }
            if (window == null)
            {
                window = CreateInstance<T>();
            }
            window.Show();
            window.Focus();
        }

        // EditorWindow 内容 ===============================================

        protected string? inputBase;
        protected string? outputBase;
        protected string[]? platformOptions;
        protected int selectedPlatformIndex;
        protected AssetBundleHelper? assetBundleHelper;

        protected string? status;
        protected Color backGroundColor;
        bool _isRunning = false;

        protected virtual void OnDisable()
        {
            this.assetBundleHelper?.Dispose();
        }

        protected virtual void OnEnable()
        {
            this.titleContent.text = TITLE;
            this.inputBase = AssetBundleHelper.DEFAULT_INPUT_BASE;
            this.outputBase = AssetBundleHelper.DEFAULT_OUTPUT_BASE;
            var labelList = new List<string>();
            foreach (var (_, label) in AssetBundleHelper.PlatformFolders)
            {
                labelList.Add(label);
            }
            this.platformOptions = labelList.ToArray();
            this.selectedPlatformIndex = 0;
            this.assetBundleHelper = new AssetBundleHelper();
            this.status = "";
        }

        protected virtual void OnGUI()
        {
            this.minSize = new Vector2(600, 200);
            this.maxSize = new Vector2(1800, 200);
            using (new EditorGUI.DisabledGroupScope(this._isRunning))
            {
                EditorGUI.DrawRect(new Rect(0, 0, this.position.width, this.position.height), this.backGroundColor);

                GUILayout.Label("AssetBundle ビルドツール", EditorStyles.boldLabel);

                GUILayout.Space(10);

                this.inputBase = EditorGUILayout.TextField("Input Folder", this.inputBase);
                this.outputBase = EditorGUILayout.TextField("Output Folder", this.outputBase);

                GUILayout.Space(10);

                if (GUILayout.Button("Assign names"))
                {
                    UniTask.Void(async () =>
                    {
                        if (this._isRunning) return;
                        try
                        {
                            this._isRunning = true;
                            this.Repaint();
                            await this.assetBundleHelper!.AssignAssetBundleNamesAsync(this.inputBase!, s =>
                            {
                                this.status = s;
                                this.Repaint();
                            });
                            this.status = $"✅ 名前割り当て完了: {this.inputBase}";
                            Debug.Log(this.status);
                        }
                        catch (Exception ex)
                        {
                            this.status = $"💥{ex.Message}";
                            Debug.LogError(this.status);
                        }
                        finally
                        {
                            this._isRunning = false;
                            this.Repaint();
                        }
                    });
                }

                using (new GUILayout.HorizontalScope())
                {
                    this.selectedPlatformIndex = EditorGUILayout.Popup(this.selectedPlatformIndex, this.platformOptions!);
                    if (GUILayout.Button("Build", GUILayout.ExpandWidth(false)))
                    {
                        var folder = this.platformOptions![this.selectedPlatformIndex];
                        var target = AssetBundleHelper.PlatformFolders.First(kvp => kvp.Value == folder).Key;
                        try
                        {
                            // 同期処理のため Repaint() はビルド完了まで反映されません（エディタが固まります）
                            this.status = $"ビルド中... {folder}";
                            this.assetBundleHelper!.Build(target, this.outputBase, folder);
                            this.status = $"✅ ビルド完了: {folder}";
                            Debug.Log(this.status);
                        }
                        catch (Exception ex)
                        {
                            this.status = $"💥{ex.Message}";
                            Debug.LogError(this.status);
                        }
                    }
                } // HorizontalScope
            }
            GUILayout.Space(10);
            if (GUILayout.Button("Clear status")) this.status = "";
            GUILayout.Label(this.status, EditorStyles.boldLabel);
        }
    }
}
