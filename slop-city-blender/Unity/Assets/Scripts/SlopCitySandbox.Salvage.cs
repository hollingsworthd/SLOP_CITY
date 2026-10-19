using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    public sealed partial class SlopCitySandbox
    {
        public const int InventoryCapacity = 64;
        private const int MaximumObjectRecords = 4096;
        private const int PawnStockCapacity = 128;
        [Serializable] public sealed class InventoryEntry
        {
            public string id, label, category;
            public int quantity, baseValue, cleanStrokes;
            public int UnitPrice => Mathf.Max(1, baseValue * (4 + Mathf.Clamp(cleanStrokes, 0, 4)) / 4);
            internal InventoryEntry Copy() => (InventoryEntry)MemberwiseClone();
        }
        [Serializable] private sealed class SalvageRecord
        {
            public string id;
            public int clean;
            public bool removed, placed;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public SalvageRecord Copy() => (SalvageRecord)MemberwiseClone();
        }
        private readonly List<SlopCityInteractable> jobVehicles = new List<SlopCityInteractable>();
        private readonly List<InventoryEntry> inventory = new List<InventoryEntry>();
        private readonly List<InventoryEntry> pawnStock = new List<InventoryEntry>();
        private readonly Dictionary<string, SalvageRecord> objectRecords = new Dictionary<string, SalvageRecord>();
        private readonly Dictionary<string, SlopCitySalvageItem> salvageObjects = new Dictionary<string, SlopCitySalvageItem>();
        private readonly List<string> deadSalvageIds = new List<string>();
        private readonly RaycastHit[] salvageHits = new RaycastHit[64];
        private readonly Collider[] placementHits = new Collider[64];
        public event Action<SlopCityPawnCounter> InventoryRequested;
        public event Action InventoryChanged;
        public int InventorySlotsUsed => inventory.Count;
        public IReadOnlyList<InventoryEntry> Inventory => inventory;
        public IReadOnlyList<InventoryEntry> PawnStock => pawnStock;
        public int BuyBackPrice(InventoryEntry entry) => entry == null ? 0 : (entry.UnitPrice * 5 + 3) / 4;
        private struct SalvageTarget
        {
            public SlopCitySalvageItem item;
            public SlopCitySalvageSurface surface;
            public int part;
            public string id;
            public float distance;
        }

        private static bool ValidSalvageArchive(Progress value)
        {
            if ((value.inventory != null && value.inventory.Length > InventoryCapacity)
                || (value.objects != null && value.objects.Length > MaximumObjectRecords)
                || (value.pawnStock != null && value.pawnStock.Length > PawnStockCapacity)) return false;
            var ids = new HashSet<string>();
            if (value.inventory != null) foreach (var entry in value.inventory)
                if (entry == null || !ValidObjectId(entry.id) || !ids.Add(entry.id) || entry.quantity <= 0 || entry.quantity > 999
                    || string.IsNullOrEmpty(entry.category) || entry.category.Length >= 100 || entry.baseValue < 1 || entry.baseValue > 10000
                    || entry.cleanStrokes < 0 || entry.cleanStrokes > 4 || (entry.label != null && entry.label.Length > 160)) return false;
            ids.Clear();
            if (value.pawnStock != null) foreach (var entry in value.pawnStock)
                if (entry == null || !ValidObjectId(entry.id) || !ids.Add(entry.id) || entry.quantity <= 0 || entry.quantity > 999
                    || string.IsNullOrEmpty(entry.category) || entry.category.Length >= 100 || entry.baseValue < 1 || entry.baseValue > 10000
                    || entry.cleanStrokes < 0 || entry.cleanStrokes > 4 || (entry.label != null && entry.label.Length > 160)) return false;
            ids.Clear();
            if (value.objects != null) foreach (var entry in value.objects)
                if (entry == null || !ValidObjectId(entry.id) || !ids.Add(entry.id) || entry.clean < 0 || entry.clean > 4 || (entry.placed && (!Finite(entry.position) || !ValidRotation(entry.rotation)))) return false;
            return true;
        }

        private static bool ValidRotation(Quaternion q) => !float.IsNaN(q.x+q.y+q.z+q.w) && !float.IsInfinity(q.x+q.y+q.z+q.w) && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w > .9f && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w < 1.1f;

        private void InitializeSalvage()
        {
            SlopCitySalvageItem.Admitted -= AdmitSalvage;
            SlopCitySalvageItem.Admitted += AdmitSalvage;
            if (progress.inventory != null)
                foreach (InventoryEntry entry in progress.inventory)
                    if (entry != null && ValidObjectId(entry.id) && !string.IsNullOrEmpty(entry.category)
                        && entry.category.Length < 100 && entry.quantity > 0 && inventory.Count < InventoryCapacity
                        && !inventory.Exists(other => other.id == entry.id))
                    {
                        var copy = entry.Copy(); copy.quantity = Mathf.Clamp(copy.quantity, 1, 999);
                        copy.baseValue = Mathf.Clamp(copy.baseValue, 1, 10000); copy.cleanStrokes = Mathf.Clamp(copy.cleanStrokes, 0, 4);
                        copy.label = string.IsNullOrEmpty(copy.label) ? copy.category : copy.label.Substring(0, Mathf.Min(160, copy.label.Length));
                        inventory.Add(copy);
                    }
            if (progress.pawnStock != null) foreach (var entry in progress.pawnStock) pawnStock.Add(entry.Copy());
            if (progress.objects != null)
                foreach (SalvageRecord record in progress.objects)
                    if (record != null && ValidObjectId(record.id) && objectRecords.Count < MaximumObjectRecords)
                    { var copy = record.Copy(); copy.clean = Mathf.Clamp(copy.clean, 0, 4); objectRecords[copy.id] = copy; }
        }
        private static bool ValidObjectId(string id) => !string.IsNullOrEmpty(id) && id.Length < 2048;
        private void CaptureSalvageProgress()
        {
            progress.inventory = inventory.ToArray();
            progress.pawnStock = pawnStock.ToArray();
            progress.objects = new SalvageRecord[objectRecords.Count]; objectRecords.Values.CopyTo(progress.objects, 0);
        }
        private void RefreshSalvage() => RefreshSalvage(true);

        /// <summary>Streamed floors admit salvage through Prepare and item events; avoid full scans every flush.</summary>
        internal void RefreshSalvageLight()
        {
            RefreshSalvage(false);
        }

        private void RefreshSalvage(bool fullScan)
        {
            Transform city = GameObject.Find("SlopCity_NineBlocks")?.transform;
            if (city) SlopCitySalvagePreparation.Prepare(city);
            if (fullScan)
            {
                Transform lounge = GameObject.Find("SlopCity_Lounge")?.transform;
                if (lounge) SlopCitySalvagePreparation.PrepareHome(lounge);
            }
            deadSalvageIds.Clear();
            foreach (var pair in salvageObjects) if (!pair.Value) deadSalvageIds.Add(pair.Key);
            foreach (string id in deadSalvageIds) salvageObjects.Remove(id);
            if (!fullScan) return;
            foreach (SlopCitySalvageItem item in FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) AdmitSalvage(item);
        }
        private void AdmitSalvage(SlopCitySalvageItem item)
        {
            if (!this || !item || !ValidObjectId(item.StableId)) return;
            for (Transform ancestor=item.transform;ancestor;ancestor=ancestor.parent)
                if (ancestor.name.StartsWith("Retired_Runtime_Floor_",StringComparison.Ordinal)) return;
            // Existing live objects already receive each mutation directly. Streamed room
            // refreshes must not replay all city pieces or allocate their full save IDs.
            if (salvageObjects.TryGetValue(item.StableId, out SlopCitySalvageItem admitted)
                && ReferenceEquals(admitted, item)) return;
            salvageObjects[item.StableId] = item;
            SlopCitySalvageSurface surface = item.GetComponent<SlopCitySalvageSurface>();
            if (surface)
            {
                surface.BeginSavedStateRestore();
                try
                {
                    foreach (SalvageRecord state in objectRecords.Values)
                        if (surface.TryGetPart(state.id, out int part))
                        { surface.ApplySavedState(part, state.clean, state.removed); }
                }
                finally { surface.EndSavedStateRestore(); }
            }
            else
            {
                objectRecords.TryGetValue(item.StableId, out SalvageRecord state);
                if (state != null && state.placed) item.transform.SetPositionAndRotation(state.position,state.rotation);
                item.SetCleanProgress(state == null ? 0 : state.clean);
                if (state != null && state.removed) item.gameObject.SetActive(false);
            }
        }
        internal SlopCitySalvageItem RegisterJobSalvage(GameObject target, string id, string category, string label, int baseValue, bool dusty)
        {
            if (!target) return null;
            var item = target.GetComponent<SlopCitySalvageItem>(); if (!item) item = target.AddComponent<SlopCitySalvageItem>();
            item.Configure(id, label, category, dusty, true, baseValue); return item;
        }
        internal bool TryGetSalvageIdentity(Transform target, out string id, out string category)
        {
            var item = target ? target.GetComponentInParent<SlopCitySalvageItem>() : null;
            id = item ? item.StableId : ""; category = item ? item.Category : ""; return item;
        }
        internal int SalvageCleanStrokes(string id) => id != null && objectRecords.TryGetValue(id, out SalvageRecord record) ? record.clean : 0;
        internal bool IsDustyTarget(SlopCityInteractable item)
        { var salvage = item ? item.GetComponentInParent<SlopCitySalvageItem>() : null; return salvage && salvage.DustProne; }
        public int InventoryCount(string category)
        { int count = 0; foreach (InventoryEntry entry in inventory) if (entry.category == category) count += entry.quantity; return count; }
        internal bool InventoryHasId(string id) => inventory.Exists(entry => entry.id == id && entry.quantity > 0);
        internal bool InventoryAdd(string id, string label, string category, int quantity, int baseValue, int cleanStrokes = 0)
        {
            if (!ValidObjectId(id) || string.IsNullOrEmpty(category) || category.Length >= 100 || quantity <= 0 || quantity > 999) return false;
            InventoryEntry existing = inventory.Find(entry => entry.id == id);
            if (existing != null)
            {
                if (existing.category != category || existing.cleanStrokes != cleanStrokes || existing.quantity > 999 - quantity) return false;
                existing.quantity += quantity; return true;
            }
            if (inventory.Count >= InventoryCapacity) return false;
            inventory.Add(new InventoryEntry { id = id, label = (label ?? category).Substring(0, Mathf.Min(160, (label ?? category).Length)), category = category,
                quantity = quantity, baseValue = Mathf.Clamp(baseValue, 1, 10000), cleanStrokes = Mathf.Clamp(cleanStrokes, 0, 4) }); return true;
        }
        internal bool InventoryRemove(string id, int quantity)
        {
            InventoryEntry entry = inventory.Find(value => value.id == id);
            if (entry == null || quantity <= 0 || entry.quantity < quantity) return false;
            entry.quantity -= quantity; if (entry.quantity == 0) inventory.Remove(entry); return true;
        }
        internal bool InventoryConsume(string category, int quantity)
        {
            if (quantity <= 0 || InventoryCount(category) < quantity) return false;
            for (int index = inventory.Count - 1; index >= 0 && quantity > 0; index--)
            {
                InventoryEntry entry = inventory[index]; if (entry.category != category) continue;
                int used = Mathf.Min(quantity, entry.quantity); quantity -= used; entry.quantity -= used;
                if (entry.quantity == 0) inventory.RemoveAt(index);
            }
            return true;
        }
        internal void NotifyWorldItemEvent(string action, string id, string category, int amount)
        { HandleUniqueWorldItemEvent(action, id, category, amount); }
        private bool FindSalvage(Camera camera, out SalvageTarget target)
        {
            target = default;
            if (!camera || !user || IsDriving || IsTravelling) return false;
            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, salvageHits, Reach, ~0, QueryTriggerInteraction.Ignore);
            if (count == salvageHits.Length) return false; // Never pick through unknown truncated blockers.
            float nearest = Reach; SlopCitySalvageItem solid = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = salvageHits[index];
                if (!hit.transform || hit.transform == user || hit.transform.IsChildOf(user) || hit.distance > nearest) continue;
                nearest = hit.distance; solid = hit.transform.GetComponentInParent<SlopCitySalvageItem>();
            }
            float best = nearest + .003f;
            foreach (SlopCitySalvageItem item in salvageObjects.Values)
            {
                if (!item || !item.isActiveAndEnabled || !item.gameObject.activeInHierarchy) continue;
                SlopCitySalvageSurface surface = item.GetComponent<SlopCitySalvageSurface>();
                int part = -1; float distance;
                if (surface)
                {
                    if (!surface.TryRaycast(ray, Mathf.Min(Reach, best), out part, out distance) || surface.IsRemoved(part)) continue;
                }
                else
                {
                    if (solid == item) distance = nearest;
                    else if (!item.TryRaycastVisibleBounds(ray,
                        Mathf.Min(best, solid ? nearest - .003f : best), out distance)) continue;
                }
                string id = surface ? surface.PartId(part) : item.StableId;
                if (objectRecords.TryGetValue(id, out SalvageRecord state) && state.removed) continue;
                if (distance > best) continue;
                best = distance; target = new SalvageTarget { item = item, surface = surface, part = part, id = id, distance = distance };
            }
            return target.item;
        }
        public void SalvageOptions(Camera camera, out bool clean, out bool take)
            => SalvageOptions(camera, out clean, out take, out _);
        public void SalvageOptions(Camera camera, out bool clean, out bool take, out int cleanStrokes)
        {
            bool found = FindSalvage(camera, out SalvageTarget target);
            cleanStrokes = found && target.item.DustProne ? SalvageCleanStrokes(target.id) : -1;
            clean = cleanStrokes >= 0 && cleanStrokes < SlopCitySalvageItem.CleaningStrokes;
            take = found && !IsResting && target.item.Pickable;
        }
        public string SalvagePrompt(Camera camera)
        {
            if (!FindSalvage(camera, out SalvageTarget target)) return "I / BAG";
            int strokes = SalvageCleanStrokes(target.id);
            return target.item.DisplayName + (target.item.DustProne ? " · " + (strokes == 4 ? "POLISHED" : "C / CLEAN " + strokes + "/4") : "")
                + (!IsResting && target.item.Pickable ? " · F / TAKE" : "") + " · I / BAG";
        }
        public bool CleanAimed(Camera camera)
        {
            if (!FindSalvage(camera, out SalvageTarget target) || !target.item.DustProne)
            { Say("Point the center dot at a dusty object within 3.4 metres, then press CLEAN."); return false; }
            int before = SalvageCleanStrokes(target.id);
            if (before >= 4) { Say(target.item.DisplayName + " is already polished."); return false; }
            if (!CanRecordObject(target.id)) return false;
            bool success = ObjectTransaction(() =>
            {
                SalvageRecord state = RecordObject(target.id); state.clean = before + 1;
                ApplySurface(target, state);
                NotifyWorldItemEvent("clean", target.id, target.item.Category, state.clean);
                return true;
            }, () => ApplySurface(target, new SalvageRecord { clean = before }));
            if (success) Say(target.item.DisplayName + " / " + (before == 3
                ? (target.item.Pickable ? "POLISHED. Ready to use or sell." : "POLISHED.")
                : "CLEAN " + (before + 1) + "/4 — the surface is getting shinier."));
            return success;
        }
        public bool TakeAimed(Camera camera)
        {
            if (IsResting) { Say("Stand up before picking up furniture or objects."); return false; }
            if (!FindSalvage(camera, out SalvageTarget target) || !target.item.ValidatePickup())
            { Say("Aim at a removable object. Floors, elevators, video displays and posters stay in place."); return false; }
            if (!CanRecordObject(target.id)) return false;
            int clean = SalvageCleanStrokes(target.id);
            bool success = ObjectTransaction(() =>
            {
                if (!InventoryAdd(target.id, target.item.DisplayName, target.item.Category, 1, target.item.BaseValue, clean))
                { Say("Your bag is full. Sell items at the pawn shop to make room."); return false; }
                SalvageRecord state = RecordObject(target.id); state.removed = true;
                ApplySurface(target, state);
                NotifyWorldItemEvent("pickup", target.id, target.item.Category, 1);
                return true;
            }, () => ApplySurface(target, new SalvageRecord { clean = clean }));
            if (success) { nextTargetAt = 0; Say("IN BAG / " + target.item.DisplayName + ". Sell it inside Signal Yard Pawn Shop."); }
            return success;
        }
        private bool CanRecordObject(string id)
        {
            if (objectRecords.ContainsKey(id) || objectRecords.Count < MaximumObjectRecords) return true;
            Say("The city's saved object history is full. This object remains in place."); return false;
        }
        private SalvageRecord RecordObject(string id)
        {
            if (!objectRecords.TryGetValue(id, out SalvageRecord record)) objectRecords[id] = record = new SalvageRecord { id = id };
            return record;
        }
        private static void ApplySurface(SalvageTarget target, SalvageRecord state)
        {
            if (!target.item) return;
            if (target.surface)
            {
                target.surface.SetCleanProgress(target.part, state.clean);
                if (!target.surface.SetRemoved(target.part, state.removed)) throw new InvalidOperationException("The surface editing budget is full; this piece remains in place.");
            }
            else { if (state.placed) target.item.transform.SetPositionAndRotation(state.position,state.rotation); target.item.SetCleanProgress(state.clean); target.item.gameObject.SetActive(!state.removed); }
        }
        private bool ObjectTransaction(Func<bool> mutation, Action restoreWorld = null)
        {
            var oldInventory = new List<InventoryEntry>(inventory.Count); foreach (var entry in inventory) oldInventory.Add(entry.Copy());
            var oldRecords = new Dictionary<string, SalvageRecord>(); foreach (var pair in objectRecords) oldRecords[pair.Key] = pair.Value.Copy();
            var oldStock = new List<InventoryEntry>(pawnStock.Count); foreach (var entry in pawnStock) oldStock.Add(entry.Copy());
            UniqueJobState oldJobs = CaptureUniqueJobState(); int oldCredits = progress.credits;
            string key = recoverySave ? SaveKey + ".recovery" : SaveKey;
            string previousJson; bool existed;
            try { previousJson = PlayerPrefs.GetString(key, ""); existed = PlayerPrefs.HasKey(key); }
            catch (Exception) { Say("City storage is unavailable. This change could not be saved."); return false; }
            bool committed = false;
            try { committed = mutation() && Save(); return committed; }
            catch (Exception exception) { Debug.LogWarning("SLOP CITY: Object transaction rolled back: " + exception.Message); Say(exception.Message); return false; }
            finally
            {
                if (!committed)
                {
                    inventory.Clear(); inventory.AddRange(oldInventory); pawnStock.Clear(); pawnStock.AddRange(oldStock); objectRecords.Clear(); foreach (var pair in oldRecords) objectRecords[pair.Key] = pair.Value;
                    progress.credits = oldCredits; RestoreUniqueJobState(oldJobs); CaptureSalvageProgress();
                    try { restoreWorld?.Invoke(); } catch (Exception exception) { Debug.LogWarning("SLOP CITY: Visual rollback will retry on admission: " + exception.Message); }
                    // A platform Save exception can follow SetString; restore the pending cache too.
                    try { if (existed) PlayerPrefs.SetString(key, previousJson); else PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); } catch (Exception exception) { Debug.LogWarning("SLOP CITY: Storage cache rollback unavailable: " + exception.Message); }
                }
                InventoryChanged?.Invoke();
            }
        }
        public void OpenInventory(SlopCityPawnCounter pawn = null) { InventoryRequested?.Invoke(CanSellAt(pawn) ? pawn : null); }
        public bool CanSellAt(SlopCityPawnCounter pawn)
        {
            if (!pawn || !pawn.isActiveAndEnabled || !user || IsDriving || IsTravelling) return false;
            Vector3 point = pawn.transform.position;
            Collider collider = pawn.GetComponent<Collider>(); if (collider) point = collider.ClosestPoint(user.position + Vector3.up);
            Vector3 origin = user.position + Vector3.up * 1.4f;
            float distance = Vector3.Distance(origin, point);
            if (distance > Reach) return false;
            if (distance < .02f) return true;
            int count = Physics.RaycastNonAlloc(origin, (point - origin) / distance, salvageHits, distance + .02f, ~0, QueryTriggerInteraction.Ignore);
            if (count == salvageHits.Length) return false;
            for (int index = 0; index < count; index++)
            {
                Transform hit = salvageHits[index].transform;
                if (!hit || hit == user || hit.IsChildOf(user) || hit == pawn.transform || hit.IsChildOf(pawn.transform)) continue;
                if (salvageHits[index].distance < distance - .02f) return false;
            }
            return true;
        }
        public bool SellInventory(string id, SlopCityPawnCounter pawn)
        {
            if (!CanSellAt(pawn)) { Say("Sales happen at the counter inside Signal Yard Pawn Shop."); return false; }
            InventoryEntry entry = inventory.Find(item => item.id == id); if (entry == null) return false;
            string label = entry.label; int price = entry.UnitPrice;
            if (progress.credits > int.MaxValue - price) { Say("Your credit balance is full."); return false; }
            bool success = ObjectTransaction(() =>
            {
                InventoryEntry stock = pawnStock.Find(item => item.id == id);
                if (stock != null && stock.quantity >= 999) return false;
                if (stock == null && pawnStock.Count >= PawnStockCapacity)
                {
                    int discard = pawnStock.FindIndex(item => !NeedsPawnRecovery(item.id));
                    if (discard < 0) { Say("The pawn shop is full. Buy back an item before selling more."); return false; }
                    pawnStock.RemoveAt(discard);
                }
                if (!InventoryRemove(id, 1)) return false;
                if (stock == null) { stock = entry.Copy(); stock.quantity = 1; pawnStock.Add(stock); } else stock.quantity++;
                progress.credits += price; NotifyWorldItemEvent("sell", id, entry.category, 1); return true;
            });
            if (success) Say("SOLD / " + label + " / +" + price + " credits."); return success;
        }
        public bool BuyBackInventory(string id, SlopCityPawnCounter pawn)
        {
            if (!CanSellAt(pawn)) { Say("Buybacks happen at the pawn shop counter."); return false; }
            InventoryEntry entry = pawnStock.Find(item => item.id == id); if (entry == null) return false;
            int price = BuyBackPrice(entry);
            if (progress.credits < price) { Say("You need " + price + " credits to buy back this item."); return false; }
            bool success = ObjectTransaction(() =>
            {
                if (!InventoryAdd(entry.id, entry.label, entry.category, 1, entry.baseValue, entry.cleanStrokes))
                { Say("Make room in your bag first."); return false; }
                progress.credits -= price; entry.quantity--; if (entry.quantity == 0) pawnStock.Remove(entry);
                NotifyWorldItemEvent("buyback", id, entry.category, 1); return true;
            });
            if (success) Say("BACK IN BAG / " + entry.label + " / −" + price + " credits.");
            return success;
        }
        private bool NeedsPawnRecovery(string id)
        {
            // World objects cannot be respawned to replace sold mission fixtures.
            if (id.StartsWith("job-world/", StringComparison.Ordinal)
                || id.StartsWith("legacy-job-cargo/", StringComparison.Ordinal)) return true;
            if (uniqueJobState?.records != null)
                foreach (UniqueJobRecord record in uniqueJobState.records)
                {
                    if (record == null || record.completed || record.awarded) continue;
                    for (int source=0;source<16;source++)
                        if ((record.issued & (1 << source)) != 0 && ItemId(record,source) == id) return true;
                }
            return false;
        }
        internal bool TryPlaceInventoryItem(string id, Vector3 position, Quaternion rotation)
        {
            if (!Finite(position) || !ValidRotation(rotation) || !InventoryHasId(id)
                || !salvageObjects.TryGetValue(id, out SlopCitySalvageItem item) || !item || !item.ValidatePickup()
                || item.GetComponent<SlopCitySalvageSurface>() || !CanRecordObject(id)) return false;
            Bounds local = item.PlacementBounds;
            Vector3 scale = item.transform.lossyScale;
            Vector3 half = Vector3.Scale(local.extents, new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            Vector3 center = position + rotation * Vector3.Scale(local.center,scale);
            int count = Physics.OverlapBoxNonAlloc(center, Vector3.Max(Vector3.one*.015f,half-Vector3.one*.015f), placementHits, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == placementHits.Length) return false;
            for (int index=0;index<count;index++)
            {
                var hit=placementHits[index]; if (!hit || hit.transform==item.transform || hit.transform.IsChildOf(item.transform)) continue;
                // The object may touch its supporting pad, but cannot be placed through walls or the player.
                if (hit.bounds.max.y > center.y-half.y+.035f) return false;
            }
            if (!InventoryRemove(id,1)) return false;
            SalvageRecord record=RecordObject(id); record.placed=true;record.position=position;record.rotation=rotation;record.removed=false;
            item.transform.SetPositionAndRotation(position,rotation);item.gameObject.SetActive(true);item.SetCleanProgress(record.clean);
            Physics.SyncTransforms(); NotifyWorldItemEvent("place",id,item.Category,1);return true;
        }
        internal Transform GetJobVehicle()
        {
            if (vehicle) return vehicle.transform;
            SlopCityInteractable nearest = null; float best = 18 * 18;
            foreach (var site in jobVehicles)
                if (site && site.isActiveAndEnabled && site.gameObject.activeInHierarchy && user)
                { float distance = (site.transform.position - user.position).sqrMagnitude; if (distance < best) { nearest = site; best = distance; } }
            return nearest ? nearest.transform : null;
        }
        internal bool TryObserveJobScreen(Transform target, out string identity, out int channel, out double time, out bool playing)
        {
            identity = ""; channel = -1; time = 0; playing = false;
            return SlopCityCityMedia.Instance && SlopCityCityMedia.Instance.TryObserveJobScreen(target, out identity, out channel, out time, out playing);
        }
        internal bool TryTuneJobScreen(Transform target, int requestedChannel, out string message)
        {
            message = "No city display is available.";
            return SlopCityCityMedia.Instance && SlopCityCityMedia.Instance.TryTuneJobScreen(target, requestedChannel, out message);
        }
    }
}
