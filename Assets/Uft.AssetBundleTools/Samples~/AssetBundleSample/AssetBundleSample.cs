#nullable enable

using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Uft.AssetBundleTools.Samples
{
    [DefaultExecutionOrder(-100)]
    public class AssetBundleSample : MonoBehaviour
    {
        static AssetBundleHelper? helper;

        [SerializeField] Canvas? _canvas;
        [SerializeField] string _assetPathBase = "Assets/Samples/AssetBundleSample/AssetBundle/";
        [SerializeField] string _loadingRootPath = "Assets/StreamingAssets/AssetBundles/";

        void Start()
        {
            helper ??= new AssetBundleHelper(
                assetPathBase: this._assetPathBase,
                loadingRootPath: this._loadingRootPath,
                logInfo: (message) => Debug.Log(message),
                logError: (message) => Debug.LogError(message));

            var prefabA = helper.LoadFromAssetBundle<GameObject>($"{this._assetPathBase}AssetBundleSample/prefabA.prefab");
            var instanceA = Instantiate(prefabA);
            var a = instanceA!.GetComponent<RectTransform>();
            a.SetParent(this._canvas!.transform, false);
            a.anchoredPosition = new Vector2(-100, 0);

            var prefabB = helper.LoadFromAssetBundle<GameObject>("AssetBundleSample/prefabb");
            var instanceB = Instantiate(prefabB);
            var b = instanceB!.GetComponent<RectTransform>();
            b.SetParent(this._canvas.transform, false);
            b.anchoredPosition = new Vector2(0, -100);

            var ct = this.destroyCancellationToken;

            UniTask.Void(async () =>
            {
                await UniTask.Delay(1000, cancellationToken: ct);
                var prefabC = await helper.LoadFromAssetBundleAsync<GameObject>($"{this._assetPathBase}AssetBundleSample/prefabC.prefab", ct);
                var instanceC = Instantiate(prefabC);
                var c = instanceC!.GetComponent<RectTransform>();
                c.SetParent(this._canvas.transform, false);
                c.anchoredPosition = new Vector2(100, 0);
            });

            UniTask.Void(async () =>
            {
                await UniTask.Delay(2000, cancellationToken: ct);
                var prefabD = await helper.LoadFromAssetBundleAsync<GameObject>("AssetBundleSample/prefabd", ct);
                var instanceD = Instantiate(prefabD);
                var d = instanceD!.GetComponent<RectTransform>();
                d.SetParent(this._canvas.transform, false);
                d.anchoredPosition = new Vector2(0, 100);
            });
        }
    }
}
