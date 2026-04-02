using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;
using Game.Prefabs.Climate;
using Game.Simulation;
using Game.Rendering;
using Unity.Entities;

namespace EXRScreenshot.Systems
{
    public static class VolumeInspection
    {
        public static string GetActiveMetadata()
        {
            var sb = new StringBuilder();
            try 
            {
                sb.AppendLine("========= DEEP RENDERING INSPECTION =========");

                // 1. LOG LIVE CLIMATE BLENDING (Matches Developer UI Climate Tab)
                LogLiveClimateProperties(sb);

                // 2. LOG SIMULATION STATE
                LogClimateState(sb);

                // 3. CHECK EFFECTIVE OUTPUT (Final Stack)
                LogEffectiveStack(sb);

                // 4. SCENE VOLUMES
                var volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
                sb.AppendLine($"\n[Analyst] Scanning {volumes.Length} scene volumes...\n");

                foreach (var vol in volumes.OrderByDescending(v => v.priority))
                {
                    if (vol.profile == null) continue;
                    sb.AppendLine($"[Volume: {vol.gameObject.name}] Priority: {vol.priority} | Weight: {vol.weight}");
                    foreach (var component in vol.profile.components)
                    {
                        LogComponentOverrides(sb, component);
                    }
                    sb.AppendLine();
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"LOGGING ERROR: {e.Message}");
            }

            return sb.ToString();
        }

        private static void LogLiveClimateProperties(StringBuilder sb)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var renderSystem = world?.GetExistingSystemManaged<ClimateRenderSystem>();
            if (renderSystem == null)
            {
                sb.AppendLine("[LIVE CLIMATE] ClimateRenderSystem not found.");
                return;
            }

            sb.AppendLine("[LIVE CLIMATE RENDER VALUES (BLENDED)]");
            sb.AppendLine($"   From prefabs: {renderSystem.fromWeatherPrefabs.Count}  |  To prefabs: {renderSystem.toWeatherPrefabs.Count}");

            LogWeatherPrefabList(sb, "FROM", renderSystem.fromWeatherPrefabs);
            LogWeatherPrefabList(sb, "TO",   renderSystem.toWeatherPrefabs);
            sb.AppendLine();
        }

        private static void LogWeatherPrefabList(StringBuilder sb, string label, IReadOnlyList<WeatherPrefab> prefabs)
        {
            for (int i = 0; i < prefabs.Count; i++)
            {
                var prefab = prefabs[i];
                sb.AppendLine($"   [{label}][{i}] WeatherPrefab: {prefab.name}");

                foreach (var prop in prefab.overrideableProperties)
                {
                    if (!prop.active) continue;

                    sb.AppendLine($"      Component: {prop.GetType().Name}");

                    var fields = prop.GetType().GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    foreach (var field in fields)
                    {
                        if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType)) continue;
                        var param = field.GetValue(prop) as VolumeParameter;
                        if (param == null || !param.overrideState) continue;

                        var valueProp = field.FieldType.GetProperty("value");
                        var val = valueProp?.GetValue(param);
                        var name = field.Name.StartsWith("m_") ? field.Name.Substring(2) : field.Name;
                        sb.AppendLine($"         {name} = {val}");
                    }
                }
            }
        }

        private static void LogClimateState(StringBuilder sb)
        {
            var climateSystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<ClimateSystem>();
            if (climateSystem != null)
            {
                sb.AppendLine("[CLIMATE & SIMULATION]");
                sb.AppendLine($"   -> Temperature: {climateSystem.temperature.value:F2} °C");
                sb.AppendLine($"   -> Precipitation: {climateSystem.precipitation.value:F2}");
                sb.AppendLine($"   -> Cloudiness: {climateSystem.cloudiness.value:F2}");
                sb.AppendLine();
            }
        }

        private static void LogEffectiveStack(StringBuilder sb)
        {
            sb.AppendLine("[GLOBAL STACK EFFECTIVE STATE]");
            VolumeStack stack = VolumeManager.instance.stack;
            if (stack == null) return;

            // Always log
            var ca = stack.GetComponent<ColorAdjustments>();
            if (ca != null)
            {
                sb.AppendLine($"   -> Color Adjustments:");
                sb.AppendLine($"      - Post Exposure: {ca.postExposure.value} EV");
                sb.AppendLine($"      - Contrast: {ca.contrast.value}");
            }
            sb.AppendLine();
        }

        private static void LogComponentOverrides(StringBuilder sb, VolumeComponent comp)
        {
            var fields = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
                {
                    var param = field.GetValue(comp) as VolumeParameter;
                    if (param != null && param.overrideState)
                    {
                        var valueProp = field.FieldType.GetProperty("value");
                        var val = valueProp?.GetValue(param);
                        sb.AppendLine($"      -> {field.Name.Replace("m_", "")} = {val}");
                    }
                }
            }
        }
    }
}