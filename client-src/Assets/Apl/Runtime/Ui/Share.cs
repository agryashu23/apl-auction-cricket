using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>Copy text, or hand it to the phone's share sheet (WhatsApp and the rest).</summary>
    public static class Share
    {
        public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;

        /// <summary>
        /// Opens Android's share sheet with <paramref name="text"/>. Returns false
        /// where there is none (editor, desktop): the caller copies instead.
        /// </summary>
        public static bool Text(string text, string title)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND")).Dispose();
                intent.Call<AndroidJavaObject>("setType", "text/plain").Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text).Dispose();
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title);
                activity.Call("startActivity", chooser);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[APL] share failed: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }
    }
}
