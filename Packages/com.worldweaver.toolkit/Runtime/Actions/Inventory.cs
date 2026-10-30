using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldWeaver
{
    [Serializable]
    public sealed class ItemStack { public string Item; public int Amount; public ItemStack(string item, int amount) { Item = item; Amount = amount; } }

    /// <summary>Inventory mutations enforce nonnegative counts and a finite capacity.</summary>
    public sealed class Inventory : MonoBehaviour
    {
        [SerializeField] private List<ItemStack> items = new List<ItemStack>();
        public int Capacity = 10000;
        public IReadOnlyList<ItemStack> Items => items;
        public int Count(string item) { int count = 0; foreach (var stack in items) if (stack.Item == item) count += stack.Amount; return count; }
        public bool CanAdd(string item, int amount)
        {
            if (string.IsNullOrWhiteSpace(item) || amount < 0) return false;
            long total = 0; foreach (var stack in items) total += stack.Amount;
            return total + amount <= Capacity;
        }
        public bool Add(string item, int amount)
        {
            if (!CanAdd(item, amount)) return false;
            var stack = items.Find(s => s.Item == item);
            if (stack == null) items.Add(new ItemStack(item, amount)); else stack.Amount += amount;
            return true;
        }
        public bool Remove(string item, int amount)
        {
            if (amount < 1 || Count(item) < amount) return false;
            int left = amount;
            foreach (var stack in items)
            {
                if (stack.Item != item) continue;
                int take = Math.Min(left, stack.Amount); stack.Amount -= take; left -= take;
                if (left == 0) break;
            }
            return true;
        }
        public static bool Transfer(Inventory source, Inventory destination, string item, int amount)
        {
            if (!source || !destination || source == destination || amount < 1 || source.Count(item) < amount || !destination.CanAdd(item, amount)) return false;
            source.Remove(item, amount); destination.Add(item, amount); return true;
        }
        public Dictionary<string, int> Snapshot()
        { var copy = new Dictionary<string, int>(StringComparer.Ordinal); foreach (var stack in items) copy[stack.Item] = Count(stack.Item); return copy; }
    }

}
