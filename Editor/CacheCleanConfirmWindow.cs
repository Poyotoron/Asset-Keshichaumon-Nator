using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// キャッシュ掃除の二段階確認ウィンドウ。
    ///   第1段階: 説明と見積り（再掲）
    ///   第2段階: 最終確認。「時間がかかることを理解しました」チェックが入るまで実行ボタンを無効化する。
    /// 実行ボタンは既定でフォーカスされず（IMGUI）、Enter 連打で実行されない。キャンセルは常に可能。
    /// </summary>
    internal class CacheCleanConfirmWindow : EditorWindow
    {
        internal enum Mode { Reserve, CleanNow }

        private Mode _mode;
        private int _step = 1;              // 1 = 説明/見積り, 2 = 最終確認
        private bool _ack;                  // 「理解しました」チェックボックス
        private List<CacheClean.CacheTarget> _targets;
        private long _libSize;
        private int _assetCount;
        private long _assetSize;
        private double _measuredReimport;
        private Vector2 _scroll;

        public static void Open(Mode mode, List<CacheClean.CacheTarget> targets,
            long libSize, int assetCount, long assetSize, double measuredReimport)
        {
            var w = CreateInstance<CacheCleanConfirmWindow>();
            w.titleContent = new GUIContent(mode == Mode.Reserve
                ? AknStrings.CacheReserveButton
                : AknStrings.CacheCleanNowButton);
            w._mode = mode;
            w._targets = targets;
            w._libSize = libSize;
            w._assetCount = assetCount;
            w._assetSize = assetSize;
            w._measuredReimport = measuredReimport;
            w.minSize = new Vector2(560, 520);
            w.ShowModalUtility();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField(AknStrings.CacheHeader, EditorStyles.boldLabel);

            // 目立つ警告
            EditorGUILayout.HelpBox(AknStrings.CacheWarnMain, MessageType.Warning);

            // 数値の根拠
            EditorGUILayout.HelpBox(string.Format(
                AknStrings.CacheStatsFormat,
                AknUtil.HumanSize(_libSize),
                _assetCount,
                AknUtil.HumanSize(_assetSize)), MessageType.Info);

            // 所要時間の目安（実測があれば優先）
            if (_measuredReimport > 0)
            {
                EditorGUILayout.HelpBox(string.Format(
                    AknStrings.CacheTimeEstimateMeasuredFormat,
                    AknUtil.HumanDuration(_measuredReimport)), MessageType.Info);
            }
            EditorGUILayout.HelpBox(AknStrings.CacheTimeEstimateGeneric, MessageType.None);

            // 削除対象を全件列挙
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AknStrings.CacheTargetsHeader, EditorStyles.boldLabel);
            foreach (var t in _targets.Where(t => t.Enabled))
            {
                var size = t.SizeBytes >= 0 ? AknUtil.HumanSize(t.SizeBytes) : "未計測";
                EditorGUILayout.LabelField($"・{t.RelativePath}/   （{size}{(t.Exists ? "" : " / 無し")}）",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField(AknStrings.CacheTargetsCsprojNote, EditorStyles.miniLabel);

            // 実行タイミング助言
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(AknStrings.CacheTimingAdvice, MessageType.None);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (_step == 1) DrawStage1();
            else DrawStage2();
        }

        private void DrawStage1()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AknStrings.Cancel, GUILayout.Height(28)))
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(AknStrings.CacheNextButton, GUILayout.Height(28)))
                {
                    _step = 2;
                }
            }
        }

        private void DrawStage2()
        {
            EditorGUILayout.LabelField(AknStrings.CacheConfirmStage2Title, EditorStyles.boldLabel);

            // チェックしなければ実行ボタンを有効化しない
            _ack = EditorGUILayout.ToggleLeft(AknStrings.CacheAckCheckbox, _ack);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(AknStrings.Cancel, GUILayout.Height(28)))
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(!_ack))
                {
                    var label = _mode == Mode.Reserve
                        ? AknStrings.CacheReserveConfirmButton
                        : AknStrings.CacheCleanNowConfirmButton;
                    if (GUILayout.Button(label, GUILayout.Height(28)))
                    {
                        Execute();
                    }
                }
            }
        }

        private void Execute()
        {
            CacheClean.Reserve(_targets);

            if (_mode == Mode.Reserve)
            {
                var pending = CacheClean.ReadPending();
                Close();
                EditorUtility.DisplayDialog(
                    AknStrings.ToolName,
                    string.Format(AknStrings.CacheReservedDialogFormat,
                        string.Format(AknStrings.CacheReservedNoteFormat, pending?.reservedAt ?? "")),
                    AknStrings.Ok);
                GUIUtility.ExitGUI();
            }
            else // CleanNow: 予約 + ヘルパー起動 + 即 Exit
            {
                CacheClean.SuppressQuitPrompt = true;
                // EditorApplication.Exit は quitting を確実には発火させないため、
                // ここでヘルパーを起動しておく（PID の終了を待つので Exit 前起動で問題ない）。
                CacheClean.LaunchHelper();
                Close();
                EditorApplication.Exit(0);
            }
        }
    }
}
