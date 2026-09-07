using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FfxTool.Gui
{
    /// <summary>
    /// One folder node of the file managers' explorer tree: its own
    /// name, the subfolder nodes + preset leaves it contains, and how
    /// many presets live anywhere below it. Namespace-level on purpose
    /// (NOT nested in FolderScan) — the pages bind XAML templates to it
    /// with x:Type, and XAML can't reference nested types.
    /// </summary>
    public class FolderNode
    {
        public string Name { get; set; }
        public string FullPath;                 // tooltip; null for a loose root
        internal FolderNode Parent;
        public ObservableCollection<object> Items { get; }
            = new ObservableCollection<object>();
        public int Count { get; internal set; } // presets anywhere below this node
        public string CountLabel => Count == 1 ? "1 preset" : Count + " presets";
    }

    /// <summary>
    /// Shared folder-walking helpers for the pages that accept a whole
    /// folder of presets (Convert + Effect Lister).
    ///
    /// The walk is hand-rolled on purpose: .NET Framework's
    /// Directory.EnumerateFiles with AllDirectories throws MID-WALK on
    /// the first locked or denied subtree and loses every file after it.
    /// A per-directory catch means one unreadable subtree contributes
    /// nothing instead of killing the whole scan.
    /// </summary>
    internal static class FolderScan
    {
        /// <summary>Every *.ffx under dir (depth per the flag), sorted
        /// case-insensitively so a queue always reads in a stable order.</summary>
        public static List<string> Collect(string dir, bool recursive)
        {
            var found = new List<string>();
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Walk(dir, recursive, found);
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        public static void Walk(string dir, bool recursive, List<string> into)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.ffx"))
                    into.Add(f);
                if (recursive)
                    foreach (var d in Directory.EnumerateDirectories(dir))
                        Walk(d, true, into);
            }
            catch { /* a locked or denied subtree just contributes nothing */ }
        }

        /// <summary>"1.2 MB" / "640.5 KB" / "512 B" for the folder report.</summary>
        public static string FmtSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return ((double)bytes / (1024 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024) return ((double)bytes / 1024).ToString("0.#") + " KB";
            return bytes + " B";
        }

        /// <summary>The path under root ("sub\inner"), "" when the file sits
        /// directly in root (or root isn't a prefix of the path). This is
        /// the tree mirroring primitive: the grouped file managers and the
        /// ZIP / mirrored-folder outputs are all derived from it.</summary>
        public static string RelUnder(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return "";
            string prefix = root.TrimEnd('\\', '/').Replace('/', '\\') + "\\";
            string p = path.Replace('/', '\\');
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return p.Substring(prefix.Length);
            return "";
        }

        /// <summary>The folder's own name ("Presets" in C:\Users\me\Presets)
        /// — names the mirrored ZIP / output folder and the file managers'
        /// root group header. Never empty ("presets" for drive roots).</summary>
        public static string LeafName(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "presets";
            string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(leaf) ? "presets" : leaf;
        }

        /// <summary>
        /// The file managers' explorer tree: every preset becomes a leaf
        /// under its real subfolder, folder nodes nesting exactly like the
        /// source directory — folders first (A→Z), presets after them in
        /// the caller's order. One root node carries the folder's own name
        /// ("Presets" for a loose multi-file list). leaf(fullPath) returns
        /// the row VM for a file, so the pages keep their own live-status /
        /// selection objects flowing through the tree.
        /// </summary>
        public static FolderNode BuildTree(IReadOnlyList<string> files, string root,
                                           Func<string, object> leaf)
        {
            var rootNode = new FolderNode
            {
                Name = root != null ? LeafName(root) : "Presets",
                FullPath = root
            };
            var dirs = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase)
            {
                { "", rootNode }
            };
            foreach (var f in files)
            {
                string rel = root != null ? RelUnder(root, f) : "";
                string dir = "";
                int slash = rel.LastIndexOf('\\');
                if (slash >= 0) dir = rel.Substring(0, slash);
                var parent = EnsureDir(dirs, rootNode, dir);
                parent.Items.Add(leaf(f));
                for (var n = parent; n != null; n = n.Parent) n.Count++;
            }
            SortTree(rootNode);
            return rootNode;
        }

        /// <summary>The node for a relative directory ("sub\inner"),
        /// creating any missing level on the way down.</summary>
        private static FolderNode EnsureDir(Dictionary<string, FolderNode> dirs,
                                            FolderNode rootNode, string dir)
        {
            if (dir.Length == 0) return rootNode;
            if (dirs.TryGetValue(dir, out var found)) return found;
            int slash = dir.LastIndexOf('\\');
            string own = slash >= 0 ? dir.Substring(slash + 1) : dir;
            var parent = slash >= 0 ? EnsureDir(dirs, rootNode, dir.Substring(0, slash)) : rootNode;
            var node = new FolderNode
            {
                Name = own,
                FullPath = parent.FullPath != null ? parent.FullPath + "\\" + own : null,
                Parent = parent
            };
            parent.Items.Add(node);
            dirs[dir] = node;
            return node;
        }

        /// <summary>Folders before presets inside every node (folders A→Z;
        /// the presets keep the caller's sorted order).</summary>
        private static void SortTree(FolderNode node)
        {
            var folders = node.Items.OfType<FolderNode>()
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (folders.Count > 0)
            {
                var leaves = node.Items.Where(x => !(x is FolderNode)).ToList();
                node.Items.Clear();
                foreach (var f in folders) node.Items.Add(f);
                foreach (var l in leaves) node.Items.Add(l);
            }
            foreach (var f in folders) SortTree(f);
        }
    }
}
