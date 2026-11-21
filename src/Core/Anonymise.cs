using System;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;

namespace VintageDoctor
{
    /// <summary>Takes addresses, player ids and user folders out of text before it goes into a report someone may post.</summary>
    public static class Anonymise
    {
        // Not preceded by "Version=" or "v" (a four-part version number is not an address).
        static readonly Regex Ip = new Regex(@"(?<![Vv]ersion=|\bv|\d\.)\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b(?!\.\d)", RegexOptions.Compiled);
        static readonly Regex Uid = new Regex(@"\b(?=[A-Za-z0-9+/_-]*\d)(?=[A-Za-z0-9+/_-]*[a-z])(?=[A-Za-z0-9+/_-]*[A-Z])[A-Za-z0-9+/_-]{20,}\b", RegexOptions.Compiled);
        static readonly Regex HomePath = new Regex(@"(/home/|/Users/|[A-Za-z]:\\Users\\)[^/\\\s""']+", RegexOptions.Compiled);

        public static string Line(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            try
            {
                line = Ip.Replace(line, "<address>");
                line = HomePath.Replace(line, "$1<user>"); // before the id pass, so a user folder isn't eaten as an id
                line = Uid.Replace(line, "<id>");
            }
            catch { }
            return line;
        }

        /// <summary>A file path with the user's own folder taken out, the way the game writes paths in its logs.</summary>
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return HomePath.Replace(GamePaths.RemoveUsername(path), "$1<user>"); }
            catch { return path; }
        }

        /// <summary>A path as the player would look for it: relative to the game's data folder when it is inside it.</summary>
        public static string Short(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string data = System.IO.Path.GetFullPath(GamePaths.DataPath).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string full = System.IO.Path.GetFullPath(path);
                if (full.StartsWith(data + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return full.Substring(data.Length + 1).Replace('\\', '/');
            }
            catch { }
            return Path(path);
        }
    }
}
