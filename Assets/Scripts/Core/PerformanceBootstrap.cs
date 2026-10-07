using UnityEngine;

namespace StackSurge.Core
{
    /// <summary>
    /// Unity caps mobile builds at 30 fps unless a target is set, and Android ignores vSyncCount.
    /// Runs before the first scene loads so input sampling and tweens run at 60 fps on device.
    /// </summary>
    static class PerformanceBootstrap
    {
        const int MobileTargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (!Application.isMobilePlatform) return;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = MobileTargetFrameRate;
        }
    }
}
