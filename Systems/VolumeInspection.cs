using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Game.Rendering;
using Unity.Entities;

namespace EXRScreenshot.Systems
{
    public static class VolumeInspection
    {
        // Caches for reflection to improve performance during log generation
        private static readonly Dictionary<Type, FieldInfo[]> SFieldCache = new();
        private static readonly Dictionary<Type, PropertyInfo> SPropertyCache = new();

        /// <summary>
        /// Retrieves and caches all fields (public and non-public instance) for a given type.
        /// </summary>
        private static FieldInfo[] GetCachedFields(Type type)
        {
            if (!SFieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                SFieldCache[type] = fields;
            }
            return fields;
        }

        /// <summary>
        /// Retrieves and caches the "value" property info for volume parameters.
        /// </summary>
        private static PropertyInfo GetCachedValueProperty(Type type)
        {
            if (!SPropertyCache.TryGetValue(type, out var propertyInfo))
            {
                propertyInfo = type.GetProperty("value");
                SPropertyCache[type] = propertyInfo;
            }
            return propertyInfo;
        }

        /// <summary>
        /// Main entry point to generate the complete active render metadata text log.
        /// </summary>
        public static string GetActiveMetadata()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var climateRenderSystem = world?.GetExistingSystemManaged<ClimateRenderSystem>();

            var stringBuilder = new StringBuilder();
            try 
            {
                var allVolumes = GetAllCandidateVolumes(climateRenderSystem, world).ToList();

                LogEffectiveRenderVolumeStackParameters(stringBuilder, allVolumes);
                LogAllVolumesWithParameters(stringBuilder, climateRenderSystem, allVolumes);
            }
            catch (Exception exception)
            {
                stringBuilder.AppendLine($"LOGGING ERROR: {exception}");
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Safely fetches the VolumeProfile, checking for runtime-instantiated profiles used by mods like Lumina first.
        /// </summary>
        private static VolumeProfile GetSafeProfile(Volume volume)
        {
            if (volume is null) return null;
            // Mods (like Lumina) or runtime scripts can create instantiated volume profiles 
            // instead of using shared assets, so we check for those first.
            return volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
        }

        /// <summary>
        /// Computes and logs the final blended effective output of all active overlapping volumes.
        /// </summary>
        private static void LogEffectiveRenderVolumeStackParameters(StringBuilder stringBuilder, List<Volume> precomputedVolumes)
        {
            stringBuilder.AppendLine("[EFFECTIVE RENDER VOLUME STACK PARAMETERS]");
            stringBuilder.AppendLine("   (Note: Post-processing parameters below are logged for reference, but are NOT baked into EXR pixels, more info on github page)");
            // Sort active volumes by priority (ascending) so lower-priority volumes form 
            // the baseline, and higher-priority volumes can correctly blend over them sequentially using BlendValues helper class
            var activeVolumes = precomputedVolumes
                .Where(volume => volume&& volume.enabled && volume.gameObject.activeInHierarchy && volume.weight > 0 && GetSafeProfile(volume))
                .OrderBy(volume => volume.priority)
                .ToList();

            if (activeVolumes.Count == 0)
            {
                stringBuilder.AppendLine("   No active volumes found.");
                stringBuilder.AppendLine();
                return;
            }

            var effectiveMap = new Dictionary<string, Dictionary<string, object>>();

            foreach (var volume in activeVolumes)
            {
                var safeProfile = GetSafeProfile(volume);
                foreach (var component in safeProfile.components)
                {
                    if (component is null || !component.active) continue;

                    var componentName = component.GetType().Name;
                    if (!effectiveMap.TryGetValue(componentName, out var parameterDictionary))
                    {
                        parameterDictionary = new Dictionary<string, object>();
                        effectiveMap[componentName] = parameterDictionary;
                    }

                    foreach (var field in GetCachedFields(component.GetType()))
                    {
                        // Skip fields that aren't volume parameters
                        if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType)) continue;
                        
                        // Skip parameters that do not have overrideState enabled
                        if (field.GetValue(component) is not VolumeParameter{ overrideState: true } parameter) continue;
                        
                        var valueProperty = GetCachedValueProperty(field.FieldType);
                        var value = valueProperty?.GetValue(parameter);
                                
                        // Clean up field names by stripping the "m_" prefix using range syntax [2..]
                        var cleanName = field.Name.StartsWith("m_") ? field.Name[2..] : field.Name;

                        if (parameterDictionary.TryGetValue(cleanName, out var existingValue))
                        {
                            parameterDictionary[cleanName] = BlendValues(existingValue, value, volume.weight);
                        }
                        else
                        {
                            parameterDictionary[cleanName] = value;
                        }
                    }
                }
            }

