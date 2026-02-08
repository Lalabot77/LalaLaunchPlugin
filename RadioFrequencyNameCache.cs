using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace LaunchPlugin
{
    public sealed class RadioFrequencyNameCache
    {
        private readonly Dictionary<long, string> _frequencyNameByKey = new Dictionary<long, string>();
        private string _lastSessionToken = string.Empty;
        private bool _hasBuilt;

        private readonly struct IndexedObject
        {
            public IndexedObject(int index, object value)
            {
                Index = index;
                Value = value;
            }

            public int Index { get; }
            public object Value { get; }
        }

        public void Reset()
        {
            _frequencyNameByKey.Clear();
            _lastSessionToken = string.Empty;
            _hasBuilt = false;
        }

        public void EnsureBuilt(string sessionToken, object radioInfo)
        {
            sessionToken = sessionToken ?? string.Empty;
            if (!string.Equals(sessionToken, _lastSessionToken, StringComparison.Ordinal))
            {
                _lastSessionToken = sessionToken;
                _frequencyNameByKey.Clear();
                _hasBuilt = false;
            }

            if (_hasBuilt || radioInfo == null)
            {
                return;
            }

            BuildMap(radioInfo);
            _hasBuilt = true;
        }

        public bool TryGetName(int radioIdx, int frequencyIdx, out string name)
        {
            name = string.Empty;
            if (radioIdx < 0 || frequencyIdx < 0)
            {
                return false;
            }

            if (_frequencyNameByKey.TryGetValue(ToKey(radioIdx, frequencyIdx), out var found))
            {
                name = found ?? string.Empty;
                return true;
            }

            return false;
        }

        private static long ToKey(int radioIdx, int frequencyIdx)
        {
            return ((long)radioIdx << 16) | (uint)(frequencyIdx & 0xFFFF);
        }

        private void BuildMap(object radioInfo)
        {
            _frequencyNameByKey.Clear();

            foreach (var radioEntry in EnumerateRadios(radioInfo))
            {
                if (radioEntry.Value == null)
                {
                    continue;
                }

                int radioIdx = ResolveIndex(radioEntry.Value, radioEntry.Index, "RadioIdx", "RadioIndex", "RadioNum", "RadioNumber");
                if (radioIdx < 0)
                {
                    continue;
                }

                foreach (var freqEntry in EnumerateFrequencies(radioEntry.Value))
                {
                    if (freqEntry.Value == null)
                    {
                        continue;
                    }

                    int frequencyIdx = ResolveIndex(freqEntry.Value, freqEntry.Index, "FrequencyIdx", "FrequencyIndex", "FrequencyNum", "FrequencyNumber");
                    if (frequencyIdx < 0)
                    {
                        continue;
                    }

                    string name = GetStringProperty(freqEntry.Value, "FrequencyName") ?? string.Empty;
                    _frequencyNameByKey[ToKey(radioIdx, frequencyIdx)] = name;
                }
            }
        }

        private static IEnumerable<IndexedObject> EnumerateRadios(object radioInfo)
        {
            if (radioInfo == null)
            {
                yield break;
            }

            if (TryGetPropertyValue(radioInfo, "Radios", out var radiosObj))
            {
                foreach (var entry in EnumerateByIndex(radiosObj))
                {
                    yield return entry;
                }
            }

            foreach (var entry in EnumeratePropertiesByPrefix(radioInfo, "Radios"))
            {
                yield return entry;
            }
        }

        private static IEnumerable<IndexedObject> EnumerateFrequencies(object radio)
        {
            if (radio == null)
            {
                yield break;
            }

            if (TryGetPropertyValue(radio, "Frequencies", out var frequenciesObj))
            {
                foreach (var entry in EnumerateByIndex(frequenciesObj))
                {
                    yield return entry;
                }
            }

            foreach (var entry in EnumeratePropertiesByPrefix(radio, "Frequencies"))
            {
                yield return entry;
            }
        }

        private static IEnumerable<IndexedObject> EnumerateByIndex(object container)
        {
            if (container == null)
            {
                yield break;
            }

            if (container is IEnumerable enumerable)
            {
                int index = 0;
                foreach (var item in enumerable)
                {
                    yield return new IndexedObject(index, item);
                    index++;
                }
            }
        }

        private static IEnumerable<IndexedObject> EnumeratePropertiesByPrefix(object source, string prefix)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                if (!prop.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(prop.Name, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = prop.GetValue(source);
                if (value == null)
                {
                    continue;
                }

                int index = TryParseTrailingIndex(prop.Name, out var parsed) ? parsed : -1;
                yield return new IndexedObject(index, value);
            }
        }

        private static int ResolveIndex(object source, int fallbackIndex, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (TryGetIntProperty(source, name, out var value))
                {
                    return value;
                }
            }

            return fallbackIndex;
        }

        private static bool TryGetIntProperty(object source, string name, out int value)
        {
            value = -1;
            if (source == null)
            {
                return false;
            }

            if (!TryGetPropertyValue(source, name, out var raw))
            {
                return false;
            }

            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetStringProperty(object source, string name)
        {
            if (source == null)
            {
                return null;
            }

            if (!TryGetPropertyValue(source, name, out var raw))
            {
                return null;
            }

            return raw?.ToString();
        }

        private static bool TryGetPropertyValue(object source, string name, out object value)
        {
            value = null;
            if (source == null)
            {
                return false;
            }

            var prop = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanRead)
            {
                return false;
            }

            value = prop.GetValue(source);
            return true;
        }

        private static bool TryParseTrailingIndex(string name, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            int end = name.Length - 1;
            int start = end;
            while (start >= 0 && char.IsDigit(name[start]))
            {
                start--;
            }

            int digitStart = start + 1;
            if (digitStart > end)
            {
                return false;
            }

            if (!int.TryParse(name.Substring(digitStart), out var raw))
            {
                return false;
            }

            index = raw > 0 ? raw - 1 : raw;
            return index >= 0;
        }
    }
}
