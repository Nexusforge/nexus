using Microsoft.AspNetCore.Identity;
using Nexus.Core;
using Prism.Mvvm;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Nexus.ViewModels
{
#warning: update user cookie when modifying claims
    internal class SettingsViewModel : BindableBase
    {
        #region Fields

        private NexusUser _user;
        private UserManager<NexusUser> _userManager;

        #endregion

        #region Constructors

        public SettingsViewModel(UserState userState, UserManager<NexusUser> userManager)
        {
            this.UserState = userState;
            _userManager = userManager;
        }

        #endregion

        #region Properties - General

        public UserState UserState { get; }

        public NexusUser User
        {
            get 
            {
                return _user; 
            }
            set 
            {
                this.UpdateClaims(value);
                base.SetProperty(ref _user, value); 
            }
        }

        public List<NexusUser> Users => _userManager.Users.ToList();

        public List<ClaimViewModel> Claims { get; set; }
        
        #endregion

        #region Methods

        public void RemoveClaim(ClaimViewModel claim)
        {
            this.Claims.Remove(claim);
            this.RaisePropertyChanged(nameof(SettingsViewModel.Claims));
        }

        public async void SaveClaimChanges()
        {
            var claimsToRemove = await _userManager.GetClaimsAsync(this.User);
            var claimsToAdd = this.Claims.Where(claim => !string.IsNullOrWhiteSpace(claim.Type)).Select(claim => claim.ToClaim());

            await _userManager.RemoveClaimsAsync(this.User, claimsToRemove);
            await _userManager.AddClaimsAsync(this.User, claimsToAdd);
        }

        private void UpdateClaims(NexusUser user)
        {
            var claims = _userManager.GetClaimsAsync(user).Result.ToList();
            claims.Add(new Claim(string.Empty, string.Empty));

            this.Claims = claims.Select(claim => new ClaimViewModel(claim)).ToList();
        }

        #endregion
    }
}
