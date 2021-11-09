using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        protected override Task OnAfterRenderAsync(bool firstRender)
        {
            // ensure there is some value to assign
            if (!this.Store.TryGetValue(this.Key, out var value))
            {
                value = this.DefaultValue.ToString();
                this.Store[this.Key] = value;
            }

            // get new value
            T newValue;

            if (typeof(T) == typeof(string))
                newValue = (T)(object)value;

            else if (typeof(T) == typeof(int))
                newValue = (T)(object)int.Parse(value);

            else
                throw new ArgumentException("Unsupported type.");

            // if value has changed, trigger a rerender
            if (_value is null || !_value.Equals(newValue))
            {
                _value = newValue;
                this.StateHasChanged();
            }

            return base.OnAfterRenderAsync(firstRender);
        }
    }

    #endregion
}
