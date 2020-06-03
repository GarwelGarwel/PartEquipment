using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PartEquipment
{
    /// <summary>
    /// Log levels:
    /// <list type="bullet">
    /// <item><definition>None: do not log</definition></item>
    /// <item><definition>Error: log only errors</definition></item>
    /// <item><definition>Important: log only errors and important information</definition></item>
    /// <item><definition>Debug: log all information</definition></item>
    /// </list>
    /// </summary>
    public enum LogLevel { None = 0, Error, Important, Debug };

    /// <summary>
    /// Provides general static methods and fields for UrgentContracts
    /// </summary>
    public static class Core
    {
        static Dictionary<string, double> partVolumesCache = new Dictionary<string, double>();

        /// <summary>
        /// Current <see cref="LogLevel"/>: either Debug or Important
        /// </summary>
        public static LogLevel Level => LogLevel.Debug;//.Instance.DebugMode ? LogLevel.Debug : LogLevel.Important;

        /// <summary>
        /// Returns true if the Part can be equipped in a container
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool IsEquipmentItem(this Part p) => p != null && p.HasModuleImplementing<ModuleEquipmentItem>();

        /// <summary>
        /// Returns true if the AvailablePart can be equipped in a container
        /// </summary>
        /// <param name="ap"></param>
        /// <returns></returns>
        public static bool IsEquipmentItem(this AvailablePart ap) => IsEquipmentItem(ap.partPrefab);

        /// <summary>
        /// Shortcut to get the amount of a specific resource in a part, or 0 if it's absent
        /// </summary>
        /// <param name="p"></param>
        /// <param name="resourceName"></param>
        /// <returns></returns>
        public static double GetResourceAmount(this Part p, string resourceName)
            => p.Resources.Contains(resourceName) ? p.Resources.Get(resourceName).amount : 0;

        public static double GetPartVolume(this Part p)
        {
            //if (partVolumesCache.ContainsKey(p.name))
            //    return partVolumesCache[p.name];
            //Core.Log("Can't find volume of " + p.name + " in the cache (" + partVolumesCache.Count + " records).");
            return PartUtils.CalculatePartVolume(p);
            //double v = p.FindModulesImplementing<ModuleEquipmentItem>().Sum(mod => mod.Volume);
            //return v > 0 ? v : PartUtils.CalculatePartVolume(p);
        }

        public static double GetPartVolume(this AvailablePart ap) => PartUtils.CalculatePartVolume(ap);

        public static string GetString(this ConfigNode n, string key, string defaultValue = null) => n.HasValue(key) ? n.GetValue(key) : defaultValue;

        public static double GetDouble(this ConfigNode n, string key, double defaultValue = 0)
            => double.TryParse(n.GetValue(key), out double res) ? res : defaultValue;

        public static int GetInt(this ConfigNode n, string key, int defaultValue = 0)
            => int.TryParse(n.GetValue(key), out int res) ? res : defaultValue;

        public static bool GetBool(this ConfigNode n, string key, bool defaultValue = false)
            => bool.TryParse(n.GetValue(key), out bool res) ? res : defaultValue;

        public static void DumpParts()
        {
            if (PartLoader.LoadedPartsList == null || PartLoader.LoadedPartsList.Count == 0)
            {
                Core.Log("PartLoader.LoadedPartsList is null or empty.");
                return;
            }
            string msg = "Name,Category,Dry Mass,EC,Monoprop,LFO,Command Module,Crew,Volume";
            foreach (AvailablePart ap in PartLoader.LoadedPartsList.Where(ap => ap.IsEquipmentItem()))
            {
                Part p = ap.partPrefab;
                msg += "\n" + ap.name + "," + ap.category + "," + p.mass + "," + p.GetResourceAmount("ElectricCharge") + "," + p.GetResourceAmount("MonoPropellant") + "," + (p.GetResourceAmount("LiquidFuel") + p.GetResourceAmount("Oxydizer")) + "," + (p.HasModuleImplementing<ModuleCommand>() ? 1 : 0) + "," + p.CrewCapacity + "," + p.GetPartVolume();
            }
            Core.Log("All parts:\n" + msg);
        }

        /// <summary>
        /// Returns true if current logging allows logging of messages at messageLevel
        /// </summary>
        /// <param name="messageLevel"></param>
        /// <returns></returns>
        public static bool IsLogging(LogLevel messageLevel = LogLevel.Debug) => messageLevel <= Level;

        /// <summary>
        /// Write into output_log.txt
        /// </summary>
        /// <param name="message">Text to log</param>
        /// <param name="messageLevel"><see cref="LogLevel"/> of the entry</param>
        public static void Log(string message, LogLevel messageLevel = LogLevel.Debug)
        {
            if (IsLogging(messageLevel) && message.Length != 0)
                Debug.Log("[PartEquipment] " + (messageLevel == LogLevel.Error ? "ERROR: " : "") + message);
        }
    }
}
