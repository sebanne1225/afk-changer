namespace Sebanne.AfkManager.Editor.Core
{
    internal static class AfkLog
    {
        private const string Prefix = "[AFK Manager] ";

        internal static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);
        internal static void Warn(string message) => UnityEngine.Debug.LogWarning(Prefix + message);
        internal static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);
    }
}
