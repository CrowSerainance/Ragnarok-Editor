using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace ROMapOverlayEditor.Patching
{
    public static class PatchExporterCompat
    {
        /// <summary>
        /// Attempts to export a patch zip using available PatchExporter API via reflection.
        /// </summary>
        public static void ExportPatchZip(object staging)
        {
            if (staging == null) throw new ArgumentNullException(nameof(staging));

            // Try to find ROMapOverlayEditor.Patching.PatchExporter
            var asm = typeof(PatchExporterCompat).Assembly;
            var patchExporterType =
                asm.GetType("ROMapOverlayEditor.Patching.PatchExporter", throwOnError: false) ??
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ROMapOverlayEditor.Patching.PatchExporter", false))
                    .FirstOrDefault(t => t != null);

            if (patchExporterType == null)
                throw new InvalidOperationException("PatchExporter type not found (ROMapOverlayEditor.Patching.PatchExporter).");

            // Common signatures we’ll attempt (static methods):
            // - Export(EditStaging)
            // - ExportZip(EditStaging)
            // - ExportPatch(EditStaging)
            // - ExportPatchZip(EditStaging)
            // - ExportPatchZip(EditStaging, string outPath)
            // - ExportZip(EditStaging, string outPath)
            // If any exists, we call it. If it needs outPath, we prompt SaveFileDialog.

            var methods = patchExporterType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            string[] nameCandidates =
            {
                "ExportPatchZip",
                "ExportZip",
                "ExportPatch",
                "Export"
            };

            // 1) Prefer one-arg methods (staging only)
            foreach (var name in nameCandidates)
            {
                var m = methods.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    x.GetParameters().Length == 1);

                if (m != null)
                {
                    m.Invoke(null, new[] { staging });
                    return;
                }
            }

            // 2) Try two-arg methods (staging, outPath)
            foreach (var name in nameCandidates)
            {
                var m = methods.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    x.GetParameters().Length == 2 &&
                    x.GetParameters()[1].ParameterType == typeof(string));

                if (m != null)
                {
                    var dlg = new SaveFileDialog
                    {
                        Title = "Export Patch ZIP",
                        Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
                        FileName = "patch.zip",
                        OverwritePrompt = true
                    };

                    if (dlg.ShowDialog() != true)
                        throw new OperationCanceledException("Export canceled.");

                    var outPath = dlg.FileName;
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                    m.Invoke(null, new object[] { staging, outPath });
                    return;
                }
            }

            throw new MissingMethodException(
                "No compatible PatchExporter method found. " +
                "Expected a static method named Export/ExportZip/ExportPatch/ExportPatchZip " +
                "with params (staging) or (staging, string outPath)."
            );
        }
    }
}
