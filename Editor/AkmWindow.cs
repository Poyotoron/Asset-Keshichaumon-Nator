using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// メインウィンドウ（要件 §7）。Roots / Protection / Scan Result の3セクション。
    /// スキャンは常に非破壊。退避は明示的なボタン + 確認ダイアログでのみ実行する。
    /// </summary>
    public class AkmWindow : EditorWindow
    {
        private enum Tab { Roots, Protection, Scan, Duplicates, Cache }

        private AkmSettings _settings;
        private Tab _tab = Tab.Roots;
        private Vector2 _scroll;

        // Roots
        private Object _individualToAdd;
        private List<string> _autoDetected = new List<string>();
        private bool _autoDetectRun;

        // Protection
        private string _newGlob = "";

        // Scan
        private ScanResult _result;
        private bool _scanBlocked;

        // Duplicates
        private DuplicateReport _dupReport;

        // Cache
        private List<CacheClean.CacheTarget> _cacheTargets;
        private bool _cacheMeasured;
        private long _cacheLibSize;
        private int _cacheAssetCount;
        private long _cacheAssetSize;

        [MenuItem(AkmStrings.MenuPath)]
        private static void Open()
        {
            var window = GetWindow<AkmWindow>();
            window.titleContent = new GUIContent(AkmStrings.WindowTitle);
            window.minSize = new Vector2(640, 480);
            window.Show();
        }

        private void OnEnable()
        {
            _settings = AkmSettings.GetOrCreate();
        }

        private void OnGUI()
        {
            if (_settings == null) _settings = AkmSettings.GetOrCreate();

            EditorGUILayout.LabelField(AkmStrings.ToolName, EditorStyles.boldLabel);
            DrawTabBar();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Roots: DrawRootsTab(); break;
                case Tab.Protection: DrawProtectionTab(); break;
                case Tab.Scan: DrawScanTab(); break;
                case Tab.Duplicates: DrawDuplicatesTab(); break;
                case Tab.Cache: DrawCacheTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTabBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _tab = (Tab)GUILayout.Toolbar((int)_tab, new[]
                {
                    AkmStrings.TabRoots, AkmStrings.TabProtection, AkmStrings.TabScan,
                    AkmStrings.TabDuplicates, AkmStrings.TabCache,
                });
            }
        }

        // ------------------------------------------------------------------ Roots

        private void DrawRootsTab()
        {
            EditorGUILayout.LabelField(AkmStrings.RootsHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.RootsHelp, MessageType.Info);

            DrawDropArea();

            if (GUILayout.Button(AkmStrings.RootsSelectFolder))
            {
                var abs = EditorUtility.OpenFolderPanel(AkmStrings.RootsSelectFolder, Application.dataPath, "");
                var assetPath = AbsToAssetPath(abs);
                if (assetPath != null) AddAvatarRootDir(assetPath);
            }

            // 登録済みディレクトリ一覧
            EditorGUILayout.Space();
            if (_settings.avatarRootDirectories.Count == 0)
            {
                EditorGUILayout.LabelField("（未登録）", EditorStyles.miniLabel);
            }
            for (int i = _settings.avatarRootDirectories.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(_settings.avatarRootDirectories[i]);
                    if (GUILayout.Button(AkmStrings.RemoveButton, GUILayout.Width(60)))
                    {
                        _settings.avatarRootDirectories.RemoveAt(i);
                        _settings.Save();
                    }
                }
            }

            // 個別ファイル追加（F-ROOT-03）
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.RootsAddIndividualHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.RootsAddIndividualHelp, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                _individualToAdd = EditorGUILayout.ObjectField(_individualToAdd, typeof(Object), false);
                using (new EditorGUI.DisabledScope(_individualToAdd == null))
                {
                    if (GUILayout.Button("追加", GUILayout.Width(60)))
                    {
                        var p = AssetDatabase.GetAssetPath(_individualToAdd);
                        var ext = AkmUtil.Ext(p);
                        if (ext == ".prefab" || ext == ".unity")
                        {
                            if (!_settings.additionalRootAssets.Contains(p))
                            {
                                _settings.additionalRootAssets.Add(p);
                                _settings.Save();
                            }
                            _individualToAdd = null;
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(AkmStrings.ToolName,
                                "Prefab または Scene(.unity) を指定してください。", AkmStrings.Ok);
                        }
                    }
                }
            }
            for (int i = _settings.additionalRootAssets.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(_settings.additionalRootAssets[i]);
                    if (GUILayout.Button(AkmStrings.RemoveButton, GUILayout.Width(60)))
                    {
                        _settings.additionalRootAssets.RemoveAt(i);
                        _settings.Save();
                    }
                }
            }

            // 自動検出（F-ROOT-02）
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.RootsAutoDetectHeader, EditorStyles.boldLabel);
            if (GUILayout.Button(AkmStrings.RootsAutoDetectButton))
            {
                _autoDetected = AvatarAutoDetector.FindAvatarPrefabs();
                _autoDetectRun = true;
            }
            if (_autoDetectRun)
            {
                if (_autoDetected.Count == 0)
                {
                    EditorGUILayout.HelpBox(AkmStrings.RootsAutoDetectNone, MessageType.None);
                }
                else
                {
                    EditorGUILayout.LabelField(AkmStrings.RootsAutoDetectExcludeHint, EditorStyles.miniLabel);
                    var excluded = _settings.excludedAutoDetectedRoots;
                    foreach (var p in _autoDetected)
                    {
                        bool included = !excluded.Contains(p);
                        bool newIncluded = EditorGUILayout.ToggleLeft(p, included);
                        if (newIncluded != included)
                        {
                            if (newIncluded) excluded.Remove(p);
                            else if (!excluded.Contains(p)) excluded.Add(p);
                            _settings.Save();
                        }
                    }
                }
            }
        }

        private void DrawDropArea()
        {
            var rect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            GUI.Box(rect, AkmStrings.RootsDropArea, EditorStyles.helpBox);

            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        var p = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p))
                        {
                            AddAvatarRootDir(p);
                        }
                    }
                }
                evt.Use();
            }
        }

        private void AddAvatarRootDir(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                EditorUtility.DisplayDialog(AkmStrings.ToolName,
                    "フォルダを指定してください: " + assetPath, AkmStrings.Ok);
                return;
            }
            if (!_settings.avatarRootDirectories.Contains(assetPath))
            {
                _settings.avatarRootDirectories.Add(assetPath);
                _settings.Save();
            }
        }

        // ------------------------------------------------------------- Protection

        private void DrawProtectionTab()
        {
            EditorGUILayout.LabelField(AkmStrings.ProtHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.ProtHelp, MessageType.Info);

            EditorGUILayout.LabelField(AkmStrings.ProtStructureHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "・.cs / .asmdef / .asmref / .dll / .shader / .hlsl / .cginc / .shadergraph を含む\n" +
                "・package.json / *.vpm.json を含む\n" +
                "・Editor/ ディレクトリを持つ",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.ProtToolListHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.Join(", ", ProtectionRules.DefaultToolNames),
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.ProtExtHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.Join(" ", ProtectionRules.AlwaysProtectedExtensions),
                EditorStyles.wordWrappedMiniLabel);

            // ユーザーホワイトリスト（F-PROT-03）
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.ProtWhitelistHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.ProtWhitelistHelp, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                _newGlob = EditorGUILayout.TextField(_newGlob);
                if (GUILayout.Button(AkmStrings.ProtWhitelistAdd, GUILayout.Width(60)))
                {
                    AddWhitelistGlob(_newGlob);
                    _newGlob = "";
                    GUI.FocusControl(null);
                }
            }
            for (int i = _settings.userWhitelistGlobs.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(_settings.userWhitelistGlobs[i]);
                    if (GUILayout.Button(AkmStrings.RemoveButton, GUILayout.Width(60)))
                    {
                        _settings.userWhitelistGlobs.RemoveAt(i);
                        _settings.Save();
                    }
                }
            }
        }

        private void AddWhitelistGlob(string glob)
        {
            glob = glob?.Trim();
            if (string.IsNullOrEmpty(glob)) return;
            if (!_settings.userWhitelistGlobs.Contains(glob))
            {
                _settings.userWhitelistGlobs.Add(glob);
                _settings.Save();
            }
        }

        // ------------------------------------------------------------------- Scan

        private void DrawScanTab()
        {
            // 判定粒度設定（F-GRAN-02）
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _settings.autoEstimateGranularity = EditorGUILayout.ToggleLeft(
                    "導入単位フォルダを自動推定する", _settings.autoEstimateGranularity, GUILayout.Width(220));
                using (new EditorGUI.DisabledScope(_settings.autoEstimateGranularity))
                {
                    EditorGUILayout.LabelField("固定深度:", GUILayout.Width(60));
                    _settings.granularityDepth = Mathf.Max(1,
                        EditorGUILayout.IntField(_settings.granularityDepth, GUILayout.Width(40)));
                }
                if (EditorGUI.EndChangeCheck()) _settings.Save();
            }

            // ファイル単位モード（F-GRAN-03）
            EditorGUI.BeginChangeCheck();
            _settings.fileUnitMode = EditorGUILayout.ToggleLeft(
                AkmStrings.ScanFileUnitToggle, _settings.fileUnitMode);
            if (EditorGUI.EndChangeCheck()) _settings.Save();
            if (_settings.fileUnitMode)
            {
                EditorGUILayout.HelpBox(AkmStrings.ScanFileUnitHelp, MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AkmStrings.ScanButton, GUILayout.Height(28)))
                {
                    RunScan();
                }
            }

            if (_scanBlocked)
            {
                EditorGUILayout.HelpBox(AkmStrings.ScanNoRootsError, MessageType.Error);
                return;
            }

            if (_result == null)
            {
                EditorGUILayout.HelpBox(AkmStrings.ScanNotRunYet, MessageType.Info);
                return;
            }

            DrawScanResult();
        }

        private void RunScan()
        {
            _result = null;
            _scanBlocked = false;
            try
            {
                EditorUtility.DisplayProgressBar(
                    AkmStrings.ProgressTitle, AkmStrings.ProgressCollectRoots, 0.05f);
                var roots = RootCollector.Collect(_settings);

                // F-ROOT-05: ルート未設定ガード
                if (roots.HasNoAvatarRoots)
                {
                    _scanBlocked = true;
                    return;
                }

                _result = UnusedAssetScanner.Scan(_settings, roots);
                _settings.lastScanTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _settings.Save();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawScanResult()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(
                AkmStrings.ScanSummaryFormat,
                _result.Candidates.Count,
                AkmUtil.HumanSize(_result.TotalCandidateSize),
                _result.UsedUnits, _result.ProtectedUnits, _result.RootCount),
                EditorStyles.boldLabel);

            if (_result.Candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(AkmStrings.ScanEmpty, MessageType.Info);
                DrawRestoreSection();
                DrawPurgeSection();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AkmStrings.ScanSelectAll, GUILayout.Width(80)))
                    foreach (var e in _result.Candidates) e.Selected = true;
                if (GUILayout.Button(AkmStrings.ScanSelectNone, GUILayout.Width(80)))
                    foreach (var e in _result.Candidates) e.Selected = false;
            }

            // ヘッダ行
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(AkmStrings.ScanColSelect, GUILayout.Width(36));
                GUILayout.Label(AkmStrings.ScanColPath, GUILayout.ExpandWidth(true));
                GUILayout.Label(AkmStrings.ScanColSize, GUILayout.Width(80));
                GUILayout.Label(AkmStrings.ScanColType, GUILayout.Width(80));
                GUILayout.Label(AkmStrings.ScanColReason, GUILayout.Width(140));
                GUILayout.Label(AkmStrings.ScanColProtect, GUILayout.Width(90));
            }

            foreach (var e in _result.Candidates)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    e.Selected = EditorGUILayout.Toggle(e.Selected, GUILayout.Width(36));

                    if (GUILayout.Button(new GUIContent(e.UnitPath, e.UnitPath),
                            EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
                    {
                        PingPath(e.UnitPath);
                    }

                    GUILayout.Label(AkmUtil.HumanSize(e.SizeBytes), GUILayout.Width(80));
                    GUILayout.Label(new GUIContent(AkmUtil.KindLabel(e.Kind), e.KindDetail), GUILayout.Width(80));
                    GUILayout.Label(e.Reason, GUILayout.Width(140));

                    if (GUILayout.Button(AkmStrings.ScanProtectButton, GUILayout.Width(90)))
                    {
                        // このフォルダをホワイトリストに追加（§4.3 ワンクリック）
                        AddWhitelistGlob(e.UnitPath + "/**");
                        AddWhitelistGlob(e.UnitPath);
                        RunScan();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            // 選択サマリ
            var selected = _result.Candidates.Where(c => c.Selected).ToList();
            long selectedSize = selected.Sum(c => c.SizeBytes);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(
                AkmStrings.ScanSelectedSummaryFormat, selected.Count, AkmUtil.HumanSize(selectedSize)));

            // F-DEL-03: 退避前 .unitypackage バックアップ
            EditorGUI.BeginChangeCheck();
            _settings.exportUnityPackageBeforeRelocate = EditorGUILayout.ToggleLeft(
                AkmStrings.RelocateExportPackageToggle, _settings.exportUnityPackageBeforeRelocate);
            if (EditorGUI.EndChangeCheck()) _settings.Save();
            if (_settings.exportUnityPackageBeforeRelocate)
            {
                EditorGUILayout.HelpBox(AkmStrings.RelocateExportPackageHelp, MessageType.None);
            }

            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                if (GUILayout.Button(AkmStrings.RelocateButton, GUILayout.Height(28)))
                {
                    Relocate(selected, selectedSize);
                }
            }

            DrawRestoreSection();
            DrawPurgeSection();
        }

        private void Relocate(List<ScanResultEntry> selected, long selectedSize)
        {
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog(AkmStrings.ToolName, AkmStrings.RelocateNothingSelected, AkmStrings.Ok);
                return;
            }

            // 確認ダイアログ（§7.4）。既定フォーカスはキャンセル。
            bool ok = EditorUtility.DisplayDialog(
                AkmStrings.RelocateConfirmTitle,
                string.Format(AkmStrings.RelocateConfirmFormat, selected.Count, AkmUtil.HumanSize(selectedSize)),
                AkmStrings.RelocateConfirmOk,
                AkmStrings.RelocateConfirmCancel);
            if (!ok) return;

            var trashRoot = AssetRelocator.Relocate(
                selected, _settings.exportUnityPackageBeforeRelocate,
                out int moved, out string exportedPackage);

            // 実行後ガイダンス（§7.5）
            var doneMsg = string.Format(AkmStrings.RelocateDoneFormat, moved, trashRoot);
            if (!string.IsNullOrEmpty(exportedPackage))
                doneMsg += string.Format(AkmStrings.RelocateExportedFormat, exportedPackage);
            EditorUtility.DisplayDialog(AkmStrings.RelocateDoneTitle, doneMsg, AkmStrings.Ok);

            // 退避済みは結果から除外して再描画
            RunScan();
            GUIUtility.ExitGUI();
        }

        private void DrawRestoreSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.RestoreHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.RestoreHelp, MessageType.None);
            if (GUILayout.Button(AkmStrings.RestoreSelectButton))
            {
                var abs = EditorUtility.OpenFolderPanel(
                    AkmStrings.RestoreSelectButton, AkmUtil.ProjectRoot, "");
                if (string.IsNullOrEmpty(abs)) return;
                abs = AkmUtil.Normalize(abs);

                if (!AssetRelocator.HasManifest(abs))
                {
                    EditorUtility.DisplayDialog(AkmStrings.ToolName, AkmStrings.RestoreInvalidFolder, AkmStrings.Ok);
                    return;
                }

                Restore(abs);
                GUIUtility.ExitGUI();
            }
        }

        private void Restore(string trashRootAbs)
        {
            int count = CountManifestEntries(trashRootAbs);
            bool ok = EditorUtility.DisplayDialog(
                AkmStrings.RestoreConfirmTitle,
                string.Format(AkmStrings.RestoreConfirmFormat, count),
                AkmStrings.RelocateConfirmOk, AkmStrings.RelocateConfirmCancel);
            if (!ok) return;

            int restored = AssetRelocator.Restore(trashRootAbs, out bool trashFolderRemoved);
            if (restored < 0)
            {
                EditorUtility.DisplayDialog(AkmStrings.ToolName, AkmStrings.RestoreInvalidFolder, AkmStrings.Ok);
                return;
            }
            var message = string.Format(AkmStrings.RestoreDoneFormat, restored)
                + (trashFolderRemoved ? AkmStrings.RestoreFolderRemoved : AkmStrings.RestoreFolderKept);
            EditorUtility.DisplayDialog(AkmStrings.ToolName, message, AkmStrings.Ok);
            if (_result != null) RunScan();
        }

        private static int CountManifestEntries(string trashRootAbs)
        {
            try
            {
                var path = Path.Combine(trashRootAbs, ".akm-relocation.json");
                if (!File.Exists(path)) return 0;
                var m = JsonUtility.FromJson<RelocationManifest>(File.ReadAllText(path));
                return m?.entries?.Count ?? 0;
            }
            catch { return 0; }
        }

        // -------------------------------------------------- 完全削除（F-DEL-04）

        private void DrawPurgeSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AkmStrings.PurgeHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.PurgeHelp, MessageType.Warning);
            if (GUILayout.Button(AkmStrings.PurgeSelectButton))
            {
                var abs = EditorUtility.OpenFolderPanel(
                    AkmStrings.PurgeSelectButton, AkmUtil.ProjectRoot, "");
                if (string.IsNullOrEmpty(abs)) return;
                abs = AkmUtil.Normalize(abs);

                if (!AssetRelocator.HasManifest(abs))
                {
                    EditorUtility.DisplayDialog(AkmStrings.ToolName, AkmStrings.PurgeNotTrashFolder, AkmStrings.Ok);
                    return;
                }
                Purge(abs);
                GUIUtility.ExitGUI();
            }
        }

        private void Purge(string trashRootAbs)
        {
            AssetRelocator.TryGetTrashFolderStats(trashRootAbs, out int count, out long size);

            // 第1段階（既定フォーカスはキャンセル側）
            bool stage1 = EditorUtility.DisplayDialog(
                AkmStrings.PurgeConfirmStage1Title,
                string.Format(AkmStrings.PurgeConfirmStage1Format,
                    trashRootAbs, count, AkmUtil.HumanSize(size)),
                AkmStrings.PurgeConfirmProceed, AkmStrings.Cancel);
            if (!stage1) return;

            // 第2段階（最終確認）
            bool stage2 = EditorUtility.DisplayDialog(
                AkmStrings.PurgeConfirmStage2Title,
                AkmStrings.PurgeConfirmStage2,
                AkmStrings.PurgeConfirmProceed, AkmStrings.Cancel);
            if (!stage2) return;

            if (AssetRelocator.PurgeTrashFolder(trashRootAbs, out string error))
            {
                EditorUtility.DisplayDialog(AkmStrings.ToolName,
                    string.Format(AkmStrings.PurgeDoneFormat, trashRootAbs), AkmStrings.Ok);
            }
            else
            {
                EditorUtility.DisplayDialog(AkmStrings.ToolName,
                    string.Format(AkmStrings.PurgeFailedFormat, error), AkmStrings.Ok);
            }
        }

        // -------------------------------------------------- 重複検出（SHA-256）

        private void DrawDuplicatesTab()
        {
            EditorGUILayout.LabelField(AkmStrings.DupHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AkmStrings.DupHelp, MessageType.Info);

            if (GUILayout.Button(AkmStrings.DupScanButton, GUILayout.Height(28)))
            {
                _dupReport = DuplicateDetector.Scan(_settings);
                GUIUtility.ExitGUI();
            }

            if (_dupReport == null)
            {
                EditorGUILayout.HelpBox(AkmStrings.DupNotRunYet, MessageType.None);
                return;
            }
            if (_dupReport.Groups.Count == 0)
            {
                EditorGUILayout.HelpBox(AkmStrings.DupEmpty, MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(
                AkmStrings.DupSummaryFormat,
                _dupReport.Groups.Count, AkmUtil.HumanSize(_dupReport.TotalWasted)),
                EditorStyles.boldLabel);

            foreach (var g in _dupReport.Groups)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(string.Format(
                    AkmStrings.DupGroupHeaderFormat,
                    g.Files.Count, AkmUtil.HumanSize(g.FileSize), AkmUtil.HumanSize(g.WastedBytes)),
                    EditorStyles.boldLabel);

                foreach (var f in g.Files)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string used = !_dupReport.UsedKnown || f.Used == null
                            ? "?"
                            : (f.Used.Value ? AkmStrings.DupUsedYes : AkmStrings.DupUsedNo);
                        GUILayout.Label(used, GUILayout.Width(48));
                        if (GUILayout.Button(new GUIContent(f.Path, f.Path),
                                EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
                        {
                            PingPath(f.Path);
                        }
                    }
                }
            }
        }

        // -------------------------------------------------- キャッシュ掃除（§6-B / F-CACHE）

        private void DrawCacheTab()
        {
            EditorGUILayout.LabelField(AkmStrings.CacheHeader, EditorStyles.boldLabel);

            // OS ガード（Phase 2 は Windows のみ）
            if (!CacheClean.IsWindows)
            {
                EditorGUILayout.HelpBox(AkmStrings.CacheWindowsOnly, MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(AkmStrings.CacheIntro, MessageType.None);
            // W-1 / W-10
            EditorGUILayout.HelpBox(AkmStrings.CacheWarnMain, MessageType.Warning);

            // 予約中の表示（W-8 導線）
            if (CacheClean.IsReserved())
            {
                var pending = CacheClean.ReadPending();
                EditorGUILayout.HelpBox(string.Format(
                    AkmStrings.CacheReservedNoteFormat, pending?.reservedAt ?? ""), MessageType.Warning);
                if (GUILayout.Button(AkmStrings.CacheCancelReserveButton))
                {
                    CacheClean.CancelReservation();
                }
                EditorGUILayout.Space();
            }

            // Logs を含めるか（F-CACHE-01）
            EditorGUI.BeginChangeCheck();
            _settings.cacheCleanIncludeLogs = EditorGUILayout.ToggleLeft(
                AkmStrings.CacheIncludeLogsToggle, _settings.cacheCleanIncludeLogs);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.Save();
                _cacheMeasured = false; // 対象が変わるので再計測を促す
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(AkmStrings.CacheMeasureButton))
            {
                MeasureCache();
            }

            // W-2: 数値の根拠
            if (_cacheMeasured)
            {
                EditorGUILayout.HelpBox(string.Format(
                    AkmStrings.CacheStatsFormat,
                    AkmUtil.HumanSize(_cacheLibSize),
                    _cacheAssetCount,
                    AkmUtil.HumanSize(_cacheAssetSize)), MessageType.Info);

                // W-4: 削除対象を全件列挙
                EditorGUILayout.LabelField(AkmStrings.CacheTargetsHeader, EditorStyles.boldLabel);
                foreach (var t in _cacheTargets.Where(t => t.Enabled))
                {
                    var size = t.SizeBytes >= 0 ? AkmUtil.HumanSize(t.SizeBytes) : "未計測";
                    EditorGUILayout.LabelField(
                        $"・{t.RelativePath}/   （{size}{(t.Exists ? "" : " / 無し")}）",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.LabelField(AkmStrings.CacheTargetsCsprojNote, EditorStyles.miniLabel);
            }

            // W-3: 所要時間の目安（実測優先）
            EditorGUILayout.Space();
            if (_settings.lastReimportSeconds > 0)
            {
                EditorGUILayout.HelpBox(string.Format(
                    AkmStrings.CacheTimeEstimateMeasuredFormat,
                    AkmUtil.HumanDuration(_settings.lastReimportSeconds)), MessageType.Info);
            }
            EditorGUILayout.HelpBox(AkmStrings.CacheTimeEstimateGeneric, MessageType.None);
            // W-7: 実行タイミング助言
            EditorGUILayout.HelpBox(AkmStrings.CacheTimingAdvice, MessageType.None);

            // 実行導線（二段階確認は専用ウィンドウ）
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_cacheMeasured))
            {
                if (GUILayout.Button(AkmStrings.CacheReserveButton, GUILayout.Height(26)))
                {
                    OpenCacheConfirm(CacheCleanConfirmWindow.Mode.Reserve);
                }
                if (GUILayout.Button(AkmStrings.CacheCleanNowButton, GUILayout.Height(22)))
                {
                    OpenCacheConfirm(CacheCleanConfirmWindow.Mode.CleanNow);
                }
            }

            // フォールバック（F-CACHE-04）
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_cacheMeasured))
            {
                if (GUILayout.Button(AkmStrings.CacheFallbackButton))
                {
                    var path = CacheClean.WriteFallbackBat(_cacheTargets);
                    EditorUtility.DisplayDialog(AkmStrings.ToolName,
                        string.Format(AkmStrings.CacheFallbackDoneFormat, path), AkmStrings.Ok);
                }
            }
        }

        private void MeasureCache()
        {
            _cacheTargets = CacheClean.EnumerateTargets(_settings);
            CacheClean.MeasureSizes(_cacheTargets);
            _cacheLibSize = CacheClean.MeasureLibrarySize();
            CacheClean.MeasureAssets(out _cacheAssetCount, out _cacheAssetSize);
            _cacheMeasured = true;
        }

        private void OpenCacheConfirm(CacheCleanConfirmWindow.Mode mode)
        {
            if (!_cacheMeasured || _cacheTargets == null) MeasureCache();
            CacheCleanConfirmWindow.Open(
                mode, _cacheTargets, _cacheLibSize, _cacheAssetCount, _cacheAssetSize,
                _settings.lastReimportSeconds);
        }

        private static void PingPath(string assetPath)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        private static string AbsToAssetPath(string abs)
        {
            if (string.IsNullOrEmpty(abs)) return null;
            abs = AkmUtil.Normalize(abs);
            var dataPath = AkmUtil.Normalize(Application.dataPath);
            if (abs == dataPath) return "Assets";
            if (abs.StartsWith(dataPath + "/"))
            {
                return "Assets" + abs.Substring(dataPath.Length);
            }
            EditorUtility.DisplayDialog(AkmStrings.ToolName,
                "Assets/ 配下のフォルダを選択してください。", AkmStrings.Ok);
            return null;
        }
    }
}
