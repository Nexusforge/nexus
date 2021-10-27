using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;

namespace Nexus.Shared
{
    public partial class DataWriterOption<T>
    {
        #region Fields

        private T _value;

        #endregion

        #region Properties

        [Parameter]
        public string Key { get; set; }

        [Parameter]
        public T DefaultValue { get; set; }

        [Parameter]
        public Dictionary<string, string> Store { get; set; }

        [Parameter]
        public RenderFragment<DataWriterOption<T>> ChildContent { get; set; }

        public T Value
        {
            get
            {
                return _value;
            }
            set
            {
                _value = value;
                this.Store[this.Key] = value.ToString();
            }
        }

        #endregion

        #region Methods

        protected override void OnInitialized()
        {
            if (!this.Store.TryGetValue(this.Key, out var value))
            {
                value = this.DefaultValue.ToString();
                this.Store[this.Key] = value;
            }

            if (typeof(T) == typeof(string))
                _value = (T)(object)value;

            else if (typeof(T) == typeof(int))
                _value = (T)(object)int.Parse(value);

            else
                throw new ArgumentException("Unsupported type.");
        }
    }

    #endregion
}
