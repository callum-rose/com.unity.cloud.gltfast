// Bootstrap for disposable validation/configuration projects; never copy into Marble's Assets.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Marble.GlbImporterSpike
{
    public static class PackageInstaller
    {
        private static AddAndRemoveRequest s_request;
        private static double s_deadline;

        public static void Install()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            InstallPackages(new[]
            {
                "com.unity.render-pipelines.universal@17.4.0",
                "com.unity.cloud.ktx@3.7.0",
                RequireDependency()
            });
        }

        public static void InstallMarbleDependency() => InstallPackages(new[] { RequireDependency() });

        private static string RequireDependency()
        {
            var dependency = Environment.GetEnvironmentVariable("MARBLE_GLB_SPIKE_DEPENDENCY");
            if (string.IsNullOrEmpty(dependency))
                throw new InvalidOperationException("MARBLE_GLB_SPIKE_DEPENDENCY must contain the pinned Git URL.");
            return dependency;
        }

        private static void InstallPackages(string[] packages)
        {
            s_request = Client.AddAndRemove(packages, Array.Empty<string>());
            s_deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!s_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > s_deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("Validation package installation timed out.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;
            if (s_request.Status == StatusCode.Success)
            {
                var dependency = RequireDependency();
                var expectedHash = dependency.Substring(dependency.LastIndexOf('#') + 1);
                var package = s_request.Result.FirstOrDefault(item =>
                    string.Equals(item.name, "com.unity.cloud.gltfast", StringComparison.Ordinal));
                if (package == null || package.source != PackageSource.Git || package.git == null ||
                    !string.Equals(package.git.hash, expectedHash, StringComparison.Ordinal) ||
                    !string.Equals(package.packageId, "com.unity.cloud.gltfast@" + dependency, StringComparison.Ordinal))
                {
                    Debug.LogError("Resolved glTFast source, URL or revision differs from the requested Git pin.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log($"Resolved glTFast: {package.packageId}; source={package.source}; hash={package.git.hash}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError(s_request.Error.message);
                EditorApplication.Exit(1);
            }
        }
    }
}
