using Nexus.Core;
using Nexus.Utilities;
using Prism.Mvvm;
using System.Collections.Generic;
using System.IO;

namespace Nexus.ViewModels
{
    internal class FilterSettingsViewModel : BindableBase
    {
        #region Fields

        private static object _editLock;

        private string _filePath;

        #endregion

        #region Constructors

        public FilterSettingsViewModel(string filePath)
        {
            if (File.Exists(filePath))
                this.Model = JsonSerializerHelper.Deserialize<FilterSettings>(File.ReadAllText(filePath));

            else
                this.Model = new FilterSettings();

            _filePath = filePath;
            _editLock = new object();
        }

        #endregion

        #region Properties

        public FilterSettings Model { get; }

        public IReadOnlyList<CodeDefinition> CodeDefinitions => this.Model.CodeDefinitions;

        #endregion

        #region Methods

        public void AddOrUpdateCodeDefinition(CodeDefinitionViewModel description)
        {
            lock (_editLock)
            {
                if (!this.Model.CodeDefinitions.Contains(description.Model))
                    this.Model.CodeDefinitions.Add(description.Model);

                var jsonString = JsonSerializerHelper.Serialize(this.Model);
                File.WriteAllText(_filePath, jsonString);
            }

            this.RaisePropertyChanged(nameof(this.CodeDefinitions));
        }

        public void RemoveCodeDefinition(CodeDefinitionViewModel description)
        {
            lock (_editLock)
            {
                this.Model.CodeDefinitions.Remove(description.Model);

                var jsonString = JsonSerializerHelper.Serialize(this.Model);
                File.WriteAllText(_filePath, jsonString);
            }

            this.RaisePropertyChanged(nameof(this.CodeDefinitions));
        }

        #endregion
    }
}
