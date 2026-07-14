using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// キャッシュ掃除のライフサイクル管理。
    ///   - 終了時フック（wantsToQuit / quitting）でヘルパーを detached 起動する。
    ///   - 起動時に予約フラグ・完了マーカーを検証する。
    /// EditorApplication.quitting の時点では Unity はまだ Library/ を掴んでいるため、ここで削除はしない。
    /// ヘルパーが Unity プロセスの終了を待ってから削除する。
    /// </summary>
    [InitializeOnLoad]
    internal static class CacheCleanLifecycle
    {
        static CacheCleanLifecycle()
        {
            EditorApplication.wantsToQuit += OnWantsToQuit;
            EditorApplication.quitting += OnQuitting;
            // 起動直後の検証は delayCall で（AssetDatabase / UI が落ち着いてから）。
            EditorApplication.delayCall += VerifyOnStartup;
        }

        // --------------------------------------------------------- 終了時

        private static bool OnWantsToQuit()
        {
            if (!CacheClean.IsReserved()) return true;

            // 即時実行モードで自ら Exit した場合は、直前に最終確認済みのため再確認しない。
            if (CacheClean.SuppressQuitPrompt) return true;

            // 予約状態での終了時、再度警告し、この時点でキャンセルできるようにする。
            int choice = EditorUtility.DisplayDialogComplex(
                AknStrings.CacheQuitWarnTitle,
                AknStrings.CacheQuitWarnMessage,
                AknStrings.CacheQuitProceed,          // 0: 掃除して終了
                AknStrings.CacheQuitAbort,            // 1: 終了しない
                AknStrings.CacheQuitCancelReservation // 2: 予約を取り消して終了
            );

            switch (choice)
            {
                case 0: // 掃除して終了（予約を維持したまま終了 → quitting でヘルパー起動）
                    return true;
                case 2: // 予約を取り消して終了
                    CacheClean.CancelReservation();
                    CacheClean.AppendLog("reservation cancelled at quit by user.");
                    return true;
                default: // 1: 終了しない
                    return false;
            }
        }

        private static void OnQuitting()
        {
            if (!CacheClean.IsReserved()) return;
            // ここで削除してはいけない（Unity はまだ生存）。ヘルパーに委譲する。
            CacheClean.LaunchHelper();
        }

        // --------------------------------------------------------- 起動時

        // SessionState はドメインリロードを跨いで保持され、Unity 再起動でクリアされる。
        // これにより「真の起動」と「スクリプト再コンパイルによるドメインリロード」を区別する。
        private const string SessionStartedKey = "net.maaaaa.akn.cacheSessionVerified";

        private static void VerifyOnStartup()
        {
            // スクリプト再コンパイル等のドメインリロードでは検証しない。
            // （ここで検証すると、予約中にコードを触っただけで予約が誤ってキャンセルされる。）
            if (SessionState.GetBool(SessionStartedKey, false)) return;
            SessionState.SetBool(SessionStartedKey, true);

            // 完了マーカーがあれば「掃除直後の起動」。再インポート通知 + 実測記録。
            if (CacheClean.HasDoneMarker())
            {
                HandleJustCleaned();
                return;
            }

            // 予約が残っている（完了マーカー無し）= 前回の掃除が実行されなかった（クラッシュ等）。
            if (CacheClean.IsReserved())
            {
                CacheClean.AppendLog("leftover reservation detected on startup (previous clean did not run).");
                CacheClean.CancelReservation();
                EditorUtility.DisplayDialog(
                    AknStrings.CacheLeftoverTitle, AknStrings.CacheLeftoverMessage, AknStrings.Ok);
            }
        }

        private static void HandleJustCleaned()
        {
            // 実測の再インポート時間。掃除直後の起動は Library 再生成が大半を占めるため、
            // 「プロセス起動から現在まで」を再インポート時間の近似として記録する（環境差は UI 側で明記）。
            double measured = 0;
            try
            {
                var start = Process.GetCurrentProcess().StartTime;
                measured = (DateTime.Now - start).TotalSeconds;
            }
            catch { measured = 0; }

            // 明らかに異常（極端に長い/負）は捨てる。
            if (measured < 1 || measured > 6 * 3600) measured = 0;

            var settings = AknSettings.GetOrCreate();
            if (measured > 0)
            {
                settings.lastReimportSeconds = measured;
                settings.Save();
                CacheClean.AppendLog($"just-cleaned startup; approx reimport {measured:0} s.");
            }
            else
            {
                CacheClean.AppendLog("just-cleaned startup; reimport time not recorded.");
            }

            CacheClean.ClearDoneMarker();

            var msg = AknStrings.CacheJustCleanedMessage;
            if (measured > 0)
                msg += string.Format(AknStrings.CacheJustCleanedMeasuredFormat, AknUtil.HumanDuration(measured));
            EditorUtility.DisplayDialog(AknStrings.CacheJustCleanedTitle, msg, AknStrings.Ok);
        }
    }
}
