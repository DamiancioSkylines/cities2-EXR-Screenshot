using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.IO;
using Colossal.Json;
using Colossal.PSI.Environment;
using EXRScreenshot.Domain;

namespace EXRScreenshot.Settings;

public static class ProfileUtils
{
    // Define the directory where profiles are stored.  This is outside the game's AssetDatabase.
    private static readonly string ProfilesDirectory = Path.Combine(
        EnvPath.kUserDataPath,
        "ModsSettings",
        nameof(EXRScreenshot), // Use the mod's name in the path
        "Profiles"
    );

    // Helper method to get the profile data directory.
    private static string GetDataDirectory() => ProfilesDirectory;

    // Helper method to get the full file path for a profile file.
    private static string GetFilePath(string filename) => Path.Combine(GetDataDirectory(), filename);

    // Helper method to ensure the profile data directory exists.
    private static bool EnsureDataDirectory() => IOUtils.EnsureDirectory(GetDataDirectory());

    // Creates a default profile.
    public static EXRScreenshotProfile CreateDefault(Setting settings)
    {
        EXRScreenshotProfile profile;
        // Try to read the default profile from a file.
        if (TryReadText(EXRScreenshotProfile.DefaultID + ".json", out var fileString) && fileString.Length > 0)
        {
            // If the default profile file exists, load it.
            profile = JSON.MakeInto<EXRScreenshotProfile>(JSON.Load(fileString));
        }
        else
        {
            // If the default profile file does not exist, create a new default profile.
            profile = new EXRScreenshotProfile
            {
                ID = EXRScreenshotProfile.DefaultID,
                Index = 0,
                Name = "Default",
            };
            // Set the shortcut from the settings.
            profile.SetValue(nameof(Setting.KeyTakeScreenshot), settings.KeyTakeScreenshot);
        }
        return profile;
    }

    // Loads all profiles from the profile data directory.
    public static List<EXRScreenshotProfile> ReadProfiles(Setting settings)
    {
        EnsureDataDirectory(); // Ensure the directory exists.
        var profiles = new List<EXRScreenshotProfile>();
        var dir = new DirectoryInfo(GetDataDirectory()); //get the directory

        if (dir.Exists) //check if the directory exists
        {
            var files = dir.GetFiles("*.json"); //get all the json files
            foreach (var fileInfo in files) //iterate throught the files
            {
                try
                {
                    var text = File.ReadAllText(fileInfo.FullName); //read the file
                    var profile = JSON.MakeInto<EXRScreenshotProfile>(JSON.Load(text)); //deserialize json
                    profiles.Add(profile); //add to the list
                }
                catch (Exception e)
                {
                    Mod.LOG.Error(e, $"Failed to read profile {fileInfo.Name}");
                }
            }
            if (profiles.FirstOrDefault(p => p.ID == EXRScreenshotProfile.DefaultID) == null) //if default profile does not exist
            {
                var defaultProfile = CreateDefault(settings); //create it
                profiles.Add(defaultProfile);  //add to the list
                Save(defaultProfile); //save it
            }
        }
        else //if directory does not exist
        {
            var defaultProfile = CreateDefault(settings); // create default
            profiles.Add(defaultProfile); // add to list
            Save(defaultProfile); // save
        }
        return profiles.OrderBy(p => p.Index).ToList(); //order the list
    }

    // Deletes a profile with the specified ID.
    public static void Delete(string id)
    {
        var file = GetFilePath(id + ".json");
        if (id.Contains("default_profile"))
        {
            return; // Prevent deletion of the default profile.
        }
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    // Saves a profile to a JSON file.
    private static void SaveText(string filename, string text)
    {
        try
        {
            File.WriteAllText(GetFilePath(filename), text);
        }
        catch (Exception e)
        {
            Mod.LOG.Error(e, $"Failed to write profile {filename}");
        }
    }

    // Tries to read text from a file.
    private static bool TryReadText(string filename, out string text)
    {
        var path = GetFilePath(filename);
        if (File.Exists(path))
        {
            text = File.ReadAllText(path);
            return true;
        }
        Mod.LOG.Info($"Tried to read {filename}, but it does not exist.");
        text = "";
        return false;
    }

    // Saves a profile.
    public static void Save(EXRScreenshotProfile profile)
    {
        EnsureDataDirectory();
        SaveText(profile.ID + ".json", JSON.Dump(profile));
    }
}
