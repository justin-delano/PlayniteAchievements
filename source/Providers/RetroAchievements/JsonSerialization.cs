using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal static class JsonHelper
    {
        private static readonly JsonSerializer _serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default(T);

            return JsonConvert.DeserializeObject<T>(json);
        }

        public static string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static T DeserializeStream<T>(Stream stream)
        {
            using (var streamReader = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                return _serializer.Deserialize<T>(jsonReader);
            }
        }

        public static void SerializeStream<T>(Stream stream, T obj)
        {
            using (var streamWriter = new StreamWriter(stream))
            using (var jsonWriter = new JsonTextWriter(streamWriter))
            {
                _serializer.Serialize(jsonWriter, obj);
            }
        }
    }
}
