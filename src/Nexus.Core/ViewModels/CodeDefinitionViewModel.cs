using Nexus.Core;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nexus.ViewModels
{
    public class CodeDefinitionViewModel : BindableBase /* Must be a class to ensure reference equality! Otherwise there would be created a new copy with every modification. */
    {
        #region Constructors

        public CodeDefinitionViewModel(CodeDefinition model)
        {
            this.Model = model;
        }

        #endregion

        #region Properties

        public CodeDefinition Model { get; }

        public string Id
        {
            get { return this.Model.Id; }
            set { this.Model.Id = value; }
        }

        public string Owner
        {
            get { return this.Model.Owner; }
            set { this.Model.Owner = value; }
        }

        public CodeType CodeType
        {
            get
            {
                return this.Model.CodeType;
            }
            set
            {
                this.Model.CodeType = value;
                this.RaisePropertyChanged();
            }
        }

        public CodeLanguage CodeLanguage
        {
            get { return this.Model.CodeLanguage; }
            set { this.Model.CodeLanguage = value; }
        }

        public string Code
        {
            get { return this.Model.Code; }
            set { this.Model.Code = value; }
        }

        public bool IsEnabled
        {
            get { return this.Model.IsEnabled; }
            set { this.Model.IsEnabled = value; }
        }

        public string Name
        {
            get { return this.Model.Name; }
            set { this.Model.Name = value; }
        }

        [Required]
        public TimeSpan SamplePeriod
        {
            get 
            {
                return this.Model.SamplePeriod; 
            }
            set 
            {
                this.Model.SamplePeriod = value;
                this.RaisePropertyChanged();
            }
        }

        public List<string> RequestedCatalogIds
        {
            get
            {
                return this.Model.RequestedCatalogIds;
            }
            set
            {
                this.Model.RequestedCatalogIds = value;
                this.RaisePropertyChanged();
            }
        }

        #endregion
    }
}
