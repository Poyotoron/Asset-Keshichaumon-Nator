using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// 設定の永続化。
    /// Assets/zzz_pytr/Asset-Keshichaumon-Nator/Settings.asset に保存する。
    /// </summary>
    internal class AknSettings : ScriptableObject
    {
        /// <summary>設定フォルダ。Project ウィンドウの末尾側に並ぶよう、先頭に zzz_ を付けている。</summary>
        public const string SettingsFolder = "Assets/zzz_pytr/Asset-Keshichaumon-Nator";
        public const string SettingsPath = SettingsFolder + "/Settings.asset";

        /// <summary>0.3.0 以前の設定フォルダ。移行と保護のためだけに残す。</summary>
        public const string LegacySettingsFolder = "Assets/Asset-Keshichaumon-Nator";
        public const string LegacySettingsPath = LegacySettingsFolder + "/Settings.asset";

        [Tooltip("アバタールートディレクトリ（Assets 配下のフォルダのアセットパス）")]
        public List<string> avatarRootDirectories = new List<string>();

        [Tooltip("個別に追加したルート（Prefab / Scene のアセットパス）")]
        public List<string> additionalRootAssets = new List<string>();

        [Tooltip("自動検出したが、ユーザーが除外したアバターのアセットパス")]
        public List<string> excludedAutoDetectedRoots = new List<string>();

        [Tooltip("暗黙ルートのうち、ユーザーが起点から除外したアセットのパス")]
        public List<string> excludedImplicitRoots = new List<string>();

        [Tooltip("ユーザーホワイトリスト（glob パターン）。マッチしたパスは常に保護する")]
        public List<string> userWhitelistGlobs = new List<string>();

        [Tooltip("未使用スキャンの対象範囲（Assets 配下のフォルダのアセットパス）。空ならプロジェクト全体")]
        public List<string> scanScopeDirectories = new List<string>();

        [Tooltip("ツールの出力フォルダ（Assets 配下のフォルダのアセットパス）。ツール本体と切り離して判定する")]
        public List<string> toolOutputDirectories = new List<string>();

        [Tooltip("導入単位フォルダとみなす深度（Assets からの階層数）。既定 2")]
        public int granularityDepth = 2;

        [Tooltip("最も浅いコンテンツフォルダを自動推定する（深度指定を無視）")]
        public bool autoEstimateGranularity = true;

        [Tooltip("上級者向け: フォルダ集約を無効化し、純粋なファイル単位で列挙する。既定 OFF")]
        public bool fileUnitMode = false;

        [Tooltip("退避実行前に対象を .unitypackage としてエクスポートする。既定 OFF")]
        public bool exportUnityPackageBeforeRelocate = false;

        [Tooltip("キャッシュ掃除で Logs/ も削除対象に含める。既定 OFF")]
        public bool cacheCleanIncludeLogs = false;

        [Tooltip("前回キャッシュ掃除後にかかった再インポート時間（秒）。0 は未計測")]
        public double lastReimportSeconds = 0;

        [Tooltip("前回スキャン日時")]
        public string lastScanTime = "";

        private static AknSettings _cached;

        public static AknSettings GetOrCreate()
        {
            if (_cached != null) return _cached;

            var settings = AssetDatabase.LoadAssetAtPath<AknSettings>(SettingsPath);
            if (settings == null)
            {
                var legacySettings = AssetDatabase.LoadAssetAtPath<AknSettings>(LegacySettingsPath);
                if (legacySettings != null)
                {
                    EnsureSettingsFolder();
                    var moveError = AssetDatabase.MoveAsset(LegacySettingsPath, SettingsPath);
                    if (string.IsNullOrEmpty(moveError))
                    {
                        settings = AssetDatabase.LoadAssetAtPath<AknSettings>(SettingsPath);
                        if (settings == null)
                        {
                            // MoveAsset 自体は成功しているため、空の設定を作らず既存インスタンスを使う。
                            settings = legacySettings;
                            Debug.LogWarning("設定の移行後に新しいパスから読み直せませんでした。移行した設定をそのまま使います。");
                        }

                        if (AssetDatabase.IsValidFolder(LegacySettingsFolder) &&
                            !AssetDatabase.GetAllAssetPaths().Any(path =>
                                path.StartsWith(LegacySettingsFolder + "/")))
                        {
                            AssetDatabase.DeleteAsset(LegacySettingsFolder);
                        }
                        Debug.Log($"設定を {LegacySettingsPath} から {SettingsPath} へ移行しました。");
                    }
                    else
                    {
                        // 空の設定で上書きせず、次回起動時にもう一度移行を試す。
                        settings = legacySettings;
                        Debug.LogWarning(
                            $"設定の移行に失敗しました。旧パスの設定をそのまま使います: {moveError}");
                    }
                }

                if (settings == null)
                {
                    EnsureSettingsFolder();
                    settings = CreateInstance<AknSettings>();
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                    AssetDatabase.SaveAssets();
                }
            }
            _cached = settings;
            return settings;
        }

        private static void EnsureSettingsFolder()
        {
            const string authorFolder = "Assets/zzz_pytr";
            if (!AssetDatabase.IsValidFolder(authorFolder))
                AssetDatabase.CreateFolder("Assets", "zzz_pytr");
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.CreateFolder(authorFolder, "Asset-Keshichaumon-Nator");
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
