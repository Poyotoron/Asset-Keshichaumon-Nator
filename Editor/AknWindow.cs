using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// メインウィンドウ。Roots / Protection / Scan Result の3セクション。
    /// スキャンは常に非破壊。退避は明示的なボタン + 確認ダイアログでのみ実行する。
    /// </summary>
    public class AknWindow : EditorWindow
    {
        private enum Tab { Roots, Protection, Scan, Duplicates, Cache, About }

        private const int CandidateGroupCollapseThreshold = 8;

        private AknSettings _settings;
        private Tab _tab = Tab.Roots;
        private Vector2 _scroll;

        // Roots
        private Object _individualToAdd;
        private List<string> _autoDetected = new List<string>();
        private bool _autoDetectRun;
        private RootSet _inspectedRoots;
        private bool _implicitRootsFoldout = false;

        // Protection
        private string _newGlob = "";

        // Scan
        private ScanResult _result;
        private bool _scanBlocked;
        private bool _scanTargetsFoldout;
        private bool _scanOptionsFoldout;
        private bool _protectedFoldout = false;
        private bool _implicitOnlyUsedFoldout = false;
        private readonly Dictionary<int, bool> _candidateGroupFoldouts = new Dictionary<int, bool>();
        private List<TrashFolderInfo> _trashFolders = new List<TrashFolderInfo>();

        // Duplicates
        private DuplicateReport _dupReport;

        // Cache
        private List<CacheClean.CacheTarget> _cacheTargets;
        private bool _cacheMeasured;
        private long _cacheLibSize;
        private int _cacheAssetCount;
        private long _cacheAssetSize;

        [MenuItem(AknStrings.MenuPath)]
        private static void Open()
        {
            var window = GetWindow<AknWindow>();
            window.titleContent = new GUIContent(AknStrings.WindowTitle);
            window.minSize = new Vector2(640, 480);
            window.Show();
        }

        private void OnEnable()
        {
            _settings = AknSettings.GetOrCreate();
            RefreshTrashFolders();
        }

        private void OnGUI()
        {
            if (_settings == null) _settings = AknSettings.GetOrCreate();

            EditorGUILayout.LabelField(AknStrings.ToolName, EditorStyles.boldLabel);
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
                case Tab.About: DrawAboutTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTabBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var previousTab = _tab;
                _tab = (Tab)GUILayout.Toolbar((int)_tab, new[]
                {
                    AknStrings.TabRoots, AknStrings.TabProtection, AknStrings.TabScan,
                    AknStrings.TabDuplicates, AknStrings.TabCache, AknStrings.TabAbout,
                });
                if (_tab == Tab.Scan && previousTab != Tab.Scan) RefreshTrashFolders();
            }
        }

        // ------------------------------------------------------------------ Roots

        private void DrawRootsTab()
        {
            EditorGUILayout.LabelField(AknStrings.RootsHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.RootsHelp, MessageType.Info);

            DrawDirectoryDropArea(_settings.avatarRootDirectories, AknStrings.RootsDropArea, false);

            if (GUILayout.Button(AknStrings.RootsSelectFolder))
            {
                var abs = EditorUtility.OpenFolderPanel(AknStrings.RootsSelectFolder, Application.dataPath, "");
                var assetPath = AbsToAssetPath(abs);
                if (assetPath != null) AddDirectory(_settings.avatarRootDirectories, assetPath, false);
            }

            DrawDirectoryList(_settings.avatarRootDirectories, AknStrings.RootsUnregistered);

            // 個別ファイル追加
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.RootsAddIndividualHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.RootsAddIndividualHelp, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                _individualToAdd = EditorGUILayout.ObjectField(_individualToAdd, typeof(Object), false);
                using (new EditorGUI.DisabledScope(_individualToAdd == null))
                {
                    if (GUILayout.Button(AknStrings.AddButton, GUILayout.Width(60)))
                    {
                        var p = AssetDatabase.GetAssetPath(_individualToAdd);
                        var ext = AknUtil.Ext(p);
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
                            EditorUtility.DisplayDialog(AknStrings.ToolName,
                                AknStrings.RootsInvalidIndividualAsset, AknStrings.Ok);
                        }
                    }
                }
            }
            DrawPathList(_settings.additionalRootAssets, AknStrings.RootsUnregistered);

            // 自動検出
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.RootsAutoDetectHeader, EditorStyles.boldLabel);
            if (GUILayout.Button(AknStrings.RootsAutoDetectButton))
            {
                _autoDetected = AvatarAutoDetector.FindAvatarPrefabs();
                _autoDetectRun = true;
            }
            if (_autoDetectRun)
            {
                if (_autoDetected.Count == 0)
                {
                    EditorGUILayout.HelpBox(AknStrings.RootsAutoDetectNone, MessageType.None);
                }
                else
                {
                    EditorGUILayout.LabelField(AknStrings.RootsAutoDetectExcludeHint, EditorStyles.miniLabel);
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

            EditorGUILayout.Space();
            string implicitHeader = AknStrings.RootsImplicitHeaderNotInspected;
            if (_inspectedRoots != null)
            {
                int implicitCount = _inspectedRoots.ImplicitRoots.Count +
                    _inspectedRoots.ExcludedImplicitRoots.Count;
                implicitHeader = string.Format(AknStrings.RootsImplicitHeaderFormat, implicitCount);
            }
            _implicitRootsFoldout = EditorGUILayout.Foldout(_implicitRootsFoldout,
                implicitHeader);
            if (_implicitRootsFoldout)
            {
                EditorGUILayout.HelpBox(AknStrings.RootsImplicitHelp, MessageType.Info);
                if (GUILayout.Button(AknStrings.RootsImplicitInspect))
                    _inspectedRoots = RootCollector.Collect(_settings);
                if (_inspectedRoots == null)
                {
                    EditorGUILayout.LabelField(AknStrings.RootsImplicitNotInspected, EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(AknStrings.RootsAutoDetectExcludeHint, EditorStyles.miniLabel);
                    var allImplicit = _inspectedRoots.ImplicitRoots
                        .Concat(_inspectedRoots.ExcludedImplicitRoots).Distinct().OrderBy(p => p).ToList();
                    foreach (var p in allImplicit)
                    {
                        bool included = !_settings.excludedImplicitRoots.Contains(p);
                        bool newIncluded = EditorGUILayout.ToggleLeft(p, included);
                        if (newIncluded == included) continue;
                        if (newIncluded) _settings.excludedImplicitRoots.Remove(p);
                        else if (!_settings.excludedImplicitRoots.Contains(p)) _settings.excludedImplicitRoots.Add(p);
                        _settings.Save();
                    }
                }
            }
        }

        private void DrawDirectoryDropArea(List<string> directories, string label, bool assetsOnly)
        {
            var rect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            GUI.Box(rect, label, EditorStyles.helpBox);

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
                            AddDirectory(directories, p, assetsOnly);
                        }
                    }
                }
                evt.Use();
            }
        }

        private void AddDirectory(List<string> directories, string assetPath, bool assetsOnly)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName,
                    string.Format(AknStrings.FolderRequiredFormat, assetPath), AknStrings.Ok);
                return;
            }
            if (assetsOnly && assetPath != "Assets" && !assetPath.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName,
                    AknStrings.AssetsFolderRequired, AknStrings.Ok);
                return;
            }
            if (!directories.Contains(assetPath))
            {
                directories.Add(assetPath);
                _settings.Save();
            }
        }

        private void DrawDirectoryList(List<string> directories, string emptyLabel)
        {
            DrawPathList(directories, emptyLabel);
        }

        private void DrawPathList(List<string> paths, string emptyLabel)
        {
            EditorGUILayout.Space();
            int removeIndex = -1;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(string.Format(
                    AknStrings.RegisteredItemsFormat, paths.Count), EditorStyles.miniBoldLabel);
                if (paths.Count == 0)
                {
                    EditorGUILayout.LabelField(emptyLabel, EditorStyles.miniLabel);
                }
                else
                {
                    Color separatorColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.10f)
                        : new Color(0f, 0f, 0f, 0.12f);
                    for (int i = 0; i < paths.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(new GUIContent(paths[i], paths[i]),
                                    EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
                                PingPath(paths[i]);
                            if (GUILayout.Button(AknStrings.RemoveButton, GUILayout.Width(60)))
                                removeIndex = i;
                        }
                        if (i < paths.Count - 1)
                        {
                            var separator = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                            EditorGUI.DrawRect(separator, separatorColor);
                        }
                    }
                }
            }
            if (removeIndex >= 0)
            {
                paths.RemoveAt(removeIndex);
                _settings.Save();
            }
            EditorGUILayout.Space();
        }

        // ------------------------------------------------------------- Protection

        private void DrawProtectionTab()
        {
            EditorGUILayout.LabelField(AknStrings.ProtHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.ProtHelp, MessageType.Info);

            EditorGUILayout.LabelField(AknStrings.ProtStructureHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(AknStrings.ProtStructureDetail, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.ProtToolListHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.Join(", ", ProtectionRules.DefaultToolNames),
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.ProtExtHeader, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.Join(" ", ProtectionRules.AlwaysProtectedExtensions),
                EditorStyles.wordWrappedMiniLabel);

            // ユーザーホワイトリスト
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.ProtWhitelistHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.ProtWhitelistHelp, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                _newGlob = EditorGUILayout.TextField(_newGlob);
                if (GUILayout.Button(AknStrings.ProtWhitelistAdd, GUILayout.Width(60)))
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
                    if (GUILayout.Button(AknStrings.RemoveButton, GUILayout.Width(60)))
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
            _scanTargetsFoldout = EditorGUILayout.Foldout(
                _scanTargetsFoldout, AknStrings.ScanTargetsFoldout, true);
            if (_scanTargetsFoldout)
            {
                EditorGUILayout.LabelField(AknStrings.ScanScopeHeader, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(AknStrings.ScanScopeHelp, MessageType.Info);
                DrawDirectoryDropArea(_settings.scanScopeDirectories, AknStrings.ScanScopeDropArea, true);
                if (GUILayout.Button(AknStrings.ScanScopeSelectFolder))
                {
                    var abs = EditorUtility.OpenFolderPanel(
                        AknStrings.ScanScopeSelectFolder, Application.dataPath, "");
                    var assetPath = AbsToAssetPath(abs);
                    if (assetPath != null)
                        AddDirectory(_settings.scanScopeDirectories, assetPath, true);
                }
                DrawDirectoryList(_settings.scanScopeDirectories, AknStrings.ScanScopeUnconfigured);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(AknStrings.ScanToolOutputHeader, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(AknStrings.ScanToolOutputHelp, MessageType.Info);
                DrawDirectoryDropArea(
                    _settings.toolOutputDirectories, AknStrings.ScanToolOutputDropArea, true);
                if (GUILayout.Button(AknStrings.ScanToolOutputSelectFolder))
                {
                    var abs = EditorUtility.OpenFolderPanel(
                        AknStrings.ScanToolOutputSelectFolder, Application.dataPath, "");
                    var assetPath = AbsToAssetPath(abs);
                    if (assetPath != null)
                        AddDirectory(_settings.toolOutputDirectories, assetPath, true);
                }
                DrawDirectoryList(
                    _settings.toolOutputDirectories, AknStrings.ScanToolOutputUnconfigured);
            }

            EditorGUILayout.Space();
            _scanOptionsFoldout = EditorGUILayout.Foldout(
                _scanOptionsFoldout, AknStrings.ScanOptionsFoldout, true);
            if (_scanOptionsFoldout)
            {
                // 判定粒度設定
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _settings.autoEstimateGranularity = EditorGUILayout.ToggleLeft(
                        AknStrings.ScanAutoGranularityToggle,
                        _settings.autoEstimateGranularity, GUILayout.Width(220));
                    using (new EditorGUI.DisabledScope(_settings.autoEstimateGranularity))
                    {
                        EditorGUILayout.LabelField(
                            AknStrings.ScanFixedDepthLabel, GUILayout.Width(60));
                        _settings.granularityDepth = Mathf.Max(1,
                            EditorGUILayout.IntField(
                                _settings.granularityDepth, GUILayout.Width(40)));
                    }
                    if (EditorGUI.EndChangeCheck()) _settings.Save();
                }

                // ファイル単位モード
                EditorGUI.BeginChangeCheck();
                _settings.fileUnitMode = EditorGUILayout.ToggleLeft(
                    AknStrings.ScanFileUnitToggle, _settings.fileUnitMode);
                if (EditorGUI.EndChangeCheck()) _settings.Save();
                if (_settings.fileUnitMode)
                {
                    EditorGUILayout.HelpBox(AknStrings.ScanFileUnitHelp, MessageType.Warning);
                }
            }

            DrawScanSectionHeader(AknStrings.ScanExecuteHeader);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AknStrings.ScanButton, GUILayout.Height(28)))
                {
                    RunScan();
                }
            }

            DrawScanSectionHeader(AknStrings.ScanResultsHeader);
            if (_scanBlocked)
            {
                EditorGUILayout.HelpBox(AknStrings.ScanNoRootsError, MessageType.Error);
            }
            else if (_result == null)
            {
                EditorGUILayout.HelpBox(AknStrings.ScanNotRunYet, MessageType.Info);
            }
            else
            {
                DrawScanResult();
            }

            DrawTrashFoldersSection();
        }

        private static void DrawScanSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.Box(GUIContent.none, GUI.skin.horizontalSlider,
                GUILayout.ExpandWidth(true), GUILayout.Height(1));
            EditorGUILayout.Space();
        }

        private void RunScan()
        {
            _result = null;
            _scanBlocked = false;
            _implicitOnlyUsedFoldout = false;
            _candidateGroupFoldouts.Clear();
            try
            {
                EditorUtility.DisplayProgressBar(
                    AknStrings.ProgressTitle, AknStrings.ProgressCollectRoots, 0.05f);
                var roots = RootCollector.Collect(_settings);

                // ルート未設定ガード
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
                AknStrings.ScanSummaryFormat,
                _result.Candidates.Count,
                AknUtil.HumanSize(_result.TotalCandidateSize),
                _result.UsedUnits, _result.ProtectedCount, _result.RootCount),
                EditorStyles.boldLabel);

            if (_result.ScopeDirectories.Count > 0)
            {
                EditorGUILayout.LabelField(string.Format(AknStrings.ScanScopeSummaryFormat,
                    string.Join("、", _result.ScopeDirectories), _result.OutOfScopeUnits));
                if (_result.TotalUnits == 0 && _result.OutOfScopeUnits > 0)
                    EditorGUILayout.HelpBox(AknStrings.ScanScopeNoUnitsWarning, MessageType.Warning);
            }

            DrawProtectedEntries();
            DrawImplicitOnlyUsedEntries();

            if (_result.Candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(AknStrings.ScanEmpty, MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AknStrings.ScanSelectAll, GUILayout.Width(80)))
                    foreach (var e in _result.Candidates) e.Selected = true;
                if (GUILayout.Button(AknStrings.ScanSelectNone, GUILayout.Width(80)))
                    foreach (var e in _result.Candidates) e.Selected = false;
            }

            // ヘッダ行
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(AknStrings.ScanColSelect, GUILayout.Width(36));
                GUILayout.Label(AknStrings.ScanColPath, GUILayout.ExpandWidth(true));
                GUILayout.Label(AknStrings.ScanColSize, GUILayout.Width(80));
                GUILayout.Label(AknStrings.ScanColType, GUILayout.Width(80));
                GUILayout.Label(AknStrings.ScanColReason, GUILayout.Width(140));
                GUILayout.Label(AknStrings.ScanColProtect, GUILayout.Width(90));
            }

            foreach (var group in _result.Candidates.GroupBy(e => e.GroupId))
            {
                var members = group.ToList();
                if (members.Count == 1)
                {
                    DrawCandidateRow(members[0], members, false);
                    continue;
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool expanded = DrawCandidateGroupHeader(group.Key, members);
                    if (expanded)
                    {
                        bool hasGenerated = members.Any(IsToolOutputUnit);
                        bool hasSource = members.Any(m => !IsToolOutputUnit(m));
                        bool showRoles = hasGenerated && hasSource;
                        foreach (var e in members)
                        {
                            DrawCandidateRow(e, members, showRoles);
                        }
                    }
                }
                EditorGUILayout.Space();
            }

            // 選択サマリ
            var selected = _result.Candidates.Where(c => c.Selected).ToList();
            long selectedSize = selected.Sum(c => c.SizeBytes);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(
                AknStrings.ScanSelectedSummaryFormat, selected.Count, AknUtil.HumanSize(selectedSize)));

            // 退避前 .unitypackage バックアップ
            EditorGUI.BeginChangeCheck();
            _settings.exportUnityPackageBeforeRelocate = EditorGUILayout.ToggleLeft(
                AknStrings.RelocateExportPackageToggle, _settings.exportUnityPackageBeforeRelocate);
            if (EditorGUI.EndChangeCheck()) _settings.Save();
            if (_settings.exportUnityPackageBeforeRelocate)
            {
                EditorGUILayout.HelpBox(AknStrings.RelocateExportPackageHelp, MessageType.None);
            }

            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                if (GUILayout.Button(AknStrings.RelocateButton, GUILayout.Height(28)))
                {
                    Relocate(selected, selectedSize);
                }
            }

        }

        private void DrawProtectedEntries()
        {
            if (_result.ProtectedEntries.Count == 0) return;
            _protectedFoldout = EditorGUILayout.Foldout(_protectedFoldout,
                string.Format(AknStrings.ScanProtectedFoldoutFormat, _result.ProtectedEntries.Count));
            if (!_protectedFoldout) return;
            EditorGUILayout.HelpBox(AknStrings.ScanProtectedHelp, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(AknStrings.ScanColPath, GUILayout.ExpandWidth(true));
                GUILayout.Label(AknStrings.ScanColCount, GUILayout.Width(70));
                GUILayout.Label(AknStrings.ScanColSize, GUILayout.Width(80));
                GUILayout.Label(AknStrings.ScanColReason, GUILayout.Width(220));
            }
            foreach (var e in _result.ProtectedEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(e.UnitPath, e.UnitPath), EditorStyles.linkLabel,
                            GUILayout.ExpandWidth(true))) PingPath(e.UnitPath);
                    GUILayout.Label(string.Format(AknStrings.ScanFileCountFormat, e.FileCount), GUILayout.Width(70));
                    GUILayout.Label(AknUtil.HumanSize(e.SizeBytes), GUILayout.Width(80));
                    GUILayout.Label(e.Reason, GUILayout.Width(220));
                }
            }
        }

        private void DrawImplicitOnlyUsedEntries()
        {
            if (_result.ImplicitOnlyUsedEntries.Count == 0) return;
            _implicitOnlyUsedFoldout = EditorGUILayout.Foldout(_implicitOnlyUsedFoldout,
                string.Format(AknStrings.ScanImplicitOnlyFoldoutFormat,
                    _result.ImplicitOnlyUsedEntries.Count));
            if (!_implicitOnlyUsedFoldout) return;

            EditorGUILayout.HelpBox(AknStrings.ScanImplicitOnlyHelp, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(AknStrings.ScanColPath, GUILayout.ExpandWidth(true));
                GUILayout.Label(AknStrings.ScanColCount, GUILayout.Width(70));
                GUILayout.Label(AknStrings.ScanColSize, GUILayout.Width(80));
                GUILayout.Label(AknStrings.ScanColImplicitRoots, GUILayout.Width(260));
                GUILayout.Space(90);
            }
            foreach (var entry in _result.ImplicitOnlyUsedEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(entry.UnitPath, entry.UnitPath),
                            EditorStyles.linkLabel, GUILayout.ExpandWidth(true))) PingPath(entry.UnitPath);
                    GUILayout.Label(string.Format(AknStrings.ScanFileCountFormat, entry.FileCount),
                        GUILayout.Width(70));
                    GUILayout.Label(AknUtil.HumanSize(entry.SizeBytes), GUILayout.Width(80));
                    string roots = entry.PinnedByImplicitRoots.Count > 0
                        ? string.Join("\n", entry.PinnedByImplicitRoots)
                        : (_result.ImplicitRootAttributionSkipped
                            ? AknStrings.ScanImplicitRootsAttributionSkipped
                            : AknStrings.ScanImplicitRootsNotIdentified);
                    GUILayout.Label(roots, EditorStyles.wordWrappedMiniLabel, GUILayout.Width(260));
                    using (new EditorGUI.DisabledScope(entry.PinnedByImplicitRoots.Count == 0))
                    {
                        if (GUILayout.Button(AknStrings.ScanExcludeImplicitRoots, GUILayout.Width(90)))
                        {
                            foreach (var implicitRoot in entry.PinnedByImplicitRoots)
                            {
                                if (!_settings.excludedImplicitRoots.Contains(implicitRoot))
                                    _settings.excludedImplicitRoots.Add(implicitRoot);
                            }
                            _settings.Save();
                            RunScan();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawCandidateRow(
            ScanResultEntry entry,
            List<ScanResultEntry> members,
            bool showRoles)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool rowSelected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(36));
                if (rowSelected != entry.Selected)
                    foreach (var member in members) member.Selected = rowSelected;

                string tooltip = entry.UnitPath;
                if (entry.ReferencedByUnits.Count > 0)
                    tooltip += "\n\n" + string.Format(AknStrings.ScanReferencedByTooltipFormat,
                        string.Join("\n", entry.ReferencedByUnits));
                if (members.Count > 1) GUILayout.Space(16);
                if (showRoles)
                {
                    bool generated = IsToolOutputUnit(entry);
                    GUILayout.Label(generated ? AknStrings.ScanGroupGeneratedAsset :
                        AknStrings.ScanGroupSourceAsset,
                        generated ? EditorStyles.miniLabel : EditorStyles.boldLabel,
                        GUILayout.Width(70));
                }
                if (GUILayout.Button(new GUIContent(entry.UnitPath, tooltip),
                        EditorStyles.linkLabel, GUILayout.ExpandWidth(true))) PingPath(entry.UnitPath);

                GUILayout.Label(AknUtil.HumanSize(entry.SizeBytes), GUILayout.Width(80));
                GUILayout.Label(new GUIContent(AknUtil.KindLabel(entry.Kind), entry.KindDetail),
                    GUILayout.Width(80));
                GUILayout.Label(entry.Reason, GUILayout.Width(140));

                if (GUILayout.Button(AknStrings.ScanProtectButton, GUILayout.Width(90)))
                {
                    AddWhitelistGlob(entry.UnitPath + "/**");
                    AddWhitelistGlob(entry.UnitPath);
                    RunScan();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private bool DrawCandidateGroupHeader(int groupId, List<ScanResultEntry> members)
        {
            if (!_candidateGroupFoldouts.TryGetValue(groupId, out var expanded))
                expanded = members.Count <= CandidateGroupCollapseThreshold;
            bool selected = members.Any(m => m.Selected);
            long size = members.Sum(m => m.SizeBytes);
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var foldoutRect = GUILayoutUtility.GetRect(
                        16, EditorGUIUtility.singleLineHeight, GUILayout.Width(16));
                    expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                    bool newSelected = EditorGUILayout.ToggleLeft(string.Format(
                        AknStrings.ScanGroupHeaderFormat, members.Count, AknUtil.HumanSize(size)), selected);
                    if (newSelected != selected)
                        foreach (var member in members) member.Selected = newSelected;
                }
                bool hasToolOutput = members.Any(m => _settings.toolOutputDirectories.Any(o =>
                    m.UnitPath == o || m.UnitPath.StartsWith(o.TrimEnd('/') + "/", System.StringComparison.Ordinal)));
                EditorGUILayout.LabelField(hasToolOutput ? AknStrings.ScanGroupToolOutputHelp :
                    AknStrings.ScanGroupGenericHelp, EditorStyles.wordWrappedMiniLabel);
            }
            _candidateGroupFoldouts[groupId] = expanded;
            return expanded;
        }

        private bool IsToolOutputUnit(ScanResultEntry entry)
        {
            return _settings.toolOutputDirectories.Any(output =>
                entry.UnitPath.StartsWith(
                    output.TrimEnd('/') + "/", System.StringComparison.Ordinal));
        }

        private void Relocate(List<ScanResultEntry> selected, long selectedSize)
        {
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName, AknStrings.RelocateNothingSelected, AknStrings.Ok);
                return;
            }

            var foldersToFold = AssetRelocator.CollectFoldersThatBecomeEmpty(
                selected.Select(entry => entry.UnitPath).ToList());
            var confirmMessage = string.Format(
                AknStrings.RelocateConfirmFormat, selected.Count, AknUtil.HumanSize(selectedSize));
            if (foldersToFold.Count > 0)
                confirmMessage += string.Format(AknStrings.RelocateFoldFoldersConfirmFormat, foldersToFold.Count);

            // 確認ダイアログ。既定フォーカスはキャンセル。
            bool ok = EditorUtility.DisplayDialog(
                AknStrings.RelocateConfirmTitle,
                confirmMessage,
                AknStrings.RelocateConfirmOk,
                AknStrings.RelocateConfirmCancel);
            if (!ok) return;

            var trashRoot = AssetRelocator.Relocate(
                selected, _settings.exportUnityPackageBeforeRelocate,
                out int moved, out string exportedPackage, out int foldedFolders);

            // 実行後ガイダンス
            var doneMsg = string.Format(AknStrings.RelocateDoneFormat, moved, trashRoot);
            if (foldedFolders > 0)
                doneMsg += string.Format(AknStrings.RelocateFoldFoldersDoneFormat, foldedFolders);
            if (!string.IsNullOrEmpty(exportedPackage))
                doneMsg += string.Format(AknStrings.RelocateExportedFormat, exportedPackage);
            EditorUtility.DisplayDialog(AknStrings.RelocateDoneTitle, doneMsg, AknStrings.Ok);

            // 退避済みは結果から除外して再描画
            RefreshTrashFolders();
            RunScan();
            GUIUtility.ExitGUI();
        }

        private void RefreshTrashFolders()
        {
            _trashFolders = AssetRelocator.FindTrashFolders();
        }

        private void DrawTrashFoldersSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.RestoreTrashFoldersHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.RestoreTrashFoldersHelp, MessageType.None);
            if (GUILayout.Button(AknStrings.RestoreTrashFoldersRefresh, GUILayout.Width(80)))
                RefreshTrashFolders();

            if (_trashFolders.Count == 0)
            {
                EditorGUILayout.LabelField(AknStrings.RestoreTrashFoldersEmpty, EditorStyles.miniLabel);
            }
            else
            {
                foreach (var folder in _trashFolders)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(string.Format(AknStrings.RestoreTrashFoldersEntryFormat,
                            AknUtil.HumanDateTime(folder.CreatedAt), folder.EntryCount,
                            AknUtil.HumanSize(folder.SizeBytes)));
                        if (folder.HasBackupPackage)
                            EditorGUILayout.LabelField(AknStrings.RestoreTrashFoldersBackup, EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(folder.AbsPath, EditorStyles.miniLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(AknStrings.RestoreTrashFoldersReveal))
                                EditorUtility.RevealInFinder(folder.AbsPath);
                            if (GUILayout.Button(AknStrings.RestoreTrashFoldersRestore))
                            {
                                Restore(folder.AbsPath);
                                GUIUtility.ExitGUI();
                            }
                            if (GUILayout.Button(AknStrings.RestoreTrashFoldersPurge))
                            {
                                Purge(folder.AbsPath);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }

            DrawRestoreSection();
            DrawPurgeSection();
        }

        private void DrawRestoreSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.RestoreHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.RestoreHelp, MessageType.None);
            if (GUILayout.Button(AknStrings.RestoreSelectButton))
            {
                var abs = EditorUtility.OpenFolderPanel(
                    AknStrings.RestoreSelectButton, AknUtil.ProjectRoot, "");
                if (string.IsNullOrEmpty(abs)) return;
                abs = AknUtil.Normalize(abs);

                if (!AssetRelocator.HasManifest(abs))
                {
                    EditorUtility.DisplayDialog(AknStrings.ToolName, AknStrings.RestoreInvalidFolder, AknStrings.Ok);
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
                AknStrings.RestoreConfirmTitle,
                string.Format(AknStrings.RestoreConfirmFormat, count),
                AknStrings.RelocateConfirmOk, AknStrings.RelocateConfirmCancel);
            if (!ok) return;

            int restored = AssetRelocator.Restore(trashRootAbs, out bool trashFolderRemoved);
            if (restored < 0)
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName, AknStrings.RestoreInvalidFolder, AknStrings.Ok);
                return;
            }
            var message = string.Format(AknStrings.RestoreDoneFormat, restored)
                + (trashFolderRemoved ? AknStrings.RestoreFolderRemoved : AknStrings.RestoreFolderKept);
            EditorUtility.DisplayDialog(AknStrings.ToolName, message, AknStrings.Ok);
            RefreshTrashFolders();
            if (_result != null) RunScan();
        }

        private static int CountManifestEntries(string trashRootAbs)
        {
            try
            {
                var path = Path.Combine(trashRootAbs, AssetRelocator.ManifestFileName);
                if (!File.Exists(path)) return 0;
                var m = JsonUtility.FromJson<RelocationManifest>(File.ReadAllText(path));
                return m?.entries?.Count ?? 0;
            }
            catch { return 0; }
        }

        // -------------------------------------------------- 完全削除

        private void DrawPurgeSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.PurgeHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.PurgeHelp, MessageType.Warning);
            if (GUILayout.Button(AknStrings.PurgeSelectButton))
            {
                var abs = EditorUtility.OpenFolderPanel(
                    AknStrings.PurgeSelectButton, AknUtil.ProjectRoot, "");
                if (string.IsNullOrEmpty(abs)) return;
                abs = AknUtil.Normalize(abs);

                if (!AssetRelocator.HasManifest(abs))
                {
                    EditorUtility.DisplayDialog(AknStrings.ToolName, AknStrings.PurgeNotTrashFolder, AknStrings.Ok);
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
                AknStrings.PurgeConfirmStage1Title,
                string.Format(AknStrings.PurgeConfirmStage1Format,
                    trashRootAbs, count, AknUtil.HumanSize(size)),
                AknStrings.PurgeConfirmProceed, AknStrings.Cancel);
            if (!stage1) return;

            // 第2段階（最終確認）
            bool stage2 = EditorUtility.DisplayDialog(
                AknStrings.PurgeConfirmStage2Title,
                AknStrings.PurgeConfirmStage2,
                AknStrings.PurgeConfirmProceed, AknStrings.Cancel);
            if (!stage2) return;

            if (AssetRelocator.PurgeTrashFolder(trashRootAbs, out string error))
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName,
                    string.Format(AknStrings.PurgeDoneFormat, trashRootAbs), AknStrings.Ok);
            }
            else
            {
                EditorUtility.DisplayDialog(AknStrings.ToolName,
                    string.Format(AknStrings.PurgeFailedFormat, error), AknStrings.Ok);
            }
            RefreshTrashFolders();
        }

        // -------------------------------------------------- 重複検出（SHA-256）

        private void DrawDuplicatesTab()
        {
            EditorGUILayout.LabelField(AknStrings.DupHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.DupHelp, MessageType.Info);

            if (GUILayout.Button(AknStrings.DupScanButton, GUILayout.Height(28)))
            {
                _dupReport = DuplicateDetector.Scan(_settings);
                GUIUtility.ExitGUI();
            }

            if (_dupReport == null)
            {
                EditorGUILayout.HelpBox(AknStrings.DupNotRunYet, MessageType.None);
                return;
            }
            if (_dupReport.Groups.Count == 0)
            {
                EditorGUILayout.HelpBox(AknStrings.DupEmpty, MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(
                AknStrings.DupSummaryFormat,
                _dupReport.Groups.Count, AknUtil.HumanSize(_dupReport.TotalWasted)),
                EditorStyles.boldLabel);

            foreach (var g in _dupReport.Groups)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(string.Format(
                    AknStrings.DupGroupHeaderFormat,
                    g.Files.Count, AknUtil.HumanSize(g.FileSize), AknUtil.HumanSize(g.WastedBytes)),
                    EditorStyles.boldLabel);

                foreach (var f in g.Files)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string used = !_dupReport.UsedKnown || f.Used == null
                            ? "?"
                            : (f.Used.Value ? AknStrings.DupUsedYes : AknStrings.DupUsedNo);
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

        // -------------------------------------------------- キャッシュ掃除

        private void DrawCacheTab()
        {
            EditorGUILayout.LabelField(AknStrings.CacheHeader, EditorStyles.boldLabel);

            // OS ガード（Phase 2 は Windows のみ）
            if (!CacheClean.IsWindows)
            {
                EditorGUILayout.HelpBox(AknStrings.CacheWindowsOnly, MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(AknStrings.CacheIntro, MessageType.None);
            // 目立つ警告
            EditorGUILayout.HelpBox(AknStrings.CacheWarnMain, MessageType.Warning);

            // 予約中の表示
            if (CacheClean.IsReserved())
            {
                var pending = CacheClean.ReadPending();
                EditorGUILayout.HelpBox(string.Format(
                    AknStrings.CacheReservedNoteFormat, pending?.reservedAt ?? ""), MessageType.Warning);
                if (GUILayout.Button(AknStrings.CacheCancelReserveButton))
                {
                    CacheClean.CancelReservation();
                }
                EditorGUILayout.Space();
            }

            // Logs を含めるか
            EditorGUI.BeginChangeCheck();
            _settings.cacheCleanIncludeLogs = EditorGUILayout.ToggleLeft(
                AknStrings.CacheIncludeLogsToggle, _settings.cacheCleanIncludeLogs);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.Save();
                _cacheMeasured = false; // 対象が変わるので再計測を促す
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(AknStrings.CacheMeasureButton))
            {
                MeasureCache();
            }

            // 数値の根拠
            if (_cacheMeasured)
            {
                EditorGUILayout.HelpBox(string.Format(
                    AknStrings.CacheStatsFormat,
                    AknUtil.HumanSize(_cacheLibSize),
                    _cacheAssetCount,
                    AknUtil.HumanSize(_cacheAssetSize)), MessageType.Info);

                // 削除対象を全件列挙
                EditorGUILayout.LabelField(AknStrings.CacheTargetsHeader, EditorStyles.boldLabel);
                foreach (var t in _cacheTargets.Where(t => t.Enabled))
                {
                    var size = t.SizeBytes >= 0 ? AknUtil.HumanSize(t.SizeBytes) : AknStrings.CacheUnmeasured;
                    EditorGUILayout.LabelField(
                        string.Format(AknStrings.CacheTargetEntryFormat, t.RelativePath, size,
                            t.Exists ? "" : AknStrings.CacheTargetMissing),
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.LabelField(AknStrings.CacheTargetsCsprojNote, EditorStyles.miniLabel);
            }

            // 所要時間の目安（実測優先）
            EditorGUILayout.Space();
            if (_settings.lastReimportSeconds > 0)
            {
                EditorGUILayout.HelpBox(string.Format(
                    AknStrings.CacheTimeEstimateMeasuredFormat,
                    AknUtil.HumanDuration(_settings.lastReimportSeconds)), MessageType.Info);
            }
            EditorGUILayout.HelpBox(AknStrings.CacheTimeEstimateGeneric, MessageType.None);
            // 実行タイミング助言
            EditorGUILayout.HelpBox(AknStrings.CacheTimingAdvice, MessageType.None);

            // 実行導線（二段階確認は専用ウィンドウ）
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_cacheMeasured))
            {
                if (GUILayout.Button(AknStrings.CacheReserveButton, GUILayout.Height(26)))
                {
                    OpenCacheConfirm(CacheCleanConfirmWindow.Mode.Reserve);
                }
                if (GUILayout.Button(AknStrings.CacheCleanNowButton, GUILayout.Height(22)))
                {
                    OpenCacheConfirm(CacheCleanConfirmWindow.Mode.CleanNow);
                }
            }

            // フォールバック
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_cacheMeasured))
            {
                if (GUILayout.Button(AknStrings.CacheFallbackButton))
                {
                    var path = CacheClean.WriteFallbackBat(_cacheTargets);
                    EditorUtility.DisplayDialog(AknStrings.ToolName,
                        string.Format(AknStrings.CacheFallbackDoneFormat, path), AknStrings.Ok);
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

        // -------------------------------------------------- About

        private void DrawAboutTab()
        {
            var packageInfo = FindPackageInfo();
            string displayName = packageInfo != null && !string.IsNullOrEmpty(packageInfo.displayName)
                ? packageInfo.displayName : AknStrings.ToolName;
            string version = packageInfo != null && !string.IsNullOrEmpty(packageInfo.version)
                ? packageInfo.version : AknStrings.AboutUnknownVersion;
            string reportInfo = string.Format(AknStrings.AboutCopyFormat,
                displayName, version, Application.unityVersion,
                SystemInfo.operatingSystemFamily.ToString());

            EditorGUILayout.LabelField(string.Format(
                AknStrings.AboutPackageFormat, displayName, version), EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.AboutIssuesHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.AboutIssuesHelp, MessageType.Warning);
            if (GUILayout.Button(AknStrings.AboutOpenIssues, GUILayout.Height(30)))
                Application.OpenURL(AknStrings.AboutIssuesUrl);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.AboutLinksHeader, EditorStyles.boldLabel);
            if (GUILayout.Button(AknStrings.AboutRepository, EditorStyles.linkLabel))
                Application.OpenURL(AknStrings.AboutRepositoryUrl);
            if (GUILayout.Button(AknStrings.AboutChangelog, EditorStyles.linkLabel))
                Application.OpenURL(AknStrings.AboutChangelogUrl);
            EditorGUILayout.LabelField(AknStrings.AboutLicense);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.AboutReportInfoHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(AknStrings.AboutReportInfo, MessageType.Info);
            if (GUILayout.Button(AknStrings.AboutCopyInfo))
            {
                EditorGUIUtility.systemCopyBuffer = reportInfo;
            }
            EditorGUILayout.LabelField(AknStrings.AboutCopyPreview, EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(
                reportInfo, EditorStyles.textArea, GUILayout.Height(58));
        }

        private static UnityEditor.PackageManager.PackageInfo FindPackageInfo()
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AknWindow).Assembly);
            }
            catch
            {
                return null;
            }
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
            abs = AknUtil.Normalize(abs);
            var dataPath = AknUtil.Normalize(Application.dataPath);
            if (abs == dataPath) return "Assets";
            if (abs.StartsWith(dataPath + "/"))
            {
                return "Assets" + abs.Substring(dataPath.Length);
            }
            EditorUtility.DisplayDialog(AknStrings.ToolName,
                AknStrings.AssetsFolderRequired, AknStrings.Ok);
            return null;
        }
    }
}
