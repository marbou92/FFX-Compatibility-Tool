using System;
using System.IO;

namespace FfxTool.Core
{
    /// <summary>
    /// Where the seed tables live. The portable build is one single .exe:
    /// plugin_table.json and effect_names.json ship embedded inside
    /// FfxTool.Core.dll (see the csproj's EmbeddedResource entries) and
    /// the loaders read them from there. An explicit path always wins
    /// (tests, power users), and the on-disk data\ folder stays as a
    /// last-resort fallback so anything built without the resources
    /// still works exactly like it did before.
    /// </summary>
    internal static class EmbeddedData
    {
        /// <summary>Reads a seed table: the explicit path when one is
        /// given, else the embedded resource, else the old data\ folder
        /// beside the binary. Callers keep their degrade-to-empty contract
        /// — any failure surfaces as the exception they already catch.</summary>
        public static string ReadJson(string explicitPath, string resourceName, string dataFileName)
        {
            if (explicitPath != null) return File.ReadAllText(explicitPath);

            var asm = typeof(EmbeddedData).Assembly;
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            return File.ReadAllText(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "data", dataFileName));
        }
    }
}
