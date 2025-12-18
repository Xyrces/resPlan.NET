using System.IO;
using MessagePack;

namespace ResPlan.Library.Data
{
    public static class PlanSerializer
    {
        /// <summary>
        /// Serializes a Plan object to MessagePack binary format.
        /// </summary>
        public static byte[] Serialize(Plan plan)
        {
            return MessagePackSerializer.Serialize(plan, ResPlanMessagePackResolver.Options);
        }

        /// <summary>
        /// Deserializes a Plan object from MessagePack binary format.
        /// </summary>
        public static Plan Deserialize(byte[] bytes)
        {
            return MessagePackSerializer.Deserialize<Plan>(bytes, ResPlanMessagePackResolver.Options);
        }

        /// <summary>
        /// Serializes a Plan object to a file.
        /// </summary>
        public static void SaveToFile(Plan plan, string filePath)
        {
            var bytes = Serialize(plan);
            File.WriteAllBytes(filePath, bytes);
        }

        /// <summary>
        /// Deserializes a Plan object from a file.
        /// </summary>
        public static Plan LoadFromFile(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            return Deserialize(bytes);
        }
    }
}
