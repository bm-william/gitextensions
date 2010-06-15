﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using System.IO;
using System.ComponentModel;

namespace GitCommands
{
    public class ConfigFile
    {
        Dictionary<string, NameValueCollection> data =
            new Dictionary<string, NameValueCollection>();

        static readonly Regex regRemoveEmptyLines =
            new Regex
            (
                @"(\s*;[\d\D]*?\r?\n)+|\r?\n(\s*\r?\n)*",
                RegexOptions.Multiline | RegexOptions.Compiled
            );

        static readonly Regex regParseIniData =
            new Regex
            (
                @"
                (?<IsSection>
                    ^\s*\[(?<SectionName>[^\]]+)?\]\s*$
                )
                |
                (?<IsKeyValue>
                    ^\s*(?<Key>[^(\s*\=\s*)]+)?\s*\=\s*(?<Value>[\d\D]*)$
                )",
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase |
                RegexOptions.IgnorePatternWhitespace
            );

        private string fileName;

        public ConfigFile()
        {
            readIniData(null, null);
        }

        public ConfigFile(string fileName)
            : this(fileName, Settings.Encoding)
        { }

        public ConfigFile(string fileName, Encoding encoding)
        {
            this.fileName = fileName;

            if (!File.Exists(fileName))
                return;

            using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            {
                readIniData(fs, encoding);
            }
        }

        private void readIniData(Stream stream, Encoding encoding)
        {
            string lastSection = string.Empty;
            data.Add(lastSection, new NameValueCollection());
            if (stream != null && encoding != null)
            {
                string iniData;
                using
                (
                    StreamReader reader =
                        new StreamReader(stream, encoding)
                )
                    iniData = reader.ReadToEnd();

                iniData = regRemoveEmptyLines.Replace(iniData, "\n");

                string[] lines =
                    iniData.Split
                    (
                        new char[] { '\n' },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                foreach (string s in lines)
                {
                    Match m = regParseIniData.Match(s);
                    if (m.Success)
                    {
                        if (m.Groups["IsSection"].Length > 0)
                        {
                            string sName =
                                m.Groups["SectionName"].Value;

                            //"fix" double spaces in entries like: [difftool "kdiff3"]
                            while (sName.Contains("  "))
                                sName = sName.Replace("  ", " ");

                            if (lastSection != sName)
                            {
                                lastSection = sName;
                                if (!data.ContainsKey(sName))
                                {
                                    data.Add
                                    (
                                        sName,
                                        new NameValueCollection()
                                    );
                                }
                            }
                        }
                        else if (m.Groups["IsKeyValue"].Length > 0)
                        {
                            data[lastSection].Add
                            (
                                m.Groups["Key"].Value,
                                unescapeString(m.Groups["Value"].Value)
                            );
                        }
                    }
                }
            }
        }

        private string unescapeString(string value)
        {
            // The .gitconfig escapes some character sequences
            return value.Replace("\\\"", "\"");
        }


        private static string escapeString(string path)
        {
            // The .gitconfig escapes some character sequences
            path = path.Replace("\"", "$QUOTE$");

            path = path.Trim();

            if (!path.StartsWith("\\\\"))
                path = path.Replace('\\', '/');

            return path.Replace("$QUOTE$", "\\\"");
        }

        private NameValueCollection this[string section]
        {
            get
            {
                if (!data.ContainsKey(section))
                    data.Add(section, new NameValueCollection());
                return data[section];
            }
        }

        private bool HasSection(string section)
        {
            return data.ContainsKey(section);
        }

        private bool HasKey(string section, string key)
        {
            return
                data.ContainsKey(section) &&
                !string.IsNullOrEmpty(data[section][key]);
        }

        public void RemoveSetting(string setting)
        {
            string[] path = setting.Split('.');

            string section;
            string key;

            if (path.Length == 2)
            {
                section = path[0];
                key = path[1];

                if (HasKey(section, key))
                    data[section].Remove(key);
            }
            else
                if (path.Length == 3)
                {
                    //sections can be defined as:
                    //[section "subsection"] (subsection is case senstive)
                    //or
                    //[section.subsection]
                    section = path[0] + " \"" + path[1] + "\"";
                    key = path[2];

                    if (HasKey(section, key))
                        data[section].Remove(key);

                    section = path[0] + "." + path[1];
                    key = path[2];

                    if (HasKey(section, key))
                        data[section].Remove(key);
                }

        }

        public string GetValue(string setting)
        {
            string[] path = setting.Split('.');

            string section;
            string key;

            if (path.Length == 2)
            {
                section = path[0];
                key = path[1];

                if (HasKey(section, key))
                    return data[section][key];
            }
            else
                if (path.Length == 3)
                {
                    section = path[0] + " \"" + path[1] + "\"";
                    key = path[2];

                    if (HasKey(section, key))
                        return data[section][key];

                    section = path[0] + "." + path[1];
                    key = path[2];

                    if (HasKey(section, key))
                        return data[section][key];
                }
                else
                    return "";

            return "";
        }

        public void SetValue(string setting, string value)
        {
            string[] path = setting.Split('.');

            string section;
            string key;

            if (path.Length == 2)
            {
                section = path[0];
                key = path[1];
            }
            else
                if (path.Length == 3)
                {
                    section = path[0] + "." + path[1];

                    if (!data.ContainsKey(section))
                        section = path[0] + " \"" + path[1] + "\"";

                    key = path[2];
                }
                else
                    return;

            if (value == null)
            {
                this[section][key] = String.Empty;
            }
            else
            {
                this[section][key] = value;
            }
        }


        public void Save()
        {
            Save(Settings.Encoding);
        }

        private void Save(Encoding encoding)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(fs, encoding);
            }
        }

        public void Save(Stream stream)
        {
            Save(stream, Settings.Encoding);
        }

        private void Save(Stream stream, Encoding encoding)
        {
            if (stream == null || stream == Stream.Null)
                throw new ArgumentNullException("stream");
            if (encoding == null)
                throw new ArgumentNullException("encoding");

            using (StreamWriter sw = new StreamWriter(stream, encoding))
            {
                Dictionary<string, NameValueCollection>.Enumerator en =
                    data.GetEnumerator();
                while (en.MoveNext())
                {
                    KeyValuePair<string, NameValueCollection> cur =
                        en.Current;
                    if (!string.IsNullOrEmpty(cur.Key))
                    {
                        sw.WriteLine("[{0}]", cur.Key);
                    }
                    NameValueCollection col = cur.Value;
                    foreach (string key in col.Keys)
                    {
                        if (!string.IsNullOrEmpty(key))
                        {
                            string value = col[key];
                            if (!string.IsNullOrEmpty(value))
                                sw.WriteLine("\t{0}={1}", key, escapeString(value));
                        }
                    }
                }
            }
        }
    }

}
