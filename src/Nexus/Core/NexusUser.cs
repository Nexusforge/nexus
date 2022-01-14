﻿using Microsoft.AspNetCore.Identity;
using Nexus.Services;
using System.Collections.Generic;

namespace Nexus.Core
{
    public class NexusUser : IdentityUser
    {
        #region Properties

        public List<RefreshToken> RefreshTokens { get; set; }

        #endregion

        #region Constructors

        public NexusUser()
        {
            //
        }

        public NexusUser(string userName) : base(userName)
        {
            //
        }

        #endregion
    }
}