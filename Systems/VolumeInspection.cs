using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.Prefabs.Climate;
using Game.Rendering;
using Game.Simulation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EXRScreenshot.Systems
{
    public static class VolumeInspection
    {
        // Caches for reflection to improve performance during metadata generation
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
        private static readonly Dictionary<Type, PropertyInfo> PropertyCache = new();
        
        private static DayNightCycleData _cachedDayNightData;
        private static bool _isDayNightDataSearched;

        /// <summary>
        /// Retrieves and caches all fields (public and non-public instance) for a given type.
        /// </summary>
        private static FieldInfo[] GetCachedFields(Type type)
        {
            if (!FieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldCache[type] = fields;
            }
            return fields;
        }

        /// <summary>
        /// Retrieves and caches the "value" property info for volume parameters.
        /// </summary>
        private static PropertyInfo GetCachedValueProperty(Type type)
        {
            if (!PropertyCache.TryGetValue(type, out var propertyInfo))
            {
                propertyInfo = type.GetProperty("value");
                PropertyCache[type] = propertyInfo;
            }
            return propertyInfo;
        }

        /// <summary>
        /// Formats field names by stripping the "m_" prefix
        /// </summary>
        private static string GetCleanName(MemberInfo member)
        {
            var name = member.Name;
            return name.StartsWith("m_", StringComparison.Ordinal) && name.Length > 2 ? name[2..] : name;
        }

        /// <summary>
        /// Main entry point to generate the complete active render metadata text log.
        /// </summary>
        public static string GetActiveMetadata()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var climateRenderSystem = world?.GetExistingSystemManaged<ClimateRenderSystem>();
            var climateSystem = world?.GetExistingSystemManaged<ClimateSystem>();
            var timeSystem = world?.GetExistingSystemManaged<TimeSystem>();
            var planetarySystem = world?.GetExistingSystemManaged<PlanetarySystem>();

            var stringBuilder = new StringBuilder();
            try 
            {
                var allVolumes = GetAllCandidateVolumes(climateRenderSystem, world).ToList();
                
                LogEffectiveRenderVolumeStackParameters(stringBuilder, allVolumes);
                LogAllVolumesWithParameters(stringBuilder, allVolumes);
                LogClimateSeasonAndTime(stringBuilder, climateSystem, climateRenderSystem, timeSystem, planetarySystem);
                LogDayNightCycleData(stringBuilder, world);
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
            // the baseline, and higher priority volumes can correctly blend over them sequentially using BlendValues helper class
            var activeVolumes = precomputedVolumes
                .Where(volume => volume is not null && volume.enabled && volume.gameObject.activeInHierarchy && volume.weight > 0f)
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
                if (safeProfile is null) continue;

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
                        if (field.GetValue(component) is not VolumeParameter { overrideState: true } parameter) continue;
                        
                        var valueProperty = GetCachedValueProperty(field.FieldType);
                        var value = valueProperty?.GetValue(parameter);
                        var cleanName = GetCleanName(field);

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
                    stringBuilder.AppendLine($"      {parameterKvp.Key} = {FormatParameterValue(parameterKvp.Value)}");
                }
            }

            stringBuilder.AppendLine();
        }

        private static void LogAllVolumesWithParameters(StringBuilder stringBuilder, List<Volume> precomputedVolumes)
        {
            stringBuilder.AppendLine("[SCENE VOLUMES LIST]");

            foreach (var volume in precomputedVolumes.OrderByDescending(volumeItem => volumeItem.priority))
            {
                var safeProfile = GetSafeProfile(volume);
                if (safeProfile is null) continue;

                stringBuilder.AppendLine($"   [Volume: {volume.gameObject.name}] Priority: {volume.priority} | Weight: {volume.weight} | Enabled: {volume.enabled}");

                foreach (var component in safeProfile.components)
                {
                    if (component is null || !component.active) continue;

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
                if (volume is not null) volumeSet.Add(volume);
            }

            // Early exit if world or systems are missing (handles the ECS check once)
            if (world?.Systems is null) return volumeSet;

            // Scan internal Photo Mode ECS systems for active internal volume references
            foreach (var system in world.Systems)
            {
                if (system is null) continue;

                foreach (var field in GetCachedFields(system.GetType()))
                {
                    if (!typeof(Volume).IsAssignableFrom(field.FieldType)) continue;
                    
                    if (field.GetValue(system) is Volume photoVolume)
                    {
                        volumeSet.Add(photoVolume);
                    }
                }
            }

            return volumeSet;
        }
        
        /// <summary>
        /// Gathers and logs simulation climate properties, in-game time, date, and coordinates.
        /// </summary>
        private static void LogClimateSeasonAndTime(StringBuilder stringBuilder, ClimateSystem climateSystem, ClimateRenderSystem climateRenderSystem, TimeSystem timeSystem, PlanetarySystem planetarySystem)
        {
            stringBuilder.AppendLine("[TIME, COORDINATE, SEASON/WEATHER]");
            
            if (timeSystem is not null)
            {
                stringBuilder.AppendLine($"   Current Date Time = {timeSystem.GetCurrentDateTime()}");
                stringBuilder.AppendLine($"   normalizedTime = {timeSystem.normalizedTime:F3}");
                stringBuilder.AppendLine($"   normalizedDate = {timeSystem.normalizedDate:F3}");
            }
            
            if (planetarySystem is not null)
            {
                stringBuilder.AppendLine($"   latitude = {planetarySystem.latitude}");
                stringBuilder.AppendLine($"   longitude = {planetarySystem.longitude}");
            }

            if (climateSystem is not null)
            {
                stringBuilder.AppendLine($"   wind = {climateSystem.wind}");
                stringBuilder.AppendLine($"   hail = {climateSystem.hail}");
                stringBuilder.AppendLine($"   rainbow = {climateSystem.rainbow}");
                stringBuilder.AppendLine($"   precipitation = {(float)climateSystem.precipitation}");
                stringBuilder.AppendLine($"   temperature = {(float)climateSystem.temperature}");
                stringBuilder.AppendLine($"   cloudiness = {(float)climateSystem.cloudiness}");
                stringBuilder.AppendLine($"   aurora = {(float)climateSystem.aurora}");
                stringBuilder.AppendLine($"   fog = {(float)climateSystem.fog}");
                stringBuilder.AppendLine($"   isRaining = {climateSystem.isRaining}");
                stringBuilder.AppendLine($"   isSnowing = {climateSystem.isSnowing}");
                stringBuilder.AppendLine($"   isPrecipitating = {climateSystem.isPrecipitating}");
                stringBuilder.AppendLine($"   classification = {climateSystem.classification}");
                stringBuilder.AppendLine($"   currentSeasonName = {climateSystem.currentSeasonName ?? "null"}");
            }
            else
            {
                stringBuilder.AppendLine("   ClimateSystem (Simulation) not found in current world.");
            }
            
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("[WEATHER PREFABS & SEASON OVERRIDES FOR CLIMATE CONTROL]");
            stringBuilder.AppendLine("   (Note: These prefabs dynamically drive the parameters of the active ClimateControlVolume above)");

            foreach (var weatherPrefab in climateRenderSystem?.fromWeatherPrefabs ?? Enumerable.Empty<WeatherPrefab>())
            {
                if (weatherPrefab is null) continue;

                stringBuilder.AppendLine($"   Weather Prefab: {weatherPrefab.name}"); 
            
                if (weatherPrefab.overrideableProperties is null) continue;
                foreach (var property in weatherPrefab.overrideableProperties)
                {
                    if (property is null) continue;
                    stringBuilder.AppendLine($"      Override Component: {property.name}");
                    LogOverrideComponentProperties(stringBuilder, property, "            ");
                }
            }

            stringBuilder.AppendLine();
        }
        
        /// <summary>
        /// Gathers and logs Day/Night cycle parameters, sun angle thresholds, and active LUT texture assets.
        /// </summary>
        private static void LogDayNightCycleData(StringBuilder stringBuilder, World world)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("[DAY/NIGHT CYCLE]");
            stringBuilder.AppendLine("   (Note: Defines sun angle thresholds, exposure caps, time filters, and 3D LUT textures that are never enabled afaik)");

            if (_cachedDayNightData is null && !_isDayNightDataSearched)
            {
                _cachedDayNightData = FindDayNightCycleDataFromWorldSystems(world);
                _isDayNightDataSearched = true;
            }

            if (_cachedDayNightData is null)
            {
                stringBuilder.AppendLine("   DayNightCycleData asset not found in memory.");
                return;
            }

            stringBuilder.AppendLine($"   Asset Name: {_cachedDayNightData.name}");

            foreach (var field in GetCachedFields(_cachedDayNightData.GetType()))
            {
                var value = field.GetValue(_cachedDayNightData);
                var cleanName = GetCleanName(field);

                if (value is Texture3D tex3d)
                {
                    stringBuilder.AppendLine($"      {cleanName} = {tex3d.name}");
                }
                else
                {
                    stringBuilder.AppendLine($"      {cleanName} = {value ?? "null"}");
                }
            }

            stringBuilder.AppendLine();
        }

        /// <summary>
        /// Discovers DayNightCycleData purely via managed C# system references in RAM.
        /// Completely avoids Unity engine C++ scene/resource searches.
        /// </summary>
        private static DayNightCycleData FindDayNightCycleDataFromWorldSystems(World world)
        {
            if (world?.Systems is null) return null;

            // Fast C# reflection walk across all active managed DOTS Systems registered in the World
            foreach (var system in world.Systems)
            {
                if (system is null) continue;

                foreach (var field in GetCachedFields(system.GetType()))
                {
                    if (typeof(DayNightCycleData).IsAssignableFrom(field.FieldType))
                    {
                        if (field.GetValue(system) is DayNightCycleData data)
                        {
                            return data;
                        }
                    }
                }
            }

            return null;
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
                var cleanName = GetCleanName(field);

                stringBuilder.AppendLine($"{indent}{cleanName} = {FormatParameterValue(value)}");
            }
        }
        
        private static void LogOverrideComponentProperties(StringBuilder stringBuilder, OverrideablePropertiesComponent property, string indent = "         ")
        {
            foreach (var field in GetCachedFields(property.GetType()))
            {
                var value = field.GetValue(property);
                var cleanName = GetCleanName(field);

                stringBuilder.AppendLine($"{indent}{cleanName} = {value ?? "null"}");
            }
        }

        /// <summary>
        /// Blends parameter values mathematically based on volume weights.
        /// </summary>
        private static object BlendValues(object oldValue, object newValue, float weight)
        {
            if (weight >= 1f || oldValue is null) return newValue;

            return oldValue switch
            {
                float floatOld when newValue is float floatNew => Mathf.Lerp(floatOld, floatNew, weight),
                Color colorOld when newValue is Color colorNew => Color.Lerp(colorOld, colorNew, weight),
                TextureCurve curveOld when newValue is TextureCurve curveNew => BlendCurves(curveOld, curveNew, weight),
                AnimationCurve curveOld when newValue is AnimationCurve curveNew => BlendCurves(curveOld, curveNew, weight),
                Vector4 vector4Old when newValue is Vector4 vector4New => Vector4.Lerp(vector4Old, vector4New, weight),
                Vector3 vector3Old when newValue is Vector3 vector3New => Vector3.Lerp(vector3Old, vector3New, weight),
                Vector2 vector2Old when newValue is Vector2 vector2New => Vector2.Lerp(vector2Old, vector2New, weight),
                _ => newValue
            };
        }

        /// <summary>
        /// Blends TextureCurve values mathematically in normalized range based on volume weights.
        /// Range for TextureCurve should be always 0-1 while AnimationCurve can have any range really
        /// </summary>
        private static TextureCurve BlendCurves(TextureCurve oldCurve, TextureCurve newCurve, float weight)
        {
            if (weight >= 1f || oldCurve is null) return null;
            if (newCurve is null) return oldCurve;
            
            var oldKeysLength = oldCurve.length;
            var newKeysLength = newCurve.length;
            var sampleCount = Math.Max(Math.Max(oldKeysLength, newKeysLength), 2);
            var keys = new Keyframe[sampleCount];
            
            for (var i = 0; i < sampleCount; i++)
            {
                var normalizedCurveTime = i / ((float)sampleCount - 1);
                var evaluatedOld = oldCurve.Evaluate(normalizedCurveTime);
                var evaluatedNew = newCurve.Evaluate(normalizedCurveTime);
                var blendedValue = Mathf.Lerp(evaluatedOld, evaluatedNew, weight);
                keys[i] = new Keyframe(normalizedCurveTime, blendedValue);
            }
            
            return new TextureCurve(keys, 0.5f, false, new Vector2(0f,1f));

        }
        
        /// <summary>
        /// Blends AnimationCurve values mathematically in normalized range based on volume weights.
        /// </summary>
        private static AnimationCurve BlendCurves(AnimationCurve oldCurve, AnimationCurve newCurve, float weight)
        {
            if (weight >= 1f || oldCurve is null) return null;
            if (newCurve is null) return oldCurve;
            
            var minTime = Mathf.Min(oldCurve.keys[0].time, newCurve.keys[0].time);
            var maxTime = Mathf.Max(oldCurve.keys[oldCurve.length - 1].time, newCurve.keys[newCurve.length - 1].time);

            if (Mathf.Approximately(minTime, maxTime))
            {
                minTime = 0;
                maxTime = 1;
            }
            
            var oldKeysLength = oldCurve.length;
            var newKeysLength = newCurve.length;
            var sampleCount = Math.Max(Math.Max(oldKeysLength, newKeysLength), 2);
            var keys = new Keyframe[sampleCount];
            
            for (var i = 0; i < sampleCount; i++)
            {
                var normalizedCurveTime = i / ((float)sampleCount - 1);
                var evaluateTime = Mathf.Lerp(minTime, maxTime, normalizedCurveTime);
                var evaluatedOld = oldCurve.Evaluate(evaluateTime);
                var evaluatedNew = newCurve.Evaluate(evaluateTime);
                var blendedValue = Mathf.Lerp(evaluatedOld, evaluatedNew, weight);
                keys[i] = new Keyframe(evaluateTime, blendedValue);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>
        /// Format values into strings
        /// </summary>
        private static string FormatParameterValue(object value)
        {
            switch (value)
            {
                case TextureCurve textCurve:
                    return FormatCurve(textCurve);
                case AnimationCurve animationCurve:
                    return FormatCurve(animationCurve);
                default:
                    return value?.ToString() ?? "null";
            }
        }
        
        /// <summary>
        /// Format TextureCurve with its keys into strings
        /// </summary>
        private static string FormatCurve(TextureCurve textureCurve)
        {
            if (textureCurve is null) return "null";
            var sb = new StringBuilder();
            sb.Append($"TextureCurve Keys: {textureCurve.length} ");
            
            // Go through all keyframes of the curve hope it's not to many
            for (var i = 0; i < textureCurve.length; i++)
            {
                var keyframe = textureCurve[i];
                sb.Append($" [time: {keyframe.time:F2}, value: {keyframe.value:F2}]");
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Format AnimationCurve with its keys into strings
        /// </summary>
        private static string FormatCurve(AnimationCurve animationCurve)
        {
            if (animationCurve is null) return "null";
            var sb = new StringBuilder();
            sb.Append($"AnimationCurve Keys: {animationCurve.length} ");
            
            // Go through all keyframes of the curve hope it's not to many
            for (var i = 0; i < animationCurve.length; i++)
            {
                var keyframe = animationCurve[i];
                sb.Append($" [time: {keyframe.time:F2}, value: {keyframe.value:F2}]");
            }
            return sb.ToString();
        }
    }
}