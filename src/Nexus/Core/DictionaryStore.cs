using System.Collections.Generic;

namespace Nexus.Core
{
    internal class DictionaryStore<T>
    {
        private T _value;
        private string _key;
        private Dictionary<string, string> _store;

        public DictionaryStore(T defaultValue, string key, Dictionary<string, string> store)
        {
            _value = defaultValue;
            _key = key;
            _store = store;
        }

        public T Value
        {
            get
            {
                return _value;
            }
            set
            {
                _store[_key] = value.ToString();
                _value = value;
            }
        }
    }
}
