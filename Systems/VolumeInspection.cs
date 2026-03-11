using System;
using System.Reflection;
using Colossal.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace EXRScreenshot.Systems
{
    public static class VolumeInspection
    {
        public static void LogGlobalStack()
        {
            try 
            {
                Mod.LOG.Info("========= DEEP RENDERING INSPECTION =========");

                // 1. Check Global Stack (Effective Output)
                VolumeStack stack = VolumeManager.instance.stack;
                if (stack != null)
                {
                    var tonemapping = stack.GetComponent<Tonemapping>();
                    var exposure = stack.GetComponent<Exposure>();
                    var colorAdjust = stack.GetComponent<ColorAdjustments>();
                    var wb = stack.GetComponent<WhiteBalance>();

                    Mod.LOG.Info("[GLOBAL STACK EFFECTIVE STATE]");
                    Mod.LOG.Info($"   --> Tonemapper Mode: {tonemapping?.mode?.value}");
                    Mod.LOG.Info($"   --> Exposure: {exposure?.fixedExposure?.value} (Mode: {exposure?.mode?.value})");
                    Mod.LOG.Info($"   --> Saturation: {colorAdjust?.saturation?.value} | Contrast: {colorAdjust?.contrast?.value}");
                    Mod.LOG.Info($"   --> Temp: {wb?.temperature?.value} | Tint: {wb?.tint?.value}");
                }

                // 2. Comprehensive Volume Scan
                Volume[] allVolumes = Object.FindObjectsOfType<Volume>();
                Mod.LOG.Info($"[Analyst] Scanning {allVolumes.Length} scene volumes...");

                foreach (var vol in allVolumes)
                {
                    if (vol.sharedProfile == null) continue;

                    Mod.LOG.Info($"[Volume: {vol.name}] Priority: {vol.priority} | Global: {vol.isGlobal} | Weight: {vol.weight}");

                    foreach (var component in vol.sharedProfile.components)
                    {
                        // Log ALL components to see what the game is hiding
                        var typeName = component.GetType().Name;
                        Mod.LOG.Info($"   -> Component: {typeName} (Active: {component.active})");
                        
                        if (component.active)
                        {
                            LogComponentFields(component);
                        }
                    }
                }

                Mod.LOG.Info("========= INSPECTION COMPLETE =========");
            }
            catch (Exception e)
            {
                Mod.LOG.Error($"[Analyst] Error: {e.Message}");
            }
        }

        private static void LogComponentFields(VolumeComponent comp)
        {
            var fields = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
                {
                    if (field.GetValue(comp) is VolumeParameter { overrideState: true } param)
                    {
                        var valueProp = field.FieldType.GetProperty("value");
                        var val = valueProp?.GetValue(param);
                        var fieldName = field.Name.StartsWith("m_") ? field.Name.Substring(2) : field.Name;
                        
                        if (val is Texture t)
                            Mod.LOG.Info($"      -> {fieldName} [Texture] = {t.name}");
                        else if (val != null)
                            Mod.LOG.Info($"      -> {fieldName} = {val}");
                    }
                }
            }
        }
    }
}