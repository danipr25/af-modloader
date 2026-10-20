using System;
using System.Collections.Generic;
using System.Text;

namespace AFModLoader.Installer.Core
{
    /// <summary>
    /// Edits the console keys of UE4SS-settings.ini, line by line, in the [Debug] section only. Every other line,
    /// its line ending and the absence of a BOM are kept.
    /// </summary>
    public static class ConsoleSetting
    {
        private static readonly string[] SwitchKeys = { "ConsoleEnabled", "GuiConsoleEnabled", "GuiConsoleVisible" };

        /// <summary>True when any of the three console keys in [Debug] is 1.</summary>
        public static bool IsEnabled(string ini)
        {
            List<Line> lines = Split(ini);
            int start, end;
            if (!FindSection(lines, out start, out end))
            {
                return false;
            }

            for (int i = start + 1; i < end; i++)
            {
                string key, value;
                if (TryKeyValue(lines[i].Text, out key, out value) && value == "1"
                    && Array.Exists(SwitchKeys, k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Turns the console windows on or off. GraphicsAPI is set to dx11 only when turning them on, and left alone
        /// when turning them off.
        /// </summary>
        public static string Apply(string ini, bool enable)
        {
            List<Line> lines = Split(ini);
            string ending = ini.Contains("\r\n") ? "\r\n" : "\n";
            string on = enable ? "1" : "0";
            foreach (string key in SwitchKeys)
            {
                SetKey(lines, key, on, ending);
            }

            if (enable)
            {
                SetKey(lines, "GraphicsAPI", "dx11", ending);
            }

            var result = new StringBuilder();
            foreach (Line line in lines)
            {
                result.Append(line.Text).Append(line.Ending);
            }

            return result.ToString();
        }

        private static List<Line> Split(string text)
        {
            var lines = new List<Line>();
            int position = 0;
            while (position < text.Length)
            {
                int newline = text.IndexOf('\n', position);
                if (newline < 0)
                {
                    lines.Add(new Line { Text = text.Substring(position), Ending = string.Empty });
                    break;
                }

                bool crlf = newline > position && text[newline - 1] == '\r';
                string content = text.Substring(position, (crlf ? newline - 1 : newline) - position);
                lines.Add(new Line { Text = content, Ending = crlf ? "\r\n" : "\n" });
                position = newline + 1;
            }

            return lines;
        }

        private static bool TryKeyValue(string text, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#' || trimmed[0] == '[')
            {
                return false;
            }

            int equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                return false;
            }

            key = trimmed.Substring(0, equals).Trim();
            value = trimmed.Substring(equals + 1).Trim();
            return true;
        }

        /// <summary>Index of the [Debug] header and of the next section header (or the line count).</summary>
        private static bool FindSection(List<Line> lines, out int start, out int end)
        {
            start = -1;
            end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Text.Trim();
                bool header = trimmed.Length > 1 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']';
                if (!header)
                {
                    continue;
                }

                if (start >= 0)
                {
                    end = i;
                    return true;
                }

                if (trimmed.Equals("[Debug]", StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                }
            }

            return start >= 0;
        }

        private static void SetKey(List<Line> lines, string key, string value, string ending)
        {
            int start, end;
            if (!FindSection(lines, out start, out end))
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Ending.Length == 0)
                {
                    lines[lines.Count - 1].Ending = ending;
                }

                lines.Add(new Line { Text = "[Debug]", Ending = ending });
                start = lines.Count - 1;
                end = lines.Count;
            }

            int lastKey = start;
            for (int i = start + 1; i < end; i++)
            {
                string k, v;
                if (!TryKeyValue(lines[i].Text, out k, out v))
                {
                    continue;
                }

                lastKey = i;
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i].Text = key + " = " + value;
                    return;
                }
            }

            // Missing: add it after the last key of the section. A last line without a line ending gets one first.
            if (lines[lastKey].Ending.Length == 0)
            {
                lines[lastKey].Ending = ending;
            }

            lines.Insert(lastKey + 1, new Line { Text = key + " = " + value, Ending = ending });
        }

        private sealed class Line
        {
            public string Text;
            public string Ending;
        }
    }
}
