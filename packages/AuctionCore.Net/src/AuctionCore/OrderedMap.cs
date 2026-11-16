using System.Collections;
using System.Collections.Generic;

namespace Apl.Core
{
    /// <summary>
    /// Insertion-ordered map, like Dart's default <c>Map</c>. A plain
    /// <c>Dictionary</c> doesn't promise enumeration order after removals,
    /// and some results depend on iteration order matching the Dart side.
    /// </summary>
    public sealed class OrderedMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : notnull
    {
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _index =
            new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new LinkedList<KeyValuePair<TKey, TValue>>();

        public int Count => _index.Count;

        public bool ContainsKey(TKey key) => _index.ContainsKey(key);

        public TValue this[TKey key]
        {
            get => _index[key].Value.Value;
            set
            {
                if (_index.TryGetValue(key, out var node))
                {
                    node.Value = new KeyValuePair<TKey, TValue>(key, value);
                }
                else
                {
                    _index[key] = _order.AddLast(new KeyValuePair<TKey, TValue>(key, value));
                }
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_index.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        public bool Remove(TKey key)
        {
            if (!_index.TryGetValue(key, out var node)) return false;
            _order.Remove(node);
            _index.Remove(key);
            return true;
        }

        public void Clear()
        {
            _index.Clear();
            _order.Clear();
        }

        public IEnumerable<TKey> Keys
        {
            get { foreach (var kv in _order) yield return kv.Key; }
        }

        public IEnumerable<TValue> Values
        {
            get { foreach (var kv in _order) yield return kv.Value; }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _order.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
