using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Modes
{
    /// <summary>
    /// A new scene never starts frozen (round 11). Time.timeScale is global: leave a match
    /// mid-replay, mid-Hawk-Eye, in the impact pause or the pause menu (all set it to 0)
    /// and the next scene inherits 0 — every timed wait in it hangs. That is what froze
    /// the round-10 challenge on its banner for six minutes (launched from the nets during
    /// a run-out replay). On every scene load: the clock runs again, at normal speed.
    /// </summary>
    public static class SceneClock
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= Reset;
            SceneManager.sceneLoaded += Reset;
        }

        private static void Reset(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.Log($"[APL modes] scene {scene.name} loaded with time x{Time.timeScale:0.00}: back to x1");
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
