using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Offline sparse-world persistence. The small WorldHead index and every
    /// accepted changed ChunkPlan are independent compressed PlayerPrefs shards;
    /// untouched frontier chunks remain deterministic and take no storage. A
    /// partial write is refused on the next load rather than replaced with an
    /// empty city, and callers can expose export/recovery UI before device limits.
    /// </summary>
    public sealed class SlopCityCityForgeLocalStore
    {
        public const string IndexKey = "SlopCity.CityForge.WorldHead.v3";
        public const string IndexPagePrefix = "SlopCity.CityForge.IndexPage.v3.";
        public const string ChunkPrefix = "SlopCity.CityForge.Chunk.v3.";
        public const int MaximumPageBytes = SlopCityCityForge.MaximumWorldPageBytes;
        public const int IndexEntriesPerPage = 128;

        public bool TrySave(CityForgeWorldHead head, IEnumerable<CityForgeChunkPlan> plans, out string error)
        {
            error = null;
            if (head == null) { error = "City Forge has no World Head to save."; return false; }
            List<CityForgeChunkPlan> values = (plans ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(plan => plan != null).OrderBy(plan => plan.coord).ToList();
            if (values.Count != (head.changedChunks ?? new List<CityForgeChunkHead>()).Count) { error = "The City Forge index does not match its changed chunks."; return false; }
            try
            {
                string generation = Guid.NewGuid().ToString("N");
                CityForgeLocalRoot previousRoot = ReadRootOrNull();
                // Write a wholly new generation first. The current root remains
                // untouched until every chunk and index page has reached disk.
                foreach (CityForgeChunkPlan plan in values)
                {
                    string packed = SlopCityCityForgeCanonical.Pack(plan);
                    if (Encoding.UTF8.GetByteCount(packed) > SlopCityCityForge.MaximumPackedChunkBytes) { error = "A City Forge chunk is too large to save safely."; return false; }
                    CityForgeLocalChunkShard shard = new CityForgeLocalChunkShard { canonicalPlanJson = SlopCityCityForgeWire.ToWireJson(plan), revision = plan.revision, revisionId = plan.revisionId };
                    string encoded = Compress(JsonUtility.ToJson(shard));
                    if (Encoding.UTF8.GetByteCount(encoded) > MaximumPageBytes) { error = "A City Forge chunk exceeds local storage capacity."; return false; }
                    PlayerPrefs.SetString(ChunkKey(plan.coord, generation), encoded);
                }
                List<CityForgeChunkHead> heads = (head.changedChunks ?? new List<CityForgeChunkHead>()).OrderBy(item => item.coord).ToList();
                int pageCount = (heads.Count + IndexEntriesPerPage - 1) / IndexEntriesPerPage;
                for (int page = 0; page < pageCount; page++)
                {
                    CityForgeLocalIndexPage indexPage = new CityForgeLocalIndexPage { page = page, heads = heads.Skip(page * IndexEntriesPerPage).Take(IndexEntriesPerPage).Select(item => item.Clone()).ToList() };
                    indexPage.hash = SlopCityCityForge.Sha256(JsonUtility.ToJson(indexPage));
                    string encoded = Compress(JsonUtility.ToJson(indexPage));
                    if (Encoding.UTF8.GetByteCount(encoded) > MaximumPageBytes) { error = "A City Forge index page exceeds local storage capacity."; return false; }
                    PlayerPrefs.SetString(IndexPageKey(page, generation), encoded);
                }
                CityForgeWorldHead rootHead = head.Clone();
                rootHead.changedChunks = new List<CityForgeChunkHead>();
                rootHead.indexIsComplete = false;
                CityForgeLocalRoot root = new CityForgeLocalRoot { head = rootHead, pageCount = pageCount, chunkCount = heads.Count, indexRoot = head.chunkMapRoot, generation = generation };
                string index = Compress(JsonUtility.ToJson(root));
                if (Encoding.UTF8.GetByteCount(index) > MaximumPageBytes) { error = "The City Forge world index exceeds local storage capacity."; return false; }
                // Flush inactive data before atomically switching the small root.
                PlayerPrefs.Save();
                PlayerPrefs.SetString(IndexKey, index);
                PlayerPrefs.Save();
                CleanupGeneration(previousRoot);
                return true;
            }
            catch (Exception)
            {
                error = "City Forge could not save on this device.";
                return false;
            }
        }

        public bool TryLoad(out CityForgeWorldHead head, out List<CityForgeChunkPlan> plans, out string error)
        {
            head = null;
            plans = new List<CityForgeChunkPlan>();
            error = null;
            if (!PlayerPrefs.HasKey(IndexKey)) return false;
            try
            {
                CityForgeLocalRoot root = JsonUtility.FromJson<CityForgeLocalRoot>(Decompress(PlayerPrefs.GetString(IndexKey)));
                if (root == null || root.head == null || root.pageCount < 0 || root.chunkCount < 0 || root.pageCount > 7813) { error = "The City Forge world index is unreadable."; return false; }
                head = root.head;
                List<CityForgeChunkHead> pointers = new List<CityForgeChunkHead>();
                for (int page = 0; page < root.pageCount; page++)
                {
                    if (!TryLoadIndexPage(page, root.generation, out CityForgeLocalIndexPage indexPage, out error)) return false;
                    pointers.AddRange(indexPage.heads ?? new List<CityForgeChunkHead>());
                }
                if (pointers.Count != root.chunkCount) { error = "The City Forge index page count does not match its root."; return false; }
                head.changedChunks = pointers.OrderBy(item => item.coord).ToList();
                head.indexIsComplete = true;
                if (!string.Equals(root.indexRoot, head.chunkMapRoot, StringComparison.Ordinal)) { error = "The City Forge root no longer matches its index pages."; return false; }
                foreach (CityForgeChunkHead pointer in head.changedChunks)
                {
                    string key = ChunkKey(pointer.coord, root.generation);
                    if (!PlayerPrefs.HasKey(key)) { error = "A saved City Forge block is missing. Keep this device copy for recovery before changing it."; return false; }
                    CityForgeLocalChunkShard shard = JsonUtility.FromJson<CityForgeLocalChunkShard>(Decompress(PlayerPrefs.GetString(key)));
                    if (shard == null || !SlopCityCityForgeWire.TryLoadChunkPlanJson(shard.canonicalPlanJson, out CityForgeChunkPlan plan, out error)) { error = "A saved City Forge block is unreadable. Keep this device copy for recovery before changing it."; return false; }
                    plan.revision = shard.revision;
                    plan.revisionId = shard.revisionId;
                    if (plan == null || !plan.coord.Equals(pointer.coord) || !string.Equals(plan.baseChunkHash, pointer.baseChunkHash, StringComparison.Ordinal)
                        || plan.revision != pointer.revision || !string.Equals(plan.revisionId, pointer.revisionId, StringComparison.Ordinal)
                        || !string.Equals(SlopCityCityForgeCanonical.Hash(plan), pointer.snapshotHash, StringComparison.Ordinal))
                    {
                        error = "A saved City Forge block no longer matches its index. Keep this device copy for recovery before changing it.";
                        return false;
                    }
                    plans.Add(plan);
                }
                if (!string.Equals(SlopCityCityForgeCanonical.ChunkMapRoot(head.changedChunks), head.chunkMapRoot, StringComparison.Ordinal))
                {
                    error = "The City Forge world index no longer matches its accepted blocks.";
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                head = null;
                plans.Clear();
                error = "City Forge could not read this device save.";
                return false;
            }
        }

        public bool HasWorld { get { return PlayerPrefs.HasKey(IndexKey); } }
        public static string ChunkKey(CityForgeCoord coord) { return ChunkKey(coord, null); }
        public static string ChunkKey(CityForgeCoord coord, string generation) { return ChunkPrefix + (string.IsNullOrEmpty(generation) ? string.Empty : generation + ".") + coord.x + "." + coord.z; }
        public static string IndexPageKey(int page) { return IndexPageKey(page, null); }
        public static string IndexPageKey(int page, string generation) { return IndexPagePrefix + (string.IsNullOrEmpty(generation) ? string.Empty : generation + ".") + page.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Reads one bounded WorldHead index shard without loading chunk blobs.</summary>
        public bool TryLoadIndexPage(int page, out CityForgeLocalIndexPage indexPage, out string error)
        {
            return TryLoadIndexPage(page, null, out indexPage, out error);
        }

        private bool TryLoadIndexPage(int page, string generation, out CityForgeLocalIndexPage indexPage, out string error)
        {
            indexPage = null;
            error = null;
            if (page < 0 || !PlayerPrefs.HasKey(IndexPageKey(page, generation))) { error = "A City Forge index page is missing."; return false; }
            try
            {
                indexPage = JsonUtility.FromJson<CityForgeLocalIndexPage>(Decompress(PlayerPrefs.GetString(IndexPageKey(page, generation))));
                if (indexPage == null || indexPage.page != page || indexPage.heads == null || indexPage.heads.Count > IndexEntriesPerPage
                    || !string.Equals(indexPage.hash, SlopCityCityForge.Sha256(JsonUtility.ToJson(new CityForgeLocalIndexPage { page = indexPage.page, heads = indexPage.heads })), StringComparison.Ordinal))
                { error = "A City Forge index page is invalid."; return false; }
                return true;
            }
            catch (Exception) { indexPage = null; error = "A City Forge index page is unreadable."; return false; }
        }

        private static CityForgeLocalRoot ReadRootOrNull()
        {
            if (!PlayerPrefs.HasKey(IndexKey)) return null;
            try { return JsonUtility.FromJson<CityForgeLocalRoot>(Decompress(PlayerPrefs.GetString(IndexKey))); }
            catch { return null; }
        }

        private void CleanupGeneration(CityForgeLocalRoot root)
        {
            if (root == null || string.IsNullOrEmpty(root.generation) || root.head == null) return;
            List<CityForgeChunkHead> heads = new List<CityForgeChunkHead>();
            for (int page = 0; page < root.pageCount; page++)
                if (TryLoadIndexPage(page, root.generation, out CityForgeLocalIndexPage indexPage, out string ignored)) heads.AddRange(indexPage.heads ?? new List<CityForgeChunkHead>());
            foreach (CityForgeChunkHead head in heads) PlayerPrefs.DeleteKey(ChunkKey(head.coord, root.generation));
            for (int page = 0; page < root.pageCount; page++) PlayerPrefs.DeleteKey(IndexPageKey(page, root.generation));
            PlayerPrefs.Save();
        }

        private static string Compress(string value)
        {
            byte[] input = Encoding.UTF8.GetBytes(value ?? string.Empty);
            using (MemoryStream target = new MemoryStream())
            {
                using (DeflateStream stream = new DeflateStream(target, System.IO.Compression.CompressionLevel.Optimal, true)) stream.Write(input, 0, input.Length);
                return Convert.ToBase64String(target.ToArray());
            }
        }

        private static string Decompress(string value)
        {
            byte[] source = Convert.FromBase64String(value ?? string.Empty);
            using (MemoryStream input = new MemoryStream(source))
            using (DeflateStream stream = new DeflateStream(input, CompressionMode.Decompress))
            using (MemoryStream target = new MemoryStream())
            {
                stream.CopyTo(target);
                if (target.Length > SlopCityCityForge.MaximumPackedChunkBytes * 2L) throw new InvalidDataException("City Forge local shard is too large.");
                return Encoding.UTF8.GetString(target.ToArray());
            }
        }
    }

    [Serializable]
    public sealed class CityForgeLocalRoot
    {
        public CityForgeWorldHead head;
        public int pageCount;
        public int chunkCount;
        public string indexRoot;
        public string generation;
    }

    [Serializable]
    public sealed class CityForgeLocalIndexPage
    {
        public int page;
        public List<CityForgeChunkHead> heads = new List<CityForgeChunkHead>();
        public string hash;
    }

    [Serializable]
    public sealed class CityForgeLocalChunkShard
    {
        public string canonicalPlanJson;
        public long revision;
        public string revisionId;
    }
}
