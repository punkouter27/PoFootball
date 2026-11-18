using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoFootball.Editor
{
    /// <summary>
    /// Stamps PlayerSettings.bundleVersion with YYMMDD before every player build so the lower-right
    /// corner of Systems_StatusHudView always displays the YYMMDD build date.
    /// </summary>
    public sealed class Editor_BuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            string stamp = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
            if (PlayerSettings.bundleVersion != stamp)
            {
                PlayerSettings.bundleVersion = stamp;
            }
            Debug.Log($"[Editor_BuildStamp] Stamped bundleVersion = {stamp}");
        }
    }
}
