using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitBatchNWCExport.RevitServer
{
    /// <summary>
    /// Список серверов, который Revit показывает в окне открытия:
    /// C:\ProgramData\Autodesk\Revit Server {год}\Config\RSN.ini, по имени сервера в строке.
    /// </summary>
    public static class RevitServerConfig
    {
        public static string RsnPath(string revitVersion) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "Revit Server " + revitVersion, "Config", "RSN.ini");

        public static List<string> GetServers(string revitVersion)
        {
            var path = RsnPath(revitVersion);
            if (!File.Exists(path)) return new List<string>();
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith(";") && !l.StartsWith("#"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
