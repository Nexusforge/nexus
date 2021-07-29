using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Logging
{
    public class LoggingConfigurationProvider : ConfigurationProvider
	{
		private readonly IEnumerable<string> _parentPath;

		public LoggingConfigurationProvider(IEnumerable<string> parentPath)
		{
			_parentPath = parentPath;
		}

		public void SetLogLevel(LogLevel level, string? category = null, string? provider = null)
		{
			var path = this.BuildLogLevelPath(category, provider);

			this.Data[path] = Enum.GetName(level);
			this.OnReload();
		}

		public void ResetLogLevel(string? category = null, string? provider = null)
		{
			if (!String.IsNullOrEmpty(category) || !String.IsNullOrWhiteSpace(provider))
			{
				var path = BuildLogLevelPath(category, provider);
				this.Data.Remove(path);
			}
			else
			{
				this.Data.Clear();
			}

			this.OnReload();
		}

		private string BuildLogLevelPath(string? category, string? provider)
		{
			var segments = _parentPath.ToList();

			if (!String.IsNullOrWhiteSpace(provider))
				segments.Add(provider!.Trim());

			segments.Add("LogLevel");
			segments.Add(String.IsNullOrWhiteSpace(category) ? "Default" : category!.Trim());

			return ConfigurationPath.Combine(segments);
		}
	}
}