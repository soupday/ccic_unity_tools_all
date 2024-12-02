using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Reallusion.Import
{
    public class UpdateManager
    {
        public static bool checkIsLocked = false;
        public static bool calledFromMenu = false;
        public static RLSettingsObject settings;

        //shader package validation
        public static string emptyVersion = "0.0.0";
        public static Version activeVersion = new Version(0, 0, 0);
        public static ShaderPackageUtil.InstalledPipeline activePipeline = ShaderPackageUtil.InstalledPipeline.None;
        public static ShaderPackageUtil.PipelineVersion activePipelineVersion = ShaderPackageUtil.PipelineVersion.None;
        public static ShaderPackageUtil.PipelineVersion installedShaderPipelineVersion = ShaderPackageUtil.PipelineVersion.None;
        public static ShaderPackageUtil.PlatformRestriction platformRestriction = ShaderPackageUtil.PlatformRestriction.None;
        public static Version installedShaderVersion = new Version(0, 0, 0);
        public static ShaderPackageUtil.InstalledPackageStatus installedPackageStatus = ShaderPackageUtil.InstalledPackageStatus.None;
        public static List<ShaderPackageUtil.ShaderPackageManifest> availablePackages;
        public static ShaderPackageUtil.ShaderPackageManifest currentPackageManifest;
        public static string activePackageString = string.Empty;
        public static List<ShaderPackageUtil.InstalledPipelines> installedPipelines;
        public static ShaderPackageUtil.PackageVailidity shaderPackageValid = ShaderPackageUtil.PackageVailidity.None;
        public static List<ShaderPackageUtil.ShaderPackageItem> missingShaderPackageItems;
        public static ShaderPackageUtil.ShaderActionRules determinedShaderAction = null;

        //software package update checker
        public static bool updateChecked = false;
        public static RLToolUpdateUtil.DeterminedSoftwareAction determinedSoftwareAction = RLToolUpdateUtil.DeterminedSoftwareAction.None;

        public static event EventHandler UpdateChecksComplete;

        private static ActivityStatus determinationStatus = ActivityStatus.None;

        public static ActivityStatus DeterminationStatus { get { return determinationStatus; } }

        public static void TryPerformUpdateChecks(bool fromMenu = false)
        {
            //Debug.LogWarning("TryPerformUpdateChecks...");
            if (!checkIsLocked)
            {
                //Debug.LogWarning("!checkIsLocked...");
                calledFromMenu = fromMenu;
                //Debug.LogWarning("Check is not locked - can perform update checks");
                PerformUpdateChecks();
            }
        }

        public static void PerformUpdateChecks()
        {
            //Debug.LogWarning("STARTING UPDATE CHECKS");
            if (Application.isPlaying)
            {
                if (EditorWindow.HasOpenInstances<ShaderPackageUpdater>())
                {
                    EditorWindow.GetWindow<ShaderPackageUpdater>().Close();
                }
            }
            else
            {                
                checkIsLocked = true;
                UpdateChecksComplete -= UpdateChecksDone;
                UpdateChecksComplete += UpdateChecksDone;
                determinationStatus = 0;
                StartUpdateMonitor();
                CheckHttp();
                CheckPackages();
            }
        }

        public static void UpdateChecksDone(object sender, object e)
        {
            //Debug.LogWarning("ALL UPDATE CHECKS COMPLETED");
            ShaderPackageUtil.DetermineShaderAction();
            checkIsLocked = false;
            ShowUpdateUtilityWindow();

            UpdateChecksComplete -= UpdateChecksDone;
        }

        public static void CheckHttp()
        {            
            RLToolUpdateUtil.HttpVersionChecked -= HttpCheckDone;
            RLToolUpdateUtil.HttpVersionChecked += HttpCheckDone;
            SetDeterminationStatusFlag(ActivityStatus.DeterminingHttp, true);
            RLToolUpdateUtil.UpdateManagerUpdateCheck();
        }

        public static void HttpCheckDone(object sender, object e)
        {
            RLToolUpdateUtil.HttpVersionChecked -= HttpCheckDone;
            SetDeterminationStatusFlag(ActivityStatus.DoneHttp, true);
        }

        public static void CheckPackages()
        {
            ShaderPackageUtil.PackageCheckDone -= PackageCheckDone;
            ShaderPackageUtil.PackageCheckDone += PackageCheckDone;
                        
            SetDeterminationStatusFlag(ActivityStatus.DeterminingPackages, true);
            ShaderPackageUtil.UpdateManagerUpdateCheck();
            
        }

        public static void PackageCheckDone(object sender, object e)
        {
            SetDeterminationStatusFlag(ActivityStatus.DonePackages, true);
            ShaderPackageUtil.PackageCheckDone -= PackageCheckDone;
        }

        public static void StartUpdateMonitor()
        {
            EditorApplication.update -= MonitorUpdateCheck;
            EditorApplication.update += MonitorUpdateCheck;
        }

        private static void MonitorUpdateCheck()
        {
            bool gotPackages = DeterminationStatus.HasFlag(ActivityStatus.DonePackages);
            bool gotHttp = DeterminationStatus.HasFlag(ActivityStatus.DoneHttp);

            if (gotPackages && gotHttp)
            {
                if (UpdateChecksComplete != null)
                    UpdateChecksComplete.Invoke(null, null);
                EditorApplication.update -= MonitorUpdateCheck;
            }
        }

        [Flags]
        public enum ActivityStatus
        {
            None = 0,
            DeterminingPackages = 1,
            DonePackages = 2,
            DeterminingHttp = 4,
            DoneHttp = 8
        }

        public static void SetDeterminationStatusFlag(ActivityStatus flag, bool value)
        {
            if (value)
            {
                if (!determinationStatus.HasFlag(flag))
                {
                    determinationStatus |= flag; // toggle changed to ON => bitwise OR to add flag                    
                }
            }
            else
            {
                if (determinationStatus.HasFlag(flag))
                {
                    determinationStatus ^= flag; // toggle changed to OFF => bitwise XOR to remove flag
                }
            }
        }

        public static void ShowUpdateUtilityWindow()
        {
            //Debug.LogWarning("ShowUpdateUtilityWindow");
            if (UpdateManager.determinedShaderAction != null)
            {
                //Debug.LogWarning("UpdateManager.determinedShaderAction != null");
                if (ImporterWindow.GeneralSettings != null)
                    settings = ImporterWindow.GeneralSettings;
                else
                    Debug.LogError("settings are null");

                bool sos = false;
                if (settings != null) sos = settings.showOnStartup;
                //if (sos) Debug.LogWarning("Show on Startup");
                bool shownOnce = true;
                if (settings != null) shownOnce = settings.updateWindowShownOnce;
                //Debug.LogWarning("Shown once already: " + shownOnce.ToString());

                bool swUpdateAvailable = UpdateManager.determinedSoftwareAction == RLToolUpdateUtil.DeterminedSoftwareAction.Software_update_available;
                if (swUpdateAvailable) Debug.LogWarning("A software update is available.");
                
                bool valid = UpdateManager.determinedShaderAction.DeterminedAction == ShaderPackageUtil.DeterminedShaderAction.CurrentValid;
                bool force = UpdateManager.determinedShaderAction.DeterminedAction == ShaderPackageUtil.DeterminedShaderAction.UninstallReinstall_force || UpdateManager.determinedShaderAction.DeterminedAction == ShaderPackageUtil.DeterminedShaderAction.Error;
                bool optional = UpdateManager.determinedShaderAction.DeterminedAction == ShaderPackageUtil.DeterminedShaderAction.UninstallReinstall_optional;
                bool shaderActionRequired = force || (optional && sos);
                bool showWindow = false;
                if (optional) Debug.LogWarning("An optional shader package is available.");
                else if (!valid) Debug.LogWarning("Problem with shader installation.");

                if (valid || optional)
                    showWindow = sos && !shownOnce;

                if ((sos && !shownOnce) || force || swUpdateAvailable)
                    showWindow = true;

                EditorApplication.quitting -= HandleQuitEvent;
                EditorApplication.quitting += HandleQuitEvent;

                if (showWindow || calledFromMenu)
                {
                    if (!Application.isPlaying)
                    {
                        bool ignore = false;
                        if (settings != null)
                        {
                            // reset the shown once flag in the settings when the application quits
                            
                            settings.updateWindowShownOnce = true;
                            if (!calledFromMenu)
                                ignore = settings.ignoreAllErrors;
                        }
                        if (!ignore) ShaderPackageUpdater.CreateWindow();
                    }

                    if (ShaderPackageUpdater.Instance != null)
                    {
                        ShaderPackageUpdater.Instance.actionRequired = shaderActionRequired;
                        ShaderPackageUpdater.Instance.softwareActionRequired = swUpdateAvailable;
                    }
                }
            }
        }
        public static void HandleQuitEvent()
        {
            settings.updateWindowShownOnce = false;
            RLSettings.SaveRLSettingsObject(settings);
        }
    }
}
