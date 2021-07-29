using Microsoft.Extensions.Configuration;
using Nexus.Services;
using System.Collections.Generic;

namespace Nexus.Logging
{
    public class LoggingConfigurationSource : IConfigurationSource
	{
		private readonly LogLevelUpdater _updater;
		private readonly IEnumerable<string> _parentPath;

		public LoggingConfigurationSource(LogLevelUpdater updater, params string[] parentPath)
		{
			_updater = updater;
			_parentPath = parentPath;
		}

		public IConfigurationProvider Build(IConfigurationBuilder builder)
		{
			var provider = new LoggingConfigurationProvider(_parentPath);
			_updater.Add(provider);

			return provider;
		}
	}
}
