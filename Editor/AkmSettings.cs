using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// 設定の永続化（要件 §7.1）。
    /// Assets/Asset-Keshichaumon-Nator/Settings.asset に保存する。
    /// </summary>
    internal class AkmSettings : ScriptableObject
    {
        public const string SettingsFolder = "Assets/Asset-Keshichaumon-Nator";
        public const string SettingsPath = SettingsFolder + "/Settings.asset";

        [Tooltip("アバタールートディレクトリ（Assets 配下のフォルダのアセットパス）")]
        public List<string> avatarRootDirectories = new List<string>();

        [Tooltip("個別に追加したルート（Prefab / Scene のアセットパス）")]
        public List<string> additionalRootAssets = new List<string>();

        [Tooltip("自動検出したが、ユーザーが除外したアバターのアセットパス")]
        public List<string> excludedAutoDetectedRoots = new List<string>();

        [Tooltip("ユーザーホワイトリスト（glob パターン）。マッチしたパスは常に保護する")]
        public List<string> userWhitelistGlobs = new List<string>();

        [Tooltip("導入単位フォルダとみなす深度（Assets からの階層数）。既定 2")]
        public int granularityDepth = 2;

        [Tooltip("最も浅いコンテンツフォルダを自動推定する（深度指定を無視）")]
        public bool autoEstimateGranularity = false;

        [Tooltip("前回スキャン日時")]
        public string lastScanTime = "";

        private static AkmSettings _cached;

        public static AkmSettings GetOrCreate()
        {
            if (_cached != null) return _cached;

            var settings = AssetDatabase.LoadAssetAtPath<AkmSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<AkmSettings>();
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    // Assets/Asset-Keshichaumon-Nator を作成
                    AssetDatabase.CreateFolder("Assets", "Asset-Keshichaumon-Nator");
                }
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            _cached = settings;
            return settings;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
