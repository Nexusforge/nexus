﻿using System.Security.Claims;

namespace Nexus.ViewModels
{
    internal class ClaimViewModel
    {
        #region Constructors

        public ClaimViewModel(Claim claim)
        {
            Type = claim.Type;
            Value = claim.Value;
        }

        #endregion

        #region Relay Properties

        public string Type { get; set; }

        public string Value { get; set; }

        #endregion

        #region Methods

        public Claim ToClaim()
        {
            return new Claim(Type, Value);
        }

        #endregion
    }
}
