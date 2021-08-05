using Microsoft.Extensions.Logging;
using Nexus.Logging;
using System.Collections.Generic;

namespace Nexus.Services
{
	internal class LogLevelUpdater
	{
		private readonly List<LoggingConfigurationProvider> _providers;

		public LogLevelUpdater()
		{
			_providers = new List<LoggingConfigurationProvider>();
		}

		public void SetLevel(LogLevel level, string? category = null, string? provider = null)
		{
			foreach (var current in _providers)
			{
				current.SetLogLevel(level, category, provider);
			}
		}

		public void ResetLevel(string? category = null, string? provider = null)
		{
			foreach (var current in _providers)
			{
				current.ResetLogLevel(category, provider);
			}
		}

		public void Add(LoggingConfigurationProvider provider)
		{
			_providers.Add(provider);
		}
	}
}
