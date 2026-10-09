// ═══════════ FileDialog.cs — окно «Открыть» для сохранений трекера (armiya_hodN.txt) и последние открытые файлы ═══════════
// Первая битва (Г98) начинается с расстановки из выгрузки трекера, а лежать она может где угодно. В редакторе —
// EditorUtility.OpenFilePanel; в сборке под Windows — системное окно GetOpenFileName (comdlg32). Последние открытые
// файлы — в PlayerPrefs: меню битв показывает их сразу, без окна.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Journal.Play
{
    public static class FileDialog
    {
        const string RecentKey = "journal.recentSaves";
        const int RecentMax = 8;

        // окно выбора файла; null — закрыли без выбора (или окна на этой платформе нет)
        public static string OpenSave(string startDir) => Open(startDir, "Сохранение трекера (armiya_hodN.txt)", "Сохранение трекера (*.txt)", "txt");
        // окно выбора файла с одним расширением ext (без точки); null — закрыли без выбора
        public static string Open(string startDir, string title, string filterName, string ext)
        {
#if UNITY_EDITOR
            var p = UnityEditor.EditorUtility.OpenFilePanel(title, startDir ?? "", ext);
            return string.IsNullOrEmpty(p) ? null : p.Replace('/', Path.DirectorySeparatorChar);
#elif UNITY_STANDALONE_WIN
            var o = new Ofn();
            o.structSize = Marshal.SizeOf(o);
            o.dlgOwner = GetActiveWindow();
            o.filter = filterName + "\0*." + ext + "\0Все файлы (*.*)\0*.*\0\0";
            o.file = new string('\0', 1024); o.maxFile = o.file.Length;
            o.fileTitle = new string('\0', 256); o.maxFileTitle = o.fileTitle.Length;
            o.initialDir = startDir; o.title = title;
            o.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;   // EXPLORER | FILEMUSTEXIST | PATHMUSTEXIST | NOCHANGEDIR
            return GetOpenFileName(o) ? o.file.TrimEnd('\0') : null;
#else
            return null;
#endif
        }

        // последние открытые (сначала свежие), только те, что ещё лежат на месте
        public static List<string> Recent() =>
            PlayerPrefs.GetString(RecentKey, "").Split('\n').Where(p => p.Length > 0 && File.Exists(p)).ToList();
        public static void Remember(string path)
        {
            var l = Recent(); l.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)); l.Insert(0, path);
            if (l.Count > RecentMax) l.RemoveRange(RecentMax, l.Count - RecentMax);
            PlayerPrefs.SetString(RecentKey, string.Join("\n", l)); PlayerPrefs.Save();
        }
        public static string LastDir() { var r = Recent(); return r.Count > 0 ? Path.GetDirectoryName(r[0]) : null; }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class Ofn
        {
            public int structSize; public IntPtr dlgOwner; public IntPtr instance;
            public string filter; public string customFilter; public int maxCustFilter; public int filterIndex;
            public string file; public int maxFile; public string fileTitle; public int maxFileTitle;
            public string initialDir; public string title; public int flags; public short fileOffset; public short fileExtension;
            public string defExt; public IntPtr custData; public IntPtr hook; public string templateName;
            public IntPtr reservedPtr; public int reservedInt; public int flagsEx;
        }
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetOpenFileName([In, Out] Ofn o);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
#endif
    }
}
