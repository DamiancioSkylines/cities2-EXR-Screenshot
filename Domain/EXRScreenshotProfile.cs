using System;
using System.Collections.Generic;

namespace EXRScreenshot.Domain
{
    public class EXRScreenshotProfile
    {
        public const string DefaultID = "default_profile";
        public string ID { get; set; } = DefaultID;
        public string Name { get; set; }
        public int Index { get; set; }
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        public EXRScreenshotProfile()
        {
            _values = new Dictionary<string, object>(); // Initialize the dictionary in the constructor
        }

        public static EXRScreenshotProfile Create(string name, int index) => new EXRScreenshotProfile(Guid.NewGuid(), index, name);
        
        private EXRScreenshotProfile(Guid id, int index, string name) : this()
        {
            ID = id.ToString();
            Index = index;
            Name = name;
        }

        public void SetValue(string key, object value)
        {
            if (_values.ContainsKey(key))
            {
                _values[key] = value;
            }
            else
            {
                _values.Add(key, value);
            }
        }

        public object GetValue(string key)
        {
            if (_values.TryGetValue(key, out object value))
            {
                return value;
            }
            return null; // Or throw an exception if the key is not found
        }
    }
}