namespace Nexus.Shared
{
    public partial class DownloadUI
    {
		#region Constructors

		public DownloadUI()
		{
			this.PropertyChanged = (sender, e) =>
			{
                switch (e.PropertyName)
                {
					case nameof(UserState.ReadProgress):
					case nameof(UserState.WriteProgress):

						this.InvokeAsync(this.StateHasChanged);
						break;

					default:
						break;
				}
			};
		}

		#endregion

		#region Properties

		public bool IsCancelling { get; private set; }

		#endregion

		#region Methods

		public void CancelDownload()
		{
			this.IsCancelling = true;
			this.UserState.CancelDownload();
		}
		
		#endregion
	}
}