            foreach (var componentKvp in effectiveMap.OrderBy(entry => entry.Key))
            {
                if (componentKvp.Value.Count == 0) continue;

                stringBuilder.AppendLine($"   Component: {componentKvp.Key}");
                foreach (var parameterKvp in componentKvp.Value.OrderBy(entry => entry.Key))
                {
                    stringBuilder.AppendLine($"      {parameterKvp.Key} = {parameterKvp.Value}");
                }
            }

            stringBuilder.AppendLine();
        }

        private static void LogAllVolumesWithParameters(StringBuilder stringBuilder, ClimateRenderSystem climateRenderSystem, List<Volume> precomputedVolumes)
        {
            stringBuilder.AppendLine("[SCENE VOLUMES LIST]");

            foreach (var volume in precomputedVolumes.OrderByDescending(volumeItem => volumeItem.priority))
            {
                var safeProfile = GetSafeProfile(volume);
                if (safeProfile is null) continue;
                
                // Skip the built-in procedural weather volume managed by ClimateRenderSystem
                if (climateRenderSystem?.climateControlVolume is not null && volume == climateRenderSystem.climateControlVolume) continue;

                stringBuilder.AppendLine($"   [Volume: {volume.gameObject.name}] Priority: {volume.priority} | Weight: {volume.weight} | Enabled: {volume.enabled}");

                foreach (var component in safeProfile.components.Where(component => component is not null && component.active))
                {
                    stringBuilder.AppendLine($"      Component: {component.GetType().Name}");
                    LogComponentOverrides(stringBuilder, component, "         ");
                }
                stringBuilder.AppendLine();
            }
        }
        
        /// <summary>
        /// Gathers all candidate volumes from the scene, climate system, and Photo Mode ECS systems.
        /// </summary>
        private static IEnumerable<Volume> GetAllCandidateVolumes(ClimateRenderSystem climateRenderSystem, World world)
        {
            var volumeSet = new HashSet<Volume>();

            // Include the procedural climate control volume created by ClimateRenderSystem
            if (climateRenderSystem?.climateControlVolume is { } controlVolume)
            {
                volumeSet.Add(controlVolume);
            }

            // Include all standard volumes found in the scene graph (active or inactive)
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (volume) volumeSet.Add(volume);
            }

            // Early exit if world or systems are missing (handles the ECS check once)
            if (world?.Systems == null) return volumeSet;

            // Scan internal Photo Mode ECS systems for active internal volume references
            foreach (var system in world.Systems)
            {
                if (system == null || !system.GetType().Name.Contains("Photo")) continue;

                foreach (var field in GetCachedFields(system.GetType()))
                {
                    if (!typeof(Volume).IsAssignableFrom(field.FieldType)) continue;
                    if (field.GetValue(system) is not Volume photoVolume || !photoVolume) continue;

                    volumeSet.Add(photoVolume);
                }
            }

            return volumeSet;
        }
        
        /// <summary>
        /// Inspects a volume component via reflection and logs its overridden parameters.
        /// </summary>
        private static void LogComponentOverrides(StringBuilder stringBuilder, VolumeComponent component, string indent = "      ")
        {
            foreach (var field in GetCachedFields(component.GetType()))
            {
                // IsAssignableFrom checks if the field's type is a VolumeParameter 
                // or any subclass (e.g., FloatParameter, ColorParameter, Vector3Parameter)
                if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType)) continue;

                if (field.GetValue(component) is not VolumeParameter { overrideState: true } parameter) continue;

                var valueProperty = GetCachedValueProperty(field.FieldType);
                var value = valueProperty?.GetValue(parameter);
        
                // Clean up field names by stripping the "m_" prefix using range syntax [2..]
                var cleanName = field.Name.StartsWith("m_") ? field.Name[2..] : field.Name;
                stringBuilder.AppendLine($"{indent}{cleanName} = {value}");
            }
        }

        /// <summary>
        /// Blends parameter values mathematically based on volume weights.
        /// </summary>
        private static object BlendValues(object oldValue, object newValue, float weight)
        {
            if (weight >= 1f || oldValue == null) return newValue;

            return oldValue switch
            {
                float floatOld when newValue is float floatNew => Mathf.Lerp(floatOld, floatNew, weight),
                Color colorOld when newValue is Color colorNew => Color.Lerp(colorOld, colorNew, weight),
                Vector4 vector4Old when newValue is Vector4 vector4New => Vector4.Lerp(vector4Old, vector4New, weight),
                Vector3 vector3Old when newValue is Vector3 vector3New => Vector3.Lerp(vector3Old, vector3New, weight),
                Vector2 vector2Old when newValue is Vector2 vector2New => Vector2.Lerp(vector2Old, vector2New, weight),
                _ => newValue
            };
        }
    }
}